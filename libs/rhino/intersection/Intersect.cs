using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection engine with automatic type-based method detection.</summary>
public static class Intersect {
    /// <summary>Type-safe optional parameters for intersection operations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOptions(
        double? Tolerance = null,
        Vector3d? ProjectionDirection = null,
        int? MaxHits = null,
        bool WithIndices = false,
        bool Sorted = false);

    /// <summary>Unified intersection output with zero nullable fields.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOutput(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
    }
    /// <summary>Performs intersection with automatic type detection, validation, and collection handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Compute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        IntersectionOptions? options = null,
        bool enableDiagnostics = false) where T1 : notnull where T2 : notnull {
        IntersectionOptions opts = options ?? new();
        Type t1Type = typeof(T1);
        Type t2Type = typeof(T2);
        Type elementType = t1Type is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? t.GetGenericArguments()[0]
            : t1Type;
        V mode = IntersectionCore._validationConfig.TryGetValue((t1Type, t2Type), out V m1) ? m1
            : IntersectionCore._validationConfig.TryGetValue((elementType, t2Type), out V m2) ? m2
            : V.None;

        return UnifiedOperation.Apply(
            geometryA,
            (Func<object, Result<IReadOnlyList<IntersectionOutput>>>)(item =>
                IntersectionCore.ExecutePair(item, geometryB, context, opts)
                    .Map(r => (IReadOnlyList<IntersectionOutput>)[r])),
            new OperationConfig<object, IntersectionOutput> {
                Context = context,
                ValidationMode = mode,
                AccumulateErrors = true,
                OperationName = $"Intersect.{t1Type.Name}.{t2Type.Name}",
                EnableDiagnostics = enableDiagnostics,
            })
        .Map(outputs => outputs.Aggregate(IntersectionOutput.Empty, (acc, curr) => new IntersectionOutput(
            [.. acc.Points, .. curr.Points],
            [.. acc.Curves, .. curr.Curves],
            [.. acc.ParametersA, .. curr.ParametersA],
            [.. acc.ParametersB, .. curr.ParametersB],
            [.. acc.FaceIndices, .. curr.FaceIndices],
            [.. acc.Sections, .. curr.Sections])));
    }
}
