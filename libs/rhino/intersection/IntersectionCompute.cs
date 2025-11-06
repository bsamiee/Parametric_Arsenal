using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using RhinoIntersect = Rhino.Geometry.Intersect.Intersection;

namespace Arsenal.Rhino.Intersection;

/// <summary>Dense intersection computation strategies using polymorphic type-based dispatch.</summary>
internal static class IntersectionCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<IntersectionResult>> Execute<T1, T2>(T1 a, T2 b, IGeometryContext ctx, IntersectionConfig cfg) where T1 : notnull where T2 : notnull =>
        (cfg.ValidateInputs, a, b) switch {
            (true, GeometryBase ga, GeometryBase gb) => ResultFactory.Create(value: (ga, gb))
                .Validate(args: [ctx, ValidationMode.Standard])
                .Bind(_ => Compute(a, b, ctx, cfg)),
            (true, GeometryBase ga, _) => ResultFactory.Create(value: ga)
                .Validate(args: [ctx, ValidationMode.Standard])
                .Bind(_ => Compute(a, b, ctx, cfg)),
            _ => Compute(a, b, ctx, cfg),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> Compute<T1, T2>(T1 a, T2 b, IGeometryContext ctx, IntersectionConfig cfg) where T1 : notnull where T2 : notnull {
        double tol = cfg.Tolerance ?? ctx.AbsoluteTolerance;
        return (a, b, cfg) switch {
            (Curve ca, Curve cb, _) => CurveCurve(ca, cb, tol),
            (Curve ca, BrepFace fb, _) => CurveBrepFace(ca, fb, tol),
            (Curve ca, Brep bb, _) => CurveBrep(ca, bb, tol),
            (Curve ca, Surface sb, _) => CurveSurface(ca, sb, tol),
            (Curve ca, Plane pb, _) => CurvePlane(ca, pb, tol),
            (Curve ca, Line lb, _) => CurveLine(ca, lb, tol),
            (Brep ba, Brep bb, _) => BrepBrep(ba, bb, tol),
            (Brep ba, Plane pb, _) => BrepPlane(ba, pb, tol),
            (Brep ba, Surface sb, _) => BrepSurface(ba, sb, tol),
            (Surface sa, Surface sb, _) => SurfaceSurface(sa, sb, tol),
            (Mesh ma, Mesh mb, _) => MeshMesh(ma, mb),
            (Mesh ma, Ray3d rb, _) => MeshRay(ma, rb),
            (Mesh ma, Plane pb, _) => MeshPlane(ma, pb),
            (Mesh ma, Line lb, _) => MeshLine(ma, lb),
            (Mesh ma, PolylineCurve pcb, _) => MeshPolyline(ma, pcb),
            (Line la, Line lb, _) => LineLine(la, lb, tol),
            (Line la, BoundingBox boxb, _) => LineBox(la, boxb, tol),
            (Line la, Plane pb, _) => LinePlane(la, pb),
            (Line la, Sphere sb, _) => LineSphere(la, sb, ctx),
            (Line la, Cylinder cylb, _) => LineCylinder(la, cylb, ctx),
            (Line la, Circle cb, _) => LineCircle(la, cb),
            (Plane pa, Plane pb, _) => PlanePlane(pa, pb),
            (ValueTuple<Plane, Plane> planes, Plane p3b, _) => PlanePlanePlane(planes.Item1, planes.Item2, p3b),
            (Plane pa, Circle cb, _) => PlaneCircle(pa, cb),
            (Plane pa, Sphere sb, _) => PlaneSphere(pa, sb),
            (Plane pa, BoundingBox boxb, _) => PlaneBoundingBox(pa, boxb),
            (Sphere sa, Sphere sb, _) => SphereSphere(sa, sb),
            (Circle ca, Circle cb, _) => CircleCircle(ca, cb, ctx),
            (Arc aa, Arc ab, _) => ArcArc(aa, ab, ctx),
            (Point3d[] pts, Brep[] breps, { ProjectionDirection: Vector3d dir }) when dir.IsValid && dir.Length > RhinoMath.ZeroTolerance =>
                ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToBreps(breps, pts, dir, RhinoMath.ZeroTolerance)])).Map(r => (IReadOnlyList<IntersectionResult>)[r]),
            (Point3d[] pts, Mesh[] meshes, { ProjectionDirection: Vector3d dir }) when dir.IsValid && dir.Length > RhinoMath.ZeroTolerance =>
                ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToMeshes(meshes, pts, dir, RhinoMath.ZeroTolerance)])).Map(r => (IReadOnlyList<IntersectionResult>)[r]),
            (Ray3d ray, GeometryBase[] geoms, { MaxHitCount: int hits }) when ray.Direction.Length > RhinoMath.ZeroTolerance && hits > 0 =>
                ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.RayShoot(ray, geoms, hits)])).Map(r => (IReadOnlyList<IntersectionResult>)[r]),
            (_, _, { ProjectionDirection: Vector3d dir }) when !dir.IsValid || dir.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Parameters.InvalidProjectionDirection),
            (Ray3d { Direction.Length: <= RhinoMath.ZeroTolerance }, _, _) =>
                ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Parameters.InvalidRayDirection),
            (_, _, { MaxHitCount: null or <= 0 }) when a is Ray3d =>
                ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Parameters.InvalidMaxHitCount),
            _ => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };
    }

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> CurveCurve(Curve ca, Curve cb, double tol) =>
        RhinoIntersect.CurveCurve(ca, cb, tol, tol) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Count: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
#pragma warning disable MA0007 // Record primary constructor parameters cannot have trailing commas
            CurveIntersections r when r.Any(e => e.IsOverlap) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new(
                Points: [.. from e in r select e.PointA],
                Curves: [.. from e in r where e.IsOverlap from c in new[] { ca.Trim(e.OverlapA) } where c is not null select c],
                ParametersA: [.. from e in r select e.ParameterA],
                ParametersB: [.. from e in r select e.ParameterB])]),
#pragma warning restore MA0007
#pragma warning disable MA0007 // Record primary constructor parameters cannot have trailing commas
            CurveIntersections r => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new(
                Points: [.. from e in r select e.PointA],
                Curves: null,
                ParametersA: [.. from e in r select e.ParameterA],
                ParametersB: [.. from e in r select e.ParameterB])]),
#pragma warning restore MA0007
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> CurveSurface(Curve ca, Surface sb, double tol) =>
        RhinoIntersect.CurveSurface(ca, sb, tol, tol) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Count: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
#pragma warning disable MA0007 // Record primary constructor parameters cannot have trailing commas
            CurveIntersections r => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new(
                Points: [.. from e in r select e.PointA],
                Curves: null,
                ParametersA: [.. from e in r select e.ParameterA],
                ParametersB: [.. from e in r select e.ParameterB])]),
#pragma warning restore MA0007
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> CurveBrep(Curve ca, Brep bb, double tol) =>
        (RhinoIntersect.CurveBrep(ca, bb, tol, out Curve[] curves, out Point3d[] points), curves, points) switch {
            (false, _, _) => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            (true, { Length: 0 }, { Length: 0 }) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            (true, Curve[] c, Point3d[] p) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. p], c.Length > 0 ? [.. c] : null)]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> CurveBrepFace(Curve ca, BrepFace fb, double tol) =>
        (RhinoIntersect.CurveBrepFace(ca, fb, tol, out Curve[] curves, out Point3d[] points), curves, points) switch {
            (false, _, _) => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            (true, { Length: 0 }, { Length: 0 }) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            (true, Curve[] c, Point3d[] p) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. p], c.Length > 0 ? [.. c] : null)]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> CurvePlane(Curve ca, Plane pb, double tol) =>
        RhinoIntersect.CurvePlane(ca, pb, tol) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Count: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            CurveIntersections r => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. from e in r select e.PointA], Curves: null, [.. from e in r select e.ParameterA])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> CurveLine(Curve ca, Line lb, double tol) =>
        RhinoIntersect.CurveLine(ca, lb, tol, tol) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Count: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            CurveIntersections r => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. from e in r select e.PointA], Curves: null, [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> BrepBrep(Brep ba, Brep bb, double tol) =>
        (RhinoIntersect.BrepBrep(ba, bb, tol, out Curve[] curves, out Point3d[] points), curves, points) switch {
            (false, _, _) => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            (true, { Length: 0 }, { Length: 0 }) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            (true, Curve[] c, Point3d[] p) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. p], c.Length > 0 ? [.. c] : null)]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> BrepPlane(Brep ba, Plane pb, double tol) =>
        (RhinoIntersect.BrepPlane(ba, pb, tol, out Curve[] curves, out Point3d[] points), curves, points) switch {
            (false, _, _) => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            (true, { Length: 0 }, { Length: 0 }) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            (true, Curve[] c, Point3d[] p) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. p], c.Length > 0 ? [.. c] : null)]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> BrepSurface(Brep ba, Surface sb, double tol) =>
        (RhinoIntersect.BrepSurface(ba, sb, tol, out Curve[] curves, out Point3d[] points), curves, points) switch {
            (false, _, _) => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            (true, { Length: 0 }, { Length: 0 }) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            (true, Curve[] c, Point3d[] p) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. p], c.Length > 0 ? [.. c] : null)]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> SurfaceSurface(Surface sa, Surface sb, double tol) =>
        (RhinoIntersect.SurfaceSurface(sa, sb, tol, out Curve[] curves, out Point3d[] points), curves, points) switch {
            (false, _, _) => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            (true, { Length: 0 }, { Length: 0 }) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            (true, Curve[] c, Point3d[] p) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. p], c.Length > 0 ? [.. c] : null)]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> MeshMesh(Mesh ma, Mesh mb) =>
        RhinoIntersect.MeshMeshFast(ma, mb) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Length: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            Line[] lines => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. from l in lines select l.From, .. from l in lines select l.To])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> MeshRay(Mesh ma, Ray3d rb) =>
        RhinoIntersect.MeshRay(ma, rb) switch {
            < 0d => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            double d => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([rb.PointAt(d)], Curves: null, [d])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> MeshPlane(Mesh ma, Plane pb) =>
        RhinoIntersect.MeshPlane(ma, pb) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Length: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            Polyline[] sections => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. from pl in sections from pt in pl select pt], Curves: null, ParametersA: null, ParametersB: null, FaceIndices: null, [.. sections])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> MeshLine(Mesh ma, Line lb) =>
        RhinoIntersect.MeshLine(ma, lb) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Length: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            Point3d[] points => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. points])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> MeshPolyline(Mesh ma, PolylineCurve pcb) =>
        RhinoIntersect.MeshPolyline(ma, pcb, out int[] ids) switch {
            null => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.ComputationFailed),
            { Length: 0 } => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            Point3d[] points => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. points], Curves: null, ParametersA: null, ParametersB: null, ids.Length > 0 ? [.. ids] : null)]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> LineLine(Line la, Line lb, double tol) =>
        RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tol, finiteSegments: false) switch {
            false => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            true => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([la.PointAt(pa)], Curves: null, [pa], [pb])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> LineBox(Line la, BoundingBox boxb, double tol) =>
        RhinoIntersect.LineBox(la, boxb, tol, out Interval interval) switch {
            false => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            true => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([la.PointAt(interval.Min), la.PointAt(interval.Max)], Curves: null, [interval.Min, interval.Max])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> LinePlane(Line la, Plane pb) =>
        RhinoIntersect.LinePlane(la, pb, out double param) switch {
            false => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            true => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([la.PointAt(param)], Curves: null, [param])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> LineSphere(Line la, Sphere sb, IGeometryContext ctx) =>
        ((int)RhinoIntersect.LineSphere(la, sb, out Point3d p1, out Point3d p2), p1, p2) switch {
            ( > 1, Point3d pt1, Point3d pt2) when pt1.DistanceTo(pt2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1, pt2])]),
            ( > 0, Point3d pt1, _) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> LineCylinder(Line la, Cylinder cylb, IGeometryContext ctx) =>
        ((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d p1, out Point3d p2), p1, p2) switch {
            ( > 1, Point3d pt1, Point3d pt2) when pt1.DistanceTo(pt2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1, pt2])]),
            ( > 0, Point3d pt1, _) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> LineCircle(Line la, Circle cb) =>
        ((int)RhinoIntersect.LineCircle(la, cb, out double t1, out Point3d p1, out double t2, out Point3d p2), p1, p2, t1, t2) switch {
            ( > 1, Point3d pt1, Point3d pt2, double param1, double param2) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1, pt2], Curves: null, [param1, param2])]),
            ( > 0, Point3d pt1, _, double param1, _) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1], Curves: null, [param1])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> PlanePlane(Plane pa, Plane pb) =>
        RhinoIntersect.PlanePlane(pa, pb, out Line line) switch {
            false => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            true => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([], [new LineCurve(line)])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> PlanePlanePlane(Plane p1, Plane p2, Plane p3) =>
        RhinoIntersect.PlanePlanePlane(p1, p2, p3, out Point3d point) switch {
            false => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            true => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([point])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> PlaneCircle(Plane pa, Circle cb) =>
        RhinoIntersect.PlaneCircle(pa, cb, out double t1, out double t2) switch {
            PlaneCircleIntersection.Tangent => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([cb.PointAt(t1)], Curves: null, ParametersA: null, [t1])]),
            PlaneCircleIntersection.Secant => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([cb.PointAt(t1), cb.PointAt(t2)], Curves: null, ParametersA: null, [t1, t2])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> PlaneSphere(Plane pa, Sphere sb) =>
        ((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle circle), circle) switch {
            (1, Circle c) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([], [new ArcCurve(c)])]),
            (2, Circle c) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([c.Center])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> PlaneBoundingBox(Plane pa, BoundingBox boxb) =>
        (RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly), poly) switch {
            (false, _) or (true, null or { Count: 0 }) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
            (true, Polyline pl) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([.. from pt in pl select pt], Curves: null, ParametersA: null, ParametersB: null, FaceIndices: null, [pl])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> SphereSphere(Sphere sa, Sphere sb) =>
        ((int)RhinoIntersect.SphereSphere(sa, sb, out Circle circle), circle) switch {
            (1, Circle c) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([], [new ArcCurve(c)])]),
            (2, Circle c) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([c.Center])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> CircleCircle(Circle ca, Circle cb, IGeometryContext ctx) =>
        ((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d p1, out Point3d p2), p1, p2) switch {
            ( > 1, Point3d pt1, Point3d pt2) when pt1.DistanceTo(pt2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1, pt2])]),
            ( > 0, Point3d pt1, _) => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };

    [Pure]
    private static Result<IReadOnlyList<IntersectionResult>> ArcArc(Arc aa, Arc ab, IGeometryContext ctx) =>
        ((int)RhinoIntersect.ArcArc(aa, ab, out Point3d p1, out Point3d p2), p1, p2) switch {
            ( > 1, Point3d pt1, Point3d pt2) when pt1.DistanceTo(pt2) > ctx.AbsoluteTolerance && pt1.IsValid => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1, pt2])]),
            ( > 0, Point3d pt1, _) when pt1.IsValid => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([pt1])]),
            _ => ResultFactory.Create(value: (IReadOnlyList<IntersectionResult>)[new([])]),
        };
}
