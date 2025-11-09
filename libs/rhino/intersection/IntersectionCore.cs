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
#pragma warning disable MA0051 // Method too long - Large pattern matching switch with 30+ intersection type combinations cannot be meaningfully reduced without extraction
    internal static Result<Intersect.IntersectionOutput> ExecutePair<T1, T2>(T1 a, T2 b, IGeometryContext ctx, Intersect.IntersectionOptions opts) where T1 : notnull where T2 : notnull {
#pragma warning restore MA0051
        double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;

        Result<Intersect.IntersectionOutput> curveIntx(CurveIntersections? r) {
            using (r) {
                return r switch {
                    { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                };
            }
        }

        Result<Intersect.IntersectionOutput> curveIntxOverlap(CurveIntersections? r, Curve ov) {
            using (r) {
                return r switch {
                    { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from e in r select e.PointA], [.. from e in r where e.IsOverlap let c = ov.Trim(e.OverlapA) where c is not null select c], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                };
            }
        }

        Result<Intersect.IntersectionOutput> brepIntx(bool success, Curve[] curves, Point3d[] points) => (success, curves, points) switch {
            (true, { Length: > 0 }, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [.. curves], [], [], [], [])),
            (true, { Length: > 0 }, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. curves], [], [], [], [])),
            (true, _, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
        };

        Result<Intersect.IntersectionOutput> countedPts(int count, Point3d p1, Point3d p2) => (count, p1, p2) switch {
            (> 1, _, _) when p1.DistanceTo(p2) > tolerance => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])),
            (> 0, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])),
            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
        };

        Result<Intersect.IntersectionOutput> countedPtsParams(int count, Point3d p1, double t1, Point3d p2, double t2) => (count, p1, t1, p2, t2) switch {
            (> 1, _, _, _, _) when p1.DistanceTo(p2) > tolerance => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [t1, t2], [], [], [])),
            (> 0, _, _, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [t1], [], [], [])),
            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
        };

        Result<Intersect.IntersectionOutput> polylines(Polyline[]? sections) => sections switch {
            { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pl in sections from pt in pl select pt], [], [], [], [], [.. sections])),
            null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
        };

        Result<Intersect.IntersectionOutput> singlePt(bool success, Point3d pt, double[]? paramsA = null, double[]? paramsB = null) =>
            success
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([pt], [], paramsA ?? [], paramsB ?? [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty);

        Result<Intersect.IntersectionOutput> doublePt(bool success, Point3d p1, Point3d p2, double[]? paramsA = null) =>
            success
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], paramsA ?? [], [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty);

        Result<Intersect.IntersectionOutput> circleIntx(int count, Circle circle, Func<Circle, Curve> createCurve) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via IntersectionOutput.Curves
            count switch {
                1 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [createCurve(circle)], [], [], [], [])),
                2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([circle.Center], [], [], [], [], [])),
                _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            };
#pragma warning restore IDISP004

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
                curveIntx(RhinoIntersect.CurveSelf(ca, tolerance)),
            (Curve ca, Curve cb, _) =>
                curveIntxOverlap(RhinoIntersect.CurveCurve(ca, cb, tolerance, tolerance), ca),
            (Curve ca, BrepFace bf, _) =>
                brepIntx(RhinoIntersect.CurveBrepFace(ca, bf, tolerance, out Curve[] c2, out Point3d[] p2), c2, p2),
            (Curve ca, Surface sb, _) =>
                curveIntx(RhinoIntersect.CurveSurface(ca, sb, tolerance, overlapTolerance: tolerance)),
            (Curve ca, Plane pb, _) =>
                curveIntx(RhinoIntersect.CurvePlane(ca, pb, tolerance)),
            (Curve ca, Line lb, _) =>
                curveIntx(RhinoIntersect.CurveLine(ca, lb, tolerance, overlapTolerance: tolerance)),
            (Curve ca, Brep bb, _) =>
                brepIntx(RhinoIntersect.CurveBrep(ca, bb, tolerance, out Curve[] c1, out Point3d[] p1), c1, p1),
            (Brep ba, Brep bb, _) =>
                brepIntx(RhinoIntersect.BrepBrep(ba, bb, tolerance, out Curve[] c3, out Point3d[] p3), c3, p3),
            (Brep ba, Plane pb, _) =>
                brepIntx(RhinoIntersect.BrepPlane(ba, pb, tolerance, out Curve[] c4, out Point3d[] p4), c4, p4),
            (Brep ba, Surface sb, _) =>
                brepIntx(RhinoIntersect.BrepSurface(ba, sb, tolerance, out Curve[] c5, out Point3d[] p5), c5, p5),
            (Surface sa, Surface sb, _) =>
                brepIntx(RhinoIntersect.SurfaceSurface(sa, sb, tolerance, out Curve[] c6, out Point3d[] p6), c6, p6),
            (Mesh ma, Mesh mb, _) when !opts.Sorted =>
                RhinoIntersect.MeshMeshFast(ma, mb) switch {
                    Line[] { Length: > 0 } lines => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from l in lines select l.From, .. from l in lines select l.To], [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Mesh mb, { Sorted: true }) =>
                polylines(RhinoIntersect.MeshMeshAccurate(ma, mb, tolerance)),
            (Mesh ma, Ray3d rb, _) =>
                RhinoIntersect.MeshRay(ma, rb) switch {
                    double d when d >= 0d => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [rb.PointAt(d)], [], [d], [], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Plane pb, _) =>
                polylines(RhinoIntersect.MeshPlane(ma, pb)),
            (Mesh ma, Line lb, _) when !opts.Sorted =>
                RhinoIntersect.MeshLine(ma, lb) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Mesh ma, Line lb, { Sorted: true }) =>
                RhinoIntersect.MeshLineSorted(ma, lb, out int[] ids3) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], ids3.Length > 0 ? [.. ids3] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: false }) =>
                RhinoIntersect.MeshPolyline(ma, pc, out int[] ids4) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], ids4.Length > 0 ? [.. ids4] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: true }) =>
                RhinoIntersect.MeshPolylineSorted(ma, pc, out int[] ids5) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], ids5.Length > 0 ? [.. ids5] : [], [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Line la, Line lb, _) =>
                singlePt(RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tolerance, finiteSegments: false), la.PointAt(pa), paramsA: [pa], paramsB: [pb]),
            (Line la, BoundingBox boxb, _) =>
                doublePt(RhinoIntersect.LineBox(la, boxb, tolerance, out Interval interval), la.PointAt(interval.Min), la.PointAt(interval.Max), paramsA: [interval.Min, interval.Max]),
            (Line la, Plane pb, _) =>
                singlePt(RhinoIntersect.LinePlane(la, pb, out double param), la.PointAt(param), paramsA: [param]),
            (Line la, Sphere sb, _) =>
                countedPts((int)RhinoIntersect.LineSphere(la, sb, out Point3d ps1, out Point3d ps2), ps1, ps2),
            (Line la, Cylinder cylb, _) =>
                countedPts((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d pc1, out Point3d pc2), pc1, pc2),
            (Line la, Circle cb, _) =>
                countedPtsParams((int)RhinoIntersect.LineCircle(la, cb, out double lct1, out Point3d lcp1, out double lct2, out Point3d lcp2), lcp1, lct1, lcp2, lct2),
            (Plane pa, Plane pb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via IntersectionOutput.Curves
                RhinoIntersect.PlanePlane(pa, pb, out Line line)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new LineCurve(line)], [], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
#pragma warning restore IDISP004
            (ValueTuple<Plane, Plane> planes, Plane p3, _) =>
                singlePt(RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3, out Point3d point), point),
            (Plane pa, Circle cb, _) =>
                RhinoIntersect.PlaneCircle(pa, cb, out double pct1, out double pct2) switch {
                    PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [cb.PointAt(pct1)], [], [], [pct1], [], [])),
                    PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [cb.PointAt(pct1), cb.PointAt(pct2)], [], [], [pct1, pct2], [], [])),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            (Plane pa, Sphere sb, _) =>
                circleIntx((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle psc), psc, c => new ArcCurve(c)),
            (Plane pa, BoundingBox boxb, _) =>
                RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly) && poly?.Count > 0
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from pt in poly select pt], [], [], [], [], [poly]))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            (Sphere sa, Sphere sb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via result
                circleIntx((int)RhinoIntersect.SphereSphere(sa, sb, out Circle ssc), ssc, c => new ArcCurve(c)),
#pragma warning restore IDISP004
            (Circle ca, Circle cb, _) =>
                countedPts((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d ccp1, out Point3d ccp2), ccp1, ccp2),
            (Arc aa, Arc ab, _) =>
                countedPts((int)RhinoIntersect.ArcArc(aa, ab, out Point3d aap1, out Point3d aap2), aap1, aap2),
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.UnsupportedIntersection),
        };
    }
}
