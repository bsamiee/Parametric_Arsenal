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

/// <summary>Internal intersection computation engine with RhinoCommon SDK algorithms and type-based dispatch.</summary>
internal static class IntersectionCore {
    private static readonly FrozenDictionary<(Type, Type), Func<object, object, double, Result<Intersect.IntersectionOutput>>> _dispatch =
        new Dictionary<(Type, Type), Func<object, object, double, Result<Intersect.IntersectionOutput>>> {
            [(typeof(Curve), typeof(Curve))] = (a, b, tol) => { Curve ca = (Curve)a; using CurveIntersections? r = RhinoIntersect.CurveCurve(ca, (Curve)b, tol, tol); return r switch {
                null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from e in r select e.PointA], [.. from e in r where e.IsOverlap let c = ca.Trim(e.OverlapA) where c is not null select c], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Curve), typeof(BrepFace))] = (a, b, tol) => RhinoIntersect.CurveBrepFace((Curve)a, (BrepFace)b, tol, out Curve[] c, out Point3d[] p) switch {
                true when c.Length > 0 && p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])), true when c.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])), true when p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Curve), typeof(Surface))] = (a, b, tol) => { using CurveIntersections? r = RhinoIntersect.CurveSurface((Curve)a, (Surface)b, tol, overlapTolerance: tol); return r switch {
                null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Curve), typeof(Plane))] = (a, b, tol) => { using CurveIntersections? r = RhinoIntersect.CurvePlane((Curve)a, (Plane)b, tol); return r switch {
                null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Curve), typeof(Line))] = (a, b, tol) => { using CurveIntersections? r = RhinoIntersect.CurveLine((Curve)a, (Line)b, tol, overlapTolerance: tol); return r switch {
                null => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), { Count: > 0 } => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from e in r select e.PointA], [], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Curve), typeof(Brep))] = (a, b, tol) => RhinoIntersect.CurveBrep((Curve)a, (Brep)b, tol, out Curve[] c, out Point3d[] p) switch {
                true when c.Length > 0 && p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])), true when c.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])), true when p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Brep), typeof(Brep))] = (a, b, tol) => RhinoIntersect.BrepBrep((Brep)a, (Brep)b, tol, out Curve[] c, out Point3d[] p) switch {
                true when c.Length > 0 && p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])), true when c.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])), true when p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Brep), typeof(Plane))] = (a, b, tol) => RhinoIntersect.BrepPlane((Brep)a, (Plane)b, tol, out Curve[] c, out Point3d[] p) switch {
                true when c.Length > 0 && p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])), true when c.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])), true when p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Brep), typeof(Surface))] = (a, b, tol) => RhinoIntersect.BrepSurface((Brep)a, (Surface)b, tol, out Curve[] c, out Point3d[] p) switch {
                true when c.Length > 0 && p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])), true when c.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])), true when p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Surface), typeof(Surface))] = (a, b, tol) => RhinoIntersect.SurfaceSurface((Surface)a, (Surface)b, tol, out Curve[] c, out Point3d[] p) switch {
                true when c.Length > 0 && p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [.. c], [], [], [], [])), true when c.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [.. c], [], [], [], [])), true when p.Length > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. p], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Line), typeof(Line))] = (a, b, tol) => RhinoIntersect.LineLine((Line)a, (Line)b, out double pa, out double pb, tol, finiteSegments: false)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(pa)], [], [pa], [pb], [], [])) : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(Line), typeof(BoundingBox))] = (a, b, tol) => RhinoIntersect.LineBox((Line)a, (BoundingBox)b, tol, out Interval iv)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(iv.Min), ((Line)a).PointAt(iv.Max)], [], [iv.Min, iv.Max], [], [], [])) : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(Line), typeof(Plane))] = (a, b, _) => RhinoIntersect.LinePlane((Line)a, (Plane)b, out double param)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([((Line)a).PointAt(param)], [], [param], [], [], [])) : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(Line), typeof(Sphere))] = (a, b, tol) => { int cnt = (int)RhinoIntersect.LineSphere((Line)a, (Sphere)b, out Point3d p1, out Point3d p2); return cnt switch {
                > 1 when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])), > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Line), typeof(Cylinder))] = (a, b, tol) => { int cnt = (int)RhinoIntersect.LineCylinder((Line)a, (Cylinder)b, out Point3d p1, out Point3d p2); return cnt switch {
                > 1 when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])), > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Line), typeof(Circle))] = (a, b, tol) => { int cnt = (int)RhinoIntersect.LineCircle((Line)a, (Circle)b, out double t1, out Point3d p1, out double t2, out Point3d p2); return cnt switch {
                > 1 when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [t1, t2], [], [], [])), > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [t1], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Plane), typeof(Plane))] = (a, b, _) => RhinoIntersect.PlanePlane((Plane)a, (Plane)b, out Line line)
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via result
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new LineCurve(line)], [], [], [], []))
#pragma warning restore IDISP004
                : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = (a, b, _) => { ValueTuple<Plane, Plane> planes = (ValueTuple<Plane, Plane>)a; return RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, (Plane)b, out Point3d point)
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([point], [], [], [], [], [])) : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty); },
            [(typeof(Plane), typeof(Circle))] = (a, b, _) => RhinoIntersect.PlaneCircle((Plane)a, (Circle)b, out double t1, out double t2) switch {
                PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)b).PointAt(t1)], [], [], [t1], [], [])), PlaneCircleIntersection.Secant => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Circle)b).PointAt(t1), ((Circle)b).PointAt(t2)], [], [], [t1, t2], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Plane), typeof(Sphere))] = (a, b, _) => (int)RhinoIntersect.PlaneSphere((Plane)a, (Sphere)b, out Circle c) switch {
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via result
                1 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(c)], [], [], [], [])),
#pragma warning restore IDISP004
                2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([c.Center], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Plane), typeof(BoundingBox))] = (a, b, _) => RhinoIntersect.PlaneBoundingBox((Plane)a, (BoundingBox)b, out Polyline poly) && poly?.Count > 0
                ? ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pt in poly select pt], [], [], [], [], [poly])) : ResultFactory.Create(value: Intersect.IntersectionOutput.Empty),
            [(typeof(Sphere), typeof(Sphere))] = (a, b, _) => (int)RhinoIntersect.SphereSphere((Sphere)a, (Sphere)b, out Circle c) switch {
#pragma warning disable IDISP004 // Don't ignore created IDisposable - ownership transferred to caller via result
                1 => ResultFactory.Create(value: new Intersect.IntersectionOutput([], [new ArcCurve(c)], [], [], [], [])),
#pragma warning restore IDISP004
                2 => ResultFactory.Create(value: new Intersect.IntersectionOutput([c.Center], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Circle), typeof(Circle))] = (a, b, tol) => { int cnt = (int)RhinoIntersect.CircleCircle((Circle)a, (Circle)b, out Point3d p1, out Point3d p2); return cnt switch {
                > 1 when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])), > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Arc), typeof(Arc))] = (a, b, tol) => { int cnt = (int)RhinoIntersect.ArcArc((Arc)a, (Arc)b, out Point3d p1, out Point3d p2); return cnt switch {
                > 1 when p1.DistanceTo(p2) > tol => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1, p2], [], [], [], [], [])), > 0 => ResultFactory.Create(value: new Intersect.IntersectionOutput([p1], [], [], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), }; },
            [(typeof(Mesh), typeof(Ray3d))] = (a, b, _) => RhinoIntersect.MeshRay((Mesh)a, (Ray3d)b) switch {
                double d when d >= 0d => ResultFactory.Create(value: new Intersect.IntersectionOutput([((Ray3d)b).PointAt(d)], [], [d], [], [], [])), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            [(typeof(Mesh), typeof(Plane))] = (a, b, _) => RhinoIntersect.MeshPlane((Mesh)a, (Plane)b) switch { Polyline[] { Length: > 0 } sections => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pl in sections from pt in pl select pt], [], [], [], [], [.. sections])), null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
        }.ToFrozenDictionary();

    [Pure]
    internal static Result<Intersect.IntersectionOutput> ExecutePair<T1, T2>(T1 a, T2 b, IGeometryContext ctx, Intersect.IntersectionOptions opts) where T1 : notnull where T2 : notnull {
        double tolerance = opts.Tolerance ?? ctx.AbsoluteTolerance;
        Type t1 = typeof(T1);
        Type t2 = typeof(T2);

        return (a, b, opts) switch {
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir, WithIndices: true }) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.ProjectPointsToBrepsEx(breps, pts, dir, ctx.AbsoluteTolerance, out int[] ids)], [], [], [], ids.Length > 0 ? [.. ids] : [], [])),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, ctx.AbsoluteTolerance)], [], [], [], [], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidProjection),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir, WithIndices: true }) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.ProjectPointsToMeshesEx(meshes, pts, dir, ctx.AbsoluteTolerance, out int[] ids)], [], [], [], ids.Length > 0 ? [.. ids] : [], [])),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, ctx.AbsoluteTolerance)], [], [], [], [], [])),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when ray.Direction.Length <= RhinoMath.ZeroTolerance => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidRay),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) when hits <= 0 => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.InvalidMaxHits),
            (Ray3d ray, GeometryBase[] geoms, { MaxHits: int hits }) => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. RhinoIntersect.RayShoot(ray, geoms, hits)], [], [], [], [], [])),
            (Mesh ma, Mesh mb, { Sorted: false }) => RhinoIntersect.MeshMeshFast(ma, mb) switch { Line[] { Length: > 0 } lines => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from l in lines select l.From, .. from l in lines select l.To], [], [], [], [], [])), null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            (Mesh ma, Mesh mb, { Sorted: true }) => RhinoIntersect.MeshMeshAccurate(ma, mb, tolerance) switch { Polyline[] { Length: > 0 } sections => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. from pl in sections from pt in pl select pt], [], [], [], [], [.. sections])), null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            (Mesh ma, Line lb, { Sorted: false }) => RhinoIntersect.MeshLine(ma, lb) switch { Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], [], [])), null => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), _ => ResultFactory.Create(value: Intersect.IntersectionOutput.Empty), },
            (Mesh ma, Line lb, { Sorted: true }) => RhinoIntersect.MeshLineSorted(ma, lb, out int[] ids) switch { Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], ids.Length > 0 ? [.. ids] : [], [])), _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), },
            (Mesh ma, PolylineCurve pc, { Sorted: false }) => RhinoIntersect.MeshPolyline(ma, pc, out int[] ids) switch { Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], ids.Length > 0 ? [.. ids] : [], [])), _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), },
            (Mesh ma, PolylineCurve pc, { Sorted: true }) => RhinoIntersect.MeshPolylineSorted(ma, pc, out int[] ids) switch { Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new Intersect.IntersectionOutput([.. points], [], [], [], ids.Length > 0 ? [.. ids] : [], [])), _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.IntersectionFailed), },
            _ when _dispatch.TryGetValue((t1, t2), out Func<object, object, double, Result<Intersect.IntersectionOutput>>? dispatch) => dispatch(a, b, tolerance),
            _ => ResultFactory.Create<Intersect.IntersectionOutput>(error: E.Geometry.UnsupportedIntersection),
        };
    }
}
