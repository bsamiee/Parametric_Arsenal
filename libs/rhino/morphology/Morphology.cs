using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Mesh morphology operations: cage deformation, subdivision, smoothing, evolution.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Morphology is the primary API entry point for Arsenal.Rhino.Morphology namespace")]
public static class Morphology {
    /// <summary>Base type for morphology operation requests.</summary>
    public abstract record MorphologyRequest;

    /// <summary>Base type for subdivision requests.</summary>
    public abstract record SubdivisionRequest(int Levels) : MorphologyRequest;

    /// <summary>Cage deformation specification.</summary>
    public sealed record CageDeformRequest(
        GeometryBase Cage,
        IReadOnlyList<Point3d> OriginalControlPoints,
        IReadOnlyList<Point3d> DeformedControlPoints) : MorphologyRequest;

    /// <summary>Catmull-Clark subdivision request.</summary>
    public sealed record CatmullClarkSubdivisionRequest(int Levels) : SubdivisionRequest(Levels);

    /// <summary>Loop subdivision request (requires triangulated input).</summary>
    public sealed record LoopSubdivisionRequest(int Levels) : SubdivisionRequest(Levels);

    /// <summary>Butterfly subdivision request (requires triangulated input).</summary>
    public sealed record ButterflySubdivisionRequest(int Levels) : SubdivisionRequest(Levels);

    /// <summary>Laplacian smoothing request.</summary>
    public sealed record LaplacianSmoothingRequest(int Iterations, bool LockBoundary) : MorphologyRequest;

    /// <summary>Taubin smoothing request.</summary>
    public sealed record TaubinSmoothingRequest(int Iterations, double Lambda, double Mu) : MorphologyRequest;

    /// <summary>Mean curvature flow evolution request.</summary>
    public sealed record MeanCurvatureFlowRequest(double TimeStep, int Iterations) : MorphologyRequest;

    /// <summary>Mesh offset request.</summary>
    public sealed record MeshOffsetRequest(double Distance, bool BothSides) : MorphologyRequest;

    /// <summary>Mesh reduction request.</summary>
    public sealed record MeshReductionRequest(int TargetFaceCount, bool PreserveBoundary, double Accuracy) : MorphologyRequest;

    /// <summary>Isotropic remeshing request.</summary>
    public sealed record RemeshRequest(double TargetEdgeLength, int MaxIterations, bool PreserveFeatures) : MorphologyRequest;

    /// <summary>Brep to mesh conversion request.</summary>
    public sealed record BrepToMeshRequest(MeshingParameters? Parameters, bool JoinMeshes) : MorphologyRequest;

    /// <summary>Mesh repair request with explicit operations.</summary>
    public sealed record MeshRepairRequest(IReadOnlyList<MeshRepairOperation> Operations, double WeldTolerance) : MorphologyRequest;

    /// <summary>Mesh thickening request.</summary>
    public sealed record MeshThickenRequest(double Thickness, bool Solidify, Vector3d Direction) : MorphologyRequest;

    /// <summary>Mesh unwrap request.</summary>
    public sealed record MeshUnwrapRequest(MeshUnwrapMode Mode) : MorphologyRequest;

    /// <summary>Mesh separation request.</summary>
    public sealed record MeshSeparationRequest() : MorphologyRequest;

    /// <summary>Mesh welding request.</summary>
    public sealed record MeshWeldRequest(double Tolerance, bool RecalculateNormals) : MorphologyRequest;

    /// <summary>Mesh repair operations.</summary>
    public abstract record MeshRepairOperation {
        public abstract string Name { get; }
    }

    public sealed record FillHolesRepairOperation() : MeshRepairOperation {
        public override string Name => "FillHoles";
    }

    public sealed record UnifyNormalsRepairOperation() : MeshRepairOperation {
        public override string Name => "UnifyNormals";
    }

    public sealed record CullDegenerateFacesRepairOperation() : MeshRepairOperation {
        public override string Name => "CullDegenerateFaces";
    }

    public sealed record CompactRepairOperation() : MeshRepairOperation {
        public override string Name => "Compact";
    }

    public sealed record WeldRepairOperation() : MeshRepairOperation {
        public override string Name => "Weld";
    }

    /// <summary>Mesh unwrap mode abstraction.</summary>
    public abstract record MeshUnwrapMode(MeshUnwrapMethod Method) {
        public abstract string Name { get; }
    }

    public sealed record AngleBasedMeshUnwrapMode() : MeshUnwrapMode(MeshUnwrapMethod.AngleBased) {
        public override string Name => "AngleBased";
    }

    public sealed record ConformalEnergyMeshUnwrapMode() : MeshUnwrapMode(MeshUnwrapMethod.Conformal) {
        public override string Name => "Conformal";
    }

    /// <summary>Marker interface for polymorphic morphology result dispatch.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface")]
    public interface IMorphologyResult;

    /// <summary>Cage deformation result with displacement and volume metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record CageDeformResult(
        GeometryBase Deformed,
        double MaxDisplacement,
        double MeanDisplacement,
        BoundingBox OriginalBounds,
        BoundingBox DeformedBounds,
        double VolumeRatio) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"CageDeform | MaxDisp={this.MaxDisplacement:F3} | VolumeΔ={this.VolumeRatio:F2}x");
    }

    /// <summary>Smoothing result with convergence and displacement metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record SmoothingResult(
        Mesh Smoothed,
        int IterationsPerformed,
        double RMSDisplacement,
        double MaxVertexDisplacement,
        double QualityScore,
        bool Converged) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"Smoothing | Iterations={this.IterationsPerformed} | RMS={this.RMSDisplacement:E2} | Quality={this.QualityScore:F3} | {(this.Converged ? "✓" : "diverged")}");
    }

    /// <summary>Mesh offset result with distance and degeneracy metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record OffsetResult(
        Mesh Offset,
        double ActualDistance,
        bool HasDegeneracies,
        int OriginalVertexCount,
        int OffsetVertexCount,
        int OriginalFaceCount,
        int OffsetFaceCount) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshOffset | Dist={this.ActualDistance:F3} | V: {this.OriginalVertexCount}→{this.OffsetVertexCount} | F: {this.OriginalFaceCount}→{this.OffsetFaceCount}{(this.HasDegeneracies ? " [degenerate]" : "")}");
    }

    /// <summary>Subdivision result with edge and triangle quality metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record SubdivisionResult(
        Mesh Subdivided,
        int OriginalFaceCount,
        int SubdividedFaceCount,
        double MinEdgeLength,
        double MaxEdgeLength,
        double MeanEdgeLength,
        double MeanAspectRatio,
        double MinTriangleAngleRadians) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"Subdivision | Faces: {this.OriginalFaceCount}→{this.SubdividedFaceCount} | AspectRatio={this.MeanAspectRatio:F2} | MinAngle={RhinoMath.ToDegrees(this.MinTriangleAngleRadians):F1}°");
    }

    /// <summary>Mesh reduction result with ratio and quality metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ReductionResult(
        Mesh Reduced,
        int OriginalFaceCount,
        int ReducedFaceCount,
        double ReductionRatio,
        double QualityScore,
        double MeanAspectRatio,
        double MinEdgeLength,
        double MaxEdgeLength) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshReduce | Faces: {this.OriginalFaceCount}→{this.ReducedFaceCount} ({this.ReductionRatio * 100.0:F1}%) | Quality={this.QualityScore:F3} | AspectRatio={this.MeanAspectRatio:F2}");
    }

    /// <summary>Isotropic remeshing result with uniformity and convergence metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record RemeshResult(
        Mesh Remeshed,
        double TargetEdgeLength,
        double MeanEdgeLength,
        double EdgeLengthStdDev,
        double UniformityScore,
        int IterationsPerformed,
        bool Converged,
        int OriginalFaceCount,
        int RemeshedFaceCount) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"Remesh | Target={this.TargetEdgeLength:F3} | Mean={this.MeanEdgeLength:F3} | StdDev={this.EdgeLengthStdDev:F3} | Uniformity={this.UniformityScore:F3} | Iter={this.IterationsPerformed} | {(this.Converged ? "✓" : "diverged")}");
    }

    /// <summary>Mesh repair result with before/after metrics and quality score.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MeshRepairResult(
        Mesh Repaired,
        int OriginalVertexCount,
        int RepairedVertexCount,
        int OriginalFaceCount,
        int RepairedFaceCount,
        IReadOnlyList<MeshRepairOperation> OperationsPerformed,
        double WeldTolerance,
        double QualityScore,
        bool HadHoles,
        bool HadBadNormals) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshRepair | V: {this.OriginalVertexCount}→{this.RepairedVertexCount} | F: {this.OriginalFaceCount}→{this.RepairedFaceCount} | Ops={string.Join('|', this.OperationsPerformed.Select(operation => operation.Name))} | Tol={this.WeldTolerance:E2} | Quality={this.QualityScore:F3}");
    }

    /// <summary>Mesh separation result with per-component statistics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MeshSeparationResult(
        Mesh[] Components,
        int ComponentCount,
        int TotalVertexCount,
        int TotalFaceCount,
        int[] VertexCountPerComponent,
        int[] FaceCountPerComponent,
        BoundingBox[] BoundsPerComponent) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshSeparate | Components={this.ComponentCount} | TotalV={this.TotalVertexCount} | TotalF={this.TotalFaceCount}");
    }

    /// <summary>Mesh welding result with vertex reduction and displacement metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MeshWeldResult(
        Mesh Welded,
        int OriginalVertexCount,
        int WeldedVertexCount,
        int VerticesRemoved,
        double WeldTolerance,
        double MeanVertexDisplacement,
        double MaxVertexDisplacement,
        bool NormalsRecalculated) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshWeld | V: {this.OriginalVertexCount}→{this.WeldedVertexCount} (-{this.VerticesRemoved}) | Tol={this.WeldTolerance:E2} | MaxDisp={this.MaxVertexDisplacement:F3}");
    }

    /// <summary>Brep to mesh conversion result with quality metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BrepToMeshResult(
        Mesh Mesh,
        int BrepFaceCount,
        int MeshFaceCount,
        double MinEdgeLength,
        double MaxEdgeLength,
        double MeanEdgeLength,
        double EdgeLengthStdDev,
        double MeanAspectRatio,
        double MaxAspectRatio,
        double MinTriangleAngleRadians,
        double MeanTriangleAngleRadians,
        int DegenerateFaceCount,
        double QualityScore) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"BrepToMesh | BrepFaces={this.BrepFaceCount} | MeshFaces={this.MeshFaceCount} | MeanEdge={this.MeanEdgeLength:F3} | AspectRatio={this.MeanAspectRatio:F2} | Quality={this.QualityScore:F3}");
    }

    /// <summary>Mesh thickening result with solid shell metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MeshThickenResult(
        Mesh Thickened,
        double OffsetDistance,
        bool IsSolid,
        int OriginalVertexCount,
        int ThickenedVertexCount,
        int OriginalFaceCount,
        int ThickenedFaceCount,
        int WallFaceCount,
        BoundingBox OriginalBounds,
        BoundingBox ThickenedBounds) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshThicken | Dist={this.OffsetDistance:F3} | Solid={this.IsSolid} | V: {this.OriginalVertexCount}→{this.ThickenedVertexCount} | F: {this.OriginalFaceCount}→{this.ThickenedFaceCount} | WallFaces={this.WallFaceCount}");
    }

    /// <summary>Mesh UV unwrapping result with texture coordinate metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MeshUnwrapResult(
        Mesh Unwrapped,
        bool HasTextureCoordinates,
        int OriginalFaceCount,
        int TextureCoordinateCount,
        double MinU,
        double MaxU,
        double MinV,
        double MaxV,
        double UVCoverage) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshUnwrap | UV={this.HasTextureCoordinates} | F={this.OriginalFaceCount} | TC={this.TextureCoordinateCount} | U:[{this.MinU:F3}, {this.MaxU:F3}] | V:[{this.MinV:F3}, {this.MaxV:F3}] | Coverage={this.UVCoverage:P1}");
    }

    /// <summary>Unified morphology operation entry with polymorphic dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IMorphologyResult>> Apply<T>(
        T input,
        MorphologyRequest request,
        IGeometryContext context) where T : GeometryBase =>
        request is null
            ? ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext("Request cannot be null"))
            : MorphologyCore.Apply(input, request, context);
}
