using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Unified intersection result containing all geometric outputs.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Colocated result type")]
public sealed record IntersectionResult(
    IReadOnlyList<Point3d> Points,
    IReadOnlyList<Curve>? Curves = null,
    IReadOnlyList<double>? ParametersA = null,
    IReadOnlyList<double>? ParametersB = null,
    IReadOnlyList<int>? FaceIndices = null,
    IReadOnlyList<Polyline>? Sections = null);

/// <summary>Polymorphic intersection analysis engine with type-based dispatch.</summary>
public static class IntersectionAnalysis {
    /// <summary>Analyzes geometric intersections using polymorphic type inference.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionResult> Analyze<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        (double? Tolerance, Vector3d? Direction, int? MaxHits, bool Validate)? config = null) where T1 : notnull where T2 : notnull =>
        UnifiedOperation.Apply(
            geometryA,
            (Func<T1, Result<IReadOnlyList<IntersectionResult>>>)(a => IntersectionCompute.Execute(a, geometryB, context, config ?? (null, null, null, true))),
            new OperationConfig<T1, IntersectionResult> {
                Context = context,
                ValidationMode = config?.Validate is false ? ValidationMode.None : ValidationMode.Standard,
                AccumulateErrors = true,
            })
        .Bind(results => results switch {
            [] => ResultFactory.Create(value: new IntersectionResult([])),
            [IntersectionResult single] => ResultFactory.Create(value: single),
            _ => ResultFactory.Create(value: new IntersectionResult(
                [.. results.SelectMany(r => r.Points)],
                results.SelectMany(r => r.Curves ?? []).ToArray() is { Length: > 0 } curves ? curves : null,
                results.SelectMany(r => r.ParametersA ?? []).ToArray() is { Length: > 0 } paramsA ? paramsA : null,
                results.SelectMany(r => r.ParametersB ?? []).ToArray() is { Length: > 0 } paramsB ? paramsB : null,
                results.SelectMany(r => r.FaceIndices ?? []).ToArray() is { Length: > 0 } indices ? indices : null,
                results.SelectMany(r => r.Sections ?? []).ToArray() is { Length: > 0 } sections ? sections : null)),
        });
}
