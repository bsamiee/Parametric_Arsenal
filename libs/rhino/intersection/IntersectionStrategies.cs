using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Rhino.Intersection;

/// <summary>Unified intersection result containing points, curves, and parametric information.</summary>
internal sealed record IntersectionResult(
    IReadOnlyList<Point3d> Points,
    IntersectionMethod Method,
    IReadOnlyList<Curve>? Curves = null,
    IReadOnlyList<double>? ParametersA = null,
    IReadOnlyList<double>? ParametersB = null,
    IReadOnlyList<int>? FaceIndices = null,
    IReadOnlyList<Polyline>? Sections = null);

/// <summary>Intersection algorithm implementations with RhinoCommon Intersect SDK integration.</summary>
internal static class IntersectionStrategies {
    private static readonly FrozenDictionary<(IntersectionMethod, Type, Type), ValidationMode> _config =
        new Dictionary<(IntersectionMethod, Type, Type), ValidationMode> {
            [(IntersectionMethod.CurveCurve, typeof(Curve), typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(IntersectionMethod.CurveSurface, typeof(Curve), typeof(Surface))] = ValidationMode.Standard,
            [(IntersectionMethod.CurveBrep, typeof(Curve), typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.CurvePlane, typeof(Curve), typeof(Plane))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(IntersectionMethod.CurveLine, typeof(Curve), typeof(Line))] = ValidationMode.Standard | ValidationMode.Degeneracy,
            [(IntersectionMethod.BrepBrep, typeof(Brep), typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.BrepPlane, typeof(Brep), typeof(Plane))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.SurfaceSurface, typeof(Surface), typeof(Surface))] = ValidationMode.Standard,
            [(IntersectionMethod.MeshMesh, typeof(Mesh), typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshRay, typeof(Mesh), typeof(Ray3d))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshPlane, typeof(Mesh), typeof(Plane))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshLine, typeof(Mesh), typeof(Line))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.CurveSelf, typeof(Curve), typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IntersectionResult> Intersect<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance = null) where T1 : notnull where T2 : notnull =>
        _config.TryGetValue((method, typeof(T1), typeof(T2)), out ValidationMode mode) switch {
            true => (method, geometryA, geometryB) switch {
                (IntersectionMethod.CurveSelf, Curve c, _) => ResultFactory.Create(value: c).Validate(args: [context, mode]).Bind(_ => IntersectCore(c, default(T2)!, method, context, tolerance)),
                (_, GeometryBase ga, GeometryBase gb) => ResultFactory.Create(value: (ga, gb)).Validate(args: [context, mode]).Bind(_ => IntersectCore(geometryA, geometryB, method, context, tolerance)),
                (_, GeometryBase ga, _) => ResultFactory.Create(value: ga).Validate(args: [context, mode]).Bind(_ => IntersectCore(geometryA, geometryB, method, context, tolerance)),
                _ => IntersectCore(geometryA, geometryB, method, context, tolerance),
            },
            false => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };

    [Pure]
    private static Result<IntersectionResult> IntersectCore<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance) where T1 : notnull where T2 : notnull {
        double tol = tolerance ?? context.AbsoluteTolerance;
        return (method, geometryA, geometryB) switch {
            (IntersectionMethod.CurveCurve, Curve c1, Curve c2) =>
                Intersection.CurveCurve(c1, c2, tol, tol) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. from e in r select e.PointA],
                        method,
                        Curves: [.. from e in r where e.OverlapA is not null select e.OverlapA],
                        ParametersA: [.. from e in r select e.ParameterA],
                        ParametersB: [.. from e in r select e.ParameterB])),
                    CurveIntersections { Count: 0 } => ResultFactory.Create(value: new IntersectionResult([], method)),
                    null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                },
            (IntersectionMethod.CurveSurface, Curve c, Surface s) =>
                Intersection.CurveSurface(c, s, tol, tol) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. from e in r select e.PointA],
                        method,
                        ParametersA: [.. from e in r select e.ParameterA],
                        ParametersB: [.. from e in r select e.ParameterB])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.CurveBrep, Curve c, Brep b) =>
                Intersection.CurveBrep(c, b, tol, out Curve[] curves, out Point3d[] points) switch {
                    true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                        [.. points],
                        method,
                        Curves: curves.Length > 0 ? [.. curves] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.BrepBrep, Brep b1, Brep b2) =>
                Intersection.BrepBrep(b1, b2, tol, out Curve[] curves, out Point3d[] points) switch {
                    true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                        [.. points],
                        method,
                        Curves: curves.Length > 0 ? [.. curves] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.MeshMesh, Mesh m1, Mesh m2) =>
                Intersection.MeshMeshFast(m1, m2, tol) switch {
                    Polyline[] { Length: > 0 } polylines => ResultFactory.Create(value: new IntersectionResult(
                        [.. from pl in polylines from pt in pl select pt],
                        method,
                        Sections: [.. polylines])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.MeshRay, Mesh m, Ray3d r) =>
                Intersection.MeshRay(m, r, out int faceIndex) switch {
                    double d when d >= 0 && faceIndex >= 0 => ResultFactory.Create(value: new IntersectionResult(
                        [r.PointAt(d)],
                        method,
                        FaceIndices: [faceIndex],
                        ParametersA: [d])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.MeshPlane, Mesh m, Plane p) =>
                Intersection.MeshPlane(m, p) switch {
                    Polyline[] { Length: > 0 } sections => ResultFactory.Create(value: new IntersectionResult(
                        [.. from pl in sections from pt in pl select pt],
                        method,
                        Sections: [.. sections])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.LineBox, Line l, BoundingBox b) =>
                Intersection.LineBox(l, b, tol, out Interval lineParameters) switch {
                    LineBoundingBoxIntersection.Overlap => ResultFactory.Create(value: new IntersectionResult(
                        [l.PointAt(lineParameters.Min), l.PointAt(lineParameters.Max)],
                        method,
                        ParametersA: [lineParameters.Min, lineParameters.Max])),
                    LineBoundingBoxIntersection.Single => ResultFactory.Create(value: new IntersectionResult(
                        [l.PointAt(lineParameters.Min)],
                        method,
                        ParametersA: [lineParameters.Min])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.CurvePlane, Curve c, Plane p) =>
                Intersection.CurvePlane(c, p, tol) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. from e in r select e.PointA],
                        method,
                        ParametersA: [.. from e in r select e.ParameterA])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.CurveLine, Curve c, Line l) =>
                Intersection.CurveLine(c, l, tol, tol) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. from e in r select e.PointA],
                        method,
                        ParametersA: [.. from e in r select e.ParameterA],
                        ParametersB: [.. from e in r select e.ParameterB])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.BrepPlane, Brep b, Plane p) =>
                Intersection.BrepPlane(b, p, tol, out Curve[] curves, out Point3d[] points) switch {
                    true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                        [.. points],
                        method,
                        Curves: curves.Length > 0 ? [.. curves] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.SurfaceSurface, Surface s1, Surface s2) =>
                Intersection.SurfaceSurface(s1, s2, tol, out Curve[] curves, out Point3d[] points) switch {
                    true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                        [.. points],
                        method,
                        Curves: curves.Length > 0 ? [.. curves] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.MeshLine, Mesh m, Line l) =>
                Intersection.MeshLine(m, l, out int[] faceIds) switch {
                    Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: new IntersectionResult(
                        [.. pts],
                        method,
                        FaceIndices: faceIds?.Length > 0 ? [.. faceIds] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.LinePlane, Line l, Plane p) =>
                Intersection.LinePlane(l, p, out double param) switch {
                    true => ResultFactory.Create(value: new IntersectionResult(
                        [l.PointAt(param)],
                        method,
                        ParametersA: [param])),
                    false => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.LineSphere, Line l, Sphere s) =>
                Intersection.LineSphere(l, s, out Point3d p1, out Point3d p2) switch {
                    LineSphereIntersection.Double => ResultFactory.Create(value: new IntersectionResult([p1, p2], method)),
                    LineSphereIntersection.Single => ResultFactory.Create(value: new IntersectionResult([p1], method)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.LineCylinder, Line l, Cylinder cyl) =>
                Intersection.LineCylinder(l, cyl, out Point3d p1, out Point3d p2) switch {
                    LineCylinderIntersection.Double => ResultFactory.Create(value: new IntersectionResult([p1, p2], method)),
                    LineCylinderIntersection.Single => ResultFactory.Create(value: new IntersectionResult([p1], method)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.PlanePlane, Plane p1, Plane p2) =>
                Intersection.PlanePlane(p1, p2, out Line line) switch {
                    true => ResultFactory.Create(value: new IntersectionResult(
                        [],
                        method,
                        Curves: [new LineCurve(line)])),
                    false => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.SphereSphere, Sphere s1, Sphere s2) =>
                Intersection.SphereSphere(s1, s2, out Circle circle) switch {
                    PlanePlaneIntersection.Circle => ResultFactory.Create(value: new IntersectionResult(
                        [],
                        method,
                        Curves: [new ArcCurve(circle)])),
                    PlanePlaneIntersection.Point => ResultFactory.Create(value: new IntersectionResult([circle.Center], method)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.CircleCircle, Circle c1, Circle c2) =>
                Intersection.CircleCircle(c1, c2, out Point3d p1, out Point3d p2) switch {
                    PlaneCircleIntersection.Tangent or PlaneCircleIntersection.Secant => ResultFactory.Create(value: new IntersectionResult(
                        p1.DistanceTo(p2) > context.AbsoluteTolerance ? [p1, p2] : [p1],
                        method)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.CurveSelf, Curve c, _) =>
                Intersection.CurveSelf(c, tol) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. from e in r select e.PointA],
                        method,
                        ParametersA: [.. from e in r select e.ParameterA])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            _ => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };
    }
}
