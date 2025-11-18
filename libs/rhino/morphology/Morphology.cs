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

    /// <summary>Base type for all morphology operation requests.</summary>
    public abstract record MorphologyRequest;

    /// <summary>Base type for subdivision operations.</summary>
    public abstract record SubdivisionRequest(int Levels) : MorphologyRequest;

    /// <summary>Catmull-Clark subdivision for quad and mixed meshes.</summary>
    public sealed record CatmullClarkSubdivision(int Levels) : SubdivisionRequest(Levels);

    /// <summary>Loop subdivision for triangulated meshes only.</summary>
    public sealed record LoopSubdivision(int Levels) : SubdivisionRequest(Levels);

    /// <summary>Butterfly subdivision for triangulated meshes only.</summary>
    public sealed record ButterflySubdivision(int Levels) : SubdivisionRequest(Levels);

    /// <summary>Base type for smoothing operations.</summary>
    public abstract record SmoothingRequest : MorphologyRequest;

    /// <summary>Laplacian smoothing with optional boundary locking.</summary>
    public sealed record LaplacianSmoothing(int Iterations, bool LockBoundary) : SmoothingRequest;

    /// <summary>Taubin smoothing with shrink-preventing lambda/mu parameters.</summary>
    public sealed record TaubinSmoothing(int Iterations, double Lambda, double Mu) : SmoothingRequest;

    /// <summary>Mean curvature flow evolution.</summary>
    public sealed record MeanCurvatureEvolution(double TimeStep, int Iterations) : SmoothingRequest;

    /// <summary>Cage deformation with control point mapping.</summary>
    public sealed record CageDeformation(GeometryBase Cage, Point3d[] OriginalControlPoints, Point3d[] DeformedControlPoints) : MorphologyRequest;

    /// <summary>Mesh offset along normals with optional both-sides mode.</summary>
    public sealed record MeshOffsetRequest(double Distance, bool BothSides) : MorphologyRequest;

    /// <summary>Mesh polygon reduction to target face count.</summary>
    public sealed record MeshReduction(int TargetFaceCount, bool PreserveBoundary, double Accuracy) : MorphologyRequest;

    /// <summary>Isotropic remeshing to uniform edge length.</summary>
    public sealed record IsotropicRemeshing(double TargetEdgeLength, int MaxIterations, bool PreserveFeatures) : MorphologyRequest;

    /// <summary>Brep to mesh conversion with meshing parameters.</summary>
    public sealed record BrepConversion(MeshingParameters Parameters, bool JoinMeshes) : MorphologyRequest;

    /// <summary>Mesh repair operations configuration.</summary>
    public sealed record MeshRepairOperations(
        bool FillHoles,
        bool UnifyNormals,
        bool CullDegenerateFaces,
        bool Compact,
        bool Weld) {
        /// <summary>No repair operations.</summary>
        public static MeshRepairOperations None => new(
            FillHoles: false,
            UnifyNormals: false,
            CullDegenerateFaces: false,
            Compact: false,
            Weld: false);

        /// <summary>All repair operations enabled.</summary>
        public static MeshRepairOperations All => new(
            FillHoles: true,
            UnifyNormals: true,
            CullDegenerateFaces: true,
            Compact: true,
            Weld: true);
    }

    /// <summary>Mesh repair with configurable operations.</summary>
    public sealed record MeshRepairRequest(MeshRepairOperations Operations, double WeldTolerance) : MorphologyRequest;

    /// <summary>Base type for UV unwrap methods.</summary>
    public abstract record UnwrapMethod;

    /// <summary>Angle-based UV unwrapping (ABF).</summary>
    public sealed record AngleBasedUnwrap : UnwrapMethod;

    /// <summary>Conformal energy minimization UV unwrapping (LSCM).</summary>
    public sealed record ConformalEnergyUnwrap : UnwrapMethod;

    /// <summary>Mesh UV unwrapping request.</summary>
    public sealed record MeshUnwrapping(UnwrapMethod Method) : MorphologyRequest;

    /// <summary>Mesh vertex welding within tolerance.</summary>
    public sealed record MeshWelding(double Tolerance, bool RecalculateNormals) : MorphologyRequest;

    /// <summary>Mesh thickening/shell creation.</summary>
    public sealed record MeshThickening(double Thickness, bool Solidify, Vector3d Direction) : MorphologyRequest;

    /// <summary>Mesh component separation.</summary>
    public sealed record MeshSeparation : MorphologyRequest;

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
        MeshRepairOperations OperationsPerformed,
        double QualityScore,
        bool HadHoles,
        bool HadBadNormals) : IMorphologyResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MeshRepair | V: {this.OriginalVertexCount}→{this.RepairedVertexCount} | F: {this.OriginalFaceCount}→{this.RepairedFaceCount} | Quality={this.QualityScore:F3}");
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

    /// <summary>Unified morphology operation entry with polymorphic dispatch on algebraic request types.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IMorphologyResult>> Apply<T>(
        T input,
        MorphologyRequest request,
        IGeometryContext context) where T : GeometryBase =>
        MorphologyCore.GetRequestHandler(request, typeof(T)) is not { } handler
            ? ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(
                error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Request: {request.GetType().Name}, Type: {typeof(T).Name}"))
            : UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<IMorphologyResult>>>)(item => handler(item, request, context)),
                config: new OperationConfig<T, IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.GetValidationMode(request, typeof(T)),
                    OperationName = string.Create(CultureInfo.InvariantCulture, $"Morphology.{MorphologyConfig.GetOperationName(request)}"),
                    EnableDiagnostics = false,
                });
}
