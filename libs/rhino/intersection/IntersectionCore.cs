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
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToBrepsEx(breps, pts, dir, ctx.AbsoluteTolerance, out int[] ids1)],
                    [], [], [], ids1.Length > 0 ? [.. ids1] : [], [])),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, ctx.AbsoluteTolerance)],
                    [], [], [], [], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToMeshesEx(meshes, pts, dir, ctx.AbsoluteTolerance, out int[] ids2)],
                    [], [], [], ids2.Length > 0 ? [.. ids2] : [], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, ctx.AbsoluteTolerance)],
                    [], [], [], [], [])),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidRay),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when hits <= 0 =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    [.. RhinoIntersect.RayShoot(ray, geoms, hits)],
                    [], [], [], [], [])),
            (Curve ca, Curve cb, _) when ReferenceEquals(ca, cb) =>
                ((Func<CurveIntersections?, Curve?, Result<Intersect.IntersectionOutput>>)((ci, src) => {
                    using (ci) {
                        return ci switch {
                            { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                                [.. from e in r select e.PointA], src is not null ? [.. from e in r where e.IsOverlap let c = src.Trim(e.OverlapA) where c is not null select c] : [],
                                [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                        };
                    }
                }))(RhinoIntersect.CurveSelf(ca, tolerance), null),
            (Curve ca, Curve cb, _) =>
                ((Func<CurveIntersections?, Curve?, Result<Intersect.IntersectionOutput>>)((ci, src) => {
                    using (ci) {
                        return ci switch {
                            { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                                [.. from e in r select e.PointA], src is not null ? [.. from e in r where e.IsOverlap let c = src.Trim(e.OverlapA) where c is not null select c] : [],
                                [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                        };
                    }
                }))(RhinoIntersect.CurveCurve(ca, cb, tolerance, tolerance), ca),
            (Curve ca, BrepFace bf, _) =>
                (RhinoIntersect.CurveBrepFace(ca, bf, tolerance, out Curve[] c2, out Point3d[] p2), c2, p2) switch {
                    (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [.. curves], [], [], [], [])),
                    (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. curves], [], [], [], [])),
                    (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Curve ca, Surface sb, _) =>
                ((Func<CurveIntersections?, Curve?, Result<Intersect.IntersectionOutput>>)((ci, src) => {
                    using (ci) {
                        return ci switch {
                            { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                                [.. from e in r select e.PointA], src is not null ? [.. from e in r where e.IsOverlap let c = src.Trim(e.OverlapA) where c is not null select c] : [],
                                [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                        };
                    }
                }))(RhinoIntersect.CurveSurface(ca, sb, tolerance, overlapTolerance: tolerance), null),
            (Curve ca, Plane pb, _) =>
                ((Func<CurveIntersections?, Curve?, Result<Intersect.IntersectionOutput>>)((ci, src) => {
                    using (ci) {
                        return ci switch {
                            { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                                [.. from e in r select e.PointA], src is not null ? [.. from e in r where e.IsOverlap let c = src.Trim(e.OverlapA) where c is not null select c] : [],
                                [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                        };
                    }
                }))(RhinoIntersect.CurvePlane(ca, pb, tolerance), null),
            (Curve ca, Line lb, _) =>
                ((Func<CurveIntersections?, Curve?, Result<Intersect.IntersectionOutput>>)((ci, src) => {
                    using (ci) {
                        return ci switch {
                            { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                                [.. from e in r select e.PointA], src is not null ? [.. from e in r where e.IsOverlap let c = src.Trim(e.OverlapA) where c is not null select c] : [],
                                [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                        };
                    }
                }))(RhinoIntersect.CurveLine(ca, lb, tolerance, overlapTolerance: tolerance), null),
            (Curve ca, Brep bb, _) =>
                (RhinoIntersect.CurveBrep(ca, bb, tolerance, out Curve[] c1, out Point3d[] p1), c1, p1) switch {
                    (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [.. curves], [], [], [], [])),
                    (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. curves], [], [], [], [])),
                    (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Brep ba, Brep bb, _) =>
                (RhinoIntersect.BrepBrep(ba, bb, tolerance, out Curve[] c3, out Point3d[] p3), c3, p3) switch {
                    (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [.. curves], [], [], [], [])),
                    (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. curves], [], [], [], [])),
                    (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Brep ba, Plane pb, _) =>
                (RhinoIntersect.BrepPlane(ba, pb, tolerance, out Curve[] c4, out Point3d[] p4), c4, p4) switch {
                    (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [.. curves], [], [], [], [])),
                    (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. curves], [], [], [], [])),
                    (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Brep ba, Surface sb, _) =>
                (RhinoIntersect.BrepSurface(ba, sb, tolerance, out Curve[] c5, out Point3d[] p5), c5, p5) switch {
                    (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [.. curves], [], [], [], [])),
                    (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. curves], [], [], [], [])),
                    (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Surface sa, Surface sb, _) =>
                (RhinoIntersect.SurfaceSurface(sa, sb, tolerance, out Curve[] c6, out Point3d[] p6), c6, p6) switch {
                    (true, { Length: > 0 } curves, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [.. curves], [], [], [], [])),
                    (true, { Length: > 0 } curves, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. curves], [], [], [], [])),
                    (true, _, { Length: > 0 } points) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
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
                    Polyline[] { Length: > 0 } sections => ResultFactory.Create(value: new Intersect.IntersectionOutput(
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
                    Polyline[] { Length: > 0 } sections => ResultFactory.Create(value: new Intersect.IntersectionOutput(
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
                RhinoIntersect.MeshLineSorted(ma, lb, out int[] ids3) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. points], [], [], [], ids3.Length > 0 ? [.. ids3] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: false }) =>
                RhinoIntersect.MeshPolyline(ma, pc, out int[] ids4) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. points], [], [], [], ids4.Length > 0 ? [.. ids4] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: true }) =>
                RhinoIntersect.MeshPolylineSorted(ma, pc, out int[] ids5) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. points], [], [], [], ids5.Length > 0 ? [.. ids5] : [], [])),
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
                ((int)RhinoIntersect.LineSphere(la, sb, out Point3d ps1, out Point3d ps2), ps1, ps2, tolerance) switch {
                    ( > 1, Point3d p1, Point3d p2, double tol) when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Line la, Cylinder cylb, _) =>
                ((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d pc1, out Point3d pc2), pc1, pc2, tolerance) switch {
                    ( > 1, Point3d p1, Point3d p2, double tol) when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Line la, Circle cb, _) =>
                ((int)RhinoIntersect.LineCircle(la, cb, out double lct1, out Point3d lcp1, out double lct2, out Point3d lcp2), lcp1, lct1, lcp2, lct2, tolerance) switch {
                    ( > 1, Point3d p1, double t1, Point3d p2, double t2, double tol) when p1.DistanceTo(p2) > tol =>
                        ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [t1, t2], [], [], [])),
                    ( > 0, Point3d p1, double t1, _, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [t1], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, Plane pb, _) =>
                RhinoIntersect.PlanePlane(pa, pb, out Line line)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new LineCurve(line)], [], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (ValueTuple<Plane, Plane> planes, Plane p3, _) =>
                RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3, out Point3d point) switch {
                    true => ResultFactory.Create(value: new Intersect.IntersectionOutput([point], [], [], [], [], [])),
                    false => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, Circle cb, _) =>
                RhinoIntersect.PlaneCircle(pa, cb, out double pct1, out double pct2) switch {
                    PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput([cb.PointAt(pct1)], [], [], [pct1], [], [])),
                    PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput([cb.PointAt(pct1), cb.PointAt(pct2)], [], [], [pct1, pct2], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, Sphere sb, _) =>
                ((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle psc)) switch {
                    1 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(psc)], [], [], [], [])),
                    2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([psc.Center], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, BoundingBox boxb, _) =>
                RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly) && poly?.Count > 0
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pt in poly select pt], [], [], [], [], [poly]))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Sphere sa, Sphere sb, _) =>
                ((int)RhinoIntersect.SphereSphere(sa, sb, out Circle ssc)) switch {
                    1 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(ssc)], [], [], [], [])),
                    2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([ssc.Center], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Circle ca, Circle cb, _) =>
                ((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d ccp1, out Point3d ccp2), ccp1, ccp2, tolerance) switch {
                    ( > 1, Point3d p1, Point3d p2, double tol) when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Arc aa, Arc ab, _) =>
                ((int)RhinoIntersect.ArcArc(aa, ab, out Point3d aap1, out Point3d aap2), aap1, aap2, tolerance) switch {
                    ( > 1, Point3d p1, Point3d p2, double tol) when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
                    ( > 0, Point3d p1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.UnsupportedIntersection),
        };
    }
}
