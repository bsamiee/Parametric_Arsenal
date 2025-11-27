using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Polymorphic geometry intersection and analysis operations.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0104:Type name should not collide", Justification = "Different namespace")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match namespace", Justification = "Intentional API design")]
public static class Intersection {
    /// <summary>Base type for intersection requests.</summary>
    public abstract record Request;

    /// <summary>General geometry pair intersection.</summary>
    public sealed record General(
        object GeometryA,
        object GeometryB,
        IntersectionSettings? Settings = null) : Request;

    /// <summary>Point projection to Brep or Mesh collections with optional direction.</summary>
    public sealed record PointProjection(
        Point3d[] Points,
        object Targets,
        Vector3d? Direction = null,
        bool WithIndices = false) : Request;

    /// <summary>Ray shooting through geometry collection with hit limit.</summary>
    public sealed record RayShoot(
        Ray3d Ray,
        GeometryBase[] Targets,
        int MaxHits = 1) : Request;

    /// <summary>Intersection operation settings controlling tolerance, projection, and output formatting.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionSettings(
        double? Tolerance = null,
        bool Sorted = false);

    /// <summary>Base type for intersection type classification.</summary>
    public abstract record IntersectionType {
        private IntersectionType() { }
        /// <summary>Tangent intersection with near-parallel approach vectors.</summary>
        public sealed record Tangent : IntersectionType {
            private Tangent() { }
            /// <summary>Singleton instance for tangent intersections.</summary>
            public static Tangent Instance { get; } = new();
        }
        /// <summary>Transverse intersection with significant angular separation.</summary>
        public sealed record Transverse : IntersectionType {
            private Transverse() { }
            /// <summary>Singleton instance for transverse intersections.</summary>
            public static Transverse Instance { get; } = new();
        }
        /// <summary>Unknown classification when insufficient data available.</summary>
        public sealed record Unknown : IntersectionType {
            private Unknown() { }
            /// <summary>Singleton instance for unknown classifications.</summary>
            public static Unknown Instance { get; } = new();
        }
    }

    /// <summary>Intersection operation result containing points, curves, parameters, and topology indices.</summary>
    /// <remarks>Consumers must dispose Curves collection elements as they implement IDisposable.</remarks>
    [DebuggerDisplay("Points={Points.Count}, Curves={Curves.Count}")]
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

    /// <summary>Result of intersection classification analysis.</summary>
    [DebuggerDisplay("Type={Type}, IsGrazing={IsGrazing}, BlendScore={BlendScore:F3}")]
    public sealed record ClassificationResult(
        IntersectionType Type,
        double[] ApproachAngles,
        bool IsGrazing,
        double BlendScore);

    /// <summary>Result of near-miss detection analysis.</summary>
    [DebuggerDisplay("Count={LocationsA.Length}, MaxDistance={Distances.Length > 0 ? Distances.Max() : 0:F6}")]
    public sealed record NearMissResult(
        Point3d[] LocationsA,
        Point3d[] LocationsB,
        double[] Distances);

    /// <summary>Result of intersection stability analysis.</summary>
    [DebuggerDisplay("Score={StabilityScore:F3}, Sensitivity={PerturbationSensitivity:F3}")]
    public sealed record StabilityResult(
        double StabilityScore,
        double PerturbationSensitivity,
        bool[] UnstableFlags);

    /// <summary>Execute intersection request on geometries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Execute(Request request, IGeometryContext context) =>
        IntersectionCore.ExecuteRequest(request: request, context: context);

    /// <summary>Classifies intersection type (tangent/transverse/unknown) via approach angle analysis.</summary>
    [Pure] public static Result<ClassificationResult> Classify(
        IntersectionOutput output,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCore.ExecuteClassification(
            output: output,
            geometryA: geometryA,
            geometryB: geometryB,
            context: context);

    /// <summary>Finds near-miss locations within tolerance band via closest point sampling.</summary>
    [Pure] public static Result<NearMissResult> FindNearMisses(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double searchRadius,
        IGeometryContext context) =>
        IntersectionCore.ExecuteNearMiss(
            geometryA: geometryA,
            geometryB: geometryB,
            searchRadius: searchRadius,
            context: context);

    /// <summary>Analyzes intersection stability via spherical perturbation sampling.</summary>
    [Pure] public static Result<StabilityResult> AnalyzeStability(
        IntersectionOutput baseIntersection,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCore.ExecuteStability(
            baseIntersection: baseIntersection,
            geometryA: geometryA,
            geometryB: geometryB,
            context: context);
}
