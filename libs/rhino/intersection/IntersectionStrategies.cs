using System.Collections.Frozen;
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

/// <summary>Unified intersection result containing points, curves, and parametric information.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Internal result type colocated with strategies")]
internal sealed record IntersectionResult(
    IReadOnlyList<Point3d> Points,
    IntersectionMethod Method,
    IReadOnlyList<Curve>? Curves = null,
    IReadOnlyList<double>? ParametersA = null,
    IReadOnlyList<double>? ParametersB = null,
    IReadOnlyList<int>? FaceIndices = null,
    IReadOnlyList<Polyline>? Sections = null);

/// <summary>Dense intersection strategy dispatcher with SDK method mapping and validation configuration.</summary>
internal static class IntersectionStrategies {
    private static readonly FrozenDictionary<(IntersectionMethod, Type, Type), (ValidationMode Mode, Func<object, object, double, IGeometryContext, Vector3d?, int?, Result<IntersectionResult>>? Computer)> _dispatch =
        new Dictionary<(IntersectionMethod, Type, Type), (ValidationMode, Func<object, object, double, IGeometryContext, Vector3d?, int?, Result<IntersectionResult>>?)> {
            [(IntersectionMethod.CurveCurve, typeof(Curve), typeof(Curve))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(IntersectionMethod.CurveSurface, typeof(Curve), typeof(Surface))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.CurveBrep, typeof(Curve), typeof(Brep))] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [(IntersectionMethod.CurveBrepFace, typeof(Curve), typeof(BrepFace))] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [(IntersectionMethod.CurvePlane, typeof(Curve), typeof(Plane))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(IntersectionMethod.CurveLine, typeof(Curve), typeof(Line))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(IntersectionMethod.CurveSelf, typeof(Curve), typeof(Curve))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null),
            [(IntersectionMethod.BrepBrep, typeof(Brep), typeof(Brep))] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [(IntersectionMethod.BrepPlane, typeof(Brep), typeof(Plane))] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [(IntersectionMethod.BrepSurface, typeof(Brep), typeof(Surface))] = (ValidationMode.Standard | ValidationMode.Topology, null),
            [(IntersectionMethod.SurfaceSurface, typeof(Surface), typeof(Surface))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.MeshMesh, typeof(Mesh), typeof(Mesh))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.MeshMeshAccurate, typeof(Mesh), typeof(Mesh))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.MeshRay, typeof(Mesh), typeof(Ray3d))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.MeshPlane, typeof(Mesh), typeof(Plane))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.MeshLine, typeof(Mesh), typeof(Line))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.MeshLineSorted, typeof(Mesh), typeof(Line))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.MeshPolyline, typeof(Mesh), typeof(PolylineCurve))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.MeshPolylineSorted, typeof(Mesh), typeof(PolylineCurve))] = (ValidationMode.MeshSpecific, null),
            [(IntersectionMethod.LineLine, typeof(Line), typeof(Line))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.LineBox, typeof(Line), typeof(BoundingBox))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.LinePlane, typeof(Line), typeof(Plane))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.LineSphere, typeof(Line), typeof(Sphere))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.LineCylinder, typeof(Line), typeof(Cylinder))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.LineCircle, typeof(Line), typeof(Circle))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.PlanePlane, typeof(Plane), typeof(Plane))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.PlanePlanePlane, typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.PlaneCircle, typeof(Plane), typeof(Circle))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.PlaneSphere, typeof(Plane), typeof(Sphere))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.PlaneBoundingBox, typeof(Plane), typeof(BoundingBox))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.SphereSphere, typeof(Sphere), typeof(Sphere))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.CircleCircle, typeof(Circle), typeof(Circle))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.ArcArc, typeof(Arc), typeof(Arc))] = (ValidationMode.Standard, null),
            [(IntersectionMethod.ProjectPointsToBreps, typeof(Point3d[]), typeof(Brep[]))] = (ValidationMode.Standard | ValidationMode.Topology, (a, b, _, _, dir, _) =>
                dir is Vector3d d && d.IsValid && d.Length > RhinoMath.ZeroTolerance ? ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToBreps((Brep[])b, (Point3d[])a, d, RhinoMath.ZeroTolerance)], IntersectionMethod.ProjectPointsToBreps)) : ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidProjectionDirection)),
            [(IntersectionMethod.ProjectPointsToBrepsWithIndices, typeof(Point3d[]), typeof(Brep[]))] = (ValidationMode.Standard | ValidationMode.Topology, (a, b, _, _, dir, _) =>
                dir is Vector3d d && d.IsValid && d.Length > RhinoMath.ZeroTolerance ? ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToBrepsEx((Brep[])b, (Point3d[])a, d, RhinoMath.ZeroTolerance, out int[] ids)], IntersectionMethod.ProjectPointsToBrepsWithIndices, FaceIndices: ids.Length > 0 ? [.. ids] : null)) : ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidProjectionDirection)),
            [(IntersectionMethod.ProjectPointsToMeshes, typeof(Point3d[]), typeof(Mesh[]))] = (ValidationMode.MeshSpecific, (a, b, _, _, dir, _) =>
                dir is Vector3d d && d.IsValid && d.Length > RhinoMath.ZeroTolerance ? ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToMeshes((Mesh[])b, (Point3d[])a, d, RhinoMath.ZeroTolerance)], IntersectionMethod.ProjectPointsToMeshes)) : ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidProjectionDirection)),
            [(IntersectionMethod.ProjectPointsToMeshesWithIndices, typeof(Point3d[]), typeof(Mesh[]))] = (ValidationMode.MeshSpecific, (a, b, _, _, dir, _) =>
                dir is Vector3d d && d.IsValid && d.Length > RhinoMath.ZeroTolerance ? ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToMeshesEx((Mesh[])b, (Point3d[])a, d, RhinoMath.ZeroTolerance, out int[] ids)], IntersectionMethod.ProjectPointsToMeshesWithIndices, FaceIndices: ids.Length > 0 ? [.. ids] : null)) : ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidProjectionDirection)),
            [(IntersectionMethod.RayShoot, typeof(Ray3d), typeof(GeometryBase[]))] = (ValidationMode.Standard, (a, b, _, _, _, hits) =>
                (a, hits) switch {
                    (Ray3d { Direction.Length: <= RhinoMath.ZeroTolerance }, _) => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidRayDirection),
                    (_, null or <= 0) => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidMaxHitCount),
                    (Ray3d ray, int h) => ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.RayShoot(ray, (GeometryBase[])b, h)], IntersectionMethod.RayShoot)),
                    _ => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidMaxHitCount),
                }),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IntersectionResult> Intersect<T1, T2>(T1 geometryA, T2 geometryB, IntersectionMethod method, IGeometryContext context, double? tolerance = null, Vector3d? projectionDirection = null, int? maxHitCount = null) where T1 : notnull where T2 : notnull =>
        _dispatch.TryGetValue((method, typeof(T1), typeof(T2)), out (ValidationMode mode, Func<object, object, double, IGeometryContext, Vector3d?, int?, Result<IntersectionResult>>? computer) config) switch {
            true when config.computer is not null => ResultFactory.Create(value: geometryA).Filter(_ => geometryA is GeometryBase, IntersectionErrors.Operation.UnsupportedMethod)
                .Validate(args: [context, config.mode]).Bind(_ => config.computer(geometryA, geometryB, tolerance ?? context.AbsoluteTolerance, context, projectionDirection, maxHitCount)),
            true => (method, geometryA, geometryB) switch {
                (IntersectionMethod.CurveSelf, Curve c, _) => ResultFactory.Create(value: c).Validate(args: [context, config.mode])
                    .Bind(_ => Compute(c, c, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount)),
                (IntersectionMethod.PlanePlanePlane, ValueTuple<Plane, Plane> planes, Plane p3) =>
                    Compute(planes, p3, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount),
                (_, GeometryBase ga, GeometryBase gb) => ResultFactory.Create(value: (ga, gb)).Validate(args: [context, config.mode])
                    .Bind(_ => Compute(geometryA, geometryB, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount)),
                (_, GeometryBase ga, _) => ResultFactory.Create(value: ga).Validate(args: [context, config.mode])
                    .Bind(_ => Compute(geometryA, geometryB, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount)),
                _ => Compute(geometryA, geometryB, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount),
            },
            false => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };

    [Pure]
    private static Result<IntersectionResult> Compute<T1, T2>(T1 a, T2 b, IntersectionMethod m, IGeometryContext ctx, double tol, Vector3d? dir, int? maxHits) where T1 : notnull where T2 : notnull =>
        (m, a, b, dir, maxHits) switch {
            (IntersectionMethod.CurveCurve or IntersectionMethod.CurveSurface or IntersectionMethod.CurvePlane or IntersectionMethod.CurveLine or IntersectionMethod.CurveSelf, Curve ca, _, _, _) =>
                ((m, ca, b) switch { (IntersectionMethod.CurveCurve, _, Curve cb) => RhinoIntersect.CurveCurve(ca, cb, tol, tol), (IntersectionMethod.CurveSurface, _, Surface sb) => RhinoIntersect.CurveSurface(ca, sb, tol, tol), (IntersectionMethod.CurvePlane, _, Plane pb) => RhinoIntersect.CurvePlane(ca, pb, tol), (IntersectionMethod.CurveLine, _, Line lb) => RhinoIntersect.CurveLine(ca, lb, tol, tol), (IntersectionMethod.CurveSelf, _, _) => RhinoIntersect.CurveSelf(ca, tol), _ => null }) switch {
                    null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                    CurveIntersections { Count: > 0 } r when m is IntersectionMethod.CurveCurve && r.Any(e => e.IsOverlap) => ResultFactory.Create(value: new IntersectionResult([.. from e in r select e.PointA], m, [.. from e in r where e.IsOverlap from c in new[] { ca.Trim(e.OverlapA) } where c is not null select c], [.. from e in r select e.ParameterA], [.. from e in r select e.ParameterB])),
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult([.. from e in r select e.PointA], m, Curves: null, ParametersA: [.. from e in r select e.ParameterA], ParametersB: (m is IntersectionMethod.CurveSurface or IntersectionMethod.CurveLine or IntersectionMethod.CurveCurve) ? [.. from e in r select e.ParameterB] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], m)),
                },
            (IntersectionMethod.CurveBrep or IntersectionMethod.CurveBrepFace or IntersectionMethod.BrepBrep or IntersectionMethod.BrepPlane or IntersectionMethod.BrepSurface or IntersectionMethod.SurfaceSurface, _, _, _, _) =>
                ((m, a, b) switch {
                    (IntersectionMethod.CurveBrep, Curve ca, Brep bb) => (RhinoIntersect.CurveBrep(ca, bb, tol, out Curve[] c, out Point3d[] p), c, p),
                    (IntersectionMethod.CurveBrepFace, Curve ca, BrepFace fb) => (RhinoIntersect.CurveBrepFace(ca, fb, tol, out Curve[] c, out Point3d[] p), c, p),
                    (IntersectionMethod.BrepBrep, Brep ba, Brep bb) => (RhinoIntersect.BrepBrep(ba, bb, tol, out Curve[] c, out Point3d[] p), c, p),
                    (IntersectionMethod.BrepPlane, Brep ba, Plane pb) => (RhinoIntersect.BrepPlane(ba, pb, tol, out Curve[] c, out Point3d[] p), c, p),
                    (IntersectionMethod.BrepSurface, Brep ba, Surface sb) => (RhinoIntersect.BrepSurface(ba, sb, tol, out Curve[] c, out Point3d[] p), c, p),
                    (IntersectionMethod.SurfaceSurface, Surface sa, Surface sb) => (RhinoIntersect.SurfaceSurface(sa, sb, tol, out Curve[] c, out Point3d[] p), c, p),
                    _ => (false, [], []),
                }) switch {
                    (true, Curve[] curves, Point3d[] points) when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult([.. points], m, curves.Length > 0 ? [.. curves] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], m)),
                },
            (IntersectionMethod.MeshMesh or IntersectionMethod.MeshMeshAccurate or IntersectionMethod.MeshRay or IntersectionMethod.MeshPlane or IntersectionMethod.MeshLine or IntersectionMethod.MeshLineSorted or IntersectionMethod.MeshPolyline or IntersectionMethod.MeshPolylineSorted, Mesh ma, _, _, _) =>
                (m, ma, b) switch {
                    (IntersectionMethod.MeshMesh, _, Mesh mb) => RhinoIntersect.MeshMeshFast(ma, mb) switch { Line[] { Length: > 0 } lines => ResultFactory.Create(value: new IntersectionResult([.. from l in lines select l.From, .. from l in lines select l.To], m)), null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.MeshMeshAccurate, _, Mesh mb) => RhinoIntersect.MeshMeshAccurate(ma, mb, tol) switch { Polyline[] { Length: > 0 } polylines => ResultFactory.Create(value: new IntersectionResult([.. from pl in polylines from pt in pl select pt], m)), null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.MeshRay, _, Ray3d rb) when RhinoIntersect.MeshRay(ma, rb) is double d && d >= 0d => ResultFactory.Create(value: new IntersectionResult([rb.PointAt(d)], m, Curves: null, ParametersA: [d])),
                    (IntersectionMethod.MeshPlane, _, Plane pb) => RhinoIntersect.MeshPlane(ma, pb) switch { Polyline[] { Length: > 0 } sections => ResultFactory.Create(value: new IntersectionResult([.. from pl in sections from pt in pl select pt], m, Curves: null, ParametersA: null, ParametersB: null, FaceIndices: null, Sections: [.. sections])), null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.MeshLine, _, Line lb) => RhinoIntersect.MeshLine(ma, lb) switch { Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult([.. points], m)), null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.MeshLineSorted, _, Line lb) when RhinoIntersect.MeshLineSorted(ma, lb, out int[] ids) is Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult([.. points], m, Curves: null, ParametersA: null, ParametersB: null, FaceIndices: ids.Length > 0 ? [.. ids] : null)),
                    (IntersectionMethod.MeshPolyline, _, PolylineCurve pcb) when RhinoIntersect.MeshPolyline(ma, pcb, out int[] ids) is Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult([.. points], m, Curves: null, ParametersA: null, ParametersB: null, FaceIndices: ids.Length > 0 ? [.. ids] : null)),
                    (IntersectionMethod.MeshPolylineSorted, _, PolylineCurve pcb) when RhinoIntersect.MeshPolylineSorted(ma, pcb, out int[] ids) is Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult([.. points], m, Curves: null, ParametersA: null, ParametersB: null, FaceIndices: ids.Length > 0 ? [.. ids] : null)),
                    (IntersectionMethod.MeshLineSorted or IntersectionMethod.MeshPolyline or IntersectionMethod.MeshPolylineSorted, _, _) => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                    _ => ResultFactory.Create(value: new IntersectionResult([], m)),
                },
            (IntersectionMethod.LineLine or IntersectionMethod.LineBox or IntersectionMethod.LinePlane or IntersectionMethod.LineSphere or IntersectionMethod.LineCylinder or IntersectionMethod.LineCircle, Line la, _, _, _) =>
                (m, la, b) switch {
                    (IntersectionMethod.LineLine, _, Line lb) when RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tol, finiteSegments: false) => ResultFactory.Create(value: new IntersectionResult([la.PointAt(pa)], m, Curves: null, ParametersA: [pa], ParametersB: [pb])),
                    (IntersectionMethod.LineBox, _, BoundingBox boxb) when RhinoIntersect.LineBox(la, boxb, tol, out Interval interval) => ResultFactory.Create(value: new IntersectionResult([la.PointAt(interval.Min), la.PointAt(interval.Max)], m, Curves: null, ParametersA: [interval.Min, interval.Max])),
                    (IntersectionMethod.LinePlane, _, Plane pb) when RhinoIntersect.LinePlane(la, pb, out double param) => ResultFactory.Create(value: new IntersectionResult([la.PointAt(param)], m, Curves: null, ParametersA: [param])),
                    (IntersectionMethod.LineSphere, _, Sphere sb) => ((int)RhinoIntersect.LineSphere(la, sb, out Point3d p1, out Point3d p2)) switch { > 1 => ResultFactory.Create(value: new IntersectionResult(p1.DistanceTo(p2) > ctx.AbsoluteTolerance ? [p1, p2] : [p1], m)), > 0 => ResultFactory.Create(value: new IntersectionResult([p1], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.LineCylinder, _, Cylinder cylb) => ((int)RhinoIntersect.LineCylinder(la, cylb, out Point3d p1, out Point3d p2)) switch { > 1 => ResultFactory.Create(value: new IntersectionResult(p1.DistanceTo(p2) > ctx.AbsoluteTolerance ? [p1, p2] : [p1], m)), > 0 => ResultFactory.Create(value: new IntersectionResult([p1], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.LineCircle, _, Circle cb) => ((int)RhinoIntersect.LineCircle(la, cb, out double t1, out Point3d p1, out double t2, out Point3d p2)) switch { > 1 when p1.DistanceTo(p2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: new IntersectionResult([p1, p2], m, Curves: null, ParametersA: [t1, t2])), > 0 => ResultFactory.Create(value: new IntersectionResult([p1], m, Curves: null, ParametersA: [t1])), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    _ => ResultFactory.Create(value: new IntersectionResult([], m)),
                },
            (IntersectionMethod.PlanePlane or IntersectionMethod.PlanePlanePlane or IntersectionMethod.PlaneCircle or IntersectionMethod.PlaneSphere or IntersectionMethod.PlaneBoundingBox or IntersectionMethod.SphereSphere, _, _, _, _) =>
                (m, a, b) switch {
                    (IntersectionMethod.PlanePlane, Plane pa, Plane pb) when RhinoIntersect.PlanePlane(pa, pb, out Line line) => ResultFactory.Create(value: new IntersectionResult([], m, [new LineCurve(line)])),
                    (IntersectionMethod.PlanePlanePlane, ValueTuple<Plane, Plane> planes, Plane p3b) when RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3b, out Point3d point) => ResultFactory.Create(value: new IntersectionResult([point], m)),
                    (IntersectionMethod.PlaneCircle, Plane pa, Circle cb) => RhinoIntersect.PlaneCircle(pa, cb, out double t1, out double t2) switch { PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new IntersectionResult([cb.PointAt(t1)], m, Curves: null, ParametersA: null, ParametersB: [t1])), PlaneCircleIntersection.Secant => ResultFactory.Create(value: new IntersectionResult([cb.PointAt(t1), cb.PointAt(t2)], m, Curves: null, ParametersA: null, ParametersB: [t1, t2])), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.PlaneSphere or IntersectionMethod.SphereSphere, _, _) => ((m, a, b) switch { (IntersectionMethod.PlaneSphere, Plane pa, Sphere sb) => ((int)RhinoIntersect.PlaneSphere(pa, sb, out Circle c), c), (IntersectionMethod.SphereSphere, Sphere sa, Sphere sb) => ((int)RhinoIntersect.SphereSphere(sa, sb, out Circle c), c), _ => (0, default) }) switch { (1, Circle c) => ResultFactory.Create(value: new IntersectionResult([], m, Curves: [new ArcCurve(c)])), (2, Circle c) => ResultFactory.Create(value: new IntersectionResult([c.Center], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
                    (IntersectionMethod.PlaneBoundingBox, Plane pa, BoundingBox boxb) when RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly) && poly?.Count > 0 => ResultFactory.Create(value: new IntersectionResult([.. from pt in poly select pt], m, Curves: null, ParametersA: null, ParametersB: null, FaceIndices: null, Sections: [poly])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], m)),
                },
            (IntersectionMethod.CircleCircle or IntersectionMethod.ArcArc, _, _, _, _) => ((m, a, b) switch { (IntersectionMethod.CircleCircle, Circle ca, Circle cb) => ((int)RhinoIntersect.CircleCircle(ca, cb, out Point3d p1, out Point3d p2), p1, p2), (IntersectionMethod.ArcArc, Arc aa, Arc ab) => ((int)RhinoIntersect.ArcArc(aa, ab, out Point3d p1, out Point3d p2), p1, p2), _ => (0, Point3d.Unset, Point3d.Unset) }) switch { ( > 1, Point3d p1, Point3d p2) when p1.DistanceTo(p2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: new IntersectionResult([p1, p2], m)), ( > 0, Point3d p1, _) when m is IntersectionMethod.ArcArc && p1.IsValid => ResultFactory.Create(value: new IntersectionResult([p1], m)), ( > 0, Point3d p1, _) => ResultFactory.Create(value: new IntersectionResult([p1], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)) },
            _ => ResultFactory.Create(value: new IntersectionResult([], m)),
        };
}
