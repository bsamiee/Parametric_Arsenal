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
    /// <summary>Executes extraction operation with geometry and config dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Execute(GeometryBase geometry, ExtractionConfig config, IGeometryContext context) =>
        ResultFactory.Create(value: geometry).Validate(args: [context, config switch {
            ExtractionConfig.Analytical => ExtractionConfig.Analytical.GetValidationMode(geometry),
            ExtractionConfig.PositionalExtrema => ExtractionConfig.PositionalExtrema.GetValidationMode(geometry),
            ExtractionConfig.UniformByCount => ExtractionConfig.UniformByCount.ValidationMode,
            ExtractionConfig.UniformByLength => ExtractionConfig.UniformByLength.ValidationMode,
            ExtractionConfig.Extremal => ExtractionConfig.Extremal.ValidationMode,
            ExtractionConfig.Quadrant => ExtractionConfig.Quadrant.ValidationMode,
            ExtractionConfig.EdgeMidpoints => ExtractionConfig.EdgeMidpoints.ValidationMode,
            ExtractionConfig.Greville => ExtractionConfig.Greville.ValidationMode,
            ExtractionConfig.Inflection => ExtractionConfig.Inflection.ValidationMode,
            ExtractionConfig.Discontinuities => ExtractionConfig.Discontinuities.ValidationMode,
            ExtractionConfig.FaceCentroids => ExtractionConfig.FaceCentroids.ValidationMode,
            _ => ValidationMode.Standard,
        },]).Bind(g => config switch {
            ExtractionConfig.UniformByCount c => ExtractUniform(g, c),
            ExtractionConfig.UniformByLength l => ExtractUniform(g, l),
            ExtractionConfig.Analytical => ExtractAnalytical(g),
            ExtractionConfig.Extremal => ExtractExtremal(g),
            ExtractionConfig.Quadrant => ExtractQuadrant(g, context),
            ExtractionConfig.EdgeMidpoints => ExtractEdgeMidpoints(g),
            ExtractionConfig.Greville => ExtractGreville(g),
            ExtractionConfig.Inflection => ExtractInflection(g),
            ExtractionConfig.Discontinuities d => ExtractDiscontinuities((Curve)g, d.Continuity),
            ExtractionConfig.FaceCentroids => ExtractFaceCentroids((Brep)g),
            ExtractionConfig.PositionalExtrema p => ExtractPositionalExtrema(g, p.Direction, context),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
        });

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractUniform(GeometryBase geometry, ExtractionConfig.UniformByCount config) =>
        config.Count <= 0 ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidCount) : geometry switch {
            Curve c => c.DivideByCount(config.Count, config.IncludeEnds) switch {
                double[] { Length: > 0 } pars => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(c.PointAt)]),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
            },
            Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from ui in Enumerable.Range(0, config.Count) from vi in Enumerable.Range(0, config.Count) let up = config.Count == 1 ? 0.5 : config.IncludeEnds ? ui / (double)(config.Count - 1) : (ui + 0.5) / config.Count let vp = config.Count == 1 ? 0.5 : config.IncludeEnds ? vi / (double)(config.Count - 1) : (vi + 0.5) / config.Count select s.PointAt(u.ParameterAt(up), v.ParameterAt(vp))]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
        };
    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractUniform(GeometryBase geometry, ExtractionConfig.UniformByLength config) =>
        config.Length <= 0 ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidLength) : geometry switch {
            Curve c => c.DivideByLength(config.Length, config.IncludeEnds) switch {
                double[] { Length: > 0 } pars => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(c.PointAt)]),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters),
            },
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractExtremal(GeometryBase geometry) => geometry switch {
        Curve c => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c.PointAtStart, c.PointAtEnd,]),
        Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),]),
        _ => ResultFactory.Create(value: (IReadOnlyList<Point3d>)geometry.GetBoundingBox(accurate: true).GetCorners()),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractQuadrant(GeometryBase geometry, IGeometryContext context) => geometry switch {
        Curve c when c.TryGetCircle(out Circle circ, context.AbsoluteTolerance) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[circ.PointAt(0), circ.PointAt(Math.PI / 2), circ.PointAt(Math.PI), circ.PointAt(3 * Math.PI / 2),]),
        Curve c when c.TryGetEllipse(out Ellipse e, context.AbsoluteTolerance) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[e.Center + (e.Plane.XAxis * e.Radius1), e.Center + (e.Plane.YAxis * e.Radius2), e.Center - (e.Plane.XAxis * e.Radius1), e.Center - (e.Plane.YAxis * e.Radius2),]),
        Curve c => c.TryGetPolyline(out Polyline pl) ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pl]) : c.IsLinear(context.AbsoluteTolerance) ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c.PointAtStart, c.PointAtEnd,]) : ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractEdgeMidpoints(GeometryBase geometry) => geometry switch {
        Extrusion ext => ext.ToBrep(splitKinkyFaces: true) switch { Brep b => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))]).Map(pts => { b.Dispose(); return pts; }), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        SubD sd => sd.ToBrep() switch { Brep b => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))]).Map(pts => { b.Dispose(); return pts; }), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        Brep b => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))]),
        Mesh m => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. Enumerable.Range(0, m.TopologyEdges.Count).Select(i => m.TopologyEdges.EdgeLine(i)).Where(ln => ln.IsValid).Select(ln => ln.PointAt(0.5))]),
        Curve c => c.DuplicateSegments() switch { Curve[] { Length: > 0 } segs => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. segs.Select(seg => seg.PointAtNormalizedLength(0.5))]), _ => c.TryGetPolyline(out Polyline pl) ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pl.GetSegments().Where(ln => ln.IsValid).Select(ln => ln.PointAt(0.5))]) : ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractGreville(GeometryBase geometry) => geometry switch {
        NurbsCurve nc => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. nc.GrevillePoints()]),
        Curve c => c.ToNurbsCurve() switch { NurbsCurve nc => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. nc.GrevillePoints()]).Map(pts => { nc.Dispose(); return pts; }), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        NurbsSurface ns when ns.Points is NurbsSurfacePointList pts => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from u in Enumerable.Range(0, pts.CountU) from v in Enumerable.Range(0, pts.CountV) let gp = pts.GetGrevillePoint(u, v) select ns.PointAt(gp.X, gp.Y)]),
        Surface s => s.ToNurbsSurface() switch { NurbsSurface ns when ns.Points is NurbsSurfacePointList pts => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from u in Enumerable.Range(0, pts.CountU) from v in Enumerable.Range(0, pts.CountV) let gp = pts.GetGrevillePoint(u, v) select ns.PointAt(gp.X, gp.Y)]).Map(p => { ns.Dispose(); return p; }), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractInflection(GeometryBase geometry) => geometry switch {
        NurbsCurve nc => nc.InflectionPoints() switch { Point3d[] { Length: > 0 } inflections => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. inflections]), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters), },
        Curve c => c.ToNurbsCurve() switch { NurbsCurve nc => (nc.InflectionPoints() switch { Point3d[] { Length: > 0 } inflections => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. inflections]), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InsufficientParameters), }).Map(pts => { nc.Dispose(); return pts; }), _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod), },
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ExtractionErrors.Operation.InvalidMethod),
    };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractFaceCentroids(Brep brep) =>
        ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. brep.Faces.Select(f => f.DuplicateFace(duplicateMeshes: false) switch {
            Brep dup => AreaMassProperties.Compute(dup) switch {
                { Centroid.IsValid: true } mp => ((Func<Point3d>)(() => { Point3d pt = mp.Centroid; mp.Dispose(); dup.Dispose(); return pt; }))(),
                _ => ((Func<Point3d>)(() => { dup.Dispose(); return Point3d.Unset; }))(),
            },
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
            Brep b when VolumeMassProperties.Compute(b) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { Point3d pt = mp.Centroid; mp.Dispose(); return pt; }))(),],
            Curve c when AreaMassProperties.Compute(c) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { Point3d pt = mp.Centroid; mp.Dispose(); return pt; }))(),],
            Surface s when AreaMassProperties.Compute(s) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { Point3d pt = mp.Centroid; mp.Dispose(); return pt; }))(),],
            Mesh m when m.Vertices.Count > 0 && VolumeMassProperties.Compute(m) is { Centroid.IsValid: true } mp => [((Func<Point3d>)(() => { Point3d pt = mp.Centroid; mp.Dispose(); return pt; }))(),],
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
