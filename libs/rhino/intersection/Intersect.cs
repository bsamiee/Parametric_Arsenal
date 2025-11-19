using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection with strongly typed algebraic requests.</summary>
public static class Intersect {
    /// <summary>Intersection option set controlling tolerance, projection, and sorting behavior.</summary>
    public sealed record IntersectionSettings(
        double? Tolerance = null,
        Vector3d? ProjectionDirection = null,
        int? MaxHits = null,
        bool IncludeIndices = false,
        bool Sorted = false) {
        /// <summary>Default settings instance.</summary>
        public static IntersectionSettings Default { get; } = new();
    }

    /// <summary>Base type for all intersection requests.</summary>
    public abstract record IntersectionRequest;

    /// <summary>Intersection computation request for two arbitrary Rhino geometry operands.</summary>
    public sealed record GeometryIntersectionRequest(
        object GeometryA,
        object GeometryB,
        IntersectionSettings? Settings = null) : IntersectionRequest;

    /// <summary>Classification request using an existing intersection output.</summary>
    public sealed record ClassificationRequest(
        IntersectionOutput Output,
        GeometryBase GeometryA,
        GeometryBase GeometryB) : IntersectionRequest;

    /// <summary>Near-miss sampling request between two geometries.</summary>
    public sealed record NearMissRequest(
        GeometryBase GeometryA,
        GeometryBase GeometryB,
        double SearchRadius) : IntersectionRequest;

    /// <summary>Stability analysis request for an existing intersection solution.</summary>
    public sealed record StabilityRequest(
        IntersectionOutput BaseIntersection,
        GeometryBase GeometryA,
        GeometryBase GeometryB) : IntersectionRequest;

    /// <summary>Intersection result containing all extracted entities and associated indices.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionOutput(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        /// <summary>Empty output singleton.</summary>
        public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
    }

    /// <summary>Classification data describing intersection tangency and grazing metrics.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct ClassificationResult(
        byte Type,
        double[] ApproachAngles,
        bool IsGrazing,
        double BlendScore);

    /// <summary>Near-miss sampling data with paired locations and distances.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct NearMissResult(
        Point3d[] LocationsA,
        Point3d[] LocationsB,
        double[] Distances);

    /// <summary>Stability evaluation result including score, sensitivity, and unstable flags.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct StabilityResult(
        double StabilityScore,
        double PerturbationSensitivity,
        bool[] UnstableFlags);

    /// <summary>Executes polymorphic intersection with automatic metadata-driven dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Execute(
        GeometryIntersectionRequest request,
        IGeometryContext context) =>
        IntersectionCore.Execute(request: request, context: context);

    /// <summary>Classifies the supplied intersection result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClassificationResult> ClassifyIntersection(
        ClassificationRequest request,
        IGeometryContext context) =>
        IntersectionCore.Classify(request: request, context: context);

    /// <summary>Computes near-miss sample pairs for the supplied geometries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NearMissResult> FindNearMisses(
        NearMissRequest request,
        IGeometryContext context) =>
        IntersectionCore.NearMisses(request: request, context: context);

    /// <summary>Evaluates intersection stability under perturbation sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<StabilityResult> AnalyzeStability(
        StabilityRequest request,
        IGeometryContext context) =>
        IntersectionCore.Stability(request: request, context: context);
}
