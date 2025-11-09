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

/// <summary>RhinoCommon intersection dispatch with FrozenDictionary-based type resolution and inline result transformation.</summary>
internal static class IntersectionCore {
    private static readonly Result<Intersect.IntersectionOutput> Empty = ResultFactory.Create(value: Intersect.IntersectionOutput.Empty);
    private static readonly Result<Intersect.IntersectionOutput> IntersectionFailed = ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed);
    private static readonly Result<Intersect.IntersectionOutput> InvalidProjection = ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection);
    private static readonly Result<Intersect.IntersectionOutput> InvalidRay = ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidRay);
    private static readonly Result<Intersect.IntersectionOutput> InvalidMaxHits = ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits);
    private static readonly FrozenDictionary<(Type, Type), Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>> _dispatch =
        new Dictionary<(Type, Type), Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>> {
            [(typeof(Curve), typeof(Curve))] = (a, b, tol, _) => {
                (Curve ca, Curve cb) = ((Curve)a, (Curve)b);
                using CurveIntersections? r = ReferenceEquals(ca, cb) ? RhinoIntersect.CurveSelf(ca, tol) : RhinoIntersect.CurveCurve(ca, cb, tol, tol);
                return r switch {
                    null => Empty,
                    { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA],
                        [.. from e in r where e.IsOverlap let c = ca.Trim(e.OverlapA) where c is not null select c],
                        [.. from e in r select e.ParameterA],
                        [.. from e in r select e.ParameterB],
                        [], [])),
                    _ => Empty,
                };
            },
            [(typeof(Curve), typeof(BrepFace))] = (a, b, tol, _) => (RhinoIntersect.CurveBrepFace((Curve)a, (BrepFace)b, tol, out Curve[] c, out Point3d[] p), c, p) switch {
                (true, { Length: > 0 }, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, c, [], [], [], [])),
                (true, { Length: > 0 }, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], c, [], [], [], [])),
                (true, _, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Curve), typeof(Surface))] = (a, b, tol, _) => {
                using CurveIntersections? r = RhinoIntersect.CurveSurface((Curve)a, (Surface)b, tol, overlapTolerance: tol);
                return r switch {
                    { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA],
                        [],
                        [.. from e in r select e.ParameterA],
                        [.. from e in r select e.ParameterB],
                        [], [])),
                    _ => Empty,
                };
            },
            [(typeof(Curve), typeof(Plane))] = (a, b, tol, _) => {
                using CurveIntersections? r = RhinoIntersect.CurvePlane((Curve)a, (Plane)b, tol);
                return r switch {
                    { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA],
                        [],
                        [.. from e in r select e.ParameterA],
                        [.. from e in r select e.ParameterB],
                        [], [])),
                    _ => Empty,
                };
            },
            [(typeof(Curve), typeof(Line))] = (a, b, tol, _) => {
                using CurveIntersections? r = RhinoIntersect.CurveLine((Curve)a, (Line)b, tol, overlapTolerance: tol);
                return r switch {
                    { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                        [.. from e in r select e.PointA],
                        [],
                        [.. from e in r select e.ParameterA],
                        [.. from e in r select e.ParameterB],
                        [], [])),
                    _ => Empty,
                };
            },
            [(typeof(Curve), typeof(Brep))] = (a, b, tol, _) => (RhinoIntersect.CurveBrep((Curve)a, (Brep)b, tol, out Curve[] c, out Point3d[] p), c, p) switch {
                (true, { Length: > 0 }, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, c, [], [], [], [])),
                (true, { Length: > 0 }, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], c, [], [], [], [])),
                (true, _, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Brep), typeof(Brep))] = (a, b, tol, _) => (RhinoIntersect.BrepBrep((Brep)a, (Brep)b, tol, out Curve[] c, out Point3d[] p), c, p) switch {
                (true, { Length: > 0 }, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, c, [], [], [], [])),
                (true, { Length: > 0 }, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], c, [], [], [], [])),
                (true, _, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Brep), typeof(Plane))] = (a, b, tol, _) => (RhinoIntersect.BrepPlane((Brep)a, (Plane)b, tol, out Curve[] c, out Point3d[] p), c, p) switch {
                (true, { Length: > 0 }, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, c, [], [], [], [])),
                (true, { Length: > 0 }, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], c, [], [], [], [])),
                (true, _, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Brep), typeof(Surface))] = (a, b, tol, _) => (RhinoIntersect.BrepSurface((Brep)a, (Surface)b, tol, out Curve[] c, out Point3d[] p), c, p) switch {
                (true, { Length: > 0 }, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, c, [], [], [], [])),
                (true, { Length: > 0 }, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], c, [], [], [], [])),
                (true, _, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Surface), typeof(Surface))] = (a, b, tol, _) => (RhinoIntersect.SurfaceSurface((Surface)a, (Surface)b, tol, out Curve[] c, out Point3d[] p), c, p) switch {
                (true, { Length: > 0 }, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, c, [], [], [], [])),
                (true, { Length: > 0 }, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], c, [], [], [], [])),
                (true, _, { Length: > 0 }) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Mesh), typeof(Mesh))] = (a, b, tol, opts) => opts.Sorted switch {
                true => RhinoIntersect.MeshMeshAccurate((Mesh)a, (Mesh)b, tol) switch {
                    Polyline[] { Length: > 0 } pl => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from p in pl from pt in p select pt], [], [], [], [], [.. pl])),
                    null => IntersectionFailed,
                    _ => Empty,
                },
                false => RhinoIntersect.MeshMeshFast((Mesh)a, (Mesh)b) switch {
                    Line[] { Length: > 0 } lines => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from l in lines select l.From, .. from l in lines select l.To], [], [], [], [], [])),
                    null => IntersectionFailed,
                    _ => Empty,
                },
            },
            [(typeof(Mesh), typeof(Ray3d))] = (a, b, _, _) => RhinoIntersect.MeshRay((Mesh)a, (Ray3d)b) switch {
                double d when d >= 0d => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Ray3d)b).PointAt(d)], [], [d], [], [], [])),
                _ => Empty,
            },
            [(typeof(Mesh), typeof(Plane))] = (a, b, _, _) => RhinoIntersect.MeshPlane((Mesh)a, (Plane)b) switch {
                Polyline[] { Length: > 0 } pl => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from p in pl from pt in p select pt], [], [], [], [], [.. pl])),
                null => IntersectionFailed,
                _ => Empty,
            },
            [(typeof(Mesh), typeof(Line))] = (a, b, _, opts) => opts.Sorted switch {
                true => (RhinoIntersect.MeshLineSorted((Mesh)a, (Line)b, out int[] ids), ids) switch {
                    (Point3d[] { Length: > 0 } pts, int[] indices) => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], indices, [])),
                    _ => IntersectionFailed,
                },
                false => RhinoIntersect.MeshLine((Mesh)a, (Line)b) switch {
                    Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], [], [])),
                    null => IntersectionFailed,
                    _ => Empty,
                },
            },
            [(typeof(Mesh), typeof(PolylineCurve))] = (a, b, _, opts) => opts.Sorted switch {
                true => (RhinoIntersect.MeshPolylineSorted((Mesh)a, (PolylineCurve)b, out int[] ids), ids) switch {
                    (Point3d[] { Length: > 0 } pts, int[] indices) => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], indices, [])),
                    _ => IntersectionFailed,
                },
                false => (RhinoIntersect.MeshPolyline((Mesh)a, (PolylineCurve)b, out int[] ids), ids) switch {
                    (Point3d[] { Length: > 0 } pts, int[] indices) => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], indices, [])),
                    _ => IntersectionFailed,
                },
            },
            [(typeof(Line), typeof(Line))] = (a, b, tol, _) => RhinoIntersect.LineLine((Line)a, (Line)b, out double pa, out double pb, tol, finiteSegments: false)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(pa)], [], [pa], [pb], [], []))
                : Empty,
            [(typeof(Line), typeof(BoundingBox))] = (a, b, tol, _) => RhinoIntersect.LineBox((Line)a, (BoundingBox)b, tol, out Interval interval)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(interval.Min), ((Line)a).PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], []))
                : Empty,
            [(typeof(Line), typeof(Plane))] = (a, b, _, _) => RhinoIntersect.LinePlane((Line)a, (Plane)b, out double param)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(param)], [], [param], [], [], []))
                : Empty,
            [(typeof(Line), typeof(Sphere))] = (a, b, tol, _) => ((int)RhinoIntersect.LineSphere((Line)a, (Sphere)b, out Point3d p1, out Point3d p2), p1, p2, tol) switch {
                (> 1, var pt1, var pt2, double t) when pt1.DistanceTo(pt2) > t => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1, pt2], [], [], [], [], [])),
                (> 0, var pt1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1], [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Line), typeof(Cylinder))] = (a, b, tol, _) => ((int)RhinoIntersect.LineCylinder((Line)a, (Cylinder)b, out Point3d p1, out Point3d p2), p1, p2, tol) switch {
                (> 1, var pt1, var pt2, double t) when pt1.DistanceTo(pt2) > t => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1, pt2], [], [], [], [], [])),
                (> 0, var pt1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1], [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Line), typeof(Circle))] = (a, b, tol, _) => ((int)RhinoIntersect.LineCircle((Line)a, (Circle)b, out double t1, out Point3d p1, out double t2, out Point3d p2), p1, t1, p2, t2, tol) switch {
                (> 1, var pt1, double ta, var pt2, double tb, double t) when pt1.DistanceTo(pt2) > t => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1, pt2], [], [ta, tb], [], [], [])),
                (> 0, var pt1, double ta, _, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1], [], [ta], [], [], [])),
                _ => Empty,
            },
            [(typeof(Plane), typeof(Plane))] = (a, b, _, _) => RhinoIntersect.PlanePlane((Plane)a, (Plane)b, out Line line)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new LineCurve(line)], [], [], [], []))
                : Empty,
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = (a, b, _, _) => {
                ValueTuple<Plane, Plane> planes = (ValueTuple<Plane, Plane>)a;
                return RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, (Plane)b, out Point3d point)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([point], [], [], [], [], []))
                    : Empty;
            },
            [(typeof(Plane), typeof(Circle))] = (a, b, _, _) => RhinoIntersect.PlaneCircle((Plane)a, (Circle)b, out double t1, out double t2) switch {
                PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)b).PointAt(t1)], [], [], [t1], [], [])),
                PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)b).PointAt(t1), ((Circle)b).PointAt(t2)], [], [], [t1, t2], [], [])),
                _ => Empty,
            },
            [(typeof(Plane), typeof(Sphere))] = (a, b, _, _) => ((int)RhinoIntersect.PlaneSphere((Plane)a, (Sphere)b, out Circle c), c) switch {
                (1, Circle circle) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(circle)], [], [], [], [])),
                (2, Circle circle) => ResultFactory.Create(value: new Intersect.IntersectionOutput([circle.Center], [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Plane), typeof(BoundingBox))] = (a, b, _, _) => (RhinoIntersect.PlaneBoundingBox((Plane)a, (BoundingBox)b, out Polyline poly), poly) switch {
                (true, Polyline { Count: > 0 } pl) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pt in pl select pt], [], [], [], [], [pl])),
                _ => Empty,
            },
            [(typeof(Sphere), typeof(Sphere))] = (a, b, _, _) => ((int)RhinoIntersect.SphereSphere((Sphere)a, (Sphere)b, out Circle c), c) switch {
                (1, Circle circle) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(circle)], [], [], [], [])),
                (2, Circle circle) => ResultFactory.Create(value: new Intersect.IntersectionOutput([circle.Center], [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Circle), typeof(Circle))] = (a, b, tol, _) => ((int)RhinoIntersect.CircleCircle((Circle)a, (Circle)b, out Point3d p1, out Point3d p2), p1, p2, tol) switch {
                (> 1, var pt1, var pt2, double t) when pt1.DistanceTo(pt2) > t => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1, pt2], [], [], [], [], [])),
                (> 0, var pt1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1], [], [], [], [], [])),
                _ => Empty,
            },
            [(typeof(Arc), typeof(Arc))] = (a, b, tol, _) => ((int)RhinoIntersect.ArcArc((Arc)a, (Arc)b, out Point3d p1, out Point3d p2), p1, p2, tol) switch {
                (> 1, var pt1, var pt2, double t) when pt1.DistanceTo(pt2) > t => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1, pt2], [], [], [], [], [])),
                (> 0, var pt1, _, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([pt1], [], [], [], [], [])),
                _ => Empty,
            },
        }.ToFrozenDictionary();

    private static Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>? FindDispatcher(Type t1, Type t2) {
        (Type, Type) key = (t1, t2);
        return _dispatch.TryGetValue(key, out Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>? dispatcher)
            ? dispatcher
            : t1.BaseType is not null && t1.BaseType != typeof(object)
                ? FindDispatcher(t1.BaseType, t2)
                : t2.BaseType is not null && t2.BaseType != typeof(object)
                    ? FindDispatcher(t1, t2.BaseType)
                    : null;
    }

    [Pure]
    internal static Result<Intersect.IntersectionOutput> ExecutePair<T1, T2>(T1 a, T2 b, IGeometryContext ctx, Intersect.IntersectionOptions opts) where T1 : notnull where T2 : notnull {
        double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;

        return (a, b, opts) switch {
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                InvalidProjection,
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    RhinoIntersect.ProjectPointsToBrepsEx(breps, pts, dir, tolerance, out int[] ids1),
                    [], [], [], ids1, [])),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, tolerance),
                    [], [], [], [], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                InvalidProjection,
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: true }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    RhinoIntersect.ProjectPointsToMeshesEx(meshes, pts, dir, tolerance, out int[] ids2),
                    [], [], [], ids2, [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, tolerance),
                    [], [], [], [], [])),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance =>
                InvalidRay,
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when hits <= 0 =>
                InvalidMaxHits,
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) =>
                ResultFactory.Create(value: new Intersect.IntersectionOutput(
                    RhinoIntersect.RayShoot(ray, geoms, hits),
                    [], [], [], [], [])),
            _ => FindDispatcher(a.GetType(), b.GetType()) is Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>> dispatcher
                ? dispatcher(a, b, tolerance, opts)
                : ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.UnsupportedIntersection.WithContext($"{a.GetType().Name} × {b.GetType().Name}")),
        };
    }
}
