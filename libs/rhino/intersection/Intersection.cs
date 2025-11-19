using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection with automatic type-based dispatch and algebraic result types.</summary>
public static class Intersection {
    /// <summary>Base type for intersection operation modes.</summary>
    public abstract record IntersectionMode;

    /// <summary>Standard intersection with optional tolerance override.</summary>
    public sealed record StandardMode(double? Tolerance = null) : IntersectionMode;

    /// <summary>Sorted intersection for mesh-line/polyline operations.</summary>
    public sealed record SortedMode(double? Tolerance = null) : IntersectionMode;

    /// <summary>Projection intersection with direction, max hits, and optional index tracking.</summary>
    public sealed record ProjectionMode(Vector3d Direction, int MaxHits, double? Tolerance = null, bool WithIndices = false) : IntersectionMode;

    /// <summary>Base type for intersection classification.</summary>
    public abstract record ClassificationType;

    /// <summary>Tangent intersection with near-parallel approach angles.</summary>
    public sealed record TangentClassification : ClassificationType;

    /// <summary>Transverse intersection with significant approach angles.</summary>
    public sealed record TransverseClassification : ClassificationType;

    /// <summary>Unknown or unclassifiable intersection type.</summary>
    public sealed record UnknownClassification : ClassificationType;

    /// <summary>Intersection operation result containing points, curves, parameters, and topology indices.</summary>
    [DebuggerDisplay("Points={Points.Count}, Curves={Curves.Count}")]
    public sealed record IntersectionResult(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        /// <summary>Empty result with zero-length collections for non-intersecting geometries.</summary>
        public static readonly IntersectionResult Empty = new([], [], [], [], [], []);
    }

    /// <summary>Classification result with type, approach angles, grazing flag, and blend score.</summary>
    [DebuggerDisplay("Type={Type}, IsGrazing={IsGrazing}, BlendScore={BlendScore:F3}")]
    public sealed record ClassificationResult(
        ClassificationType Type,
        double[] ApproachAngles,
        bool IsGrazing,
        double BlendScore);

    /// <summary>Near-miss result with corresponding locations and distances.</summary>
    [DebuggerDisplay("Count={LocationsA.Length}")]
    public sealed record NearMissResult(
        Point3d[] LocationsA,
        Point3d[] LocationsB,
        double[] Distances);

    /// <summary>Stability analysis result with score, sensitivity, and per-point instability flags.</summary>
    [DebuggerDisplay("Score={Score:F3}, Sensitivity={Sensitivity:F3}")]
    public sealed record StabilityResult(
        double Score,
        double Sensitivity,
        bool[] UnstableFlags);

    /// <summary>Executes type-detected intersection with automatic validation and collection aggregation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionResult> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        IntersectionMode? mode = null) where T1 : notnull where T2 : notnull =>
        IntersectionCore.Execute(
            geometryA: geometryA,
            geometryB: geometryB,
            context: context,
            mode: mode ?? new StandardMode());

    /// <summary>Classifies intersection type (tangent/transverse/unknown) via approach angle analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClassificationResult> Classify(
        IntersectionResult result,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCore.Classify(
            result: result,
            geometryA: geometryA,
            geometryB: geometryB,
            context: context);

    /// <summary>Finds near-miss locations within tolerance band via closest point sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NearMissResult> FindNearMisses(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double searchRadius,
        IGeometryContext context) =>
        IntersectionCore.FindNearMisses(
            geometryA: geometryA,
            geometryB: geometryB,
            searchRadius: searchRadius,
            context: context);

    /// <summary>Analyzes intersection stability via spherical perturbation sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<StabilityResult> AnalyzeStability(
        IntersectionResult baseResult,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCore.AnalyzeStability(
            baseResult: baseResult,
            geometryA: geometryA,
            geometryB: geometryB,
            context: context);
}
