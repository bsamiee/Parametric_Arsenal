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
        double QualityScore,
        bool HadHoles,
        bool HadBadNormals) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshRepair | V: {this.OriginalVertexCount}→{this.RepairedVertexCount} | F: {this.OriginalFaceCount}→{this.RepairedFaceCount} | Ops={this.OperationsPerformed.Count} | Quality={this.QualityScore:F3}");
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

    /// <summary>Base request for all morphology operations.</summary>
    public abstract record MorphologyRequest {
        internal abstract GeometryBase Geometry { get; }
    }

    /// <summary>Base request for mesh-bound operations.</summary>
    public abstract record MeshRequest(Mesh Mesh) : MorphologyRequest {
        internal override GeometryBase Geometry => Mesh;
    }

    /// <summary>Base request for brep-bound operations.</summary>
    public abstract record BrepRequest(Brep Brep) : MorphologyRequest {
        internal override GeometryBase Geometry => Brep;
    }

    /// <summary>Cage deformation on meshes.</summary>
    public sealed record MeshCageDeformationRequest(
        Mesh Mesh,
        GeometryBase Cage,
        IReadOnlyList<Point3d> OriginalControlPoints,
        IReadOnlyList<Point3d> DeformedControlPoints) : MeshRequest(Mesh);

    /// <summary>Cage deformation on breps.</summary>
    public sealed record BrepCageDeformationRequest(
        Brep Brep,
        GeometryBase Cage,
        IReadOnlyList<Point3d> OriginalControlPoints,
        IReadOnlyList<Point3d> DeformedControlPoints) : BrepRequest(Brep);

    /// <summary>Mesh subdivision request with strategy selection.</summary>
    public sealed record SubdivisionRequest(
        Mesh Mesh,
        SubdivisionStrategy Strategy,
        int Levels) : MeshRequest(Mesh);

    /// <summary>Base type for subdivision strategies.</summary>
    public abstract record SubdivisionStrategy;

    /// <summary>Catmull-Clark subdivision.</summary>
    public sealed record CatmullClarkSubdivisionStrategy : SubdivisionStrategy;

    /// <summary>Loop subdivision.</summary>
    public sealed record LoopSubdivisionStrategy : SubdivisionStrategy;

    /// <summary>Butterfly subdivision.</summary>
    public sealed record ButterflySubdivisionStrategy : SubdivisionStrategy;

    /// <summary>Smoothing request with algorithm selection.</summary>
    public sealed record SmoothingRequest(
        Mesh Mesh,
        SmoothingStrategy Strategy) : MeshRequest(Mesh);

    /// <summary>Base smoothing strategy.</summary>
    public abstract record SmoothingStrategy(int Iterations);

    /// <summary>Laplacian smoothing strategy.</summary>
    public sealed record LaplacianSmoothingStrategy(int Iterations, bool LockBoundary) : SmoothingStrategy(Iterations);

    /// <summary>Taubin smoothing strategy.</summary>
    public sealed record TaubinSmoothingStrategy(int Iterations, double Lambda, double Mu) : SmoothingStrategy(Iterations);

    /// <summary>Mean curvature flow strategy.</summary>
    public sealed record MeanCurvatureFlowStrategy(int Iterations, double TimeStep) : SmoothingStrategy(Iterations);

    /// <summary>Mesh offset request.</summary>
    public sealed record MeshOffsetRequest(Mesh Mesh, double Distance, bool BothSides) : MeshRequest(Mesh);

    /// <summary>Mesh reduction request.</summary>
    public sealed record MeshReductionRequest(Mesh Mesh, int TargetFaceCount, bool PreserveBoundary, double Accuracy) : MeshRequest(Mesh);

    /// <summary>Mesh remeshing request.</summary>
    public sealed record MeshRemeshRequest(Mesh Mesh, double TargetEdgeLength, int MaxIterations, bool PreserveFeatures) : MeshRequest(Mesh);

    /// <summary>Brep to mesh conversion request.</summary>
    public sealed record BrepMeshingRequest(Brep Brep, MeshingParameters Parameters, bool JoinMeshes) : BrepRequest(Brep);

    /// <summary>Mesh repair request with explicit operations.</summary>
    public sealed record MeshRepairRequest(
        Mesh Mesh,
        double WeldTolerance,
        IReadOnlyList<MeshRepairOperation> Operations) : MeshRequest(Mesh);

    /// <summary>Base type for mesh repair operations.</summary>
    public abstract record MeshRepairOperation;

    /// <summary>Fill holes repair operation.</summary>
    public sealed record FillHolesRepair : MeshRepairOperation;

    /// <summary>Unify normals repair operation.</summary>
    public sealed record UnifyNormalsRepair : MeshRepairOperation;

    /// <summary>Cull degenerate faces repair operation.</summary>
    public sealed record CullDegenerateFacesRepair : MeshRepairOperation;

    /// <summary>Compact mesh repair operation.</summary>
    public sealed record CompactRepair : MeshRepairOperation;

    /// <summary>Weld vertices repair operation.</summary>
    public sealed record WeldVerticesRepair : MeshRepairOperation;

    /// <summary>Mesh thickening request.</summary>
    public sealed record MeshThickenRequest(Mesh Mesh, double Thickness, bool Solidify, Vector3d Direction) : MeshRequest(Mesh);

    /// <summary>Mesh unwrap request with explicit strategy.</summary>
    public sealed record MeshUnwrapRequest(Mesh Mesh, MeshUnwrapStrategy Strategy) : MeshRequest(Mesh);

    /// <summary>Base type for mesh unwrap strategies.</summary>
    public abstract record MeshUnwrapStrategy;

    /// <summary>Angle-based unwrap.</summary>
    public sealed record AngleBasedUnwrapStrategy : MeshUnwrapStrategy;

    /// <summary>Conformal energy minimization unwrap.</summary>
    public sealed record ConformalEnergyUnwrapStrategy : MeshUnwrapStrategy;

    /// <summary>Mesh separation request.</summary>
    public sealed record MeshSeparationRequest(Mesh Mesh) : MeshRequest(Mesh);

    /// <summary>Mesh welding request.</summary>
    public sealed record MeshWeldRequest(Mesh Mesh, double Tolerance, bool RecalculateNormals) : MeshRequest(Mesh);

    /// <summary>Unified morphology operation entry with algebraic configuration.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IMorphologyResult>> Apply(
        MorphologyRequest request,
        IGeometryContext context) =>
        request is null
            ? ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext("Request is null"))
            : MorphologyCore.Apply(request, context);
}
