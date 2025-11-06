using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection engine with unified operation dispatch.</summary>
public static class IntersectionEngine {
    /// <summary>Performs intersection operations with validation and error handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(IReadOnlyList<Point3d> Points, IReadOnlyList<Curve>? Curves, IReadOnlyList<double>? ParametersA, IReadOnlyList<double>? ParametersB, IReadOnlyList<int>? FaceIndices, IReadOnlyList<Polyline>? Sections)> Intersect<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IntersectionMethod method,
        IGeometryContext context,
        double? tolerance = null,
        Vector3d? projectionDirection = null,
        int? maxHitCount = null) where T1 : notnull where T2 : notnull =>
        UnifiedOperation.Apply(
            geometryA,
            (Func<object, Result<IReadOnlyList<IntersectionResult>>>)(item => item switch {
                GeometryBase g => IntersectionStrategies.Intersect(g, geometryB, method, context, tolerance, projectionDirection, maxHitCount)
                    .Map(r => (IReadOnlyList<IntersectionResult>)[r]),
                _ => ResultFactory.Create<IReadOnlyList<IntersectionResult>>(error: IntersectionErrors.Operation.UnsupportedMethod),
            }),
            new OperationConfig<object, IntersectionResult> {
                Context = context,
                ValidationMode = ValidationMode.None,
                AccumulateErrors = true,
            })
        .Map(results => (
            (IReadOnlyList<Point3d>)[.. results.SelectMany(r => r.Points)],
            (IReadOnlyList<Curve>?)(results.SelectMany(r => r.Curves ?? []).ToArray() is { Length: > 0 } curves ? curves : null),
            (IReadOnlyList<double>?)(results.SelectMany(r => r.ParametersA ?? []).ToArray() is { Length: > 0 } paramsA ? paramsA : null),
            (IReadOnlyList<double>?)(results.SelectMany(r => r.ParametersB ?? []).ToArray() is { Length: > 0 } paramsB ? paramsB : null),
            (IReadOnlyList<int>?)(results.SelectMany(r => r.FaceIndices ?? []).ToArray() is { Length: > 0 } indices ? indices : null),
            (IReadOnlyList<Polyline>?)(results.SelectMany(r => r.Sections ?? []).ToArray() is { Length: > 0 } sections ? sections : null)));
}
