# Morphology.cs Blueprint - Public API Implementation

## File Purpose
Public API surface with single unified entry point. Contains nested result types for polymorphic return values. This is the ONLY file with namespace-class name match suppression.

## Full Implementation Code

```csharp
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Mesh morphology operations: cage deformation, subdivision, smoothing, evolution.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Morphology is the primary API entry point for Arsenal.Rhino.Morphology namespace")]
public static class Morphology {
    /// <summary>Morphology result marker for polymorphic dispatch.</summary>
    public interface IMorphologyResult;

    /// <summary>Cage deformation result with displacement metrics and boundary tracking.</summary>
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

    /// <summary>Subdivision result with quality metrics and face count changes.</summary>
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

    /// <summary>Smoothing result with convergence data and quality score.</summary>
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

    /// <summary>Unified morphology operation entry point with polymorphic parameter dispatch.</summary>
    /// <typeparam name="T">Geometry type (Mesh, Brep)</typeparam>
    /// <param name="input">Input geometry to morph</param>
    /// <param name="spec">Operation specification: (operation byte, parameters object)</param>
    /// <param name="context">Geometry context for tolerance and validation</param>
    /// <returns>Result containing list of morphology results</returns>
    /// <remarks>
    /// Operation IDs:
    /// 1 = CageDeform: params = (GeometryBase cage, Point3d[] original, Point3d[] deformed)
    /// 2 = SubdivideCatmullClark: params = int levels
    /// 3 = SubdivideLoop: params = int levels
    /// 4 = SubdivideButterfly: params = int levels
    /// 10 = SmoothLaplacian: params = (int iterations, bool lockBoundary)
    /// 11 = SmoothTaubin: params = (int iterations, double lambda, double mu)
    /// 20 = EvolveMeanCurvature: params = (double timeStep, int iterations)
    /// </remarks>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IMorphologyResult>> Apply<T>(
        T input,
        (byte Operation, object Parameters) spec,
        IGeometryContext context) where T : GeometryBase =>
        MorphologyCore.OperationDispatch.TryGetValue((spec.Operation, typeof(T)), out Func<object, object, IGeometryContext, Result<IReadOnlyList<IMorphologyResult>>> executor)
            ? UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<IMorphologyResult>>>)(item => executor(item, spec.Parameters, context)),
                config: new OperationConfig<T, IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.ValidationModes.TryGetValue((spec.Operation, typeof(T)), out V mode) ? mode : V.Standard,
                    OperationName = $"Morphology.{(MorphologyConfig.OperationNames.TryGetValue(spec.Operation, out string? name) ? name : $"Op{spec.Operation}")}",
                    EnableDiagnostics = false,
                })
            : ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(
                error: E.Morphology.UnsupportedConfiguration.WithContext($"Operation: {spec.Operation}, Type: {typeof(T).Name}"));
}
```

## Code Pattern Explanations

### Type Nesting
All result types are nested within the `Morphology` static class:
- `IMorphologyResult` interface
- `CageDeformResult` sealed record
- `SubdivisionResult` sealed record
- `SmoothingResult` sealed record

This matches the pattern from `Analysis.cs` where `CurveData`, `SurfaceData`, etc. are nested in the `Analysis` class.

### DebuggerDisplay
Each result record has a private `DebuggerDisplay` property using `string.Create` for zero-allocation string formatting with `CultureInfo.InvariantCulture`. This matches `Analysis.CurveData` pattern (line 34 in Analysis.cs).

### Single Entry Point
The `Apply<T>` method is the ONLY public API method, matching the `Spatial.Analyze<TInput, TQuery>` pattern. It:
1. Looks up executor in FrozenDictionary dispatch table
2. Wraps in UnifiedOperation.Apply for validation and error handling
3. Uses ternary operator for validation mode lookup with fallback
4. Inline ternary for operation name with fallback

### No Helper Methods
All logic is either inlined in Apply or delegated to MorphologyCore. No extracted helpers.

### Advanced C# Features
- Tuple parameter: `(byte Operation, object Parameters) spec`
- TryGetValue with out parameter in ternary
- Inline string interpolation in error context
- Type constraints: `where T : GeometryBase`
- MethodImpl inlining attribute
- Pure and Contract attributes

### RhinoMath Integration
- `RhinoMath.ToDegrees()` in DebuggerDisplay for angle conversion (no magic numbers)
- Matches pattern from AnalysisConfig.cs line 64 where constants use RhinoMath

## File Metrics
- **Types**: 5 (1 static class, 1 interface, 3 sealed records - all nested)
- **Public Methods**: 1 (Apply)
- **LOC**: ~130 lines (well under 300 LOC limit)
- **Suppressions**: 1 (MA0049 for namespace match - ONLY allowed suppression)

## Integration Points
- **MorphologyCore.OperationDispatch**: FrozenDictionary lookup for executors
- **MorphologyConfig.ValidationModes**: FrozenDictionary lookup for V.* modes
- **MorphologyConfig.OperationNames**: FrozenDictionary lookup for operation names
- **E.Morphology.UnsupportedConfiguration**: Error code 2812
