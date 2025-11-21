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
    public abstract record Request;

    public sealed record General(
        object GeometryA,
        object GeometryB,
        IntersectionSettings? Settings = null) : Request;

    public sealed record PointProjection(
        Point3d[] Points,
        object Targets,
        Vector3d? Direction = null,
        bool WithIndices = false) : Request;

    public sealed record RayShoot(
        Ray3d Ray,
        GeometryBase[] Targets,
        int MaxHits = 1) : Request;

    /// <summary>Settings controlling tolerance and output formatting.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct IntersectionSettings(
        double? Tolerance = null,
        bool Sorted = false);

    public abstract record IntersectionType {
        private IntersectionType() { }
        public sealed record Tangent : IntersectionType {
            private Tangent() { }
            public static Tangent Instance { get; } = new();
        }
        public sealed record Transverse : IntersectionType {
            private Transverse() { }
            public static Transverse Instance { get; } = new();
        }
        public sealed record Unknown : IntersectionType {
            private Unknown() { }
            public static Unknown Instance { get; } = new();
        }
    }

    /// <summary>Intersection results with points, curves, parameters, and indices. Curves implement IDisposable.</summary>
    [DebuggerDisplay("Points={Points.Count}, Curves={Curves.Count}")]
    public readonly record struct IntersectionOutput(
        IReadOnlyList<Point3d> Points,
        IReadOnlyList<Curve> Curves,
        IReadOnlyList<double> ParametersA,
        IReadOnlyList<double> ParametersB,
        IReadOnlyList<int> FaceIndices,
        IReadOnlyList<Polyline> Sections) {
        public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
    }

    [DebuggerDisplay("Type={Type}, IsGrazing={IsGrazing}, BlendScore={BlendScore:F3}")]
    public sealed record ClassificationResult(
        IntersectionType Type,
        double[] ApproachAngles,
        bool IsGrazing,
        double BlendScore);

    [DebuggerDisplay("Count={LocationsA.Length}, MaxDistance={Distances.Length > 0 ? Distances.Max() : 0:F6}")]
    public sealed record NearMissResult(
        Point3d[] LocationsA,
        Point3d[] LocationsB,
        double[] Distances);

    [DebuggerDisplay("Score={StabilityScore:F3}, Sensitivity={PerturbationSensitivity:F3}")]
    public sealed record StabilityResult(
        double StabilityScore,
        double PerturbationSensitivity,
        bool[] UnstableFlags);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IntersectionOutput> Execute(Request request, IGeometryContext context) =>
        IntersectionCore.ExecuteRequest(request: request, context: context);

    [Pure]
    public static Result<ClassificationResult> Classify(
        IntersectionOutput output,
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        IntersectionCore.ExecuteClassification(
            output: output,
            geometryA: geometryA,
            geometryB: geometryB,
            context: context);

    [Pure]
    public static Result<NearMissResult> FindNearMisses(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double searchRadius,
        IGeometryContext context) =>
        IntersectionCore.ExecuteNearMiss(
            geometryA: geometryA,
            geometryB: geometryB,
            searchRadius: searchRadius,
            context: context);

    [Pure]
    public static Result<StabilityResult> AnalyzeStability(
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
