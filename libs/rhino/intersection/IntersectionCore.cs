using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using RhinoIntersect = Rhino.Geometry.Intersect.Intersection;

namespace Arsenal.Rhino.Intersection;

/// <summary>RhinoCommon intersection dispatch with 40+ type combinations and result normalization.</summary>
internal static class IntersectionCore {
    private enum ConverterType : byte {
        CurveIntersections = 1,
        BrepIntersection = 2,
        CountedPoints = 3,
        CountedPointsWithParams = 4,
        Polylines = 5,
    }

    private static readonly FrozenDictionary<ConverterType, Func<object, Intersect.IntersectionOutput>> _converters =
        new Dictionary<ConverterType, Func<object, Intersect.IntersectionOutput>> {
            [ConverterType.CurveIntersections] = data => data switch {
                ((CurveIntersections r, Curve? overlap), _) when r.Count > 0 => new Intersect.IntersectionOutput(
                    [.. from e in r select e.PointA],
                    overlap is not null ? [.. from e in r where e.IsOverlap let c = overlap.Trim(e.OverlapA) where c is not null select c] : [],
                    [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], []),
                _ => Intersect.IntersectionOutput.Empty,
            },
            [ConverterType.BrepIntersection] = data => data switch {
                ((true, Curve[] { Length: > 0 } c, Point3d[] { Length: > 0 } p), _) => new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], []),
                ((true, Curve[] { Length: > 0 } c, _), _) => new Intersect.IntersectionOutput([], [.. c], [], [], [], []),
                ((true, _, Point3d[] { Length: > 0 } p), _) => new Intersect.IntersectionOutput([.. p], [], [], [], [], []),
                _ => Intersect.IntersectionOutput.Empty,
            },
            [ConverterType.CountedPoints] = data => data switch {
                ((> 1, Point3d p1, Point3d p2), double tol) when p1.DistanceTo(p2) > tol => new Intersect.IntersectionOutput([p1, p2], [], [], [], [], []),
                ((> 0, Point3d p1, _), _) => new Intersect.IntersectionOutput([p1], [], [], [], [], []),
                _ => Intersect.IntersectionOutput.Empty,
            },
            [ConverterType.CountedPointsWithParams] = data => data switch {
                ((> 1, Point3d p1, double t1, Point3d p2, double t2), double tol) when p1.DistanceTo(p2) > tol => new Intersect.IntersectionOutput([p1, p2], [], [t1, t2], [], [], []),
                ((> 0, Point3d p1, double t1, _, _), _) => new Intersect.IntersectionOutput([p1], [], [t1], [], [], []),
                _ => Intersect.IntersectionOutput.Empty,
            },
            [ConverterType.Polylines] = data => data switch {
                (Polyline[] { Length: > 0 } s, _) => new Intersect.IntersectionOutput([.. from pl in s from pt in pl select pt], [], [], [], [], [.. s]),
                _ => Intersect.IntersectionOutput.Empty,
            },
        }.ToFrozenDictionary();

    [Pure]
    internal static Result<Intersect.IntersectionOutput> ExecutePair<T1, T2>(T1 a, T2 b, IGeometryContext ctx, Intersect.IntersectionOptions opts) where T1 : notnull where T2 : notnull {
        double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;

        static Result<Intersect.IntersectionOutput> convertCurveIntersections(CurveIntersections? results, Curve? overlapSource) {
            using (results) {
                return results is { Count: > 0 }
                    ? ResultFactory.Create(value: _converters[ConverterType.CurveIntersections](((results, overlapSource), 0)))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty);
            }
        }

        return (a, b, opts) switch {
            (Point3d[], Brep[], { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToBrepsEx(breps, pts, dir, ctx.AbsoluteTolerance, out int[] ids1)], [], [], [], [.. ids1], [])),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, ctx.AbsoluteTolerance)], [], [], [], [], [])),
            (Point3d[], Mesh[], { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToMeshesEx(meshes, pts, dir, ctx.AbsoluteTolerance, out int[] ids2)], [], [], [], [.. ids2], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, ctx.AbsoluteTolerance)], [], [], [], [], [])),
            (Ray3d ray, GeometryBase[], { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance || hits <= 0 =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: ray.Direction.Length <= RhinoMath.ZeroTolerance ? E.Geometry.InvalidRay : E.Geometry.InvalidMaxHits),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.RayShoot(ray, geoms, hits)], [], [], [], [], [])),
            (Curve ca, Curve cb, _) when ReferenceEquals(ca, cb) =>
                convertCurveIntersections(RhinoIntersect.CurveSelf(ca, tolerance), overlapSource: null),
            (Curve ca, Curve cb, _) =>
                convertCurveIntersections(RhinoIntersect.CurveCurve(ca, cb, tolerance, tolerance), ca),
            (Curve ca, BrepFace bf, _) =>
                ResultFactory.Create(value: _converters[ConverterType.BrepIntersection](((RhinoIntersect.CurveBrepFace(ca, bf, tolerance, out Curve[] c2, out Point3d[] p2), c2, p2), 0))),
            (Curve ca, Surface sb, _) =>
                convertCurveIntersections(RhinoIntersect.CurveSurface(ca, sb, tolerance, overlapTolerance: tolerance), overlapSource: null),
            (Curve ca, Plane pb, _) =>
                convertCurveIntersections(RhinoIntersect.CurvePlane(ca, pb, tolerance), overlapSource: null),
            (Curve ca, Line lb, _) =>
                convertCurveIntersections(RhinoIntersect.CurveLine(ca, lb, tolerance, overlapTolerance: tolerance), overlapSource: null),
            (Curve ca, Brep bb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.BrepIntersection](((RhinoIntersect.CurveBrep(ca, bb, tolerance, out Curve[] c1, out Point3d[] p1), c1, p1), 0))),
            (Brep ba, Brep bb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.BrepIntersection](((RhinoIntersect.BrepBrep(ba, bb, tolerance, out Curve[] c3, out Point3d[] p3), c3, p3), 0))),
            (Brep ba, Plane pb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.BrepIntersection](((RhinoIntersect.BrepPlane(ba, pb, tolerance, out Curve[] c4, out Point3d[] p4), c4, p4), 0))),
            (Brep ba, Surface sb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.BrepIntersection](((RhinoIntersect.BrepSurface(ba, sb, tolerance, out Curve[] c5, out Point3d[] p5), c5, p5), 0))),
            (Surface sa, Surface sb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.BrepIntersection](((RhinoIntersect.SurfaceSurface(sa, sb, tolerance, out Curve[] c6, out Point3d[] p6), c6, p6), 0))),
            (Mesh ma, Mesh mb, _) when !opts.Sorted =>
                RhinoIntersect.MeshMeshFast(ma, mb) switch {
                    Line[] { Length: > 0 } lines => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from l in lines select l.From, .. from l in lines select l.To], [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Mesh mb, { Sorted: true }) =>
                RhinoIntersect.MeshMeshAccurate(ma, mb, tolerance) switch {
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    Polyline[] sections => ResultFactory.Create(value: _converters[ConverterType.Polylines]((sections, 0))),
                },
            (Mesh ma, Ray3d rb, _) =>
                RhinoIntersect.MeshRay(ma, rb) switch {
                    double d when d >= 0d => ResultFactory.Create(value: new Intersect.IntersectionOutput([rb.PointAt(d)], [], [d], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Plane pb, _) =>
                RhinoIntersect.MeshPlane(ma, pb) switch {
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    Polyline[] sections => ResultFactory.Create(value: _converters[ConverterType.Polylines]((sections, 0))),
                },
            (Mesh ma, Line lb, _) when !opts.Sorted =>
                RhinoIntersect.MeshLine(ma, lb) switch {
                    Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. pts], [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Line lb, { Sorted: true }) =>
                RhinoIntersect.MeshLineSorted(ma, lb, out int[] ids3) switch {
                    Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. pts], [], [], [], [.. ids3], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: false }) =>
                RhinoIntersect.MeshPolyline(ma, pc, out int[] ids4) switch {
                    Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. pts], [], [], [], [.. ids4], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: true }) =>
                RhinoIntersect.MeshPolylineSorted(ma, pc, out int[] ids5) switch {
                    Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. pts], [], [], [], [.. ids5], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Line la, Line lb, _) =>
                RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tolerance, finiteSegments: false)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([la.PointAt(pa)], [], [pa], [pb], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Line la, BoundingBox boxb, _) =>
                RhinoIntersect.LineBox(la, boxb, tolerance, out Interval interval)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([la.PointAt(interval.Min), la.PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Line la, Plane pb, _) =>
                RhinoIntersect.LinePlane(la, pb, out double param)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([la.PointAt(param)], [], [param], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Line la, Sphere sb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.CountedPoints]((((int)RhinoIntersect.LineSphere(la, sb, out Point3d ps1, out Point3d ps2), ps1, ps2), tolerance))),
            (Line la, Cylinder cylb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.CountedPoints]((((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d pc1, out Point3d pc2), pc1, pc2), tolerance))),
            (Line la, Circle cb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.CountedPointsWithParams]((((int)RhinoIntersect.LineCircle(la, cb, out double lct1, out Point3d lcp1, out double lct2, out Point3d lcp2), lcp1, lct1, lcp2, lct2), tolerance))),
            (Plane pa, Plane pb, _) =>
                RhinoIntersect.PlanePlane(pa, pb, out Line line) switch {
                    true => ((Func<Result<Intersect.IntersectionOutput>>)(() => { using LineCurve lc = new(line); return ResultFactory.Create(value: new Intersect.IntersectionOutput([], [lc.ToNurbsCurve()], [], [], [], [])); }))(),
                    false => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (ValueTuple<Plane, Plane> planes, Plane p3, _) =>
                RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3, out Point3d point)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([point], [], [], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Plane pa, Circle cb, _) =>
                RhinoIntersect.PlaneCircle(pa, cb, out double pct1, out double pct2) switch {
                    PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput([cb.PointAt(pct1)], [], [], [pct1], [], [])),
                    PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput([cb.PointAt(pct1), cb.PointAt(pct2)], [], [], [pct1, pct2], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, Sphere sb, _) =>
                ((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle psc)) switch {
                    1 => ((Func<Result<Intersect.IntersectionOutput>>)(() => { using ArcCurve ac = new(psc); return ResultFactory.Create(value: new Intersect.IntersectionOutput([], [ac.ToNurbsCurve()], [], [], [], [])); }))(),
                    2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([psc.Center], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, BoundingBox boxb, _) =>
                (RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly), poly?.Count > 0) switch {
                    (true, true) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pt in poly select pt], [], [], [], [], [poly])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Sphere sa, Sphere sb, _) =>
                ((int)RhinoIntersect.SphereSphere(sa, sb, out Circle ssc)) switch {
                    1 => ((Func<Result<Intersect.IntersectionOutput>>)(() => { using ArcCurve ac = new(ssc); return ResultFactory.Create(value: new Intersect.IntersectionOutput([], [ac.ToNurbsCurve()], [], [], [], [])); }))(),
                    2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([ssc.Center], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Circle ca, Circle cb, _) =>
                ResultFactory.Create(value: _converters[ConverterType.CountedPoints]((((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d ccp1, out Point3d ccp2), ccp1, ccp2), tolerance))),
            (Arc aa, Arc ab, _) =>
                ResultFactory.Create(value: _converters[ConverterType.CountedPoints]((((int)RhinoIntersect.ArcArc(aa, ab, out Point3d aap1, out Point3d aap2), aap1, aap2), tolerance))),
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.UnsupportedIntersection),
        };
    }
}
