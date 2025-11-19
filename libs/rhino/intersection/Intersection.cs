using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic intersection APIs with algebraic request types.</summary>
public static class Intersection {
    /// <summary>Algebraic intersection request root type.</summary>
    public abstract record Request {
        private protected Request() {
        }

        /// <summary>General intersection request with optional tolerance override.</summary>
        public sealed record General(double? Tolerance = null, bool UseSortedMeshEvaluation = false) : Request;

        /// <summary>Point projection request specifying direction and index capture.</summary>
        public sealed record PointProjection(Vector3d Direction, bool IncludeIndices, double? Tolerance = null) : Request;

        /// <summary>Ray shooting request specifying maximum hit count.</summary>
        public sealed record RayShoot(int MaxHits, double? Tolerance = null) : Request;
    }

    /// <summary>Intersection execution result containing aggregated primitives.</summary>
    public sealed record IntersectionResult(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        /// <summary>Reusable empty result instance.</summary>
        public static readonly IntersectionResult Empty = new([], [], [], [], [], []);
    }

    /// <summary>Intersection classification result.</summary>
    public sealed record ClassificationResult(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore);

    /// <summary>Near-miss sampling result.</summary>
    public sealed record NearMissResult(Point3d[] LocationsA, Point3d[] LocationsB, double[] Distances);

    /// <summary>Stability analysis result.</summary>
    public sealed record StabilityResult(double Score, double Sensitivity, bool[] UnstableFlags);

    private static readonly Request.General DefaultRequest = new();

    /// <summary>Executes polymorphic intersection between two geometries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionResult> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        IGeometryContext context,
        Request? request = null) where T1 : notnull where T2 : notnull =>
        IntersectionCore.Execute(
            geometryA: geometryA,
            geometryB: geometryB,
            context: context,
            request: request ?? DefaultRequest);

    /// <summary>Classifies intersection type using tangent analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClassificationResult> ClassifyIntersection(
        IntersectionResult output,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCompute.Classify(output: output, geomA: geometryA, geomB: geometryB, context: context);

    /// <summary>Finds near-miss locations using closest point sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<NearMissResult> FindNearMisses(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double searchRadius,
        IGeometryContext context) =>
        IntersectionCompute.FindNearMisses(geomA: geometryA, geomB: geometryB, searchRadius: searchRadius, context: context);

    /// <summary>Analyzes intersection stability using perturbation sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<StabilityResult> AnalyzeStability(
        IntersectionResult baseIntersection,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCompute.AnalyzeStability(geomA: geometryA, geomB: geometryB, baseOutput: baseIntersection, context: context);
}
