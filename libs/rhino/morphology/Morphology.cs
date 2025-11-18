using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
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
    /// <summary>Abstract base request for morphology operations.</summary>
    public abstract record MorphologyRequest;

    /// <summary>Mesh cage deformation request.</summary>
    public sealed record MeshCageDeformRequest(
        Mesh Mesh,
        GeometryBase Cage,
        Point3d[] OriginalControlPoints,
        Point3d[] DeformedControlPoints) : MorphologyRequest;

    /// <summary>Brep cage deformation request.</summary>
    public sealed record BrepCageDeformRequest(
        Brep Brep,
        GeometryBase Cage,
        Point3d[] OriginalControlPoints,
        Point3d[] DeformedControlPoints) : MorphologyRequest;

    /// <summary>Subdivision request base.</summary>
    public abstract record SubdivisionRequest(Mesh Mesh, int Levels) : MorphologyRequest;

    /// <summary>Catmull-Clark subdivision request.</summary>
    public sealed record CatmullClarkSubdivisionRequest(Mesh Mesh, int Levels) : SubdivisionRequest(Mesh, Levels);

    /// <summary>Loop subdivision request.</summary>
    public sealed record LoopSubdivisionRequest(Mesh Mesh, int Levels) : SubdivisionRequest(Mesh, Levels);

    /// <summary>Butterfly subdivision request.</summary>
    public sealed record ButterflySubdivisionRequest(Mesh Mesh, int Levels) : SubdivisionRequest(Mesh, Levels);

    /// <summary>Smoothing request base.</summary>
    public abstract record MeshSmoothingRequest(Mesh Mesh) : MorphologyRequest;

    /// <summary>Laplacian smoothing request.</summary>
    public sealed record LaplacianSmoothRequest(Mesh Mesh, int Iterations, bool LockBoundary) : MeshSmoothingRequest(Mesh);

    /// <summary>Taubin smoothing request.</summary>
    public sealed record TaubinSmoothRequest(Mesh Mesh, int Iterations, double Lambda, double Mu) : MeshSmoothingRequest(Mesh);

    /// <summary>Mean curvature flow request.</summary>
    public sealed record MeanCurvatureFlowRequest(Mesh Mesh, double TimeStep, int Iterations) : MeshSmoothingRequest(Mesh);

    /// <summary>Mesh offset request.</summary>
    public sealed record MeshOffsetRequest(Mesh Mesh, double Distance, bool BothSides) : MorphologyRequest;

    /// <summary>Mesh reduction request.</summary>
    public sealed record MeshReductionRequest(Mesh Mesh, int TargetFaceCount, bool PreserveBoundary, double Accuracy) : MorphologyRequest;

    /// <summary>Isotropic remeshing request.</summary>
    public sealed record RemeshIsotropicRequest(Mesh Mesh, double TargetEdgeLength, int MaxIterations, bool PreserveFeatures) : MorphologyRequest;

    /// <summary>Brep meshing request.</summary>
    public sealed record BrepMeshingRequest(Brep Brep, MeshingParameters Parameters, bool JoinMeshes) : MorphologyRequest;

    /// <summary>Mesh repair operation base.</summary>
    public abstract record MeshRepairOperation;

    /// <summary>Fill holes repair operation.</summary>
    public sealed record FillHolesOperation : MeshRepairOperation;

    /// <summary>Unify normals repair operation.</summary>
    public sealed record UnifyNormalsOperation : MeshRepairOperation;

    /// <summary>Cull degenerate faces repair operation.</summary>
    public sealed record CullDegenerateFacesOperation : MeshRepairOperation;

    /// <summary>Compact mesh repair operation.</summary>
    public sealed record CompactOperation : MeshRepairOperation;

    /// <summary>Weld vertices repair operation.</summary>
    public sealed record WeldVerticesOperation : MeshRepairOperation;

    /// <summary>Mesh repair request.</summary>
    public sealed record MeshRepairRequest(Mesh Mesh, IReadOnlyList<MeshRepairOperation> Operations, double WeldTolerance) : MorphologyRequest;

    /// <summary>Mesh separation request.</summary>
    public sealed record MeshSeparationRequest(Mesh Mesh) : MorphologyRequest;

    /// <summary>Mesh thickening request.</summary>
    public sealed record MeshThickenRequest(Mesh Mesh, double Thickness, bool Solidify, Vector3d Direction) : MorphologyRequest;

    /// <summary>Mesh unwrap request.</summary>
    public sealed record MeshUnwrapRequest(Mesh Mesh, MeshUnwrapMethod Method) : MorphologyRequest;

    /// <summary>Mesh weld request.</summary>
    public sealed record MeshWeldRequest(Mesh Mesh, double Tolerance, bool RecomputeNormals) : MorphologyRequest;

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
        IReadOnlyList<string> OperationsPerformed,
        double QualityScore,
        bool HadHoles,
        bool HadBadNormals) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshRepair | V: {this.OriginalVertexCount}→{this.RepairedVertexCount} | F: {this.OriginalFaceCount}→{this.RepairedFaceCount} | Ops={string.Join(",", this.OperationsPerformed)} | Quality={this.QualityScore:F3}");
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
    public static Result<IReadOnlyList<IMorphologyResult>> Apply(
        MorphologyRequest request,
        IGeometryContext context) =>
        request is null
            ? ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(error: E.Geometry.InsufficientParameters.WithContext("Request cannot be null"))
            : MorphologyCore.Apply(request, context);
}
