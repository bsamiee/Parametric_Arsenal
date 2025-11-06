using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Arsenal.Rhino.Extraction;

/// <summary>Internal extraction algorithms with Rhino SDK geometry processing.</summary>
internal static class ExtractionStrategies {
    private static readonly FrozenDictionary<(ExtractionMethod Method, Type GeometryType), ValidationMode> _validationConfig =
        new Dictionary<(ExtractionMethod, Type), ValidationMode> {
            [(ExtractionMethod.Uniform, typeof(GeometryBase))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(ExtractionMethod.Analytical, typeof(GeometryBase))] = ValidationMode.Standard,
            [(ExtractionMethod.Analytical, typeof(Brep))] = ValidationMode.Standard | ValidationMode.MassProperties,
            [(ExtractionMethod.Analytical, typeof(Curve))] = ValidationMode.Standard | ValidationMode.AreaCentroid,
            [(ExtractionMethod.Analytical, typeof(Surface))] = ValidationMode.Standard | ValidationMode.AreaCentroid,
            [(ExtractionMethod.Extremal, typeof(GeometryBase))] = ValidationMode.BoundingBox,
            [(ExtractionMethod.Quadrant, typeof(GeometryBase))] = ValidationMode.Tolerance,
            [(ExtractionMethod.EdgeMidpoints, typeof(GeometryBase))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Extrusion))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(SubD))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Mesh))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.EdgeMidpoints, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.Greville, typeof(NurbsCurve))] = ValidationMode.Standard,
            [(ExtractionMethod.Greville, typeof(NurbsSurface))] = ValidationMode.Standard,
            [(ExtractionMethod.Inflection, typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(ExtractionMethod.Discontinuities, typeof(Curve))] = ValidationMode.Standard,
            [(ExtractionMethod.FaceCentroids, typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(ExtractionMethod.PositionalExtrema, typeof(Curve))] = ValidationMode.Standard,
            [(ExtractionMethod.PositionalExtrema, typeof(Surface))] = ValidationMode.Standard | ValidationMode.BoundingBox,
            [(ExtractionMethod.PositionalExtrema, typeof(Brep))] = ValidationMode.Standard | ValidationMode.BoundingBox,
            [(ExtractionMethod.PositionalExtrema, typeof(Mesh))] = ValidationMode.Standard | ValidationMode.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Extracts points using specified method with validation and error mapping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Extract(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count = null, double? length = null, bool includeEnds = true, Vector3d? direction = null, Continuity continuity = Continuity.C1_continuous) =>
        (method, count, length, direction) switch {
            (ExtractionMethod.Uniform, int c, _, _) when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidCount),
            (ExtractionMethod.Uniform, _, double l, _) when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidLength),
            (ExtractionMethod.PositionalExtrema, _, _, null or Vector3d { Length: <= 0 }) => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidDirection),
            _ => ResultFactory.Create(value: geometry).Validate(args: [context, _validationConfig.TryGetValue((method, geometry.GetType()), out ValidationMode mode) ? mode : _validationConfig.Where(kv => kv.Key.Method == method && kv.Key.GeometryType.IsAssignableFrom(geometry.GetType())).OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create((a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0)).Select(kv => kv.Value).DefaultIfEmpty(ValidationMode.Standard).First()])
                .Bind(g => ExtractCore(g, method, context, count, length, includeEnds, direction, continuity) switch {
                    Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: (IReadOnlyList<Point3d>)pts.AsReadOnly()),
                    null => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
                    _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
                }),
        };

    /// <summary>Core extraction logic with Rhino SDK geometry type dispatch.</summary>
    [Pure]
    private static Point3d[]? ExtractCore(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count, double? length, bool includeEnds, Vector3d? direction, Continuity continuity) =>
        (method, geometry) switch {
            (ExtractionMethod.Analytical or ExtractionMethod.EdgeMidpoints, Extrusion ext) => ext.ToBrep(splitKinkyFaces: true) switch { Brep brep => ((Func<Brep, Point3d[]?>)(b => { try { return ExtractCore(b, method, context, count, length, includeEnds, direction, continuity); } finally { b.Dispose(); } }))(brep), _ => null },
            (ExtractionMethod.Analytical or ExtractionMethod.EdgeMidpoints, SubD sd) => sd.ToBrep() switch { Brep brep => ((Func<Brep, Point3d[]?>)(b => { try { return ExtractCore(b, method, context, count, length, includeEnds, direction, continuity); } finally { b.Dispose(); } }))(brep), _ => null },
            (ExtractionMethod.Uniform, Curve curve) when count is int c => curve.DivideByCount(c, includeEnds)?.Select(curve.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Curve curve) when length is double l => curve.DivideByLength(l, includeEnds)?.Select(curve.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Surface s) when (count ?? 10, s.Domain(0), s.Domain(1)) is (int d, Interval u, Interval v) => [.. from ui in Enumerable.Range(0, d) from vi in Enumerable.Range(0, d) let up = d == 1 ? 0.5 : includeEnds ? ui / (double)(d - 1) : (ui + 0.5) / d let vp = d == 1 ? 0.5 : includeEnds ? vi / (double)(d - 1) : (vi + 0.5) / d select s.PointAt(u.ParameterAt(up), v.ParameterAt(vp)),],
            (ExtractionMethod.Analytical, GeometryBase a) => [.. (a switch {
                Brep b when VolumeMassProperties.Compute(b)?.Centroid is { IsValid: true } c => [c,],
                Curve c when AreaMassProperties.Compute(c)?.Centroid is { IsValid: true } ct => [ct,],
                Surface s when AreaMassProperties.Compute(s)?.Centroid is { IsValid: true } cs => [cs,],
                Mesh m when m.Vertices.Count > 0 && VolumeMassProperties.Compute(m)?.Centroid is { IsValid: true } cm => [cm,],
                PointCloud pc when pc.Count > 0 && pc.GetPoints() is Point3d[] pts => [pts.Aggregate(Point3d.Origin, (sum, pt) => sum + pt) / pc.Count,],
                _ => Enumerable.Empty<Point3d>(),
            }),
                .. (a switch {
                    NurbsCurve nc => nc.Points.Select(cp => cp.Location),
                    NurbsSurface ns => from i in Enumerable.Range(0, ns.Points.CountU * ns.Points.CountV) select ns.Points.GetControlPoint(i / ns.Points.CountV, i % ns.Points.CountV).Location,
                    Curve c => [c.PointAtStart, c.PointAt(c.Domain.ParameterAt(0.5)), c.PointAtEnd,],
                    Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),],
                    Brep b => b.Vertices.Select(v => v.Location),
                    Mesh m => m.Vertices.ToPoint3dArray(),
                    PointCloud pc => pc.GetPoints(),
                    _ => [],
                }),
            ],
            (ExtractionMethod.Extremal, Curve c) => [c.PointAtStart, c.PointAtEnd,],
            (ExtractionMethod.Extremal, Surface s) when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),],
            (ExtractionMethod.Extremal, GeometryBase g) => g.GetBoundingBox(accurate: true).GetCorners(),
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetCircle(out Circle circ, context.AbsoluteTolerance) => [circ.PointAt(0), circ.PointAt(Math.PI / 2), circ.PointAt(Math.PI), circ.PointAt(3 * Math.PI / 2),],
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetEllipse(out Ellipse e, context.AbsoluteTolerance) => [e.Center + (e.Plane.XAxis * e.Radius1), e.Center + (e.Plane.YAxis * e.Radius2), e.Center - (e.Plane.XAxis * e.Radius1), e.Center - (e.Plane.YAxis * e.Radius2),],
            (ExtractionMethod.Quadrant, Curve c) => c.TryGetPolyline(out Polyline pl) ? [.. pl,] : c.IsLinear(context.AbsoluteTolerance) ? [c.PointAtStart, c.PointAtEnd,] : null,
            (ExtractionMethod.EdgeMidpoints, Brep b) => [.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5)),],
            (ExtractionMethod.EdgeMidpoints, Mesh m) => [.. Enumerable.Range(0, m.TopologyEdges.Count).Select(i => m.TopologyEdges.EdgeLine(i)).Where(ln => ln.IsValid).Select(ln => ln.PointAt(0.5)),],
            (ExtractionMethod.EdgeMidpoints, Curve c) => c.DuplicateSegments() is Curve[] { Length: > 0 } segs ? [.. segs.Select(seg => seg.PointAtNormalizedLength(0.5)),] : c.TryGetPolyline(out Polyline pl) ? [.. pl.GetSegments().Where(ln => ln.IsValid).Select(ln => ln.PointAt(0.5)),] : null,
            (ExtractionMethod.Greville, NurbsCurve nc) => [.. nc.GrevillePoints(),],
            (ExtractionMethod.Greville, Curve c) => c.ToNurbsCurve() switch { NurbsCurve nc => ((Func<NurbsCurve, Point3d[]>)(n => { try { return [.. n.GrevillePoints(),]; } finally { n.Dispose(); } }))(nc), _ => null },
            (ExtractionMethod.Greville, NurbsSurface ns) when ns.Points is NurbsSurfacePointList pts => [.. from u in Enumerable.Range(0, pts.CountU) from v in Enumerable.Range(0, pts.CountV) let gp = pts.GetGrevillePoint(u, v) select ns.PointAt(gp.X, gp.Y),],
            (ExtractionMethod.Greville, Surface s) => s.ToNurbsSurface() switch { NurbsSurface ns when ns.Points is NurbsSurfacePointList pts => ((Func<NurbsSurface, Point3d[]>)(n => { try { return [.. from u in Enumerable.Range(0, pts.CountU) from v in Enumerable.Range(0, pts.CountV) let gp = pts.GetGrevillePoint(u, v) select n.PointAt(gp.X, gp.Y),]; } finally { n.Dispose(); } }))(ns), _ => null },
            (ExtractionMethod.Inflection, NurbsCurve nc) when nc.InflectionPoints() is Point3d[] inflections && inflections.Length > 0 => inflections,
            (ExtractionMethod.Inflection, Curve c) => c.ToNurbsCurve() switch { NurbsCurve nc => ((Func<NurbsCurve, Point3d[]?>)(n => { try { return n.InflectionPoints() is Point3d[] inflections && inflections.Length > 0 ? inflections : null; } finally { n.Dispose(); } }))(nc), _ => null },
            (ExtractionMethod.Discontinuities, Curve c) => ((Func<List<Point3d>>)(() => { List<Point3d> pts = []; double t0 = c.Domain.Min; while (c.GetNextDiscontinuity(continuity, t0, c.Domain.Max, out double t)) { pts.Add(c.PointAt(t)); t0 = t; } return pts; }))() switch { { Count: > 0 } list => [.. list,], _ => null },
            (ExtractionMethod.FaceCentroids, Brep b) => [.. b.Faces.Select(f => f.DuplicateFace(duplicateMeshes: false) switch { Brep dup => ((Func<Brep, Point3d>)(d => { try { return AreaMassProperties.Compute(d)?.Centroid ?? Point3d.Unset; } finally { d.Dispose(); } }))(dup), _ => Point3d.Unset }).Where(p => p != Point3d.Unset),],
            (ExtractionMethod.PositionalExtrema, Curve c) when direction is Vector3d dir && dir.Length > context.AbsoluteTolerance => c.ExtremeParameters(dir)?.Select(c.PointAt).ToArray(),
            (ExtractionMethod.PositionalExtrema, Surface s) when direction is Vector3d dir && dir.Length > context.AbsoluteTolerance && (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => [.. new[] { (u.Min, v.Min), (u.Max, v.Min), (u.Max, v.Max), (u.Min, v.Max) }.Select(uv => (Point: s.PointAt(uv.Item1, uv.Item2), Proj: s.PointAt(uv.Item1, uv.Item2) - Point3d.Origin)).OrderBy(x => x.Proj * dir).Take(1).Concat(new[] { (u.Min, v.Min), (u.Max, v.Min), (u.Max, v.Max), (u.Min, v.Max) }.Select(uv => (Point: s.PointAt(uv.Item1, uv.Item2), Proj: s.PointAt(uv.Item1, uv.Item2) - Point3d.Origin)).OrderByDescending(x => x.Proj * dir).Take(1)).Select(x => x.Point),],
            _ => null,
        };
}
