using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Mesh morphology operations: cage deformation, subdivision, smoothing, evolution.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Morphology is the primary API entry point for Arsenal.Rhino.Morphology namespace")]
public static class Morphology {
    /// <summary>Morphology result marker for polymorphic dispatch.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface for polymorphic result dispatch")]
    public interface IMorphologyResult;

    /// <summary>Cage deformation result with displacement and volume tracking.</summary>
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

    /// <summary>Subdivision result with quality metrics and face counts.</summary>
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

    /// <summary>Smoothing result with convergence tracking and quality metrics.</summary>
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

    /// <summary>Mesh offset result with distance metrics and degeneracy tracking.</summary>
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

    /// <summary>Mesh reduction result with quality metrics and reduction statistics.</summary>
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

    /// <summary>Isotropic remeshing result with edge uniformity and convergence metrics.</summary>
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

    /// <summary>Unified morphology operation entry with polymorphic dispatch via (operation ID, parameters tuple).</summary>
    /// <remarks>Operation IDs: 1=CageDeform (cage,original[],deformed[]), 2=CatmullClark (levels), 3=Loop (levels), 4=Butterfly (levels), 10=Laplacian (iters,lockBoundary), 11=Taubin (iters,λ,μ), 20=MeanCurvature (timeStep,iters).</remarks>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IMorphologyResult>> Apply<T>(
        T input,
        (byte Operation, object Parameters) spec,
        IGeometryContext context) where T : GeometryBase =>
        MorphologyCore.OperationDispatch.TryGetValue((spec.Operation, typeof(T)), out Func<object, object, IGeometryContext, Result<IReadOnlyList<IMorphologyResult>>>? executor) && executor is not null
            ? UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<IMorphologyResult>>>)(item => executor(item, spec.Parameters, context)),
                config: new OperationConfig<T, IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.ValidationModes.TryGetValue((spec.Operation, typeof(T)), out V mode) ? mode : V.Standard,
                    OperationName = string.Create(CultureInfo.InvariantCulture, $"Morphology.{(MorphologyConfig.OperationNames.TryGetValue(spec.Operation, out string? opName) ? opName ?? $"Op{spec.Operation}" : $"Op{spec.Operation}")}"),
                    EnableDiagnostics = false,
                })
            : ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(
                error: E.Geometry.Morphology.UnsupportedConfiguration.WithContext($"Operation: {spec.Operation}, Type: {typeof(T).Name}"));
}
