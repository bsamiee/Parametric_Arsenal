using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

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
        }.ToFrozenDictionary();

    /// <summary>Extracts points using specified method with validation and error mapping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Extract(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count = null, double? length = null, bool includeEnds = true) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context, ((Func<ValidationMode>)(() => {
                for (Type? t = geometry.GetType(); t is not null && t != typeof(object); t = t.BaseType) {
                    if (_validationConfig.TryGetValue((method, t), out ValidationMode m)) {
                        return m;
                    }
                }
                return _validationConfig.GetValueOrDefault((method, typeof(GeometryBase)), ValidationMode.Standard);
            }))(),])
            .Bind(g => ExtractCore(g, method, context, count, length, includeEnds) switch {
                Point3d[] { Length: > 0 } result => ResultFactory.Create(value: (IReadOnlyList<Point3d>)result.AsReadOnly()),
                null => ResultFactory.Create<IReadOnlyList<Point3d>>(error: (method, count, length) switch {
                    (ExtractionMethod.Uniform, not null, _) => ExtractionErrors.Operation.InvalidCount,
                    (ExtractionMethod.Uniform, _, not null) => ExtractionErrors.Operation.InvalidLength,
                    _ => ExtractionErrors.Operation.InvalidMethod,
                }),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
            });

    /// <summary>Core extraction logic with Rhino SDK geometry type dispatch.</summary>
    [Pure]
    private static Point3d[]? ExtractCore(
        GeometryBase geometry, ExtractionMethod method,
        IGeometryContext context, int? count, double? length, bool includeEnds) =>
        (method, geometry) switch {
            (ExtractionMethod.Analytical or ExtractionMethod.EdgeMidpoints, Extrusion extrusion) when extrusion.ToBrep(splitKinkyFaces: true) is Brep brep =>
                ((Func<Brep, Point3d[]?>)(b => { try { return ExtractCore(b, method, context, count, length, includeEnds); } finally { b.Dispose(); } }))(brep),
            (ExtractionMethod.Analytical or ExtractionMethod.EdgeMidpoints, SubD subD) when subD.ToBrep() is Brep brep =>
                ((Func<Brep, Point3d[]?>)(b => { try { return ExtractCore(b, method, context, count, length, includeEnds); } finally { b.Dispose(); } }))(brep),
            (ExtractionMethod.Uniform, Curve curve) when count is int uniformCount =>
                curve.DivideByCount(uniformCount, includeEnds)?.Select(curve.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Curve curve) when length is double uniformLength =>
                curve.DivideByLength(uniformLength, includeEnds)?.Select(curve.PointAt).ToArray(),
            (ExtractionMethod.Uniform, Surface surface) =>
                [.. from uIndex in Enumerable.Range(0, count ?? 10)
                    from vIndex in Enumerable.Range(0, count ?? 10)
                    let density = count ?? 10
                    let uParameter = density switch { 1 => 0.5, _ => includeEnds ? uIndex / (double)(density - 1) : (uIndex + 0.5) / density }
                    let vParameter = density switch { 1 => 0.5, _ => includeEnds ? vIndex / (double)(density - 1) : (vIndex + 0.5) / density }
                    select surface.PointAt(surface.Domain(0).ParameterAt(uParameter), surface.Domain(1).ParameterAt(vParameter)),],
            (ExtractionMethod.Uniform, _) => null,
            (ExtractionMethod.Analytical, GeometryBase analytic) => [..
                (analytic switch {
                    Brep b when VolumeMassProperties.Compute(b)?.Centroid is { IsValid: true } c => [c,],
                    Curve c when AreaMassProperties.Compute(c)?.Centroid is { IsValid: true } ct => [ct,],
                    Surface s when AreaMassProperties.Compute(s)?.Centroid is { IsValid: true } cs => [cs,],
                    Mesh m when m.Vertices.Count > 0 && VolumeMassProperties.Compute(m)?.Centroid is { IsValid: true } cm => [cm,],
                    PointCloud pc when pc.Count > 0 => [pc.GetPoints().Aggregate(Point3d.Origin, (Point3d sum, Point3d pt) => sum + pt) / pc.Count,],
                    _ => Enumerable.Empty<Point3d>(),
                }),
                .. (analytic switch {
                    NurbsCurve nc => nc.Points.Select(cp => cp.Location),
                    NurbsSurface ns => from i in Enumerable.Range(0, ns.Points.CountU * ns.Points.CountV)
                        select ns.Points.GetControlPoint(i / ns.Points.CountV, i % ns.Points.CountV).Location,
                    Curve c => [c.PointAtStart, c.PointAt(c.Domain.ParameterAt(0.5)), c.PointAtEnd,],
                    Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) =>
                        [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),],
                    Brep b => b.Vertices.Select(v => v.Location),
                    Mesh m => m.Vertices.ToPoint3dArray(),
                    PointCloud pc => pc.GetPoints(),
                    _ => [],
                }),],
            (ExtractionMethod.Extremal, Curve c) => [c.PointAtStart, c.PointAtEnd,],
            (ExtractionMethod.Extremal, Surface s) when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) =>
                [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),],
            (ExtractionMethod.Extremal, GeometryBase g) => g.GetBoundingBox(accurate: true).GetCorners(),
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetCircle(out Circle circ, context.AbsoluteTolerance) =>
                [circ.PointAt(0), circ.PointAt(Math.PI / 2), circ.PointAt(Math.PI), circ.PointAt(3 * Math.PI / 2),],
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetEllipse(out Ellipse e, context.AbsoluteTolerance) =>
                [e.Center + (e.Plane.XAxis * e.Radius1), e.Center + (e.Plane.YAxis * e.Radius2),
                    e.Center - (e.Plane.XAxis * e.Radius1), e.Center - (e.Plane.YAxis * e.Radius2),],
            (ExtractionMethod.Quadrant, Curve c) when c.TryGetPolyline(out Polyline pl) => [.. pl,],
            (ExtractionMethod.Quadrant, Curve c) when c.IsLinear(context.AbsoluteTolerance) => [c.PointAtStart, c.PointAtEnd,],
            (ExtractionMethod.Quadrant, _) => null,
            (ExtractionMethod.EdgeMidpoints, Brep b) => [.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5)),],
            (ExtractionMethod.EdgeMidpoints, Mesh m) => [.. Enumerable.Range(0, m.TopologyEdges.Count)
                .Select(i => m.TopologyEdges.EdgeLine(i))
                .Where(ln => ln.IsValid)
                .Select(ln => ln.PointAt(0.5)),],
            (ExtractionMethod.EdgeMidpoints, Curve c) => c.DuplicateSegments() switch {
                Curve[] segs when segs.Length > 0 => [.. segs.Select(seg => seg.PointAtNormalizedLength(0.5)),],
                _ => c.TryGetPolyline(out Polyline pl)
                    ? [.. pl.GetSegments().Where(ln => ln.IsValid).Select(ln => ln.PointAt(0.5)),]
                    : null,
            },
            (ExtractionMethod.EdgeMidpoints, _) => null,
            _ => null,
        };
}
