using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Algebraic public API for geometry orientation, alignment, and diagnostics.</summary>
public static class Orient {
    /// <summary>Base request for alignment operations.</summary>
    public abstract record AlignmentRequest;

    public sealed record PlaneAlignment(Plane Target) : AlignmentRequest;

    public sealed record CurveFrameAlignment(Curve Curve, double Parameter) : AlignmentRequest;

    public sealed record SurfaceFrameAlignment(Surface Surface, double U, double V) : AlignmentRequest;

    public sealed record WorldXYAlignment() : AlignmentRequest;

    public sealed record WorldYZAlignment() : AlignmentRequest;

    public sealed record WorldXZAlignment() : AlignmentRequest;

    public sealed record BoundingBoxOriginAlignment() : AlignmentRequest;

    public sealed record VolumeOriginAlignment() : AlignmentRequest;

    public sealed record BoundingBoxPointAlignment(Point3d Target) : AlignmentRequest;

    public sealed record MassPointAlignment(Point3d Target) : AlignmentRequest;

    public sealed record VectorAlignment(Vector3d Source, Vector3d Target, AnchorSpecification Anchor) : AlignmentRequest;

    public sealed record BestFitAlignment() : AlignmentRequest;

    public sealed record MirrorAlignment(Plane Plane) : AlignmentRequest;

    public sealed record FlipDirectionAlignment() : AlignmentRequest;

    /// <summary>Anchor specification for vector alignment operations.</summary>
    public abstract record AnchorSpecification {
        private protected AnchorSpecification() { }

        public sealed record BoundingBoxAnchor : AnchorSpecification;

        public sealed record CustomAnchor(Point3d Anchor) : AnchorSpecification;
    }

    public sealed record OrientationOptimizationRequest(OptimizationCriterion Criterion);

    public abstract record OptimizationCriterion {
        private protected OptimizationCriterion() { }

        internal abstract byte Code { get; }
    }

    public sealed record CompactnessCriterion() : OptimizationCriterion {
        internal override byte Code => 1;
    }

    public sealed record CentroidCriterion() : OptimizationCriterion {
        internal override byte Code => 2;
    }

    public sealed record FlatnessCriterion() : OptimizationCriterion {
        internal override byte Code => 3;
    }

    public sealed record CanonicalCriterion() : OptimizationCriterion {
        internal override byte Code => 4;
    }

    public sealed record OrientationOptimizationResult(
        Transform OptimalTransform,
        double Score,
        IReadOnlyList<OptimizationCriterion> CriteriaMet);

    public sealed record RelativeOrientationRequest(GeometryBase Primary, GeometryBase Secondary);

    public abstract record SymmetryClassification {
        private protected SymmetryClassification() { }

        public sealed record None : SymmetryClassification;

        public sealed record Mirror : SymmetryClassification;

        public sealed record Rotational : SymmetryClassification;
    }

    public abstract record OrientationRelationship {
        private protected OrientationRelationship() { }

        public sealed record Aligned : OrientationRelationship;

        public sealed record Orthogonal : OrientationRelationship;

        public sealed record Skew : OrientationRelationship;
    }

    public sealed record RelativeOrientationResult(
        Transform RelativeTransform,
        double Twist,
        double Tilt,
        SymmetryClassification Symmetry,
        OrientationRelationship Relationship);

    public sealed record PatternDetectionRequest(GeometryBase[] Geometries);

    public abstract record PatternClassification {
        private protected PatternClassification() { }

        public sealed record Linear : PatternClassification;

        public sealed record Radial : PatternClassification;
    }

    public sealed record PatternDetectionResult(
        PatternClassification Classification,
        Transform[] IdealTransforms,
        int[] Anomalies,
        double Deviation);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, AlignmentRequest request, IGeometryContext context) where T : GeometryBase =>
        OrientCore.Align(geometry, request, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<OrientationOptimizationResult> Optimize(
        Brep brep,
        OrientationOptimizationRequest request,
        IGeometryContext context) =>
        OrientCore.Optimize(brep, request, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<RelativeOrientationResult> Relative(
        RelativeOrientationRequest request,
        IGeometryContext context) =>
        OrientCore.Relative(request, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternDetectionResult> Detect(
        PatternDetectionRequest request,
        IGeometryContext context) =>
        OrientCore.Detect(request, context);
}
