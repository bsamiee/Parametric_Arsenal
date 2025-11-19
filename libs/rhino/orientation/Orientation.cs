using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation and canonical alignment operations.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0104:Type name should not collide", Justification = "Different namespace")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match namespace", Justification = "Intentional API design")]
public static class Orientation {
    /// <summary>Base type for orientation transform operations.</summary>
    public abstract record Operation;

    /// <summary>Align geometry from source plane to target plane.</summary>
    public sealed record ToPlane(Plane Target) : Operation;

    /// <summary>Align geometry to canonical world orientation.</summary>
    public sealed record ToCanonical(CanonicalMode Mode) : Operation;

    /// <summary>Translate geometry centroid to target point.</summary>
    public sealed record ToPoint(Point3d Target, CentroidMode CentroidType) : Operation;

    /// <summary>Rotate geometry to align source vector with target vector.</summary>
    public sealed record ToVector(Vector3d Target, Vector3d? Source = null, Point3d? Anchor = null) : Operation;

    /// <summary>Orient geometry to best-fit plane (PCA).</summary>
    public sealed record ToBestFit : Operation;

    /// <summary>Mirror geometry across a plane.</summary>
    public sealed record Mirror(Plane MirrorPlane) : Operation;

    /// <summary>Flip curve direction or surface/mesh normals.</summary>
    public sealed record FlipDirection : Operation;

    /// <summary>Orient to curve frame at parameter.</summary>
    public sealed record ToCurveFrame(Curve Curve, double Parameter) : Operation;

    /// <summary>Orient to surface frame at UV coordinates.</summary>
    public sealed record ToSurfaceFrame(Surface Surface, double U, double V) : Operation;

    /// <summary>Base type for canonical orientation modes.</summary>
    public abstract record CanonicalMode;

    /// <summary>Align bounding box center to world XY plane.</summary>
    public sealed record WorldXY : CanonicalMode;

    /// <summary>Align bounding box center to world YZ plane.</summary>
    public sealed record WorldYZ : CanonicalMode;

    /// <summary>Align bounding box center to world XZ plane.</summary>
    public sealed record WorldXZ : CanonicalMode;

    /// <summary>Translate area centroid to world origin.</summary>
    public sealed record AreaCentroid : CanonicalMode;

    /// <summary>Translate volume centroid to world origin.</summary>
    public sealed record VolumeCentroid : CanonicalMode;

    /// <summary>Base type for centroid extraction modes.</summary>
    public abstract record CentroidMode;

    /// <summary>Use bounding box center as centroid.</summary>
    public sealed record BoundingBoxCentroid : CentroidMode;

    /// <summary>Use mass properties centroid.</summary>
    public sealed record MassCentroid : CentroidMode;

    /// <summary>Base type for optimization criteria.</summary>
    public abstract record OptimizationCriteria;

    /// <summary>Minimize bounding box diagonal (compact).</summary>
    public sealed record CompactCriteria : OptimizationCriteria;

    /// <summary>Center centroid on XY plane.</summary>
    public sealed record CenteredCriteria : OptimizationCriteria;

    /// <summary>Maximize flatness (degenerate dimensions).</summary>
    public sealed record FlatnessCriteria : OptimizationCriteria;

    /// <summary>Canonical positioning with multiple factors.</summary>
    public sealed record CanonicalCriteria : OptimizationCriteria;

    /// <summary>Base type for symmetry classification.</summary>
    public abstract record SymmetryType;

    /// <summary>No detected symmetry.</summary>
    public sealed record NoSymmetry : SymmetryType;

    /// <summary>Mirror symmetry detected.</summary>
    public sealed record MirrorSymmetry : SymmetryType;

    /// <summary>Rotational symmetry detected.</summary>
    public sealed record RotationalSymmetry : SymmetryType;

    /// <summary>Base type for geometric relationship.</summary>
    public abstract record RelationshipType;

    /// <summary>Parallel orientation.</summary>
    public sealed record Parallel : RelationshipType;

    /// <summary>Perpendicular orientation.</summary>
    public sealed record Perpendicular : RelationshipType;

    /// <summary>Neither parallel nor perpendicular.</summary>
    public sealed record Oblique : RelationshipType;

    /// <summary>Base type for detected patterns.</summary>
    public abstract record PatternType;

    /// <summary>Linear arrangement with consistent spacing.</summary>
    public sealed record LinearPattern : PatternType;

    /// <summary>Radial arrangement around center.</summary>
    public sealed record RadialPattern : PatternType;

    /// <summary>No recognizable pattern.</summary>
    public sealed record NoPattern : PatternType;

    /// <summary>Result of optimization operation.</summary>
    [DebuggerDisplay("Score={Score:F3}, Satisfied={CriteriaSatisfied.Length}")]
    public sealed record OptimizationResult(
        Transform OptimalTransform,
        double Score,
        OptimizationCriteria[] CriteriaSatisfied);

    /// <summary>Result of relative orientation computation.</summary>
    [DebuggerDisplay("Twist={Twist:F3}°, Tilt={Tilt:F3}°")]
    public sealed record RelativeOrientationResult(
        Transform RelativeTransform,
        double Twist,
        double Tilt,
        SymmetryType Symmetry,
        RelationshipType Relationship);

    /// <summary>Result of pattern detection.</summary>
    [DebuggerDisplay("Pattern={Pattern}, Deviation={Deviation:F4}")]
    public sealed record PatternDetectionResult(
        PatternType Pattern,
        Transform[] IdealTransforms,
        int[] Anomalies,
        double Deviation);

    /// <summary>Execute orientation operation on geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Execute<T>(T geometry, Operation operation, IGeometryContext context) where T : GeometryBase =>
        OrientationCore.Execute(geometry: geometry, operation: operation, context: context);

    /// <summary>Optimize orientation based on criteria.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<OptimizationResult> OptimizeOrientation(Brep brep, OptimizationCriteria criteria, IGeometryContext context) =>
        OrientationCore.ExecuteOptimization(brep: brep, criteria: criteria, context: context);

    /// <summary>Compute relative orientation between two geometries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<RelativeOrientationResult> ComputeRelativeOrientation(GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context) =>
        OrientationCore.ExecuteRelative(geometryA: geometryA, geometryB: geometryB, context: context);

    /// <summary>Detect and align pattern in geometry array.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternDetectionResult> DetectAndAlign(GeometryBase[] geometries, IGeometryContext context) =>
        OrientationCore.ExecutePatternDetection(geometries: geometries, context: context);
}
