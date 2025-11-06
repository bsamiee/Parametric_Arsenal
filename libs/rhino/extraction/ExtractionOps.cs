using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Arsenal.Rhino.Extraction;

/// <summary>Core extraction algorithms with polymorphic dispatch via pattern matching.</summary>
internal static class ExtractionOps {
    private static readonly FrozenDictionary<Type, (ValidationMode Standard, Func<GeometryBase, ValidationMode>? Dynamic)> _validationModes =
        new Dictionary<Type, (ValidationMode, Func<GeometryBase, ValidationMode>?)> {
            [typeof(ExtractionConfig.UniformByCount)] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [typeof(ExtractionConfig.UniformByLength)] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [typeof(ExtractionConfig.Extremal)] = (ValidationMode.BoundingBox, null),
            [typeof(ExtractionConfig.Quadrant)] = (ValidationMode.Tolerance, null),
            [typeof(ExtractionConfig.EdgeMidpoints)] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [typeof(ExtractionConfig.Greville)] = (ValidationMode.Standard, null),
            [typeof(ExtractionConfig.Inflection)] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [typeof(ExtractionConfig.Discontinuities)] = (ValidationMode.Standard, null),
            [typeof(ExtractionConfig.FaceCentroids)] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [typeof(ExtractionConfig.Analytical)] = (ValidationMode.Standard, g => g switch {
                Brep => ValidationMode.Standard | ValidationMode.MassProperties,
                Curve => ValidationMode.Standard | ValidationMode.AreaCentroid,
                Surface => ValidationMode.Standard | ValidationMode.AreaCentroid,
                _ => ValidationMode.Standard,
            }),
            [typeof(ExtractionConfig.PositionalExtrema)] = (ValidationMode.Standard, g => g switch {
                Surface or Brep => ValidationMode.Standard | ValidationMode.BoundingBox,
                Mesh => ValidationMode.Standard | ValidationMode.MeshSpecific,
                _ => ValidationMode.Standard,
            }),
        }.ToFrozenDictionary();

    /// <summary>Executes extraction operation with geometry and config dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Execute(GeometryBase geometry, ExtractionConfig config, IGeometryContext context) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context, _validationModes.TryGetValue(config.GetType(), out (ValidationMode Standard, Func<GeometryBase, ValidationMode>? Dynamic) modes)
                ? modes.Dynamic?.Invoke(geometry) ?? modes.Standard
                : ValidationMode.Standard,])
            .Bind(g => (config, g) switch {
                (ExtractionConfig.UniformByCount { Count: <= 0 }, _) => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidCount),
                (ExtractionConfig.UniformByLength { Length: <= 0 }, _) => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidLength),
                (ExtractionConfig.PositionalExtrema { Direction.Length: <= 0 }, _) => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidDirection),
                (ExtractionConfig.UniformByCount c, Curve curve) => ExtractUniformByCount(curve, c.Count, c.IncludeEnds),
                (ExtractionConfig.UniformByCount c, Surface surface) => ExtractUniformGrid(surface, c.Count, c.IncludeEnds),
                (ExtractionConfig.UniformByLength l, Curve curve) => ExtractUniformByLength(curve, l.Length, l.IncludeEnds),
                (ExtractionConfig.Analytical, GeometryBase gb) => ExtractAnalytical(gb),
                (ExtractionConfig.Extremal, Curve c) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c.PointAtStart, c.PointAtEnd,]),
                (ExtractionConfig.Extremal, Surface s) when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),]),
                (ExtractionConfig.Extremal, GeometryBase gb) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)gb.GetBoundingBox(accurate: true).GetCorners()),
                (ExtractionConfig.Quadrant, Curve c) => ExtractQuadrant(c, context),
                (ExtractionConfig.EdgeMidpoints, Extrusion or SubD or Brep or Mesh or Curve) => ExtractEdgeMidpoints(g),
                (ExtractionConfig.Greville, NurbsCurve or Curve or NurbsSurface or Surface) => ExtractGreville(g),
                (ExtractionConfig.Inflection, NurbsCurve or Curve) => ExtractInflection(g),
                (ExtractionConfig.Discontinuities d, Curve c) => ExtractDiscontinuities(c, d.Continuity),
                (ExtractionConfig.FaceCentroids, Brep b) => ExtractFaceCentroids(b),
                (ExtractionConfig.PositionalExtrema p, Curve or Surface) => ExtractPositionalExtrema(g, p.Direction, context),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
            });

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractUniformByCount(Curve curve, int count, bool includeEnds) =>
        curve.DivideByCount(count, includeEnds) switch {
            double[] { Length: > 0 } pars => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(curve.PointAt)]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractUniformGrid(Surface surface, int count, bool includeEnds) =>
        (surface.Domain(0), surface.Domain(1)) switch {
            (Interval u, Interval v) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from ui in Enumerable.Range(0, count) from vi in Enumerable.Range(0, count) let up = count == 1 ? 0.5 : includeEnds ? ui / (double)(count - 1) : (ui + 0.5) / count let vp = count == 1 ? 0.5 : includeEnds ? vi / (double)(count - 1) : (vi + 0.5) / count select surface.PointAt(u.ParameterAt(up), v.ParameterAt(vp))]),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractUniformByLength(Curve curve, double length, bool includeEnds) =>
        curve.DivideByLength(length, includeEnds) switch {
            double[] { Length: > 0 } pars => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(curve.PointAt)]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractQuadrant(Curve curve, IGeometryContext context) => curve switch {
        _ when curve.TryGetCircle(out Circle circ, context.AbsoluteTolerance) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[circ.PointAt(0), circ.PointAt(Math.PI / 2), circ.PointAt(Math.PI), circ.PointAt(3 * Math.PI / 2),]),
        _ when curve.TryGetEllipse(out Ellipse e, context.AbsoluteTolerance) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[e.Center + (e.Plane.XAxis * e.Radius1), e.Center + (e.Plane.YAxis * e.Radius2), e.Center - (e.Plane.XAxis * e.Radius1), e.Center - (e.Plane.YAxis * e.Radius2),]),
        _ when curve.TryGetPolyline(out Polyline pl) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pl]),
        _ when curve.IsLinear(context.AbsoluteTolerance) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[curve.PointAtStart, curve.PointAtEnd,]),
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractEdgeMidpoints(GeometryBase geometry) => geometry switch {
        Extrusion ext => ext.ToBrep(splitKinkyFaces: true) switch { Brep b => ((Func<Result<IReadOnlyList<Point3d>>>)(() => { try { return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))]); } finally { b.Dispose(); } }))(), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        SubD sd => sd.ToBrep() switch { Brep b => ((Func<Result<IReadOnlyList<Point3d>>>)(() => { try { return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))]); } finally { b.Dispose(); } }))(), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        Brep b => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))]),
        Mesh m => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. Enumerable.Range(0, m.TopologyEdges.Count).Select(i => m.TopologyEdges.EdgeLine(i)).Where(ln => ln.IsValid).Select(ln => ln.PointAt(0.5))]),
        Curve c => c.DuplicateSegments() switch { Curve[] { Length: > 0 } segs => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. segs.Select(seg => seg.PointAtNormalizedLength(0.5))]), _ => c.TryGetPolyline(out Polyline pl) ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pl.GetSegments().Where(ln => ln.IsValid).Select(ln => ln.PointAt(0.5))]) : ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractGreville(GeometryBase geometry) => geometry switch {
        NurbsCurve nc => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. nc.GrevillePoints()]),
        Curve c => c.ToNurbsCurve() switch { NurbsCurve nc => ((Func<Result<IReadOnlyList<Point3d>>>)(() => { try { return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. nc.GrevillePoints()]); } finally { nc.Dispose(); } }))(), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        NurbsSurface ns when ns.Points is NurbsSurfacePointList pts => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from u in Enumerable.Range(0, pts.CountU) from v in Enumerable.Range(0, pts.CountV) let gp = pts.GetGrevillePoint(u, v) select ns.PointAt(gp.X, gp.Y)]),
        Surface s => s.ToNurbsSurface() switch { NurbsSurface ns when ns.Points is NurbsSurfacePointList pts => ((Func<Result<IReadOnlyList<Point3d>>>)(() => { try { return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from u in Enumerable.Range(0, pts.CountU) from v in Enumerable.Range(0, pts.CountV) let gp = pts.GetGrevillePoint(u, v) select ns.PointAt(gp.X, gp.Y)]); } finally { ns.Dispose(); } }))(), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractInflection(GeometryBase geometry) => geometry switch {
        NurbsCurve nc => nc.InflectionPoints() switch { Point3d[] { Length: > 0 } inflections => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. inflections]), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters), },
        Curve c => c.ToNurbsCurve() switch { NurbsCurve nc => ((Func<Result<IReadOnlyList<Point3d>>>)(() => { try { return nc.InflectionPoints() switch { Point3d[] { Length: > 0 } inflections => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. inflections]), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters), }; } finally { nc.Dispose(); } }))(), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractFaceCentroids(Brep brep) =>
        ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. brep.Faces.Select(f => f.DuplicateFace(duplicateMeshes: false) switch {
            Brep dup => ((Func<Point3d>)(() => {
                try {
                    return AreaMassProperties.Compute(dup) switch {
                        { Centroid.IsValid: true } mp => ((Func<Point3d>)(() => { try { return mp.Centroid; } finally { mp.Dispose(); } }))(),
                        _ => Point3d.Unset,
                    };
                } finally {
                    dup.Dispose();
                }
            }))(),
            _ => Point3d.Unset,
        }).Where(p => p.IsValid),]);

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractPositionalExtrema(GeometryBase geometry, Vector3d direction, IGeometryContext context) =>
        direction.Length <= context.AbsoluteTolerance ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidDirection) : geometry switch {
            Curve c => c.ExtremeParameters(direction) switch { double[] { Length: > 0 } pars => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(c.PointAt)]), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters), },
            Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. new[] { (u.Min, v.Min), (u.Max, v.Min), (u.Max, v.Max), (u.Min, v.Max), }.Select(uv => (Point: s.PointAt(uv.Item1, uv.Item2), Proj: s.PointAt(uv.Item1, uv.Item2) - Point3d.Origin)).OrderBy(x => x.Proj * direction).Take(1).Concat(new[] { (u.Min, v.Min), (u.Max, v.Min), (u.Max, v.Max), (u.Min, v.Max), }.Select(uv => (Point: s.PointAt(uv.Item1, uv.Item2), Proj: s.PointAt(uv.Item1, uv.Item2) - Point3d.Origin)).OrderByDescending(x => x.Proj * direction).Take(1)).Select(x => x.Point)]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
        };
    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractAnalytical(GeometryBase geometry) {
        IEnumerable<Point3d> centroids = geometry switch {
            Brep b when VolumeMassProperties.Compute(b) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { try { return mp.Centroid; } finally { mp.Dispose(); } }))(),],
            Curve c when AreaMassProperties.Compute(c) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { try { return mp.Centroid; } finally { mp.Dispose(); } }))(),],
            Surface s when AreaMassProperties.Compute(s) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { try { return mp.Centroid; } finally { mp.Dispose(); } }))(),],
            Mesh m when m.Vertices.Count > 0 && VolumeMassProperties.Compute(m) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { try { return mp.Centroid; } finally { mp.Dispose(); } }))(),],
            PointCloud pc when pc.Count > 0 && pc.GetPoints() is Point3d[] pts => [pts.Aggregate(Point3d.Origin, (sum, pt) => sum + pt) / pc.Count,],
            _ => [],
        };
        IEnumerable<Point3d> features = geometry switch {
            NurbsCurve nc => nc.Points.Select(cp => cp.Location),
            NurbsSurface ns => from i in Enumerable.Range(0, ns.Points.CountU * ns.Points.CountV) select ns.Points.GetControlPoint(i / ns.Points.CountV, i % ns.Points.CountV).Location,
            Curve c => [c.PointAtStart, c.PointAt(c.Domain.ParameterAt(0.5)), c.PointAtEnd,],
            Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),],
            Brep b => b.Vertices.Select(v => v.Location),
            Mesh m => m.Vertices.ToPoint3dArray(),
            PointCloud pc => pc.GetPoints(),
            _ => [],
        };
        return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. centroids, .. features,]);
    }
    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractDiscontinuities(Curve curve, Continuity continuity) {
        List<Point3d> points = [];
        double t0 = curve.Domain.Min;
        while (curve.GetNextDiscontinuity(continuity, t0, curve.Domain.Max, out double t)) {
            points.Add(curve.PointAt(t));
            t0 = t;
        }
        return points.Count > 0 ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. points]) : ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters);
    }
}
