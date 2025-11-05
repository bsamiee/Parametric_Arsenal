using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense analysis result containing point, derivatives, frame, curvature, and metrics.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Internal result type colocated with strategies")]
internal sealed record AnalysisResult(
    Point3d Point,
    Vector3d[] Derivatives,
    Plane Frame,
    (double? Gaussian, double? Mean, double? Min, double? Max, Vector3d? MinDir, Vector3d? MaxDir, Vector3d? Curve)? Curvature,
    (double? Length, double? Area, double? Volume)? Metrics,
    Interval[]? Domains,
    (Vector3d[]? TangentBasis, Vector3d? Normal, Plane? OrientationFrame)? Orientation,
    (double? CurveParam, (double, double)? SurfaceParams, int? MeshIndex, int DerivativeOrder) EvaluatedParams);

/// <summary>Dense analysis strategy dispatcher following extraction pattern with separate validation and core.</summary>
internal static class AnalysisStrategies {
    /// <summary>Validation configuration mapping geometry types to required validation modes.</summary>
    private static readonly FrozenDictionary<Type, ValidationMode> _validation =
        new Dictionary<Type, ValidationMode> {
            [typeof(Curve)] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [typeof(Surface)] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [typeof(BrepFace)] = ValidationMode.Standard | ValidationMode.Topology | ValidationMode.SurfaceContinuity,
            [typeof(Brep)] = ValidationMode.Standard | ValidationMode.Topology | ValidationMode.MassProperties,
            [typeof(SubD)] = ValidationMode.Standard | ValidationMode.SurfaceContinuity,
            [typeof(Mesh)] = ValidationMode.MeshSpecific,
            [typeof(PointCloud)] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [typeof(Point3d[])] = ValidationMode.None,
            [typeof(Point3d)] = ValidationMode.None,
            [typeof(Vector3d)] = ValidationMode.None,
        }.ToFrozenDictionary();

    /// <summary>Analyzes geometry with validation dispatch and parameter checking.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<AnalysisResult>> Analyze(
        object source, IGeometryContext context,
        double? curveParam, (double, double)? surfaceParams, int? meshIndex,
        int derivativeOrder, bool includeMetrics, bool includeDomains, bool includeOrientation) =>
        (source switch {
            BrepFace face => (typeof(BrepFace), face),
            GeometryBase geom => (geom.GetType(), geom),
            Point3d[] pts => (typeof(Point3d[]), pts),
            Point3d pt => (typeof(Point3d), pt),
            Vector3d vec => (typeof(Vector3d), vec),
            _ => (source.GetType(), source),
        }) switch {
            (Type t, object obj) => ResultFactory.Create(value: obj)
                .Validate(args: _validation.TryGetValue(t, out ValidationMode mode) && mode != ValidationMode.None
                    ? [context, mode] : null)
                .Map(_ => AnalyzeCore(obj, context, curveParam, surfaceParams, meshIndex,
                    derivativeOrder, includeMetrics, includeDomains, includeOrientation))
                .Bind(result => result switch {
                    AnalysisResult r => ResultFactory.Create(value: (IReadOnlyList<AnalysisResult>)[r]),
                    null when obj is Point3d[] pts => ResultFactory.Create(value: (IReadOnlyList<AnalysisResult>)
                        Array.ConvertAll(pts, pt => new AnalysisResult(Point: pt, Derivatives: [], Frame: new Plane(pt, Vector3d.ZAxis),
                            Curvature: null, Metrics: null, Domains: null, Orientation: null,
                            EvaluatedParams: (curveParam, surfaceParams, meshIndex, derivativeOrder)))),
                    _ => ResultFactory.Create<IReadOnlyList<AnalysisResult>>(error: AnalysisErrors.Operation.UnsupportedGeometry),
                }),
        };

    /// <summary>Core analysis logic with ultra-dense switch expressions and inline tuple operations.</summary>
    [Pure]
    private static AnalysisResult? AnalyzeCore(object source, IGeometryContext context, double? cP, (double, double)? sP,
        int? mI, int dO, bool iM, bool iD, bool iO) => source switch {
            Curve c when c.Domain.IncludesParameter(cP ?? c.Domain.ParameterAt(0.5)) && (cP ?? c.Domain.ParameterAt(0.5), c.FrameAt(cP ?? c.Domain.ParameterAt(0.5), out Plane f) ? f : new(c.PointAt(cP ?? c.Domain.ParameterAt(0.5)), Vector3d.ZAxis)) is (var t, var frame) =>
                new(c.PointAt(t), [c.TangentAt(t)], frame, c.CurvatureAt(t).IsValid ? (null, null, c.CurvatureAt(t).Length, null, null, null, c.CurvatureAt(t)) : null,
                    iM ? (c.GetLength(), null, null) : null, iD ? [c.Domain] : null, iO ? ([frame.XAxis, frame.YAxis], frame.ZAxis, frame) : null,
                    (t, sP, mI, dO)),
            Surface s when s.Domain(0).IncludesParameter((sP ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5))).Item1) && s.Domain(1).IncludesParameter((sP ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5))).Item2) && (sP ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5)), s.FrameAt((sP ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5))).Item1, (sP ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5))).Item2, out Plane f) ? f : Plane.WorldXY, s.CurvatureAt((sP ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5))).Item1, (sP ?? (s.Domain(0).ParameterAt(0.5), s.Domain(1).ParameterAt(0.5))).Item2)) is ((var u, var v), var frame, var sc) =>
                new(s.PointAt(u, v), [frame.XAxis, frame.YAxis], frame, (sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1), sc.Direction(0), sc.Direction(1), null),
                    iM ? (null, s is BrepFace bf ? AreaMassProperties.Compute(bf)?.Area : AreaMassProperties.Compute(s)?.Area, s is BrepFace { Brep: Brep b } ? VolumeMassProperties.Compute(b)?.Volume : null) : null,
                    iD ? [s.Domain(0), s.Domain(1)] : null, iO ? ([frame.XAxis, frame.YAxis], frame.ZAxis, frame) : null,
                    (cP, (u, v), mI, dO)),
            BrepFace face => AnalyzeCore(face.UnderlyingSurface(), context, cP, sP, mI, dO, iM, iD, iO) switch {
                AnalysisResult r => r with { Metrics = iM ? (null, AreaMassProperties.Compute(face)?.Area, face.Brep is Brep b ? VolumeMassProperties.Compute(b)?.Volume : null) : null },
                _ => null,
            },
            Brep brep when (mI ?? 0) >= 0 && (mI ?? 0) < brep.Faces.Count => AnalyzeCore(brep.Faces[mI ?? 0], context, cP, sP, mI ?? 0, dO, iM, iD, iO) switch {
                AnalysisResult r => r with { Metrics = iM ? (null, AreaMassProperties.Compute(brep)?.Area, VolumeMassProperties.Compute(brep)?.Volume) : null },
                _ => null,
            },
            SubD subd => subd.ToBrep() switch {
                Brep b when b.Faces.Count > 0 => ((Func<AnalysisResult?>)(() => {
                    try {
                        return AnalyzeCore(b.Faces[0], context, cP, sP, 0, dO, iM, iD, iO) switch {
                            AnalysisResult r => r with { Metrics = iM ? (null, AreaMassProperties.Compute(b)?.Area, VolumeMassProperties.Compute(b)?.Volume) : null },
                            _ => null,
                        };
                    } finally { b.Dispose(); }
                }))(),
                _ => null,
            },
            Mesh m when (mI ?? 0) >= 0 && (mI ?? 0) < m.Vertices.Count && (m.Vertices.Point3dAt(mI ?? 0), m.Normals.Count > (mI ?? 0) ? m.Normals[mI ?? 0] : m.Normals.ComputeNormals() && m.Normals.Count > (mI ?? 0) ? m.Normals[mI ?? 0] : Vector3d.ZAxis) is (var pt, var n) =>
                new(pt, [], new Plane(pt, n.IsValid ? n : Vector3d.ZAxis), Curvature: null,
                    iM ? (null, AreaMassProperties.Compute(m)?.Area, VolumeMassProperties.Compute(m)?.Volume) : null,
                    iD ? [new(0, m.Vertices.Count)] : null, iO ? ([], n.IsValid ? n : null, new Plane(pt, n.IsValid ? n : Vector3d.ZAxis)) : null,
                    (cP, sP, mI ?? 0, dO)),
            PointCloud cloud when cloud.Count > 0 && (cloud.GetPoints(), cloud.GetBoundingBox(accurate: true).Center, Plane.FitPlaneToPoints(cloud.GetPoints(), out Plane fitted) == PlaneFitResult.Success ? fitted : Plane.WorldXY) is (var pts, var center, var plane) =>
                new(Point: center, Derivatives: [], Frame: plane, Curvature: null, Metrics: null, Domains: iD ? [new(0, cloud.Count)] : null, Orientation: iO ? ([plane.XAxis, plane.YAxis], plane.ZAxis, plane) : null,
                    EvaluatedParams: (cP, sP, mI, dO)),
            Point3d pt => new(Point: pt, Derivatives: [], Frame: Plane.WorldXY, Curvature: null, Metrics: null, Domains: null, Orientation: null,
                EvaluatedParams: (cP, sP, mI, dO)),
            Vector3d vec => new(Point: Point3d.Origin, Derivatives: [vec], Frame: new Plane(Point3d.Origin, vec), Curvature: null, Metrics: null, Domains: null, Orientation: iO ? ([vec], null, new Plane(Point3d.Origin, vec)) : null,
                EvaluatedParams: (cP, sP, mI, dO)),
            _ => null,
        };
}
