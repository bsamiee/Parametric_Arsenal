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
    [Pure]
    internal static Result<Intersect.IntersectionOutput> ExecutePair<T1, T2>(T1 a, T2 b, IGeometryContext ctx, Intersect.IntersectionOptions opts) where T1 : notnull where T2 : notnull {
        double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;

        return (a, b, opts) switch {
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: bool withIdx }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToBrepsEx(breps, pts, dir, ctx.AbsoluteTolerance, out int[] brepIds)],
                    [], [], [], brepIds.Length > 0 ? [.. brepIds] : [], [])),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, ctx.AbsoluteTolerance)], [], [], [], [], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: bool withIdx }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToMeshesEx(meshes, pts, dir, ctx.AbsoluteTolerance, out int[] meshIds)],
                    [], [], [], meshIds.Length > 0 ? [.. meshIds] : [], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, ctx.AbsoluteTolerance)], [], [], [], [], [])),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidRay),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when hits <= 0 =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.RayShoot(ray, geoms, hits)], [], [], [], [], [])),
            (Curve ca, Curve cb, _) when ReferenceEquals(ca, cb) =>
                (RhinoIntersect.CurveSelf(ca, tolerance)) switch {
                    null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                    { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Curve ca, Curve cb, _) =>
                (RhinoIntersect.CurveCurve(ca, cb, tolerance, tolerance), ca) switch {
                    (null, _) => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                    ( { Count: > 0 } r, Curve overlap) => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA],
                        overlap is not null ? [.. from e in r where e.IsOverlap let c = overlap.Trim(e.OverlapA) where c is not null select c] : [],
                        [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Curve ca, BrepFace bf, _) =>
                (RhinoIntersect.CurveBrepFace(ca, bf, tolerance, out Curve[] curves1, out Point3d[] points1), curves1, points1) switch {
                    (true, { Length: > 0 } c, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])),
                    (true, { Length: > 0 } c, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])),
                    (true, _, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Curve ca, Surface sb, _) =>
                RhinoIntersect.CurveSurface(ca, sb, tolerance, overlapTolerance: tolerance) switch {
                    null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                    { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Curve ca, Plane pb, _) =>
                RhinoIntersect.CurvePlane(ca, pb, tolerance) switch {
                    null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                    { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Curve ca, Line lb, _) =>
                RhinoIntersect.CurveLine(ca, lb, tolerance, overlapTolerance: tolerance) switch {
                    null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                    { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Curve ca, Brep bb, _) =>
                (RhinoIntersect.CurveBrep(ca, bb, tolerance, out Curve[] curves2, out Point3d[] points2), curves2, points2) switch {
                    (true, { Length: > 0 } c, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])),
                    (true, { Length: > 0 } c, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])),
                    (true, _, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Brep ba, Brep bb, _) =>
                (RhinoIntersect.BrepBrep(ba, bb, tolerance, out Curve[] curves3, out Point3d[] points3), curves3, points3) switch {
                    (true, { Length: > 0 } c, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])),
                    (true, { Length: > 0 } c, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])),
                    (true, _, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Brep ba, Plane pb, _) =>
                (RhinoIntersect.BrepPlane(ba, pb, tolerance, out Curve[] curves4, out Point3d[] points4), curves4, points4) switch {
                    (true, { Length: > 0 } c, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])),
                    (true, { Length: > 0 } c, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])),
                    (true, _, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Brep ba, Surface sb, _) =>
                (RhinoIntersect.BrepSurface(ba, sb, tolerance, out Curve[] curves5, out Point3d[] points5), curves5, points5) switch {
                    (true, { Length: > 0 } c, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])),
                    (true, { Length: > 0 } c, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])),
                    (true, _, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Surface sa, Surface sb, _) =>
                (RhinoIntersect.SurfaceSurface(sa, sb, tolerance, out Curve[] curves6, out Point3d[] points6), curves6, points6) switch {
                    (true, { Length: > 0 } c, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])),
                    (true, { Length: > 0 } c, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])),
                    (true, _, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Mesh mb, _) when !opts.Sorted =>
                RhinoIntersect.MeshMeshFast(ma, mb) switch {
                    Line[] { Length: > 0 } lines => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from l in lines select l.From, .. from l in lines select l.To], [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Mesh mb, { Sorted: true }) =>
                RhinoIntersect.MeshMeshAccurate(ma, mb, tolerance) switch {
                    { Length: > 0 } sections => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from pl in sections from pt in pl select pt], [], [], [], [], [.. sections])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Ray3d rb, _) =>
                RhinoIntersect.MeshRay(ma, rb) switch {
                    double d when d >= 0d => ResultFactory.Create(value: new Intersect.IntersectionOutput([rb.PointAt(d)], [], [d], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Plane pb, _) =>
                RhinoIntersect.MeshPlane(ma, pb) switch {
                    { Length: > 0 } sections => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from pl in sections from pt in pl select pt], [], [], [], [], [.. sections])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Line lb, _) when !opts.Sorted =>
                RhinoIntersect.MeshLine(ma, lb) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Line lb, { Sorted: true }) =>
                RhinoIntersect.MeshLineSorted(ma, lb, out int[] lineIds) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. points], [], [], [], lineIds.Length > 0 ? [.. lineIds] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: false }) =>
                RhinoIntersect.MeshPolyline(ma, pc, out int[] polyIds1) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. points], [], [], [], polyIds1.Length > 0 ? [.. polyIds1] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: true }) =>
                RhinoIntersect.MeshPolylineSorted(ma, pc, out int[] polyIds2) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. points], [], [], [], polyIds2.Length > 0 ? [.. polyIds2] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Line la, Line lb, _) =>
                RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tolerance, finiteSegments: false)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([la.PointAt(pa)], [], [pa], [pb], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Line la, BoundingBox boxb, _) =>
                RhinoIntersect.LineBox(la, boxb, tolerance, out Interval interval)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [la.PointAt(interval.Min), la.PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Line la, Plane pb, _) =>
                RhinoIntersect.LinePlane(la, pb, out double param)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([la.PointAt(param)], [], [param], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Line la, Sphere sb, _) =>
                ((int)RhinoIntersect.LineSphere(la, sb, out Point3d sp1, out Point3d sp2), sp1, sp2) switch {
                    ( > 1, Point3d p1, Point3d p2) when p1.DistanceTo(p2) > tolerance => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Line la, Cylinder cylb, _) =>
                ((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d cp1, out Point3d cp2), cp1, cp2) switch {
                    ( > 1, Point3d p1, Point3d p2) when p1.DistanceTo(p2) > tolerance => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Line la, Circle cb, _) =>
                ((int)RhinoIntersect.LineCircle(la, cb, out double t1, out Point3d lp1, out double t2, out Point3d lp2), lp1, t1, lp2, t2) switch {
                    ( > 1, Point3d p1, double pt1, Point3d p2, double pt2) when p1.DistanceTo(p2) > tolerance =>
                        ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [pt1, pt2], [], [], [])),
                    ( > 0, Point3d p1, double pt1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [pt1], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, Plane pb, _) =>
                RhinoIntersect.PlanePlane(pa, pb, out Line line)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new LineCurve(line)], [], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (ValueTuple<Plane, Plane> planes, Plane p3, _) =>
                RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3, out Point3d point)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([point], [], [], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Plane pa, Circle cb, _) =>
                RhinoIntersect.PlaneCircle(pa, cb, out double ct1, out double ct2) switch {
                    PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput([cb.PointAt(ct1)], [], [], [ct1], [], [])),
                    PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput([cb.PointAt(ct1), cb.PointAt(ct2)], [], [], [ct1, ct2], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, Sphere sb, _) =>
                ((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle circle)) switch {
                    1 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(circle)], [], [], [], [])),
                    2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([circle.Center], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, BoundingBox boxb, _) =>
                RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly) && poly?.Count > 0
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pt in poly select pt], [], [], [], [], [poly]))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Sphere sa, Sphere sb, _) =>
                ((int)RhinoIntersect.SphereSphere(sa, sb, out Circle sphereCircle)) switch {
                    1 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(sphereCircle)], [], [], [], [])),
                    2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([sphereCircle.Center], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Circle ca, Circle cb, _) =>
                ((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d cip1, out Point3d cip2), cip1, cip2) switch {
                    ( > 1, Point3d p1, Point3d p2) when p1.DistanceTo(p2) > tolerance => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Arc aa, Arc ab, _) =>
                ((int)RhinoIntersect.ArcArc(aa, ab, out Point3d aip1, out Point3d aip2), aip1, aip2) switch {
                    ( > 1, Point3d p1, Point3d p2) when p1.DistanceTo(p2) > tolerance => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.UnsupportedIntersection),
        };
    }
}
