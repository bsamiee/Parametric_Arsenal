using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation and canonical alignment operations.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0104:Type name should not collide", Justification = "Different namespace")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match namespace", Justification = "Intentional API design")]
public static class Orientation {
    public abstract record CentroidMode;
    public sealed record BoundingBoxCentroid : CentroidMode;
    public sealed record MassCentroid : CentroidMode;
    public abstract record CanonicalMode;
    public sealed record WorldXY : CanonicalMode;
    public sealed record WorldYZ : CanonicalMode;
    public sealed record WorldXZ : CanonicalMode;
    public sealed record AreaCentroid : CanonicalMode;
    public sealed record VolumeCentroid : CanonicalMode;
    public abstract record SymmetryType;
    public sealed record NoSymmetry : SymmetryType;
    public sealed record MirrorSymmetry : SymmetryType;
    public sealed record RotationalSymmetry : SymmetryType;
    public abstract record RelationshipType;
    public sealed record Parallel : RelationshipType;
    public sealed record Perpendicular : RelationshipType;
    public sealed record Oblique : RelationshipType;
    public abstract record PatternType;
    public sealed record LinearPattern : PatternType;
    public sealed record RadialPattern : PatternType;
    public sealed record NoPattern : PatternType;
    public abstract record OptimizationCriteria;
    public sealed record CompactCriteria : OptimizationCriteria;
    public sealed record CenteredCriteria : OptimizationCriteria;
    public sealed record FlatnessCriteria : OptimizationCriteria;
    public sealed record CanonicalCriteria : OptimizationCriteria;
    public abstract record Operation;
    public sealed record ToPlane(Plane Target) : Operation;
    public sealed record ToBestFit : Operation;
    public sealed record Mirror(Plane MirrorPlane) : Operation;
    public sealed record FlipDirection : Operation;
    public sealed record ToCanonical(CanonicalMode Mode) : Operation;
    public sealed record ToPoint(Point3d Target, CentroidMode CentroidType) : Operation;
    public sealed record ToCurveFrame(Curve Curve, double Parameter) : Operation;
    public sealed record ToSurfaceFrame(Surface Surface, double U, double V) : Operation;
    public sealed record ToVector(Vector3d Target, Vector3d? Source = null, Point3d? Anchor = null) : Operation;

    [DebuggerDisplay("Score={Score:F3}, Satisfied={CriteriaSatisfied.Length}")]
    public sealed record OptimizationResult(
        Transform OptimalTransform,
        double Score,
        OptimizationCriteria[] CriteriaSatisfied);

    [DebuggerDisplay("Pattern={Pattern}, Deviation={Deviation:F4}")]
    public sealed record PatternDetectionResult(
        PatternType Pattern,
        Transform[] IdealTransforms,
        int[] Anomalies,
        double Deviation);

    [DebuggerDisplay("Twist={TwistDegrees:F3}°, Tilt={TiltDegrees:F3}°")]
    public sealed record RelativeOrientationResult(
        Transform RelativeTransform,
        double Twist,
        double Tilt,
        SymmetryType Symmetry,
        RelationshipType Relationship) {
        [Pure]
        public double TwistDegrees => RhinoMath.ToDegrees(this.Twist);
        [Pure]
        public double TiltDegrees => RhinoMath.ToDegrees(this.Tilt);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Execute<T>(T geometry, Operation operation, IGeometryContext context) where T : GeometryBase =>
        OrientationCore.Execute(geometry: geometry, operation: operation, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<OptimizationResult> OptimizeOrientation(Brep brep, OptimizationCriteria criteria, IGeometryContext context) =>
        OrientationCore.ExecuteOptimization(brep: brep, criteria: criteria, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<RelativeOrientationResult> ComputeRelativeOrientation(GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context) =>
        OrientationCore.ExecuteRelative(geometryA: geometryA, geometryB: geometryB, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternDetectionResult> DetectAndAlign(GeometryBase[] geometries, IGeometryContext context) =>
        OrientationCore.ExecutePatternDetection(geometries: geometries, context: context);
}
