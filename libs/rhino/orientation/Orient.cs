using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation and canonical alignment.</summary>
public static class Orient {
    // ═══════════════════════════════════════════════════════════════════════════════
    // CANONICAL MODES - Algebraic hierarchy for world plane alignment
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base type for canonical orientation modes.</summary>
    public abstract record CanonicalMode;
    /// <summary>Align bounding box center to world XY plane.</summary>
    public sealed record WorldXYMode : CanonicalMode;
    /// <summary>Align bounding box center to world YZ plane.</summary>
    public sealed record WorldYZMode : CanonicalMode;
    /// <summary>Align bounding box center to world XZ plane.</summary>
    public sealed record WorldXZMode : CanonicalMode;
    /// <summary>Translate bounding box center to world origin.</summary>
    public sealed record AreaCentroidMode : CanonicalMode;
    /// <summary>Translate volume centroid to world origin.</summary>
    public sealed record VolumeCentroidMode : CanonicalMode;

    // ═══════════════════════════════════════════════════════════════════════════════
    // ORIENTATION TARGETS - Algebraic hierarchy for orientation destinations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base type for orientation targets.</summary>
    public abstract record OrientationTarget;
    /// <summary>Plane-to-plane alignment target.</summary>
    public sealed record PlaneTarget(Plane Target) : OrientationTarget;
    /// <summary>Point-to-point translation using bounding box centroid.</summary>
    public sealed record PointTarget(Point3d Target) : OrientationTarget;
    /// <summary>Point-to-point translation using mass properties centroid.</summary>
    public sealed record MassPointTarget(Point3d Target) : OrientationTarget;
    /// <summary>Vector-to-vector rotation alignment.</summary>
    public sealed record VectorTarget(Vector3d Target, Vector3d Source, Point3d? Anchor) : OrientationTarget;
    /// <summary>Curve frame alignment at parameter t.</summary>
    public sealed record CurveFrameTarget(Curve Curve, double Parameter) : OrientationTarget;
    /// <summary>Surface frame alignment at UV coordinates.</summary>
    public sealed record SurfaceFrameTarget(Surface Surface, double U, double V) : OrientationTarget;

    // ═══════════════════════════════════════════════════════════════════════════════
    // OPTIMIZATION CRITERIA - Algebraic hierarchy for optimization objectives
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base type for optimization criteria.</summary>
    public abstract record OptimizationCriteria;
    /// <summary>Minimize bounding box diagonal length.</summary>
    public sealed record CompactCriteria : OptimizationCriteria;
    /// <summary>Minimize centroid Z-displacement from XY plane.</summary>
    public sealed record CentroidCriteria : OptimizationCriteria;
    /// <summary>Maximize dimensional degeneracy (flatness).</summary>
    public sealed record FlatnessCriteria : OptimizationCriteria;
    /// <summary>Multi-objective: above XY, centered, low profile.</summary>
    public sealed record CanonicalCriteria : OptimizationCriteria;

    // ═══════════════════════════════════════════════════════════════════════════════
    // SYMMETRY AND RELATIONSHIP KINDS - Algebraic classification types
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base type for symmetry classification.</summary>
    public abstract record SymmetryKind;
    /// <summary>No detected symmetry.</summary>
    public sealed record NoSymmetry : SymmetryKind;
    /// <summary>Mirror symmetry detected.</summary>
    public sealed record MirrorSymmetry : SymmetryKind;
    /// <summary>Rotational symmetry detected.</summary>
    public sealed record RotationalSymmetry : SymmetryKind;

    /// <summary>Base type for spatial relationship classification.</summary>
    public abstract record RelationshipKind;
    /// <summary>Parallel orientation (Z-axes aligned).</summary>
    public sealed record ParallelRelationship : RelationshipKind;
    /// <summary>Perpendicular orientation (Z-axes orthogonal).</summary>
    public sealed record PerpendicularRelationship : RelationshipKind;
    /// <summary>Oblique orientation (neither parallel nor perpendicular).</summary>
    public sealed record ObliqueRelationship : RelationshipKind;

    // ═══════════════════════════════════════════════════════════════════════════════
    // PATTERN KINDS - Algebraic hierarchy for detected patterns
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base type for pattern classification.</summary>
    public abstract record PatternKind;
    /// <summary>Linear pattern with uniform spacing.</summary>
    public sealed record LinearPattern : PatternKind;
    /// <summary>Radial pattern with uniform angular distribution.</summary>
    public sealed record RadialPattern : PatternKind;

    // ═══════════════════════════════════════════════════════════════════════════════
    // RESULT TYPES - Strongly-typed results for complex operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Result of orientation optimization.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct OptimizationResult(
        Transform OptimalTransform,
        double Score,
        OptimizationCriteria CriteriaMet);

    /// <summary>Result of relative orientation computation.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct RelativeOrientationResult(
        Transform RelativeTransform,
        double Twist,
        double Tilt,
        SymmetryKind Symmetry,
        RelationshipKind Relationship);

    /// <summary>Result of pattern detection and alignment.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct PatternResult(
        PatternKind Pattern,
        Transform[] IdealTransforms,
        int[] Anomalies,
        double Deviation);

    // ═══════════════════════════════════════════════════════════════════════════════
    // PUBLIC API - Orientation operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Orient geometry to a canonical world plane alignment.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, CanonicalMode mode, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToCanonical(geometry: geometry, mode: mode, context: context);

    /// <summary>Orient geometry to a target plane, point, vector, or frame.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToTarget<T>(T geometry, OrientationTarget target, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToTarget(geometry: geometry, target: target, context: context);

    /// <summary>Orient geometry to a target plane using plane-to-plane transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(T geometry, Plane target, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToTarget(geometry: geometry, target: new PlaneTarget(Target: target), context: context);

    /// <summary>Orient geometry to a target point using bounding box centroid.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d target, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToTarget(geometry: geometry, target: new PointTarget(Target: target), context: context);

    /// <summary>Orient geometry to a target point using mass properties centroid.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToMassPoint<T>(T geometry, Point3d target, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToTarget(geometry: geometry, target: new MassPointTarget(Target: target), context: context);

    /// <summary>Orient geometry by rotating from source to target vector around anchor point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(
        T geometry,
        Vector3d target,
        Vector3d? source,
        Point3d? anchor,
        IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToTarget(
            geometry: geometry,
            target: new VectorTarget(Target: target, Source: source ?? Vector3d.ZAxis, Anchor: anchor),
            context: context);

    /// <summary>Orient geometry to a curve frame at parameter t.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCurveFrame<T>(T geometry, Curve curve, double parameter, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToTarget(geometry: geometry, target: new CurveFrameTarget(Curve: curve, Parameter: parameter), context: context);

    /// <summary>Orient geometry to a surface frame at UV coordinates.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToSurfaceFrame<T>(T geometry, Surface surface, double u, double v, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToTarget(geometry: geometry, target: new SurfaceFrameTarget(Surface: surface, U: u, V: v), context: context);

    /// <summary>Orient geometry to best-fit plane (point cloud or mesh only).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToBestFit<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        OrientCore.OrientToBestFit(geometry: geometry, context: context);

    /// <summary>Mirror geometry across a plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(T geometry, Plane plane, IGeometryContext context) where T : GeometryBase =>
        OrientCore.Mirror(geometry: geometry, plane: plane, context: context);

    /// <summary>Flip direction of curves, Breps, or meshes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        OrientCore.FlipDirection(geometry: geometry, context: context);

    /// <summary>Optimize orientation based on algebraic criteria.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<OptimizationResult> OptimizeOrientation(
        Brep brep,
        OptimizationCriteria criteria,
        IGeometryContext context) =>
        OrientCore.OptimizeOrientation(brep: brep, criteria: criteria, context: context);

    /// <summary>Compute relative orientation between two geometries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<RelativeOrientationResult> ComputeRelativeOrientation(
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        OrientCore.ComputeRelative(geometryA: geometryA, geometryB: geometryB, context: context);

    /// <summary>Detect and align patterns in geometry arrays.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternResult> DetectAndAlign(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        OrientCore.DetectPattern(geometries: geometries, context: context);
}
