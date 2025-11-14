# Morph.cs Implementation Blueprint

## File Purpose
Public API surface for morphology operations with byte-based semantic type definitions following Extract.cs pattern exactly.

## Complete Implementation

```csharp
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Polymorphic mesh and surface morphology operations.</summary>
public static class Morph {
    /// <summary>Deformation operation discriminator for FFD/smoothing/subdivision dispatch.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct DeformationMode(byte kind) {
        internal readonly byte Kind = kind;

        /// <summary>Free-form deformation via trivariate Bernstein basis control cage.</summary>
        public static readonly DeformationMode FFD = new(1);
        /// <summary>Laplacian mesh smoothing with cotangent/uniform/mean-value weights.</summary>
        public static readonly DeformationMode Smooth = new(2);
        /// <summary>Recursive subdivision surface refinement (Catmull-Clark primary).</summary>
        public static readonly DeformationMode Subdivide = new(3);
    }

    /// <summary>Laplacian weight scheme discriminator for smoothing operations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct SmoothingWeight(byte kind) {
        internal readonly byte Kind = kind;

        /// <summary>Uniform weights: w_ij = 1/degree for all neighbors.</summary>
        public static readonly SmoothingWeight Uniform = new(0);
        /// <summary>Cotangent weights: w_ij = (cot(α) + cot(β))/2 for angle-based geometry.</summary>
        public static readonly SmoothingWeight Cotangent = new(1);
        /// <summary>Mean-value weights: w_ij = (tan(α/2) + tan(β/2)) for harmonic coordinates.</summary>
        public static readonly SmoothingWeight MeanValue = new(2);
    }

    /// <summary>Surface evolution PDE discriminator for curvature-driven flows.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct EvolutionFlow(byte kind) {
        internal readonly byte Kind = kind;

        /// <summary>Mean curvature flow: ∂x/∂t = H*n where H = (κ1+κ2)/2 minimizes surface area.</summary>
        public static readonly EvolutionFlow MeanCurvature = new(0);
        /// <summary>Geodesic active contour: edge-driven evolution with image gradient stopping term.</summary>
        public static readonly EvolutionFlow GeodesicActive = new(1);
        /// <summary>Willmore flow: ∂x/∂t = -ΔH*n - 2H(H²-K)*n minimizes bending energy.</summary>
        public static readonly EvolutionFlow Willmore = new(2);
    }

    /// <summary>Normalized deformation request computed from heterogeneous specifications.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    internal readonly struct Request {
        internal readonly byte Kind;
        internal readonly object? Parameter;
        internal readonly V ValidationMode;

        internal Request(byte kind, object? parameter, V validationMode) {
            this.Kind = kind;
            this.Parameter = parameter;
            this.ValidationMode = validationMode;
        }
    }

    /// <summary>Unified deformation entry point with byte-based mode dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Mesh>> Deform<T>(
        T geometry,
        DeformationMode mode,
        object specification,
        IGeometryContext context) where T : GeometryBase {
        Type geometryType = geometry.GetType();

        Result<Request> requestResult = (mode.Kind, specification) switch {
            (1, (Point3d[] controlPoints, int[] dimensions, Transform transform, int[] fixedIndices, Point3d[] targets, double[] weights))
                when controlPoints.Length >= MorphConfig.FFDMinControlPoints && dimensions.Length is 3 =>
                ResultFactory.Create(value: new Request(
                    kind: 1,
                    parameter: (controlPoints, dimensions, transform, fixedIndices, targets, weights),
                    validationMode: MorphConfig.GetValidationMode(1, geometryType))),
            (1, (Point3d[] controlPoints, int[] dimensions, Transform transform, int[] fixedIndices, Point3d[] targets, double[] weights))
                when controlPoints.Length < MorphConfig.FFDMinControlPoints =>
                ResultFactory.Create<Request>(
                    error: E.Geometry.FFDInsufficientControlPoints.WithContext(
                        $"Need ≥{MorphConfig.FFDMinControlPoints.ToString(System.Globalization.CultureInfo.InvariantCulture)} control points, got {controlPoints.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (1, (Point3d[] controlPoints, int[] dimensions, Transform transform, int[] fixedIndices, Point3d[] targets, double[] weights))
                when dimensions.Length != 3 =>
                ResultFactory.Create<Request>(
                    error: E.Geometry.FFDInvalidParameters.WithContext("Dimensions array must have length 3 (nx, ny, nz)")),
            (2, (byte weightKind, int iterations, double lambda))
                when iterations > 0 && iterations <= MorphConfig.LaplacianMaxIterations
                    && lambda > 0.0 && lambda < 1.0 =>
                ResultFactory.Create(value: new Request(
                    kind: 2,
                    parameter: (weightKind, iterations, lambda),
                    validationMode: MorphConfig.GetValidationMode(2, geometryType))),
            (2, (byte weightKind, int iterations, double lambda))
                when iterations <= 0 || iterations > MorphConfig.LaplacianMaxIterations =>
                ResultFactory.Create<Request>(
                    error: E.Geometry.LaplacianInvalidParameters.WithContext(
                        $"Iterations must be in range [1, {MorphConfig.LaplacianMaxIterations.ToString(System.Globalization.CultureInfo.InvariantCulture)}], got {iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (2, (byte weightKind, int iterations, double lambda))
                when lambda <= 0.0 || lambda >= 1.0 =>
                ResultFactory.Create<Request>(
                    error: E.Geometry.LaplacianInvalidParameters.WithContext(
                        $"Lambda must be in range (0, 1), got {lambda.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (3, (byte schemeKind, int levels))
                when levels > 0 && levels <= MorphConfig.MaxSubdivisionLevels =>
                ResultFactory.Create(value: new Request(
                    kind: 3,
                    parameter: (schemeKind, levels),
                    validationMode: MorphConfig.GetValidationMode(3, geometryType))),
            (3, (byte schemeKind, int levels))
                when levels <= 0 || levels > MorphConfig.MaxSubdivisionLevels =>
                ResultFactory.Create<Request>(
                    error: E.Geometry.SubdivisionInvalidLevels.WithContext(
                        $"Levels must be in range [1, {MorphConfig.MaxSubdivisionLevels.ToString(System.Globalization.CultureInfo.InvariantCulture)}], got {levels.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            _ => ResultFactory.Create<Request>(
                error: E.Geometry.MorphInvalidSpecification.WithContext(
                    $"Mode kind {mode.Kind.ToString(System.Globalization.CultureInfo.InvariantCulture)} specification does not match expected pattern")),
        };

        return requestResult.Bind(request =>
            UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<Mesh>>>)(item =>
                    MorphCore.Execute(geometry: item, request: request, context: context)),
                config: new OperationConfig<T, Mesh> {
                    Context = context,
                    ValidationMode = request.ValidationMode,
                    OperationName = $"Morph.Deform.{mode.Kind.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    EnableDiagnostics = false,
                }));
    }

    /// <summary>Surface evolution via PDE integration with CFL stability bounds.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Surface>> Evolve(
        Surface surface,
        EvolutionFlow flow,
        (double StepSize, int MaxSteps) parameters,
        IGeometryContext context) {
        Result<(double StepSize, int MaxSteps)> parametersResult = parameters switch {
            (double stepSize, _) when stepSize <= context.AbsoluteTolerance || stepSize < MorphConfig.EvolutionMinStepSize =>
                ResultFactory.Create<(double, int)>(
                    error: E.Geometry.EvolutionInvalidStepSize.WithContext(
                        $"StepSize must exceed {MorphConfig.EvolutionMinStepSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}, got {parameters.StepSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            (_, int maxSteps) when maxSteps <= 0 || maxSteps > MorphConfig.EvolutionMaxSteps =>
                ResultFactory.Create<(double, int)>(
                    error: E.Geometry.EvolutionInvalidStepSize.WithContext(
                        $"MaxSteps must be in range [1, {MorphConfig.EvolutionMaxSteps.ToString(System.Globalization.CultureInfo.InvariantCulture)}], got {parameters.MaxSteps.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            _ => ResultFactory.Create(value: parameters),
        };

        return parametersResult.Bind(validParams =>
            UnifiedOperation.Apply(
                input: surface,
                operation: (Func<Surface, Result<IReadOnlyList<Surface>>>)(item =>
                    MorphCore.ExecuteEvolution(
                        surface: item,
                        flowKind: flow.Kind,
                        stepSize: validParams.StepSize,
                        maxSteps: validParams.MaxSteps,
                        context: context)),
                config: new OperationConfig<Surface, Surface> {
                    Context = context,
                    ValidationMode = V.Standard | V.SurfaceContinuity | V.UVDomain,
                    OperationName = $"Morph.Evolve.{flow.Kind.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    EnableDiagnostics = false,
                }));
    }
}
```

## Key Patterns Followed

1. **Byte-based semantic types**: `DeformationMode(byte)`, `SmoothingWeight(byte)`, `EvolutionFlow(byte)` exactly match `Extract.Semantic` pattern
2. **StructLayout attribute**: `[System.Runtime.InteropServices.StructLayout(LayoutKind.Auto)]` on all readonly structs
3. **Internal Request struct**: Consolidates kind, parameter, validation mode like `Extract.Request`
4. **Pattern matching validation**: Exhaustive switch expressions with named parameters and context-rich error messages
5. **UnifiedOperation integration**: All operations go through `UnifiedOperation.Apply` with proper `OperationConfig`
6. **Named parameters**: All non-obvious parameters named (`kind:`, `parameter:`, `validationMode:`, etc.)
7. **No var**: Explicit type declarations throughout
8. **No if/else**: Only switch expressions and ternary patterns
9. **Trailing commas**: In all multi-line initializers
10. **K&R braces**: Opening braces on same line
11. **InvariantCulture**: All `.ToString()` calls use `CultureInfo.InvariantCulture`
12. **Pure/AggressiveInlining**: Performance attributes on public methods

## LOC Estimate
160-180 lines (dense, no helper methods, follows Extract.cs structure)