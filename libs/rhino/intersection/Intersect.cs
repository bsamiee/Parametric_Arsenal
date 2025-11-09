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

        /// <summary>Creates output with only points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntersectionOutput FromPoints(IReadOnlyList<Point3d> points) => new(points, [], [], [], [], []);

        /// <summary>Creates output with only points and parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntersectionOutput FromPoints(IReadOnlyList<Point3d> points, IReadOnlyList<double> paramsA, IReadOnlyList<double> paramsB = null!) =>
            new(points, [], paramsA, paramsB ?? [], [], []);

        /// <summary>Creates output with points and topology indices.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntersectionOutput FromPointsWithIndices(IReadOnlyList<Point3d> points, IReadOnlyList<int> indices) =>
            new(points, [], [], [], indices, []);

        /// <summary>Creates output with only curves.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntersectionOutput FromCurves(IReadOnlyList<Curve> curves) => new([], curves, [], [], [], []);

        /// <summary>Creates output with points and curves.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntersectionOutput FromGeometry(IReadOnlyList<Point3d> points, IReadOnlyList<Curve> curves) =>
            new(points, curves, [], [], [], []);
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
        Type t1Type = typeof(T1);
        Type t2Type = typeof(T2);
        Type elementType = t1Type is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? t.GetGenericArguments()[0]
            : t1Type;
        V mode = IntersectionConfig.GetValidationMode(t1Type, elementType, t2Type);

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
        .Map(outputs => outputs.Aggregate(IntersectionOutput.Empty, (a, c) => new IntersectionOutput(
            [.. a.Points, .. c.Points], [.. a.Curves, .. c.Curves],
            [.. a.ParametersA, .. c.ParametersA], [.. a.ParametersB, .. c.ParametersB],
            [.. a.FaceIndices, .. c.FaceIndices], [.. a.Sections, .. c.Sections])));
    }
}
