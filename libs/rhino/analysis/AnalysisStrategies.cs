using System.Buffers;
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

/// <summary>Ultra-dense analysis dispatcher with polymorphic SDK integration and zero-allocation patterns.</summary>
internal static class AnalysisStrategies {
    /// <summary>Validation configuration mapping analysis methods and geometry types to required validation modes.</summary>
    private static readonly FrozenDictionary<(AnalysisMethod Method, Type GeometryType), ValidationMode> _validationConfig =
        new Dictionary<(AnalysisMethod, Type), ValidationMode> {
            [(AnalysisMethod.Derivatives, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(AnalysisMethod.Derivatives, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Frame, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(AnalysisMethod.Frame, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Curvature, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(AnalysisMethod.Curvature, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Discontinuity, typeof(Curve))] = ValidationMode.Standard,
            [(AnalysisMethod.Discontinuity, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Topology, typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(AnalysisMethod.Topology, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Proximity, typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(AnalysisMethod.Proximity, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Singularity, typeof(Surface))] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [(AnalysisMethod.Metrics, typeof(Curve))] = ValidationMode.Standard | ValidationMode.AreaCentroid,
            [(AnalysisMethod.Metrics, typeof(Surface))] = ValidationMode.Standard | ValidationMode.MassProperties,
            [(AnalysisMethod.Metrics, typeof(Brep))] = ValidationMode.Standard | ValidationMode.MassProperties,
            [(AnalysisMethod.Metrics, typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(AnalysisMethod.Metrics, typeof(PointCloud))] = ValidationMode.Standard,
        }.ToFrozenDictionary();
    /// <summary>Polymorphic analysis with direct SDK dispatch, validation, and comprehensive evaluation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "Dense polymorphic dispatch requires comprehensive pattern matching")]
    internal static Result<IReadOnlyList<AnalysisResult>> Analyze(object source, AnalysisMethod method, IGeometryContext context, (double? Curve, (double, double)? Surface, int? Mesh)? parameters = null, int derivativeOrder = 2) =>
        ((source, method, parameters, derivativeOrder) switch {
            (Curve cv, AnalysisMethod m, var p, int o) => ResultFactory.Create(value: cv).Validate(args: [context, _validationConfig.GetValueOrDefault((m, typeof(Curve)), ValidationMode.Standard | ValidationMode.Degeneracy)])
                .Map(_ => {
                    double t = p?.Curve ?? cv.Domain.ParameterAt(0.5);
                    ArrayPool<double> pool = ArrayPool<double>.Shared;
                    double[] buffer = pool.Rent(20);
                    try {
                        int dc = 0; double s = cv.Domain.Min;
                        while (dc < 20 && cv.GetNextDiscontinuity(Continuity.C1_continuous, s, cv.Domain.Max, out double td)) { buffer[dc++] = td; s = td + context.AbsoluteTolerance; }
                        Point3d[] inflPts = cv.InflectionPoints() ?? []; Point3d[] maxCurvPts = cv.MaxCurvaturePoints() ?? [];
                        return new AnalysisResult(cv.PointAt(t), m.HasFlag(AnalysisMethod.Derivatives) ? cv.DerivativeAt(t, o) ?? [] : [], m.HasFlag(AnalysisMethod.Frame) && cv.FrameAt(t, out Plane f) ? (f, cv.GetPerpendicularFrames([.. Enumerable.Range(0, 5).Select(i => cv.Domain.ParameterAt(i * 0.25))]), inflPts.Length > 0 ? [.. inflPts.Select(p => cv.ClosestPoint(p, out double tp) ? tp : 0)] : null, maxCurvPts.Length > 0 ? [.. maxCurvPts.Select(p => cv.ClosestPoint(p, out double tp) ? tp : 0)] : null, cv.IsClosed ? cv.TorsionAt(t) : null) : null, m.HasFlag(AnalysisMethod.Curvature) && cv.CurvatureAt(t) is Vector3d k && k.IsValid ? (Gaussian: null, Mean: null, k.Length, K2: null, Dir1: null, Dir2: null) : null, m.HasFlag(AnalysisMethod.Discontinuity) && dc > 0 ? ([.. buffer[..dc]], buffer[..dc].Select(dp => cv.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous).ToArray()) : null, Topology: null, Proximity: null, Singularities: null, m.HasFlag(AnalysisMethod.Metrics) ? (cv.GetLength(), Area: null, Volume: null, AreaMassProperties.Compute(cv)?.Centroid) : null, m.HasFlag(AnalysisMethod.Domains) ? [cv.Domain] : null, (t, Surface: null, Mesh: null, o));
                    } finally { pool.Return(buffer, clearArray: true); }
                }),

            (Surface sf, AnalysisMethod m, var p, int o) => ResultFactory.Create(value: sf).Validate(args: [context, _validationConfig.GetValueOrDefault((m, typeof(Surface)), ValidationMode.Standard | ValidationMode.SurfaceContinuity)])
                .Map(_ => {
                    (double u, double v) = p?.Surface ?? (sf.Domain(0).ParameterAt(0.5), sf.Domain(1).ParameterAt(0.5));
                    ArrayPool<double> pool = ArrayPool<double>.Shared; double[] buffer = pool.Rent(20);
                    try {
                        int dc = 0;
                        foreach (int dir in Enumerable.Range(0, 2)) {
                            double s = sf.Domain(dir).Min;
                            while (dc < 20 && sf.GetNextDiscontinuity(dir, Continuity.C1_continuous, s, sf.Domain(dir).Max, out double td)) { buffer[dc++] = td; s = td + context.AbsoluteTolerance; }
                        }
                        return new AnalysisResult(sf.PointAt(u, v), m.HasFlag(AnalysisMethod.Derivatives) && sf.Evaluate(u, v, o, out Point3d _, out Vector3d[] d) ? d : [], m.HasFlag(AnalysisMethod.Frame) && sf.FrameAt(u, v, out Plane f) ? (f, Perpendicular: null, InflectionParams: null, MaxCurvatureParams: null, Torsion: null) : null, m.HasFlag(AnalysisMethod.Curvature) && sf.CurvatureAt(u, v) is SurfaceCurvature sc ? (sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1), sc.Direction(0), sc.Direction(1)) : null, m.HasFlag(AnalysisMethod.Discontinuity) && dc > 0 ? ([.. buffer[..dc]], buffer[..dc].Select(_ => Continuity.C1_continuous).ToArray()) : null, Topology: null, Proximity: null, m.HasFlag(AnalysisMethod.Singularity) ? (sf.IsAtSeam(u, v) != 0, sf.IsAtSingularity(u, v, exact: true), null) : null, m.HasFlag(AnalysisMethod.Metrics) ? (Length: null, AreaMassProperties.Compute(sf)?.Area, Volume: null, AreaMassProperties.Compute(sf)?.Centroid) : null, m.HasFlag(AnalysisMethod.Domains) ? [sf.Domain(0), sf.Domain(1)] : null, (Curve: null, (u, v), Mesh: null, o));
                    } finally { pool.Return(buffer, clearArray: true); }
                }),

            (Brep brep, AnalysisMethod m, var p, int o) => ResultFactory.Create(value: brep).Validate(args: [context, _validationConfig.GetValueOrDefault((m, typeof(Brep)), ValidationMode.Standard | ValidationMode.Topology)])
                .Map(_ => {
                    int fIdx = Math.Max(0, Math.Min(p?.Mesh ?? 0, brep.Faces.Count - 1));
                    using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
                    (double u, double v) = p?.Surface ?? (sf.Domain(0).ParameterAt(0.5), sf.Domain(1).ParameterAt(0.5)); Point3d pt = sf.PointAt(u, v);
                    return new AnalysisResult(pt, m.HasFlag(AnalysisMethod.Derivatives) && sf.Evaluate(u, v, o, out Point3d _, out Vector3d[] d) ? d : [], m.HasFlag(AnalysisMethod.Frame) && sf.FrameAt(u, v, out Plane f) ? (f, Perpendicular: null, InflectionParams: null, MaxCurvatureParams: null, Torsion: null) : null, m.HasFlag(AnalysisMethod.Curvature) && sf.CurvatureAt(u, v) is SurfaceCurvature sc ? (sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1), sc.Direction(0), sc.Direction(1)) : null, Discontinuities: null, m.HasFlag(AnalysisMethod.Topology) ? (brep.Vertices.Select((v, i) => (i, v.Location)).ToArray(), brep.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))).ToArray(), brep.IsManifold, brep.IsSolid) : null, m.HasFlag(AnalysisMethod.Proximity) && brep.ClosestPoint(pt, out Point3d cp, out ComponentIndex ci, out double s, out double t, context.AbsoluteTolerance * 100, out Vector3d _) ? (cp, ci, pt.DistanceTo(cp), (s, t)) : null, Singularities: null, m.HasFlag(AnalysisMethod.Metrics) ? (Length: null, AreaMassProperties.Compute(brep)?.Area, VolumeMassProperties.Compute(brep)?.Volume, VolumeMassProperties.Compute(brep)?.Centroid) : null, Domains: null, (Curve: null, (u, v), fIdx, o));
                }),

            (Mesh mesh, AnalysisMethod m, var p, int o) => ResultFactory.Create(value: mesh).Validate(args: [context, _validationConfig.GetValueOrDefault((m, typeof(Mesh)), ValidationMode.MeshSpecific)])
                .Map(_ => {
                    int vIdx = Math.Max(0, Math.Min(p?.Mesh ?? 0, mesh.Vertices.Count - 1));
                    ((int, Point3d)[]?, (int, Line)[]?, bool?, bool?)? topo = m.HasFlag(AnalysisMethod.Topology) ? (Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])).ToArray(),
                        Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))).ToArray(), mesh.IsManifold(topologicalTest: true, out bool _, out bool _), mesh.IsClosed) : null;
                    return new AnalysisResult(mesh.Vertices[vIdx], [],
                        m.HasFlag(AnalysisMethod.Frame) ? (new Plane(mesh.Vertices[vIdx], mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis), null, null, null, null) : ((Plane, Plane[]?, double[]?, double[]?, double?)?)(null),
                        Curvature: null, Discontinuities: null, topo,
                        m.HasFlag(AnalysisMethod.Proximity) && mesh.ClosestMeshPoint(mesh.Vertices[vIdx], context.AbsoluteTolerance * 100) is MeshPoint mp ? (Closest: mesh.PointAt(mp), Component: null, Distance: 0d, SurfaceUV: null) : null,
                        Singularities: null,
                        m.HasFlag(AnalysisMethod.Metrics) ? (Length: null, AreaMassProperties.Compute(mesh)?.Area, VolumeMassProperties.Compute(mesh)?.Volume, Centroid: null) : null,
                        m.HasFlag(AnalysisMethod.Domains) ? [new Interval(0, mesh.Vertices.Count)] : null, (Curve: null, Surface: null, vIdx, o));
                }),

            (SubD subd, AnalysisMethod m, var p, int o) => ResultFactory.Create(deferred: () => {
                using Brep br = subd.ToBrep();
                return br is not null ? Analyze(br, m, context, p, o).Map(r => r[0]) :
                    ResultFactory.Create<AnalysisResult>(error: AnalysisErrors.Operation.UnsupportedGeometry);
            }),

            (PointCloud cloud, AnalysisMethod m, _, int o) when cloud.Count > 0 => ResultFactory.Create(value: cloud.GetBoundingBox(accurate: true).Center)
                .Map(center => new AnalysisResult(center, [],
                    m.HasFlag(AnalysisMethod.Frame) && Plane.FitPlaneToPoints(cloud.GetPoints(), out Plane f) == PlaneFitResult.Success ? (f, Perpendicular: null, InflectionParams: null, MaxCurvatureParams: null, Torsion: null) : null,
                    Curvature: null, Discontinuities: null, Topology: null, Proximity: null, Singularities: null,
                    m.HasFlag(AnalysisMethod.Metrics) ? (Length: null, Area: null, Volume: null, center) : null,
                    m.HasFlag(AnalysisMethod.Domains) ? [new Interval(0, cloud.Count)] : null, (Curve: null, Surface: null, Mesh: null, o))),

            (Point3d pt, _, _, int o) => ResultFactory.Create(value: new AnalysisResult(pt, [], Frames: null, Curvature: null, Discontinuities: null, Topology: null, Proximity: null, Singularities: null, Metrics: null, Domains: null, (Curve: null, Surface: null, Mesh: null, o))),

            (Vector3d vec, AnalysisMethod m, _, int o) => ResultFactory.Create(value: new AnalysisResult(Point3d.Origin, [vec],
                m.HasFlag(AnalysisMethod.Frame) ? (Primary: new Plane(Point3d.Origin, vec), Perpendicular: null, InflectionParams: null, MaxCurvatureParams: null, Torsion: null) : null,
                Curvature: null, Discontinuities: null, Topology: null, Proximity: null, Singularities: null, Metrics: null, Domains: null, (Curve: null, Surface: null, Mesh: null, o))),

            _ => ResultFactory.Create<AnalysisResult>(error: AnalysisErrors.Operation.UnsupportedGeometry),
        }).Map(result => (IReadOnlyList<AnalysisResult>)[result]);
}
