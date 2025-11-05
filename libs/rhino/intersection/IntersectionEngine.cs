using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection engine with RhinoCommon Intersect SDK integration and unified operation dispatch.</summary>
public static class IntersectionEngine {
    /// <summary>Performs intersection operations using RhinoCommon Intersect algorithms with tolerance-aware geometry processing and batch operation support.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(IReadOnlyList<Point3d> Points, IReadOnlyList<Curve>? Curves, IReadOnlyList<double>? ParametersA, IReadOnlyList<double>? ParametersB, IReadOnlyList<int>? FaceIndices, IReadOnlyList<Polyline>? Sections)> Intersect<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance = null,
        Vector3d? projectionDirection = null,
        int? maxHitCount = null) where T1 : notnull where T2 : notnull =>
        (geometryA, geometryB) switch {
            (GeometryBase ga, GeometryBase gb) => IntersectionStrategies.Intersect(ga, gb, method, context, tolerance, projectionDirection, maxHitCount)
                .Map(r => (r.Points, r.Curves, r.ParametersA, r.ParametersB, r.FaceIndices, r.Sections)),
            var (a, b) when (a is System.Collections.IEnumerable && a is not string && a is not GeometryBase && b is GeometryBase) || (b is System.Collections.IEnumerable && b is not string && b is not GeometryBase && a is GeometryBase) =>
                UnifiedOperation.Apply(
                    (a is System.Collections.IEnumerable && a is not string && a is not GeometryBase ? (System.Collections.IEnumerable)a : (System.Collections.IEnumerable)b).Cast<object>(),
                    (Func<object, Result<IntersectionResult>>)(item => a is GeometryBase
                        ? IntersectionStrategies.Intersect((GeometryBase)(object)a, item, method, context, tolerance, projectionDirection, maxHitCount)
                        : IntersectionStrategies.Intersect(item, (GeometryBase)(object)b, method, context, tolerance, projectionDirection, maxHitCount)),
                    new OperationConfig<object, IntersectionResult> { Context = context, ValidationMode = ValidationMode.None, AccumulateErrors = true, })
                .Map(results => (
                    (IReadOnlyList<Point3d>)[.. results.SelectMany(r => r.Points)],
                    [.. results.SelectMany(r => r.Curves ?? [])] is { Count: > 0 } curves ? (IReadOnlyList<Curve>?)curves : null,
                    [.. results.SelectMany(r => r.ParametersA ?? [])] is { Count: > 0 } paramsA ? (IReadOnlyList<double>?)paramsA : null,
                    [.. results.SelectMany(r => r.ParametersB ?? [])] is { Count: > 0 } paramsB ? (IReadOnlyList<double>?)paramsB : null,
                    [.. results.SelectMany(r => r.FaceIndices ?? [])] is { Count: > 0 } indices ? (IReadOnlyList<int>?)indices : null,
                    [.. results.SelectMany(r => r.Sections ?? [])] is { Count: > 0 } sections ? (IReadOnlyList<Polyline>?)sections : null)),
            (Point3d[] points, GeometryBase[] targets) when (method & (IntersectionMethod.ProjectPointsToBreps | IntersectionMethod.ProjectPointsToMeshes | IntersectionMethod.ProjectPointsToBrepsEx | IntersectionMethod.ProjectPointsToMeshesEx)) != IntersectionMethod.None =>
                IntersectionStrategies.Intersect(points, targets, method, context, tolerance, projectionDirection, maxHitCount)
                    .Map(r => (r.Points, r.Curves, r.ParametersA, r.ParametersB, r.FaceIndices, r.Sections)),
            _ => IntersectionStrategies.Intersect(geometryA, geometryB, method, context, tolerance, projectionDirection, maxHitCount)
                .Map(r => (r.Points, r.Curves, r.ParametersA, r.ParametersB, r.FaceIndices, r.Sections)),
        };
}
