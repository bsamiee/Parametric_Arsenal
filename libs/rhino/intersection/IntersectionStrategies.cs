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
    private static readonly FrozenDictionary<(IntersectionMethod, Type, Type), ValidationMode> _config =
        new Dictionary<(IntersectionMethod, Type, Type), ValidationMode> {
            [(IntersectionMethod.CurveCurve, typeof(Curve), typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(IntersectionMethod.CurveSurface, typeof(Curve), typeof(Surface))] = ValidationMode.Standard,
            [(IntersectionMethod.CurveBrep, typeof(Curve), typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.CurveBrepFace, typeof(Curve), typeof(BrepFace))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.CurvePlane, typeof(Curve), typeof(Plane))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(IntersectionMethod.CurveLine, typeof(Curve), typeof(Line))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(IntersectionMethod.CurveSelf, typeof(Curve), typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(IntersectionMethod.BrepBrep, typeof(Brep), typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.BrepPlane, typeof(Brep), typeof(Plane))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.BrepSurface, typeof(Brep), typeof(Surface))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.SurfaceSurface, typeof(Surface), typeof(Surface))] = ValidationMode.Standard,
            [(IntersectionMethod.MeshMesh, typeof(Mesh), typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshMeshAccurate, typeof(Mesh), typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshRay, typeof(Mesh), typeof(Ray3d))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshPlane, typeof(Mesh), typeof(Plane))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshLine, typeof(Mesh), typeof(Line))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshLineSorted, typeof(Mesh), typeof(Line))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshPolyline, typeof(Mesh), typeof(PolylineCurve))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshPolylineSorted, typeof(Mesh), typeof(PolylineCurve))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.LineLine, typeof(Line), typeof(Line))] = ValidationMode.Standard,
            [(IntersectionMethod.LineBox, typeof(Line), typeof(BoundingBox))] = ValidationMode.Standard,
            [(IntersectionMethod.LinePlane, typeof(Line), typeof(Plane))] = ValidationMode.Standard,
            [(IntersectionMethod.LineSphere, typeof(Line), typeof(Sphere))] = ValidationMode.Standard,
            [(IntersectionMethod.LineCylinder, typeof(Line), typeof(Cylinder))] = ValidationMode.Standard,
            [(IntersectionMethod.LineCircle, typeof(Line), typeof(Circle))] = ValidationMode.Standard,
            [(IntersectionMethod.PlanePlane, typeof(Plane), typeof(Plane))] = ValidationMode.Standard,
            [(IntersectionMethod.PlanePlanePlane, typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = ValidationMode.Standard,
            [(IntersectionMethod.PlaneCircle, typeof(Plane), typeof(Circle))] = ValidationMode.Standard,
            [(IntersectionMethod.PlaneSphere, typeof(Plane), typeof(Sphere))] = ValidationMode.Standard,
            [(IntersectionMethod.PlaneBoundingBox, typeof(Plane), typeof(BoundingBox))] = ValidationMode.Standard,
            [(IntersectionMethod.SphereSphere, typeof(Sphere), typeof(Sphere))] = ValidationMode.Standard,
            [(IntersectionMethod.CircleCircle, typeof(Circle), typeof(Circle))] = ValidationMode.Standard,
            [(IntersectionMethod.ArcArc, typeof(Arc), typeof(Arc))] = ValidationMode.Standard,
            [(IntersectionMethod.ProjectPointsToBreps, typeof(Point3d[]), typeof(Brep[]))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.ProjectPointsToBrepsEx, typeof(Point3d[]), typeof(Brep[]))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.ProjectPointsToMeshes, typeof(Point3d[]), typeof(Mesh[]))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.ProjectPointsToMeshesEx, typeof(Point3d[]), typeof(Mesh[]))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.RayShoot, typeof(Ray3d), typeof(GeometryBase[]))] = ValidationMode.Standard,
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IntersectionResult> Intersect<T1, T2>(T1 geometryA, T2 geometryB, IntersectionMethod method, IGeometryContext context, double? tolerance = null, Vector3d? projectionDirection = null, int? maxHitCount = null) where T1 : notnull where T2 : notnull =>
        _config.TryGetValue((method, typeof(T1), typeof(T2)), out ValidationMode mode) switch {
            true => (method, geometryA, geometryB) switch {
                (IntersectionMethod.CurveSelf, Curve c, _) => ResultFactory.Create(value: c).Validate(args: [context, mode])
                    .Bind(_ => Compute(c, c, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount)),
                (IntersectionMethod.PlanePlanePlane, ValueTuple<Plane, Plane> planes, Plane p3) =>
                    Compute(planes, p3, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount),
                (_, GeometryBase ga, GeometryBase gb) => ResultFactory.Create(value: (ga, gb)).Validate(args: [context, mode])
                    .Bind(_ => Compute(geometryA, geometryB, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount)),
                (_, GeometryBase ga, _) => ResultFactory.Create(value: ga).Validate(args: [context, mode])
                    .Bind(_ => Compute(geometryA, geometryB, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount)),
                _ => Compute(geometryA, geometryB, method, context, tolerance ?? context.AbsoluteTolerance, projectionDirection, maxHitCount),
            },
            false => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };

    [Pure]
    private static Result<IntersectionResult> Compute<T1, T2>(T1 a, T2 b, IntersectionMethod m, IGeometryContext ctx, double tol, Vector3d? dir, int? maxHits) where T1 : notnull where T2 : notnull =>
        (m, a, b) switch {
            (IntersectionMethod.CurveCurve, Curve ca, Curve cb) => RhinoIntersect.CurveCurve(ca, cb, tol, tol) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                    [.. from e in r select e.PointA], m,
                    Curves: [.. from e in r where e.IsOverlap from c in new[] { ca.Trim(e.OverlapA) } where c is not null select c],
                    ParametersA: [.. from e in r select e.ParameterA],
                    ParametersB: [.. from e in r select e.ParameterB])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.CurveSurface, Curve ca, Surface sb) => RhinoIntersect.CurveSurface(ca, sb, tol, tol) switch {
                CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                    [.. from e in r select e.PointA], m,
                    ParametersA: [.. from e in r select e.ParameterA],
                    ParametersB: [.. from e in r select e.ParameterB])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.CurveBrep, Curve ca, Brep bb) => RhinoIntersect.CurveBrep(ca, bb, tol, out Curve[] curves, out Point3d[] points) switch {
                true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    Curves: curves.Length > 0 ? [.. curves] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.CurveBrepFace, Curve ca, BrepFace fb) => RhinoIntersect.CurveBrepFace(ca, fb, tol, out Curve[] curves, out Point3d[] points) switch {
                true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    Curves: curves.Length > 0 ? [.. curves] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.CurvePlane, Curve ca, Plane pb) => RhinoIntersect.CurvePlane(ca, pb, tol) switch {
                CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                    [.. from e in r select e.PointA], m,
                    ParametersA: [.. from e in r select e.ParameterA])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.CurveLine, Curve ca, Line lb) => RhinoIntersect.CurveLine(ca, lb, tol, tol) switch {
                CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                    [.. from e in r select e.PointA], m,
                    ParametersA: [.. from e in r select e.ParameterA],
                    ParametersB: [.. from e in r select e.ParameterB])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.CurveSelf, Curve ca, _) => RhinoIntersect.CurveSelf(ca, tol) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                    [.. from e in r select e.PointA], m,
                    ParametersA: [.. from e in r select e.ParameterA])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.BrepBrep, Brep ba, Brep bb) => RhinoIntersect.BrepBrep(ba, bb, tol, out Curve[] curves, out Point3d[] points) switch {
                true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    Curves: curves.Length > 0 ? [.. curves] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.BrepPlane, Brep ba, Plane pb) => RhinoIntersect.BrepPlane(ba, pb, tol, out Curve[] curves, out Point3d[] points) switch {
                true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    Curves: curves.Length > 0 ? [.. curves] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.BrepSurface, Brep ba, Surface sb) => RhinoIntersect.BrepSurface(ba, sb, tol, out Curve[] curves, out Point3d[] points) switch {
                true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    Curves: curves.Length > 0 ? [.. curves] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.SurfaceSurface, Surface sa, Surface sb) => RhinoIntersect.SurfaceSurface(sa, sb, tol, out Curve[] curves, out Point3d[] points) switch {
                true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    Curves: curves.Length > 0 ? [.. curves] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.MeshMesh, Mesh ma, Mesh mb) => RhinoIntersect.MeshMeshFast(ma, mb) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                Line[] { Length: > 0 } lines => ResultFactory.Create(value: new IntersectionResult([.. from l in lines select l.From, .. from l in lines select l.To], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.MeshMeshAccurate, Mesh ma, Mesh mb) when RhinoIntersect.MeshMeshAccurate(ma, mb, tol) is Polyline[] polylines && polylines.Length > 0 =>
                ResultFactory.Create(value: new IntersectionResult([.. from pl in polylines from pt in pl select pt], m)),
            (IntersectionMethod.MeshMeshAccurate, Mesh ma, Mesh mb) when RhinoIntersect.MeshMeshAccurate(ma, mb, tol) is null =>
                ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
            (IntersectionMethod.MeshMeshAccurate, Mesh ma, Mesh mb) =>
                ResultFactory.Create(value: new IntersectionResult([], m)),
            (IntersectionMethod.MeshRay, Mesh ma, Ray3d rb) => RhinoIntersect.MeshRay(ma, rb) switch {
                double d when d >= 0d => ResultFactory.Create(value: new IntersectionResult([rb.PointAt(d)], m, ParametersA: [d])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.MeshPlane, Mesh ma, Plane pb) when RhinoIntersect.MeshPlane(ma, pb) is Polyline[] sections && sections.Length > 0 =>
                ResultFactory.Create(value: new IntersectionResult(
                    [.. from pl in sections from pt in pl select pt], m,
                    Sections: [.. sections])),
            (IntersectionMethod.MeshPlane, Mesh ma, Plane pb) when RhinoIntersect.MeshPlane(ma, pb) is null =>
                ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
            (IntersectionMethod.MeshPlane, Mesh ma, Plane pb) =>
                ResultFactory.Create(value: new IntersectionResult([], m)),
            (IntersectionMethod.MeshLine, Mesh ma, Line lb) => RhinoIntersect.MeshLine(ma, lb) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult([.. points], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.MeshLineSorted, Mesh ma, Line lb) => RhinoIntersect.MeshLineSorted(ma, lb, out int[] faceIds) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    FaceIndices: faceIds.Length > 0 ? [.. faceIds] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.MeshPolyline, Mesh ma, PolylineCurve pcb) => RhinoIntersect.MeshPolyline(ma, pcb, out int[] faceIds) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    FaceIndices: faceIds.Length > 0 ? [.. faceIds] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.MeshPolylineSorted, Mesh ma, PolylineCurve pcb) => RhinoIntersect.MeshPolylineSorted(ma, pcb, out int[] faceIds) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                Point3d[] { Length: > 0 } points => ResultFactory.Create(value: new IntersectionResult(
                    [.. points], m,
                    FaceIndices: faceIds.Length > 0 ? [.. faceIds] : null)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.LineLine, Line la, Line lb) => RhinoIntersect.LineLine(la, lb, out double pa, out double pb, tol, false) switch {
                true => ResultFactory.Create(value: new IntersectionResult(Points: [la.PointAt(pa)], Method: m, ParametersA: [pa], ParametersB: [pb])),
                false => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.LineBox, Line la, BoundingBox boxb) => RhinoIntersect.LineBox(la, boxb, tol, out Interval interval) switch {
                true => ResultFactory.Create(value: new IntersectionResult([la.PointAt(interval.Min), la.PointAt(interval.Max)], m, ParametersA: [interval.Min, interval.Max])),
                false => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.LinePlane, Line la, Plane pb) => RhinoIntersect.LinePlane(la, pb, out double param) switch {
                true => ResultFactory.Create(value: new IntersectionResult([la.PointAt(param)], m, ParametersA: [param])),
                false => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.LineSphere, Line la, Sphere sb) => RhinoIntersect.LineSphere(la, sb, out Point3d p1, out Point3d p2) switch {
                LineSphereIntersection.Multiple => ResultFactory.Create(value: new IntersectionResult([p1, p2], m)),
                LineSphereIntersection.Single => ResultFactory.Create(value: new IntersectionResult([p1], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.LineCylinder, Line la, Cylinder cylb) => RhinoIntersect.LineCylinder(la, cylb, out Point3d p1, out Point3d p2) switch {
                LineCylinderIntersection.Multiple => ResultFactory.Create(value: new IntersectionResult([p1, p2], m)),
                LineCylinderIntersection.Single => ResultFactory.Create(value: new IntersectionResult([p1], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.LineCircle, Line la, Circle cb) => RhinoIntersect.LineCircle(la, cb, out double t1, out Point3d p1, out double t2, out Point3d p2) switch {
                LineCircleIntersection.Multiple when p1.DistanceTo(p2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: new IntersectionResult([p1, p2], m, ParametersA: [t1, t2])),
                LineCircleIntersection.Single or LineCircleIntersection.Multiple => ResultFactory.Create(value: new IntersectionResult([p1], m, ParametersA: [t1])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.PlanePlane, Plane pa, Plane pb) => RhinoIntersect.PlanePlane(pa, pb, out Line line) switch {
                true => ResultFactory.Create(value: new IntersectionResult([], m, Curves: [new LineCurve(line)])),
                false => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.PlanePlanePlane, ValueTuple<Plane, Plane> planes, Plane p3b) =>
                RhinoIntersect.PlanePlanePlane(planes.Item1, planes.Item2, p3b, out Point3d point) switch {
                    true => ResultFactory.Create(value: new IntersectionResult([point], m)),
                    false => ResultFactory.Create(value: new IntersectionResult([], m)),
                },
            (IntersectionMethod.PlaneCircle, Plane pa, Circle cb) => RhinoIntersect.PlaneCircle(pa, cb, out double t1, out double t2) switch {
                PlaneCircleIntersection.Tangent => ResultFactory.Create(value: new IntersectionResult([cb.PointAt(t1)], m, ParametersB: [t1])),
                PlaneCircleIntersection.Secant => ResultFactory.Create(value: new IntersectionResult([cb.PointAt(t1), cb.PointAt(t2)], m, ParametersB: [t1, t2])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.PlaneSphere, Plane pa, Sphere sb) => RhinoIntersect.PlaneSphere(pa, sb, out Circle circle) switch {
                PlaneSphereIntersection.Circle => ResultFactory.Create(value: new IntersectionResult([], m, Curves: [new ArcCurve(circle)])),
                PlaneSphereIntersection.Point => ResultFactory.Create(value: new IntersectionResult([circle.Center], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.PlaneBoundingBox, Plane pa, BoundingBox boxb) => RhinoIntersect.PlaneBoundingBox(pa, boxb, out Polyline poly) switch {
                true when poly?.Count > 0 => ResultFactory.Create(value: new IntersectionResult([.. from pt in poly select pt], m, Sections: [poly])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.SphereSphere, Sphere sa, Sphere sb) => RhinoIntersect.SphereSphere(sa, sb, out Circle circle) switch {
                SphereSphereIntersection.Circle => ResultFactory.Create(value: new IntersectionResult([], m, Curves: [new ArcCurve(circle)])),
                SphereSphereIntersection.Point => ResultFactory.Create(value: new IntersectionResult([circle.Center], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.CircleCircle, Circle ca, Circle cb) => RhinoIntersect.CircleCircle(ca, cb, out Point3d p1, out Point3d p2) switch {
                CircleCircleIntersection.Single => ResultFactory.Create(value: new IntersectionResult([p1], m)),
                CircleCircleIntersection.Multiple when p1.DistanceTo(p2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: new IntersectionResult([p1, p2], m)),
                CircleCircleIntersection.Multiple => ResultFactory.Create(value: new IntersectionResult([p1], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.ArcArc, Arc aa, Arc ab) => RhinoIntersect.ArcArc(aa, ab, out Point3d p1, out Point3d p2) switch {
                ArcArcIntersection.Multiple when p1.DistanceTo(p2) > ctx.AbsoluteTolerance => ResultFactory.Create(value: new IntersectionResult([p1, p2], m)),
                ArcArcIntersection.Single or ArcArcIntersection.Multiple when p1.IsValid => ResultFactory.Create(value: new IntersectionResult([p1], m)),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            },
            (IntersectionMethod.ProjectPointsToBreps, Point3d[] points, Brep[] breps) when dir?.IsValid == true && dir.Value.Length > RhinoMath.ZeroTolerance =>
                ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToBreps(breps, points, dir.Value, RhinoMath.ZeroTolerance)], m)),
            (IntersectionMethod.ProjectPointsToBrepsEx, Point3d[] points, Brep[] breps) when dir?.IsValid == true && dir.Value.Length > RhinoMath.ZeroTolerance =>
                ResultFactory.Create(value: new IntersectionResult(
                    [.. RhinoIntersect.ProjectPointsToBrepsEx(breps, points, dir.Value, RhinoMath.ZeroTolerance, out int[] indices)], m,
                    FaceIndices: indices.Length > 0 ? [.. indices] : null)),
            (IntersectionMethod.ProjectPointsToMeshes, Point3d[] points, Mesh[] meshes) when dir?.IsValid == true && dir.Value.Length > RhinoMath.ZeroTolerance =>
                ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.ProjectPointsToMeshes(meshes, points, dir.Value, RhinoMath.ZeroTolerance)], m)),
            (IntersectionMethod.ProjectPointsToMeshesEx, Point3d[] points, Mesh[] meshes) when dir?.IsValid == true && dir.Value.Length > RhinoMath.ZeroTolerance =>
                ResultFactory.Create(value: new IntersectionResult(
                    [.. RhinoIntersect.ProjectPointsToMeshesEx(meshes, points, dir.Value, RhinoMath.ZeroTolerance, out int[] indices)], m,
                    FaceIndices: indices.Length > 0 ? [.. indices] : null)),
            (IntersectionMethod.RayShoot, Ray3d ray, GeometryBase[] geometry) when maxHits > 0 && ray.Direction.Length > RhinoMath.ZeroTolerance =>
                ResultFactory.Create(value: new IntersectionResult([.. RhinoIntersect.RayShoot(ray, geometry, maxHits.Value)], m)),
            (IntersectionMethod method, _, _) when method is IntersectionMethod.ProjectPointsToBreps or IntersectionMethod.ProjectPointsToBrepsEx or
                IntersectionMethod.ProjectPointsToMeshes or IntersectionMethod.ProjectPointsToMeshesEx =>
                ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidProjectionDirection),
            (IntersectionMethod.RayShoot, _, _) when maxHits is null or <= 0 =>
                ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidMaxHitCount),
            (IntersectionMethod.RayShoot, Ray3d ray, _) when ray.Direction.Length <= RhinoMath.ZeroTolerance =>
                ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Parameters.InvalidRayDirection),
            _ => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };
}
