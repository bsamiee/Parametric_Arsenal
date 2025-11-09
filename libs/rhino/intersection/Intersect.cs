using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection engine with automatic type-based method detection.</summary>
public static class Intersect {
    /// <summary>Configuration parameters for intersection computation: tolerance, projection, output format.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOptions(
        double? Tolerance = null,
        Vector3d? ProjectionDirection = null,
        int? MaxHits = null,
        bool WithIndices = false,
        bool Sorted = false);

    /// <summary>Polymorphic intersection result containing points, curves, parameters, and topology indices.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOutput(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        /// <summary>Empty intersection result with zero-length collections.</summary>
        public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
    }
    /// <summary>Performs intersection with automatic type detection, validation, and collection handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        IntersectionOptions? options = null,
        bool enableDiagnostics = false) where T1 : notnull where T2 : notnull {
        IntersectionOptions opts = options ?? new();
        (Type t1, Type t2) = (typeof(T1), typeof(T2));
        Type elem = t1 is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? t.GetGenericArguments()[0] : t1;
        V mode = IntersectionConfig.ValidationModes.TryGetValue((t1, t2), out V m1) ? m1 : IntersectionConfig.ValidationModes.TryGetValue((elem, t2), out V m2) ? m2 : V.None;

        return UnifiedOperation.Apply(
            geometryA,
            (Func<object, Result<IReadOnlyList<IntersectionOutput>>>)(item => IntersectionCore.ExecutePair(item, geometryB, context, opts).Map(r => (IReadOnlyList<IntersectionOutput>)[r])),
            new OperationConfig<object, IntersectionOutput> {
                Context = context,
                ValidationMode = mode,
                AccumulateErrors = true,
                OperationName = $"Intersect.{t1.Name}.{t2.Name}",
                EnableDiagnostics = enableDiagnostics,
            })
        .Map(outputs => outputs.Count switch {
            0 => IntersectionOutput.Empty,
            _ => new IntersectionOutput(
                [.. outputs.SelectMany(output => output.Points)],
                [.. outputs.SelectMany(output => output.Curves)],
                [.. outputs.SelectMany(output => output.ParametersA)],
                [.. outputs.SelectMany(output => output.ParametersB)],
                [.. outputs.SelectMany(output => output.FaceIndices)],
                [.. outputs.SelectMany(output => output.Sections)]),
        });
    }
}
