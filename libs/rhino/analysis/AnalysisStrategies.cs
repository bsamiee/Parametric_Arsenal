using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense analysis result containing point, derivatives, frames, curvature, topology, and metrics.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Internal result type colocated with strategies")]
internal sealed record AnalysisResult(
    Point3d Point,
    Vector3d[] Derivatives,
    (Plane Primary, Plane[]? Perpendicular, double[]? InflectionParams, double[]? MaxCurvatureParams, double? Torsion)? Frames,
    (double? Gaussian, double? Mean, double? K1, double? K2, Vector3d? Dir1, Vector3d? Dir2)? Curvature,
    (double[]? Parameters, Continuity[]? Types)? Discontinuities,
    ((int Index, Point3d Location)[]? Vertices, (int Index, Line Geometry)[]? Edges, bool? IsManifold, bool? IsClosed)? Topology,
    (Point3d? Closest, ComponentIndex? Component, double? Distance, (double, double)? SurfaceUV)? Proximity,
    (bool AtSeam, bool AtSingularity, Point3d[]? SeamPoints)? Singularities,
    (double? Length, double? Area, double? Volume, Point3d? Centroid)? Metrics,
    Interval[]? Domains,
    (double? Curve, (double, double)? Surface, int? Mesh, int DerivOrder) Params);

/// <summary>Dense analysis strategy dispatcher with method-type configuration and SDK integration.</summary>
internal static class AnalysisStrategies {
    private static readonly FrozenDictionary<(AnalysisMethod, Type), ValidationMode> _validation =
        new Dictionary<(AnalysisMethod, Type), ValidationMode> {
            [(AnalysisMethod.Point, typeof(Curve))] = ValidationMode.Standard,
            [(AnalysisMethod.Derivatives, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(AnalysisMethod.Curvature, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(AnalysisMethod.Frame, typeof(Curve))] = ValidationMode.Standard,
            [(AnalysisMethod.Discontinuity, typeof(Curve))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Metrics, typeof(Curve))] = ValidationMode.Standard | ValidationMode.AreaCentroid,
            [(AnalysisMethod.Point, typeof(Surface))] = ValidationMode.Standard,
            [(AnalysisMethod.Derivatives, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Curvature, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Frame, typeof(Surface))] = ValidationMode.Standard,
            [(AnalysisMethod.Discontinuity, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Singularity, typeof(Surface))] = ValidationMode.Standard,
            [(AnalysisMethod.Metrics, typeof(Surface))] = ValidationMode.Standard | ValidationMode.AreaCentroid,
            [(AnalysisMethod.Metrics, typeof(BrepFace))] = ValidationMode.Standard | ValidationMode.MassProperties,
            [(AnalysisMethod.Topology, typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(AnalysisMethod.Proximity, typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(AnalysisMethod.Metrics, typeof(Brep))] = ValidationMode.Standard | ValidationMode.MassProperties,
            [(AnalysisMethod.Point, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Curvature, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Topology, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Proximity, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Metrics, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Point, typeof(SubD))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Topology, typeof(SubD))] = ValidationMode.Standard,
            [(AnalysisMethod.Point, typeof(PointCloud))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(AnalysisMethod.Frame, typeof(PointCloud))] = ValidationMode.Standard,
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<AnalysisResult>> Analyze(
        object source, AnalysisMethod method, IGeometryContext context,
        (double?, (double, double)?, int?)? parameters, int derivativeOrder) =>
        ResultFactory.Create(value: source)
            .Validate(args: [context,
                ((Func<ValidationMode>)(() => {
                    Type type = source switch {
                        BrepFace => typeof(BrepFace),
                        GeometryBase g => g.GetType(),
                        _ => source.GetType(),
                    };
                    return Enum.GetValues<AnalysisMethod>()
                        .Where(flag => flag is not (AnalysisMethod.None or AnalysisMethod.Standard or AnalysisMethod.Comprehensive) && method.HasFlag(flag))
                        .Select(flag => {
                            for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType) {
                                if (_validation.TryGetValue((flag, t), out ValidationMode m)) {
                                    return m;
                                }
                            }
                            return ValidationMode.None;
                        })
                        .Aggregate(ValidationMode.None, (acc, m) => acc | m);
                }))(),
            ])
            .Map(_ => AnalyzeCore(source, method, context, parameters, derivativeOrder))
            .Bind(result => result switch {
                AnalysisResult r => ResultFactory.Create(value: (IReadOnlyList<AnalysisResult>)[r]),
                null => ResultFactory.Create<IReadOnlyList<AnalysisResult>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                _ => ResultFactory.Create<IReadOnlyList<AnalysisResult>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
            });

    [Pure]
    private static AnalysisResult? AnalyzeCore(object source, AnalysisMethod method, IGeometryContext context, (double?, (double, double)?, int?)? p, int dO) =>
        (method, source, p?.Item1, p?.Item2, p?.Item3, dO, context) switch {
            (var m, Curve cv, var tP, _, _, var order, var ctx) when cv.Domain.IncludesParameter(tP ?? cv.Domain.ParameterAt(0.5)) && (tP ?? cv.Domain.ParameterAt(0.5)) is var t =>
                new AnalysisResult(
                    Point: cv.PointAt(t),
                    Derivatives: m.HasFlag(AnalysisMethod.Derivatives) && order >= 1 ?
                        (cv.DerivativeAt(t), cv.TangentAt(t)) switch {
                            (Vector3d d, _) when d.IsValid => [cv.TangentAt(t), d],
                            (_, Vector3d tang) when tang.IsValid => [tang],
                            _ => [],
                        } : [],
                    Frames: m.HasFlag(AnalysisMethod.Frame) ?
                        (cv.FrameAt(t, out Plane frame) ? frame : new Plane(cv.PointAt(t), Vector3d.ZAxis),
                         cv.GetPerpendicularFrames([0, 0.25, 0.5, 0.75, 1.0].Select(cv.Domain.ParameterAt).ToArray()),
                         cv.InflectionPoints() is double[] ipts && ipts.Length > 0 ? ipts : null,
                         cv.MaxCurvaturePoints() is double[] mpts && mpts.Length > 0 ? mpts : null,
                         cv.IsClosed ? cv.TorsionAt(t) : null) : null,
                    Curvature: m.HasFlag(AnalysisMethod.Curvature) && cv.CurvatureAt(t) is Vector3d curv && curv.IsValid ?
                        (null, null, curv.Length, null, null, null) : null,
                    Discontinuities: m.HasFlag(AnalysisMethod.Discontinuity) ?
                        (((Func<double[]>)(() => {
                            List<double> dParams = new();
                            double current = cv.Domain.Min;
                            for (int i = 0; i < 20 && cv.GetNextDiscontinuity(Continuity.C1_continuous, current, cv.Domain.Max, out double td); i++, current = td + ctx.AbsoluteTolerance) {
                                dParams.Add(td);
                            }
                            return [.. dParams];
                        }))() is double[] dPs && dPs.Length > 0 ? dPs : null,
                        null) : null,
                    Topology: null,
                    Proximity: null,
                    Singularities: null,
                    Metrics: m.HasFlag(AnalysisMethod.Metrics) ?
                        (cv.GetLength(), null, null, AreaMassProperties.Compute(cv)?.Centroid) : null,
                    Domains: m.HasFlag(AnalysisMethod.Domains) ? [cv.Domain] : null,
                    Params: (t, p?.Item2, p?.Item3, order)),

            (var m, Surface sf, _, var (u, v), _, var order, var ctx) when sf.Domain(0).IncludesParameter(u ?? sf.Domain(0).ParameterAt(0.5)) &&
                sf.Domain(1).IncludesParameter(v ?? sf.Domain(1).ParameterAt(0.5)) &&
                (u ?? sf.Domain(0).ParameterAt(0.5), v ?? sf.Domain(1).ParameterAt(0.5)) is (var uP, var vP) =>
                new AnalysisResult(
                    Point: m.HasFlag(AnalysisMethod.Derivatives) && sf.Evaluate(uP, vP, order, out Point3d evalPt, out Vector3d[] derivs) ? evalPt : sf.PointAt(uP, vP),
                    Derivatives: m.HasFlag(AnalysisMethod.Derivatives) && sf.Evaluate(uP, vP, order, out Point3d _, out Vector3d[] derivs) ? derivs : [],
                    Frames: m.HasFlag(AnalysisMethod.Frame) ?
                        (m.HasFlag(AnalysisMethod.Derivatives) && sf.Evaluate(uP, vP, order, out Point3d pt, out Vector3d[] ds) && ds.Length >= 2 ? new Plane(pt, ds[0], ds[1]) :
                         sf.FrameAt(uP, vP, out Plane fr) ? fr : Plane.WorldXY,
                        null, null, null, null) : null,
                    Curvature: m.HasFlag(AnalysisMethod.Curvature) && sf.CurvatureAt(uP, vP) is SurfaceCurvature sc ?
                        (sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1), sc.Direction(0), sc.Direction(1)) : null,
                    Discontinuities: m.HasFlag(AnalysisMethod.Discontinuity) ?
                        (((Func<double[]>)(() => {
                            List<double> dParams = new();
                            for (int dir = 0; dir <= 1; dir++) {
                                double current = dir == 0 ? sf.Domain(0).Min : sf.Domain(1).Min;
                                double max = dir == 0 ? sf.Domain(0).Max : sf.Domain(1).Max;
                                for (int i = 0; i < 10 && sf.GetNextDiscontinuity(dir, Continuity.C1_continuous, current, max, out double td); i++, current = td + ctx.AbsoluteTolerance) {
                                    dParams.Add(td);
                                }
                            }
                            return [.. dParams];
                        }))() is double[] dPs && dPs.Length > 0 ? dPs : null,
                        null) : null,
                    Topology: null,
                    Proximity: null,
                    Singularities: m.HasFlag(AnalysisMethod.Singularity) ?
                        (sf.IsAtSeam(uP, vP), sf.IsAtSingularity(uP, vP, true), null) : null,
                    Metrics: m.HasFlag(AnalysisMethod.Metrics) ?
                        (null, AreaMassProperties.Compute(sf)?.Area, null, AreaMassProperties.Compute(sf)?.Centroid) : null,
                    Domains: m.HasFlag(AnalysisMethod.Domains) ? [sf.Domain(0), sf.Domain(1)] : null,
                    Params: (p?.Item1, (uP, vP), p?.Item3, order)),

            (var m, BrepFace face, _, var sP, _, var order, var ctx) when
                AnalyzeCore(face.UnderlyingSurface(), m & ~AnalysisMethod.Metrics, ctx, (null, sP, null), order) is AnalysisResult faceResult =>
                faceResult with {
                    Metrics = m.HasFlag(AnalysisMethod.Metrics) ?
                        (null, AreaMassProperties.Compute(face)?.Area,
                         face.Brep is Brep fb ? VolumeMassProperties.Compute(fb)?.Volume : null,
                         AreaMassProperties.Compute(face)?.Centroid) : null,
                },

            (var m, Brep brep, _, _, var fIdx, var order, var ctx) when (fIdx ?? 0) >= 0 && (fIdx ?? 0) < brep.Faces.Count &&
                AnalyzeCore(brep.Faces[fIdx ?? 0], m & ~(AnalysisMethod.Topology | AnalysisMethod.Proximity | AnalysisMethod.Metrics), ctx, (null, null, fIdx ?? 0), order) is AnalysisResult brepBase =>
                brepBase with {
                    Topology = m.HasFlag(AnalysisMethod.Topology) ?
                        (brep.Vertices.Select((v, i) => (i, v.Location)).ToArray(),
                         brep.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))).ToArray(),
                         brep.IsManifold,
                         brep.IsSolid) : null,
                    Proximity = m.HasFlag(AnalysisMethod.Proximity) && brep.ClosestPoint(brepBase.Point, out Point3d closestBr, out ComponentIndex ci, out double s, out double tBr, ctx.AbsoluteTolerance * 100, out Vector3d _) ?
                        (closestBr, ci, brepBase.Point.DistanceTo(closestBr), (s, tBr)) : null,
                    Metrics = m.HasFlag(AnalysisMethod.Metrics) ?
                        (null, AreaMassProperties.Compute(brep)?.Area, VolumeMassProperties.Compute(brep)?.Volume, VolumeMassProperties.Compute(brep)?.Centroid) : null,
                },

            (var m, Mesh mesh, _, _, var vIdx, _, var ctx) when (vIdx ?? 0) >= 0 && (vIdx ?? 0) < mesh.Vertices.Count &&
                (mesh.Normals.Count > (vIdx ?? 0) ? mesh.Normals[vIdx ?? 0] :
                 mesh.Normals.ComputeNormals() && mesh.Normals.Count > (vIdx ?? 0) ? mesh.Normals[vIdx ?? 0] : Vector3d.ZAxis) is var normal =>
                new AnalysisResult(
                    Point: mesh.Vertices.Point3dAt(vIdx ?? 0),
                    Derivatives: [],
                    Frames: m.HasFlag(AnalysisMethod.Frame) ?
                        (new Plane(mesh.Vertices.Point3dAt(vIdx ?? 0), normal.IsValid ? normal : Vector3d.ZAxis), null, null, null, null) : null,
                    Curvature: m.HasFlag(AnalysisMethod.Curvature) ?
                        ((Func<(double?, double?, double?, double?, Vector3d?, Vector3d?)?>)(() => {
                            double[] gaussValues = new double[mesh.Vertices.Count];
                            double[] meanValues = new double[mesh.Vertices.Count];
                            double[] k1Values = new double[mesh.Vertices.Count];
                            double[] k2Values = new double[mesh.Vertices.Count];
                            return (mesh.Vertices.ComputeCurvature(1, gaussValues) && mesh.Vertices.ComputeCurvature(2, meanValues) &&
                                    mesh.Vertices.ComputeCurvature(3, k1Values) && mesh.Vertices.ComputeCurvature(4, k2Values)) ?
                                (gaussValues[vIdx ?? 0], meanValues[vIdx ?? 0], k1Values[vIdx ?? 0], k2Values[vIdx ?? 0], null, null) : null;
                        }))() : null,
                    Discontinuities: null,
                    Topology: m.HasFlag(AnalysisMethod.Topology) ?
                        (Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, new Point3d(mesh.TopologyVertices[i].X, mesh.TopologyVertices[i].Y, mesh.TopologyVertices[i].Z))).ToArray(),
                         Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))).Where(e => e.Item2.IsValid).ToArray(),
                         mesh.IsManifold,
                         mesh.IsClosed) : null,
                    Proximity: m.HasFlag(AnalysisMethod.Proximity) && mesh.ClosestMeshPoint(mesh.Vertices.Point3dAt(vIdx ?? 0), ctx.AbsoluteTolerance * 100) is MeshPoint mp ?
                        (mesh.PointAt(mp), null, 0, null) : null,
                    Singularities: null,
                    Metrics: m.HasFlag(AnalysisMethod.Metrics) ?
                        (null, AreaMassProperties.Compute(mesh)?.Area, VolumeMassProperties.Compute(mesh)?.Volume,
                         mesh.Vertices.Count > 0 ? new Point3d(mesh.Vertices.Average(v => v.X), mesh.Vertices.Average(v => v.Y), mesh.Vertices.Average(v => v.Z)) : Point3d.Origin) : null,
                    Domains: m.HasFlag(AnalysisMethod.Domains) ? [new Interval(0, mesh.Vertices.Count)] : null,
                    Params: (p?.Item1, p?.Item2, vIdx ?? 0, dO)),

            (var m, SubD subd, _, var sP, var idx, var order, var ctx) when subd.ToBrep() is Brep sbr =>
                ((Func<AnalysisResult?>)(() => { try { return AnalyzeCore(sbr, m, ctx, (null, sP, idx), order); } finally { sbr.Dispose(); } }))(),

            (var m, PointCloud cloud, _, _, _, _, var ctx) when cloud.Count > 0 &&
                (cloud.GetPoints(), cloud.GetBoundingBox(accurate: true).Center,
                 Plane.FitPlaneToPoints(cloud.GetPoints(), out Plane fitted) == PlaneFitResult.Success ? fitted : Plane.WorldXY) is (var pts, var center, var plane) =>
                new AnalysisResult(
                    Point: center,
                    Derivatives: [],
                    Frames: m.HasFlag(AnalysisMethod.Frame) ? (plane, null, null, null, null) : null,
                    Curvature: null,
                    Discontinuities: null,
                    Topology: null,
                    Proximity: null,
                    Singularities: null,
                    Metrics: m.HasFlag(AnalysisMethod.Metrics) ? (null, null, null, center) : null,
                    Domains: m.HasFlag(AnalysisMethod.Domains) ? [new Interval(0, cloud.Count)] : null,
                    Params: (p?.Item1, p?.Item2, p?.Item3, dO)),

            (var m, Point3d pt, _, _, _, var order, _) =>
                new AnalysisResult(pt, [], null, null, null, null, null, null, null, null, (p?.Item1, p?.Item2, p?.Item3, order)),

            (var m, Vector3d vec, _, _, _, var order, _) =>
                new AnalysisResult(Point3d.Origin, [vec],
                    m.HasFlag(AnalysisMethod.Frame) ? (new Plane(Point3d.Origin, vec), null, null, null, null) : null,
                    null, null, null, null, null, null, null, (p?.Item1, p?.Item2, p?.Item3, order)),

            _ => null,
        };
}
