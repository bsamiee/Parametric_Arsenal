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
    /// <summary>Builds result from bool/arrays tuple with automatic empty/partial/full discrimination.</summary>
    private static readonly Func<(bool, Curve[]?, Point3d[]?), Result<Intersect.IntersectionOutput>> ArrayResultBuilder = t => t switch {
        (true, { Length: > 0 } c, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, c, [], [], [], [])),
        (true, { Length: > 0 } c, _) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], c, [], [], [], [])),
        (true, _, { Length: > 0 } p) => ResultFactory.Create(value: new Intersect.IntersectionOutput(p, [], [], [], [], [])),
        _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
    };

    /// <summary>Processes CurveIntersections into standardized output with points and parameters.</summary>
    private static readonly Func<CurveIntersections?, Curve, Result<Intersect.IntersectionOutput>> IntersectionProcessor
        = (r, curve) => r switch { { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(
        [.. from e in r select e.PointA],
        [.. from e in r where e.IsOverlap let c = curve.Trim(e.OverlapA) where c is not null select c],
        [.. from e in r select e.ParameterA],
        [.. from e in r select e.ParameterB],
        [], [])),
            _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
        };

    /// <summary>Handles two-point intersection results with distance threshold validation.</summary>
    private static readonly Func<int, Point3d, Point3d, double, double[]?, Result<Intersect.IntersectionOutput>> TwoPointHandler = (count, p1, p2, tol, parameters) =>
        count > 1 && p1.DistanceTo(p2) > tol ? ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], parameters ?? [], [], [], [])) :
        count > 0 ? ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], parameters is { Length: > 0 } ? [parameters[0]] : [], [], [], [])) :
        ResultFactory.Create(value: Intersect.IntersectionOutput.Empty);

    /// <summary>Handles circle intersection results with type discrimination for curve vs point output.</summary>
    private static readonly Func<int, Circle, Result<Intersect.IntersectionOutput>> CircleHandler = (type, circle) => (type, circle) switch {
        (1, Circle c) => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(c)], [], [], [], [])),
        (2, Circle c) => ResultFactory.Create(value: new Intersect.IntersectionOutput([c.Center], [], [], [], [], [])),
        _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
    };

    /// <summary>Processes polyline arrays with automatic flattening and section preservation.</summary>
    private static readonly Func<Polyline[]?, Result<Intersect.IntersectionOutput>> PolylineProcessor = pl => pl switch { { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from p in pl from pt in p select pt], [], [], [], [], [.. pl])),
        null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
        _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
    };

    /// <summary>Handles mesh intersection with sorted/unsorted dispatch for points and indices.</summary>
    private static readonly Func<Mesh, object, bool, (Func<Point3d[]?, int[]?, Result<Intersect.IntersectionOutput>>, Func<Point3d[]?, Result<Intersect.IntersectionOutput>>), Result<Intersect.IntersectionOutput>> MeshIntersectionHandler =
        (mesh, target, sorted, handlers) => sorted switch {
            true => target switch {
                Line line => handlers.Item1(RhinoIntersect.MeshLineSorted(mesh, line, out int[] ids1), ids1),
                PolylineCurve poly => handlers.Item1(RhinoIntersect.MeshPolylineSorted(mesh, poly, out int[] ids2), ids2),
                _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
            },
            false => target switch {
                Line line => handlers.Item2(RhinoIntersect.MeshLine(mesh, line)),
                PolylineCurve poly when RhinoIntersect.MeshPolyline(mesh, poly, out int[] ids3) is Point3d[] pts =>
                    ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], ids3, [])),
                _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
            },
        };

    /// <summary>Unified point projection with direction validation and index extraction.</summary>
    private static readonly Func<Point3d[], object, Vector3d, bool, double, Result<Intersect.IntersectionOutput>> ProjectionHandler = (points, targets, dir, withIndices, tol) =>
        !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance ? ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection) :
        (targets, withIndices) switch {
            (Brep[] breps, true) => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                RhinoIntersect.ProjectPointsToBrepsEx(breps, points, dir, tol, out int[] ids1), [], [], [], ids1, [])),
            (Brep[] breps, false) => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                RhinoIntersect.ProjectPointsToBreps(breps, points, dir, tol), [], [], [], [], [])),
            (Mesh[] meshes, true) => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                RhinoIntersect.ProjectPointsToMeshesEx(meshes, points, dir, tol, out int[] ids2), [], [], [], ids2, [])),
            (Mesh[] meshes, false) => ResultFactory.Create(value: new Intersect.IntersectionOutput(
                RhinoIntersect.ProjectPointsToMeshes(meshes, points, dir, tol), [], [], [], [], [])),
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
        };

    private static readonly FrozenDictionary<(Type, Type), Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>> _dispatch =
        new Dictionary<(Type, Type), Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>> {
            [(typeof(Curve), typeof(Curve))] = (a, b, tol, _) => {
                (Curve ca, Curve cb) = ((Curve)a, (Curve)b);
                using CurveIntersections? r = ReferenceEquals(ca, cb) ? RhinoIntersect.CurveSelf(ca, tol) : RhinoIntersect.CurveCurve(ca, cb, tol, tol);
                return IntersectionProcessor(r, ca);
            },
            [(typeof(Curve), typeof(BrepFace))] = (a, b, tol, _) => ArrayResultBuilder((RhinoIntersect.CurveBrepFace((Curve)a, (BrepFace)b, tol, out Curve[] c, out Point3d[] p), c, p)),
            [(typeof(Curve), typeof(Surface))] = (a, b, tol, _) => { using CurveIntersections? r = RhinoIntersect.CurveSurface((Curve)a, (Surface)b, tol, overlapTolerance: tol); return IntersectionProcessor(r, (Curve)a); },
            [(typeof(Curve), typeof(Plane))] = (a, b, tol, _) => { using CurveIntersections? r = RhinoIntersect.CurvePlane((Curve)a, (Plane)b, tol); return IntersectionProcessor(r, (Curve)a); },
            [(typeof(Curve), typeof(Line))] = (a, b, tol, _) => { using CurveIntersections? r = RhinoIntersect.CurveLine((Curve)a, (Line)b, tol, overlapTolerance: tol); return IntersectionProcessor(r, (Curve)a); },
            [(typeof(Curve), typeof(Brep))] = (a, b, tol, _) => ArrayResultBuilder((RhinoIntersect.CurveBrep((Curve)a, (Brep)b, tol, out Curve[] c, out Point3d[] p), c, p)),
            [(typeof(Brep), typeof(Brep))] = (a, b, tol, _) => ArrayResultBuilder((RhinoIntersect.BrepBrep((Brep)a, (Brep)b, tol, out Curve[] c, out Point3d[] p), c, p)),
            [(typeof(Brep), typeof(Plane))] = (a, b, tol, _) => ArrayResultBuilder((RhinoIntersect.BrepPlane((Brep)a, (Plane)b, tol, out Curve[] c, out Point3d[] p), c, p)),
            [(typeof(Brep), typeof(Surface))] = (a, b, tol, _) => ArrayResultBuilder((RhinoIntersect.BrepSurface((Brep)a, (Surface)b, tol, out Curve[] c, out Point3d[] p), c, p)),
            [(typeof(Surface), typeof(Surface))] = (a, b, tol, _) => ArrayResultBuilder((RhinoIntersect.SurfaceSurface((Surface)a, (Surface)b, tol, out Curve[] c, out Point3d[] p), c, p)),
            [(typeof(Mesh), typeof(Mesh))] = (a, b, tol, opts) => opts.Sorted switch {
                true => PolylineProcessor(RhinoIntersect.MeshMeshAccurate((Mesh)a, (Mesh)b, tol)),
                false => RhinoIntersect.MeshMeshFast((Mesh)a, (Mesh)b) switch {
                    Line[] { Length: > 0 } lines => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from l in lines select l.From, .. from l in lines select l.To], [], [], [], [], [])),
                    null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed),
                    _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
                },
            },
            [(typeof(Mesh), typeof(Ray3d))] = (a, b, _, _) => RhinoIntersect.MeshRay((Mesh)a, (Ray3d)b) switch {
                double d when d >= 0d => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Ray3d)b).PointAt(d)], [], [d], [], [], [])),
                _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            },
            [(typeof(Mesh), typeof(Plane))] = (a, b, _, _) => PolylineProcessor(RhinoIntersect.MeshPlane((Mesh)a, (Plane)b)),
            [(typeof(Mesh), typeof(Line))] = (a, b, _, opts) => MeshIntersectionHandler((Mesh)a, b, opts.Sorted,
                ((pts, ids) => pts switch { { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], ids ?? [], [])), _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed) },
                pts => pts switch { { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], [], [])), null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty) })),
            [(typeof(Mesh), typeof(PolylineCurve))] = (a, b, _, opts) => MeshIntersectionHandler((Mesh)a, b, opts.Sorted,
                ((pts, ids) => pts switch { { Length: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts, [], [], [], ids ?? [], [])), _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed) },
                pts => ResultFactory.Create(value: new Intersect.IntersectionOutput(pts ?? [], [], [], [], [], [])))),
            [(typeof(Line), typeof(Line))] = (a, b, tol, _) => RhinoIntersect.LineLine((Line)a, (Line)b, out double pa, out double pb, tol, finiteSegments: false)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(pa)], [], [pa], [pb], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(Line), typeof(BoundingBox))] = (a, b, tol, _) => RhinoIntersect.LineBox((Line)a, (BoundingBox)b, tol, out Interval interval)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(interval.Min), ((Line)a).PointAt(interval.Max)], [], [interval.Min, interval.Max], [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(Line), typeof(Plane))] = (a, b, _, _) => RhinoIntersect.LinePlane((Line)a, (Plane)b, out double param)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(param)], [], [param], [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(Line), typeof(Sphere))] = (a, b, tol, _) => TwoPointHandler((int)RhinoIntersect.LineSphere((Line)a, (Sphere)b, out Point3d p1, out Point3d p2), p1, p2, tol, null),
            [(typeof(Line), typeof(Cylinder))] = (a, b, tol, _) => TwoPointHandler((int)RhinoIntersect.LineCylinder((Line)a, (Cylinder)b, out Point3d p1, out Point3d p2), p1, p2, tol, null),
            [(typeof(Line), typeof(Circle))] = (a, b, tol, _) => {
                int count = (int)RhinoIntersect.LineCircle((Line)a, (Circle)b, out double t1, out Point3d p1, out double t2, out Point3d p2);
                return TwoPointHandler(count, p1, p2, tol, count > 1 ? [t1, t2] : count > 0 ? [t1] : null);
            },
            [(typeof(Plane), typeof(Plane))] = (a, b, _, _) => RhinoIntersect.PlanePlane((Plane)a, (Plane)b, out Line line)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new LineCurve(line)], [], [], [], []))
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = (a, b, _, _) => {
                (Plane planeA, Plane planeB) = (ValueTuple<Plane, Plane>)a;
                return RhinoIntersect.PlanePlanePlane(planeA, planeB, (Plane)b, out Point3d point)
                    ? ResultFactory.Create(value: new Intersect.IntersectionOutput([point], [], [], [], [], []))
                    : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty);
            },
            [(typeof(Plane), typeof(Circle))] = (a, b, _, _) => RhinoIntersect.PlaneCircle((Plane)a, (Circle)b, out double t1, out double t2) switch {
                PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)b).PointAt(t1)], [], [], [t1], [], [])),
                PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)b).PointAt(t1), ((Circle)b).PointAt(t2)], [], [], [t1, t2], [], [])),
                _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            },
            [(typeof(Plane), typeof(Sphere))] = (a, b, _, _) => CircleHandler((int)RhinoIntersect.PlaneSphere((Plane)a, (Sphere)b, out Circle c), c),
            [(typeof(Plane), typeof(BoundingBox))] = (a, b, _, _) => (RhinoIntersect.PlaneBoundingBox((Plane)a, (BoundingBox)b, out Polyline poly), poly) switch {
                (true, Polyline { Count: > 0 } pl) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pt in pl select pt], [], [], [], [], [pl])),
                _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            },
            [(typeof(Sphere), typeof(Sphere))] = (a, b, _, _) => CircleHandler((int)RhinoIntersect.SphereSphere((Sphere)a, (Sphere)b, out Circle c), c),
            [(typeof(Circle), typeof(Circle))] = (a, b, tol, _) => TwoPointHandler((int)RhinoIntersect.CircleCircle((Circle)a, (Circle)b, out Point3d p1, out Point3d p2), p1, p2, tol, null),
            [(typeof(Arc), typeof(Arc))] = (a, b, tol, _) => TwoPointHandler((int)RhinoIntersect.ArcArc((Arc)a, (Arc)b, out Point3d p1, out Point3d p2), p1, p2, tol, null),
        }.ToFrozenDictionary();

    private static Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>? FindDispatcher(Type t1, Type t2) {
        (Type, Type) key = (t1, t2);
        return _dispatch.TryGetValue(key, out Func<object, object, double, Intersect.IntersectionOptions, Result<Intersect.IntersectionOutput>>? dispatcher)
            ? dispatcher
            : (t1.BaseType, t2.BaseType) switch {
                (Type base1, _) when base1 != typeof(object) => FindDispatcher(base1, t2),
                (_, Type base2) when base2 != typeof(object) => FindDispatcher(t1, base2),
                _ => null,
            };
    }

    [Pure]
    internal static Result<Intersect.IntersectionOutput> ExecutePair<T1, T2>(T1 a, T2 b, IGeometryContext ctx, Intersect.IntersectionOptions opts) where T1 : notnull where T2 : notnull {
        double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;

        return (a, b, opts) switch {
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) =>
                ProjectionHandler(pts, breps, dir, opts.WithIndices, tolerance),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) =>
                ProjectionHandler(pts, meshes, dir, opts.WithIndices, tolerance),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidRay),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when hits <= 0 =>
                ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits),
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
