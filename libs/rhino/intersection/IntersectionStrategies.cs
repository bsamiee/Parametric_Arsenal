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
            [(IntersectionMethod.BrepBrep, typeof(Brep), typeof(Brep))] = ValidationMode.Standard | ValidationMode.Topology,
            [(IntersectionMethod.MeshMesh, typeof(Mesh), typeof(Mesh))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshRay, typeof(Mesh), typeof(Ray3d))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.MeshPlane, typeof(Mesh), typeof(Plane))] = ValidationMode.MeshSpecific,
            [(IntersectionMethod.LineBox, typeof(Line), typeof(BoundingBox))] = ValidationMode.BoundingBox,
            [(IntersectionMethod.CurveSelf, typeof(Curve), typeof(Curve))] = ValidationMode.Standard | ValidationMode.Degeneracy,
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IntersectionResult> Intersect<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance = null) where T1 : notnull where T2 : notnull =>
        (method, geometryA, geometryB, _config.TryGetValue((method, typeof(T1), typeof(T2)), out ValidationMode mode)) switch {
            (IntersectionMethod.CurveSelf, Curve c, _, true) =>
                ResultFactory.Create(value: c)
                    .Validate(args: [context, mode])
                    .Bind(_ => IntersectCore(c, default(T2)!, method, context, tolerance)),
            (_, GeometryBase ga, GeometryBase gb, true) =>
                ResultFactory.Create(value: ga)
                    .Validate(args: [context, mode])
                    .Bind(_ => ResultFactory.Create(value: gb).Validate(args: [context, mode]))
                    .Bind(_ => IntersectCore(geometryA, geometryB, method, context, tolerance)),
            (_, GeometryBase ga, _, true) =>
                ResultFactory.Create(value: ga)
                    .Validate(args: [context, mode])
                    .Bind(_ => IntersectCore(geometryA, geometryB, method, context, tolerance)),
            (_, _, _, true) => IntersectCore(geometryA, geometryB, method, context, tolerance),
            _ => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };

    [Pure]
    private static Result<IntersectionResult> IntersectCore<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance) where T1 : notnull where T2 : notnull =>
        (method, geometryA, geometryB) switch {
            (IntersectionMethod.CurveCurve, Curve c1, Curve c2) =>
                Intersection.CurveCurve(c1, c2, tolerance ?? context.AbsoluteTolerance, tolerance ?? context.AbsoluteTolerance) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. r.Select(e => e.PointA)],
                        method,
                        Curves: r.Any(e => e.OverlapA is not null) ? [.. r.Where(e => e.OverlapA is not null).Select(e => e.OverlapA!)] : null,
                        ParametersA: [.. r.Select(e => e.ParameterA)],
                        ParametersB: [.. r.Select(e => e.ParameterB)])),
                    CurveIntersections { Count: 0 } => ResultFactory.Create(value: new IntersectionResult([], method)),
                    null => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.ComputationFailed),
                },
            (IntersectionMethod.CurveSurface, Curve c, Surface s) =>
                Intersection.CurveSurface(c, s, tolerance ?? context.AbsoluteTolerance, tolerance ?? context.AbsoluteTolerance) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. r.Select(e => e.PointA)],
                        method,
                        ParametersA: [.. r.Select(e => e.ParameterA)],
                        ParametersB: [.. r.Select(e => e.ParameterB)])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.CurveBrep, Curve c, Brep b) =>
                Intersection.CurveBrep(c, b, tolerance ?? context.AbsoluteTolerance, out Curve[] curves, out Point3d[] points) switch {
                    true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                        [.. points],
                        method,
                        Curves: curves.Length > 0 ? [.. curves] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.BrepBrep, Brep b1, Brep b2) =>
                Intersection.BrepBrep(b1, b2, tolerance ?? context.AbsoluteTolerance, out Curve[] curves, out Point3d[] points) switch {
                    true when points.Length > 0 || curves.Length > 0 => ResultFactory.Create(value: new IntersectionResult(
                        [.. points],
                        method,
                        Curves: curves.Length > 0 ? [.. curves] : null)),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.MeshMesh, Mesh m1, Mesh m2) =>
                Intersection.MeshMeshFast(m1, m2, tolerance ?? context.AbsoluteTolerance) switch {
                    Polyline[] { Length: > 0 } polylines => ResultFactory.Create(value: new IntersectionResult(
                        [.. polylines.SelectMany(pl => pl)],
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
                        [.. sections.SelectMany(pl => pl)],
                        method,
                        Sections: [.. sections])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            (IntersectionMethod.LineBox, Line l, BoundingBox b) =>
                Intersection.LineBox(l, b, tolerance ?? context.AbsoluteTolerance, out Interval lineParameters) switch {
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
            (IntersectionMethod.CurveSelf, Curve c, _) =>
                Intersection.CurveSelf(c, tolerance ?? context.AbsoluteTolerance) switch {
                    CurveIntersections { Count: > 0 } r => ResultFactory.Create(value: new IntersectionResult(
                        [.. r.Select(e => e.PointA)],
                        method,
                        ParametersA: [.. r.Select(e => e.ParameterA)])),
                    _ => ResultFactory.Create(value: new IntersectionResult([], method)),
                },
            _ => ResultFactory.Create<IntersectionResult>(error: IntersectionErrors.Operation.UnsupportedMethod),
        };
}
