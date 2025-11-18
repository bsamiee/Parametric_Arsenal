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

    /// <summary>Abstract base for all morphology operation requests. Use sealed derived types to specify operation parameters.</summary>
    public abstract record MorphologyRequest;

    /// <summary>Cage deformation request with control point arrays.</summary>
    public sealed record CageDeformRequest(
        GeometryBase Cage,
        Point3d[] OriginalControlPoints,
        Point3d[] DeformedControlPoints) : MorphologyRequest;

    /// <summary>Catmull-Clark subdivision request with iteration count.</summary>
    public sealed record SubdivideCatmullClarkRequest(int Levels) : MorphologyRequest;

    /// <summary>Loop subdivision request with iteration count (requires triangulated mesh).</summary>
    public sealed record SubdivideLoopRequest(int Levels) : MorphologyRequest;

    /// <summary>Butterfly subdivision request with iteration count (requires triangulated mesh).</summary>
    public sealed record SubdiveButterflyRequest(int Levels) : MorphologyRequest;

    /// <summary>Laplacian smoothing request with iteration and boundary lock parameters.</summary>
    public sealed record SmoothLaplacianRequest(int Iterations, bool LockBoundary) : MorphologyRequest;

    /// <summary>Taubin smoothing request with lambda/mu parameters for pass-band filtering.</summary>
    public sealed record SmoothTaubinRequest(int Iterations, double Lambda, double Mu) : MorphologyRequest;

    /// <summary>Mean curvature flow evolution request with time step and iterations.</summary>
    public sealed record EvolveMeanCurvatureRequest(double TimeStep, int Iterations) : MorphologyRequest;

    /// <summary>Mesh offset request with distance and two-sided flag.</summary>
    public sealed record OffsetRequest(double Distance, bool BothSides) : MorphologyRequest;

    /// <summary>Mesh reduction request with target face count and accuracy parameters.</summary>
    public sealed record ReduceRequest(int TargetFaces, bool PreserveBoundary, double Accuracy) : MorphologyRequest;

    /// <summary>Isotropic remeshing request with target edge length and iteration limit.</summary>
    public sealed record RemeshRequest(double TargetEdgeLength, int MaxIterations, bool PreserveFeatures) : MorphologyRequest;

    /// <summary>Brep to mesh conversion request with meshing parameters.</summary>
    public sealed record BrepToMeshRequest(MeshingParameters? Parameters, bool JoinMeshes) : MorphologyRequest;

    /// <summary>Abstract base for mesh repair operations.</summary>
    public abstract record MeshRepairOperation;

    /// <summary>Fill holes mesh repair operation.</summary>
    public sealed record FillHolesRepairOperation : MeshRepairOperation;

    /// <summary>Unify normals mesh repair operation.</summary>
    public sealed record UnifyNormalsRepairOperation : MeshRepairOperation;

    /// <summary>Cull degenerate faces mesh repair operation.</summary>
    public sealed record CullDegenerateFacesRepairOperation : MeshRepairOperation;

    /// <summary>Compact mesh repair operation.</summary>
    public sealed record CompactRepairOperation : MeshRepairOperation;

    /// <summary>Weld vertices mesh repair operation.</summary>
    public sealed record WeldVerticesRepairOperation : MeshRepairOperation;

    /// <summary>Mesh repair request with operation list and weld tolerance.</summary>
    public sealed record MeshRepairRequest(IReadOnlyList<MeshRepairOperation> Operations, double WeldTolerance) : MorphologyRequest;

    /// <summary>Mesh thickening request with offset distance and solidification option.</summary>
    public sealed record MeshThickenRequest(double Thickness, bool Solidify, Vector3d Direction) : MorphologyRequest;

    /// <summary>Abstract base for mesh unwrap strategies.</summary>
    public abstract record MeshUnwrapStrategy;

    /// <summary>Angle-based mesh unwrapping strategy.</summary>
    public sealed record AngleBasedUnwrapStrategy : MeshUnwrapStrategy;

    /// <summary>Conformal energy minimization mesh unwrapping strategy.</summary>
    public sealed record ConformalEnergyMinimizationUnwrapStrategy : MeshUnwrapStrategy;

    /// <summary>Mesh UV unwrapping request with strategy selection.</summary>
    public sealed record MeshUnwrapRequest(MeshUnwrapStrategy Strategy) : MorphologyRequest;

    /// <summary>Mesh component separation request (no parameters).</summary>
    public sealed record MeshSeparateRequest : MorphologyRequest;

    /// <summary>Mesh vertex welding request with tolerance and normal handling.</summary>
    public sealed record MeshWeldRequest(double Tolerance, bool WeldNormals) : MorphologyRequest;

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

    /// <summary>Unified morphology operation entry with polymorphic dispatch. CRITICAL: Validates the GEOMETRY, not the request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IMorphologyResult>> Apply<T>(
        T geometry,
        MorphologyRequest request,
        IGeometryContext context) where T : GeometryBase =>
        MorphologyCore.ExecuteRequest(
            geometry: geometry,
            request: request,
            context: context);
}
