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
        static Result<Intersect.IntersectionOutput> empty() => empty();

        static Result<Intersect.IntersectionOutput> fromCurveIntersections(CurveIntersections? results, Curve? overlapSource) {
#pragma warning disable IDISP007 // Don't dispose injected - CurveIntersections created by caller and owned by this method
            using (results) {
#pragma warning restore IDISP007
                return results switch {
                    null => empty(), { Count: > 0 } r => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                                                                                                 [.. from e in r select e.PointA],
                                                                                                 overlapSource is not null ? [.. from e in r where e.IsOverlap let c = overlapSource.Trim(e.OverlapA) where c is not null select c] : [],
                                                                                                 [.. from e in r select e.ParameterA],
                                                                                                 [.. from e in r select e.ParameterB],
                                                                                                 [], [])),
                    _ => empty(),
                };
            }
        }

        static Result<Intersect.IntersectionOutput> fromGeometry((bool success, Curve[] curves, Point3d[] points) r) =>
            (r.success, r.curves.Length, r.points.Length) switch {
                (true, > 0, > 0) => ResultFactory.Create(value: Intersect.IntersectionOutput.FromGeometry([.. r.points], [.. r.curves])),
                (true, > 0, _) => ResultFactory.Create(value: Intersect.IntersectionOutput.FromCurves([.. r.curves])),
                (true, _, > 0) => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([.. r.points])),
                _ => empty(),
            };

        static Result<Intersect.IntersectionOutput> fromPoints((int count, Point3d p1, Point3d p2, double[] parameters, double tolerance) d) =>
            (d.count, d.p1.DistanceTo(d.p2) > d.tolerance, d.parameters.Length) switch {
                ( > 1, true, 2) => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([d.p1, d.p2], [d.parameters[0], d.parameters[1]])),
                ( > 1, true, 0) => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([d.p1, d.p2])),
                ( > 0, _, 1) => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([d.p1], [d.parameters[0]])),
                ( > 0, _, 0) => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([d.p1])),
                _ => empty(),
            };

        static Result<Intersect.IntersectionOutput> fromCircle((int result, Circle circle) data) =>
            data.result switch {
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via result
                1 => ResultFactory.Create(value: Intersect.IntersectionOutput.FromCurves([new ArcCurve(data.circle)])),
#pragma warning restore IDISP004
                2 => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([data.circle.Center])),
                _ => empty(),
            };

        static Result<Intersect.IntersectionOutput> fromPolylines(Polyline[]? sections) =>
            sections switch { { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pl in sections from pt in pl select pt], [], [], [], [], [.. sections])),
                null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                _ => empty(),
            };

        double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;
        static bool invalidProjection(Vector3d dir) => !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance;

        return (a, b, opts) switch {
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) when invalidProjection(dir) =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: Intersect.IntersectionOutput.FromPointsWithIndices(
                    [.. RhinoIntersect.ProjectPointsToBrepsEx(breps, pts, dir, ctx.AbsoluteTolerance, out int[] ids1)],
                    ids1.Length > 0 ? [.. ids1] : [])),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints(
                    [.. RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, ctx.AbsoluteTolerance)])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) when invalidProjection(dir) =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: Intersect.IntersectionOutput.FromPointsWithIndices(
                    [.. RhinoIntersect.ProjectPointsToMeshesEx(meshes, pts, dir, ctx.AbsoluteTolerance, out int[] ids2)],
                    ids2.Length > 0 ? [.. ids2] : [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints(
                    [.. RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, ctx.AbsoluteTolerance)])),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidRay),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when hits <= 0 =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) =>
                ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints(
                    [.. RhinoIntersect.RayShoot(ray, geoms, hits)])),
            (Curve ca, Curve cb, _) when ReferenceEquals(ca, cb) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                fromCurveIntersections(RhinoIntersect.CurveSelf(ca, tolerance), overlapSource: null),
#pragma warning restore IDISP004
            (Curve ca, Curve cb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                fromCurveIntersections(RhinoIntersect.CurveCurve(ca, cb, tolerance, tolerance), ca),
#pragma warning restore IDISP004
            (Curve ca, BrepFace bf, _) =>
                fromGeometry((RhinoIntersect.CurveBrepFace(ca, bf, tolerance, out Curve[] c2, out Point3d[] p2), c2, p2)),
            (Curve ca, Surface sb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                fromCurveIntersections(RhinoIntersect.CurveSurface(ca, sb, tolerance, overlapTolerance: tolerance), overlapSource: null),
#pragma warning restore IDISP004
            (Curve ca, Plane pb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                fromCurveIntersections(RhinoIntersect.CurvePlane(ca, pb, tolerance), overlapSource: null),
#pragma warning restore IDISP004
            (Curve ca, Line lb, _) =>
#pragma warning disable IDISP004 // Don't ignore created IDisposable - disposed in fromCurveIntersections
                fromCurveIntersections(RhinoIntersect.CurveLine(ca, lb, tolerance, overlapTolerance: tolerance), overlapSource: null),
#pragma warning restore IDISP004
            (Curve ca, Brep bb, _) =>
                fromGeometry((RhinoIntersect.CurveBrep(ca, bb, tolerance, out Curve[] c1, out Point3d[] p1), c1, p1)),
            (Brep ba, Brep bb, _) =>
                fromGeometry((RhinoIntersect.BrepBrep(ba, bb, tolerance, out Curve[] c3, out Point3d[] p3), c3, p3)),
            (Brep ba, Plane pb, _) =>
                fromGeometry((RhinoIntersect.BrepPlane(ba, pb, tolerance, out Curve[] c4, out Point3d[] p4), c4, p4)),
            (Brep ba, Surface sb, _) =>
                fromGeometry((RhinoIntersect.BrepSurface(ba, sb, tolerance, out Curve[] c5, out Point3d[] p5), c5, p5)),
            (Surface sa, Surface sb, _) =>
                fromGeometry((RhinoIntersect.SurfaceSurface(sa, sb, tolerance, out Curve[] c6, out Point3d[] p6), c6, p6)),
            (Mesh ma, Mesh mb, _) when !opts.Sorted =>
                RhinoIntersect.MeshMeshFast(ma, mb) switch {
                    Line[] { Length: > 0 } lines => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints(
                        [.. from l in lines select l.From, .. from l in lines select l.To])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => empty(),
                },
            (Mesh ma, Mesh mb, { Sorted: true }) =>
                fromPolylines(RhinoIntersect.MeshMeshAccurate(ma, mb, tolerance)),
            (Mesh ma, Ray3d rb, _) =>
                RhinoIntersect.MeshRay(ma, rb) switch {
                    double d when d >= 0d => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([rb.PointAt(d)], [d])),
                    _ => empty(),
                },
            (Mesh ma, Plane pb, _) =>
                fromPolylines(RhinoIntersect.MeshPlane(ma, pb)),
            (Mesh ma, Line lb, _) when !opts.Sorted =>
                RhinoIntersect.MeshLine(ma, lb) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([.. points])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => empty(),
                },
            (Mesh ma, Line lb, { Sorted: true }) =>
                RhinoIntersect.MeshLineSorted(ma, lb, out int[] ids3) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPointsWithIndices(
                        [.. points], ids3.Length > 0 ? [.. ids3] : [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Mesh ma, PolylineCurve pc, { Sorted: bool sorted }) =>
                (sorted ? RhinoIntersect.MeshPolylineSorted(ma, pc, out int[] ids) : RhinoIntersect.MeshPolyline(ma, pc, out ids)) switch {
                    Point3d[] { Length: > 0 } points => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPointsWithIndices(
                        [.. points], ids.Length > 0 ? [.. ids] : [])),
                    _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                },
            (Line la, Line lb, _) =>
                RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tolerance, finiteSegments: false)
                    ? ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([la.PointAt(pa)], [pa], [pb]))
                    : empty(),
            (Line la, BoundingBox boxb, _) =>
                RhinoIntersect.LineBox(la, boxb, tolerance, out Interval interval)
                    ? ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints(
                        [la.PointAt(interval.Min), la.PointAt(interval.Max)], [interval.Min, interval.Max]))
                    : empty(),
            (Line la, Plane pb, _) =>
                RhinoIntersect.LinePlane(la, pb, out double param)
                    ? ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([la.PointAt(param)], [param]))
                    : empty(),
            (Line la, Sphere sb, _) =>
                fromPoints(((int)RhinoIntersect.LineSphere(la, sb, out Point3d ps1, out Point3d ps2), ps1, ps2, [], tolerance)),
            (Line la, Cylinder cylb, _) =>
                fromPoints(((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d pc1, out Point3d pc2), pc1, pc2, [], tolerance)),
            (Line la, Circle cb, _) =>
                fromPoints(((int)RhinoIntersect.LineCircle(la, cb, out double lct1, out Point3d lcp1, out double lct2, out Point3d lcp2), lcp1, lcp2, [lct1, lct2], tolerance)),
            (Plane pa, Plane pb, _) =>
                RhinoIntersect.PlanePlane(pa, pb, out Line line)
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via result
                    ? ResultFactory.Create(value: Intersect.IntersectionOutput.FromCurves([new LineCurve(line)]))
#pragma warning restore IDISP004
                    : empty(),
            (ValueTuple<Plane, Plane> planes, Plane p3, _) =>
                RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3, out Point3d point)
                    ? ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints([point]))
                    : empty(),
            (Plane pa, Circle cb, _) =>
                RhinoIntersect.PlaneCircle(pa, cb, out double pct1, out double pct2) switch {
                    PlaneCircleIntersection.Tangent => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints(
                        [cb.PointAt(pct1)], [], [pct1])),
                    PlaneCircleIntersection.Secant => ResultFactory.Create(value: Intersect.IntersectionOutput.FromPoints(
                        [cb.PointAt(pct1), cb.PointAt(pct2)], [], [pct1, pct2])),
                    _ => empty(),
                },
            (Plane pa, Sphere sb, _) =>
                fromCircle(((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle psc), psc)),
            (Plane pa, BoundingBox boxb, _) =>
                RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly) && poly?.Count > 0
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([.. poly], [], [], [], [], [poly]))
                    : empty(),
            (Sphere sa, Sphere sb, _) =>
                fromCircle(((int)RhinoIntersect.SphereSphere(sa, sb, out Circle ssc), ssc)),
            (Circle ca, Circle cb, _) =>
                fromPoints(((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d ccp1, out Point3d ccp2), ccp1, ccp2, [], tolerance)),
            (Arc aa, Arc ab, _) =>
                fromPoints(((int)RhinoIntersect.ArcArc(aa, ab, out Point3d aap1, out Point3d aap2), aap1, aap2, [], tolerance)),
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.UnsupportedIntersection),
        };
    }
}
