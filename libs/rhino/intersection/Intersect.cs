using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection with automatic type-based dispatch.</summary>
public static class Intersect {
    /// <summary>Intersection classification type discriminating tangent, transverse, and unknown intersections.</summary>
    public enum ClassificationType : byte {
        /// <summary>Tangent intersection with near-parallel approach vectors.</summary>
        Tangent = 0,
        /// <summary>Transverse intersection with significant angular separation.</summary>
        Transverse = 1,
        /// <summary>Unknown classification when insufficient data available.</summary>
        Unknown = 2,
    }

    /// <summary>Intersection operation options controlling tolerance, projection, and output formatting.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOptions(
        double? Tolerance = null,
        Vector3d? ProjectionDirection = null,
        int? MaxHits = null,
        bool WithIndices = false,
        bool Sorted = false);

    /// <summary>Intersection operation result containing points, curves, parameters, and topology indices.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOutput(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        /// <summary>Empty result with zero-length collections for non-intersecting geometries.</summary>
        public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
    }

    /// <summary>Classification result containing type discrimination, approach angles, and blend quality score.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct ClassificationResult(
        ClassificationType Type,
        IReadOnlyList<double> ApproachAngles,
        bool IsGrazing,
        double BlendScore);

    /// <summary>Near-miss result containing paired locations and distances within search radius.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct NearMissResult(
        IReadOnlyList<Point3d> LocationsOnGeometryA,
        IReadOnlyList<Point3d> LocationsOnGeometryB,
        IReadOnlyList<double> Distances) {
        /// <summary>Empty result with zero-length collections for geometries with no near-misses.</summary>
        public static readonly NearMissResult Empty = new([], [], []);
    }

    /// <summary>Stability result containing score, sensitivity, and per-point instability flags.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct StabilityResult(
        double Score,
        double Sensitivity,
        IReadOnlyList<bool> UnstableFlags) {
        /// <summary>Stable result with maximum score and zero sensitivity for empty intersections.</summary>
        public static readonly StabilityResult Stable = new(Score: 1.0, Sensitivity: 0.0, UnstableFlags: []);
    }

    /// <summary>Executes type-detected intersection with automatic validation and collection aggregation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        IntersectionOptions? options = null) where T1 : notnull where T2 : notnull {
        IntersectionOptions opts = options ?? new IntersectionOptions();
        (Type t1, Type t2) = (typeof(T1), typeof(T2));

        return IntersectionCore.NormalizeOptions(opts, context)
            .Bind(normalized => UnifiedOperation.Apply(
                geometryA,
                (Func<object, Result<IReadOnlyList<IntersectionOutput>>>)(item => IntersectionCore.ExecuteWithOptions(
                        item,
                        geometryB,
                        context,
                        normalized)
                    .Map(output => (IReadOnlyList<IntersectionOutput>)[output,])),
                new OperationConfig<object, IntersectionOutput> {
                    Context = context,
                    ValidationMode = V.None,
                    AccumulateErrors = true,
                    OperationName = $"Intersect.{t1.Name}.{t2.Name}",
                    EnableDiagnostics = false,
                }))
        .Map(outputs => outputs.Count == 0
            ? IntersectionOutput.Empty
            : new IntersectionOutput(
                [.. outputs.SelectMany(static output => output.Points),],
                [.. outputs.SelectMany(static output => output.Curves),],
                [.. outputs.SelectMany(static output => output.ParametersA),],
                [.. outputs.SelectMany(static output => output.ParametersB),],
                [.. outputs.SelectMany(static output => output.FaceIndices),],
                [.. outputs.SelectMany(static output => output.Sections),]));
    }

    /// <summary>Classifies intersection type (tangent/transverse/unknown) via approach angle analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClassificationResult> ClassifyIntersection(
        IntersectionOutput output,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCompute.Classify(output: output, geomA: geometryA, geomB: geometryB, context: context);

    /// <summary>Finds near-miss locations within tolerance band via closest point sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NearMissResult> FindNearMisses(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double searchRadius,
        IGeometryContext context) =>
        IntersectionCompute.FindNearMisses(geomA: geometryA, geomB: geometryB, searchRadius: searchRadius, context: context);

    /// <summary>Analyzes intersection stability via spherical perturbation sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<StabilityResult> AnalyzeStability(
        IntersectionOutput baseIntersection,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCompute.AnalyzeStability(geomA: geometryA, geomB: geometryB, baseOutput: baseIntersection, context: context);
}
