using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
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

/// <summary>Intersection algorithm implementations with RhinoCommon Intersect SDK integration.</summary>
internal static class IntersectionStrategies {
    private static readonly FrozenDictionary<(IntersectionMethod, Type, Type), (ValidationMode Mode, Func<object, object, double, IntersectionMethod, IGeometryContext, Result<IntersectionResult>> Compute)> _dispatch =
        new Dictionary<(IntersectionMethod, Type, Type), (ValidationMode, Func<object, object, double, IntersectionMethod, IGeometryContext, Result<IntersectionResult>>)> {
            [(IntersectionMethod.CurveCurve, typeof(Curve), typeof(Curve))] = (ValidationMode.Standard | ValidationMode.Degeneracy, (a, b, tol, m, _) => RhinoIntersect.CurveCurve((Curve)a, (Curve)b, tol, tol) switch {
                null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult([.. from e in r select e.PointA], m, Curves: [.. from e in r where e.IsOverlap from c in new[] { ((Curve)a).Trim(e.OverlapA) } where c is not null select c], ParametersA: [.. from e in r select e.ParameterA], ParametersB: [.. from e in r select e.ParameterB])),
                _ => ResultFactory.Create(value: new IntersectionResult([], m)),
            }),
            [(IntersectionMethod.CurveSurface, typeof(Curve), typeof(Surface))] = (ValidationMode.Standard, (a, b, tol, m, _) => RhinoIntersect.CurveSurface((Curve)a, (Surface)b, tol, tol) switch { CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult([.. from e in r select e.PointA], m, ParametersA: [.. from e in r select e.ParameterA], ParametersB: [.. from e in r select e.ParameterB])), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.CurveBrep, typeof(Curve), typeof(Brep))] = (ValidationMode.Standard | ValidationMode.Topology, (a, b, tol, m, _) => RhinoIntersect.CurveBrep((Curve)a, (Brep)b, tol, out Curve[] c, out Point3d[] p) switch { true when p.Length > 0 || c.Length > 0 => ResultFactory.Create(value: new IntersectionResult([.. p], m, Curves: c.Length > 0 ? [.. c] : null)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.BrepBrep, typeof(Brep), typeof(Brep))] = (ValidationMode.Standard | ValidationMode.Topology, (a, b, tol, m, _) => RhinoIntersect.BrepBrep((Brep)a, (Brep)b, tol, out Curve[] c, out Point3d[] p) switch { true when p.Length > 0 || c.Length > 0 => ResultFactory.Create(value: new IntersectionResult([.. p], m, Curves: c.Length > 0 ? [.. c] : null)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.MeshMesh, typeof(Mesh), typeof(Mesh))] = (ValidationMode.MeshSpecific, (a, b, _, m, _) => RhinoIntersect.MeshMeshFast((Mesh)a, (Mesh)b) switch { null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), Line[] { Length: > 0 } lines => ResultFactory.Create(value: new IntersectionResult([.. from l in lines select l.From, .. from l in lines select l.To], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.MeshRay, typeof(Mesh), typeof(Ray3d))] = (ValidationMode.MeshSpecific, (a, b, _, m, _) => { double d = RhinoIntersect.MeshRay((Mesh)a, (Ray3d)b); return d >= 0 ? ResultFactory.Create(value: new IntersectionResult([((Ray3d)b).PointAt(d)], m, ParametersA: [d])) : ResultFactory.Create(value: new IntersectionResult([], m)); }),
            [(IntersectionMethod.MeshPlane, typeof(Mesh), typeof(Plane))] = (ValidationMode.MeshSpecific, (a, b, _, m, _) => RhinoIntersect.MeshPlane((Mesh)a, (Plane)b) switch { null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), Polyline[] { Length: > 0 } s => ResultFactory.Create(value: new IntersectionResult([.. from pl in s from pt in pl select pt], m, Sections: [.. s])), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.MeshLine, typeof(Mesh), typeof(Line))] = (ValidationMode.MeshSpecific, (a, b, _, m, _) => RhinoIntersect.MeshLine((Mesh)a, (Line)b) switch { null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), Point3d[] { Length: > 0 } pts => ResultFactory.Create(value: new IntersectionResult([.. pts], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.LineBox, typeof(Line), typeof(BoundingBox))] = (ValidationMode.Standard, (a, b, tol, m, _) => RhinoIntersect.LineBox((Line)a, (BoundingBox)b, tol, out Interval i) ? ResultFactory.Create(value: new IntersectionResult([((Line)a).PointAt(i.Min), ((Line)a).PointAt(i.Max)], m, ParametersA: [i.Min, i.Max])) : ResultFactory.Create(value: new IntersectionResult([], m))),
            [(IntersectionMethod.CurvePlane, typeof(Curve), typeof(Plane))] = (ValidationMode.Standard | ValidationMode.Degeneracy, (a, b, tol, m, _) => RhinoIntersect.CurvePlane((Curve)a, (Plane)b, tol) switch { CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult([.. from e in r select e.PointA], m, ParametersA: [.. from e in r select e.ParameterA])), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.CurveLine, typeof(Curve), typeof(Line))] = (ValidationMode.Standard | ValidationMode.Degeneracy, (a, b, tol, m, _) => RhinoIntersect.CurveLine((Curve)a, (Line)b, tol, tol) switch { CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult([.. from e in r select e.PointA], m, ParametersA: [.. from e in r select e.ParameterA], ParametersB: [.. from e in r select e.ParameterB])), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.BrepPlane, typeof(Brep), typeof(Plane))] = (ValidationMode.Standard | ValidationMode.Topology, (a, b, tol, m, _) => RhinoIntersect.BrepPlane((Brep)a, (Plane)b, tol, out Curve[] c, out Point3d[] pts) switch { true when pts.Length > 0 || c.Length > 0 => ResultFactory.Create(value: new IntersectionResult([.. pts], m, Curves: c.Length > 0 ? [.. c] : null)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.SurfaceSurface, typeof(Surface), typeof(Surface))] = (ValidationMode.Standard, (a, b, tol, m, _) => RhinoIntersect.SurfaceSurface((Surface)a, (Surface)b, tol, out Curve[] c, out Point3d[] pts) switch { true when pts.Length > 0 || c.Length > 0 => ResultFactory.Create(value: new IntersectionResult([.. pts], m, Curves: c.Length > 0 ? [.. c] : null)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.LinePlane, typeof(Line), typeof(Plane))] = (ValidationMode.Standard, (a, b, _, m, _) => RhinoIntersect.LinePlane((Line)a, (Plane)b, out double param) ? ResultFactory.Create(value: new IntersectionResult([((Line)a).PointAt(param)], m, ParametersA: [param])) : ResultFactory.Create(value: new IntersectionResult([], m))),
            [(IntersectionMethod.LineSphere, typeof(Line), typeof(Sphere))] = (ValidationMode.Standard, (a, b, _, m, _) => RhinoIntersect.LineSphere((Line)a, (Sphere)b, out Point3d p1, out Point3d p2) switch { LineSphereIntersection.Multiple => ResultFactory.Create(value: new IntersectionResult([p1, p2], m)), LineSphereIntersection.Single => ResultFactory.Create(value: new IntersectionResult([p1], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.LineCylinder, typeof(Line), typeof(Cylinder))] = (ValidationMode.Standard, (a, b, _, m, _) => RhinoIntersect.LineCylinder((Line)a, (Cylinder)b, out Point3d p1, out Point3d p2) switch { LineCylinderIntersection.Multiple => ResultFactory.Create(value: new IntersectionResult([p1, p2], m)), LineCylinderIntersection.Single => ResultFactory.Create(value: new IntersectionResult([p1], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.PlanePlane, typeof(Plane), typeof(Plane))] = (ValidationMode.Standard, (a, b, _, m, _) => RhinoIntersect.PlanePlane((Plane)a, (Plane)b, out Line line) ? ResultFactory.Create(value: new IntersectionResult([], m, Curves: [new LineCurve(line)])) : ResultFactory.Create(value: new IntersectionResult([], m))),
            [(IntersectionMethod.SphereSphere, typeof(Sphere), typeof(Sphere))] = (ValidationMode.Standard, (a, b, _, m, _) => RhinoIntersect.SphereSphere((Sphere)a, (Sphere)b, out Circle circle) switch { SphereSphereIntersection.Circle => ResultFactory.Create(value: new IntersectionResult([], m, Curves: [new ArcCurve(circle)])), SphereSphereIntersection.Point => ResultFactory.Create(value: new IntersectionResult([circle.Center], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.CircleCircle, typeof(Circle), typeof(Circle))] = (ValidationMode.Standard, (a, b, _, m, ctx) => RhinoIntersect.CircleCircle((Circle)a, (Circle)b, out Point3d p1, out Point3d p2) switch { CircleCircleIntersection.Single or CircleCircleIntersection.Multiple => ResultFactory.Create(value: new IntersectionResult(p1.DistanceTo(p2) > ctx.AbsoluteTolerance ? [p1, p2] : [p1], m)), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
            [(IntersectionMethod.CurveSelf, typeof(Curve), typeof(Curve))] = (ValidationMode.Standard | ValidationMode.Degeneracy, (a, _, tol, m, _) => RhinoIntersect.CurveSelf((Curve)a, tol) switch { null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed), CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult([.. from e in r select e.PointA], m, ParametersA: [.. from e in r select e.ParameterA])), _ => ResultFactory.Create(value: new IntersectionResult([], m)), }),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IntersectionResult> Intersect<T1, T2>(T1 geometryA, T2 geometryB, IntersectionMethod method, IGeometryContext context, double? tolerance = null) where T1 : notnull where T2 : notnull =>
        _dispatch.TryGetValue((method, typeof(T1), typeof(T2)), out (ValidationMode mode, Func<object, object, double, IntersectionMethod, IGeometryContext, Result<IntersectionResult>> compute) entry) switch {
            true => (method, geometryA, geometryB) switch {
                (IntersectionMethod.CurveSelf, Curve c, _) => ResultFactory.Create(value: c).Validate(args: [context, entry.mode]).Bind(_ => entry.compute(c, c, tolerance ?? context.AbsoluteTolerance, method, context)),
                (_, GeometryBase ga, GeometryBase gb) => ResultFactory.Create(value: (ga, gb)).Validate(args: [context, entry.mode]).Bind(_ => entry.compute(geometryA!, geometryB!, tolerance ?? context.AbsoluteTolerance, method, context)),
                (_, GeometryBase ga, _) => ResultFactory.Create(value: ga).Validate(args: [context, entry.mode]).Bind(_ => entry.compute(geometryA!, geometryB!, tolerance ?? context.AbsoluteTolerance, method, context)),
                _ => entry.compute(geometryA!, geometryB!, tolerance ?? context.AbsoluteTolerance, method, context),
            },
            false => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };
}
