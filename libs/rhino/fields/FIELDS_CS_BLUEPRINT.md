# Fields.cs - Public API Implementation Blueprint

## File Purpose
Public API surface for scalar/vector field operations with UnifiedOperation dispatch. Single entry point for distance fields, gradient fields, streamline tracing, and isosurface extraction.

## Type Count
**1 type**: `Fields` (static class - public API)

## Critical Patterns
- NO enums - byte-based operation dispatch via FieldsCore registry
- NO var - explicit types everywhere
- NO if/else - pattern matching and switch expressions only
- UnifiedOperation integration for all polymorphic operations
- Named parameters for non-obvious arguments
- K&R braces, trailing commas, target-typed new()

## Complete Implementation

```csharp
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fields;

/// <summary>Scalar and vector field operations for computational field analysis.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fields is the primary API entry point")]
public static class Fields {
    /// <summary>Field specification for grid resolution, bounds, and step size.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct FieldSpec {
        /// <summary>Grid resolution (cube root of sample count).</summary>
        public readonly int Resolution;
        /// <summary>Sample region bounding box (null uses geometry bounds).</summary>
        public readonly BoundingBox? Bounds;
        /// <summary>Integration/sampling step size.</summary>
        public readonly double StepSize;

        public FieldSpec(int resolution = FieldsConfig.DefaultResolution, BoundingBox? bounds = null, double? stepSize = null) {
            this.Resolution = resolution >= FieldsConfig.MinResolution
                ? resolution
                : FieldsConfig.DefaultResolution;
            this.Bounds = bounds;
            this.StepSize = stepSize ?? FieldsConfig.DefaultStepSize;
        }
    }

    /// <summary>Compute signed distance field: geometry → (grid points[], distances[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
        T geometry,
        FieldSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        FieldsCore.OperationRegistry.TryGetValue((FieldsConfig.OperationDistance, typeof(T)), out (Func<object, FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> execute, byte integrationMethod) config) switch {
            true => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<(Point3d[], double[])>>>)(item =>
                    config.execute(item, spec, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result])),
                config: new OperationConfig<T, (Point3d[], double[])> {
                    Context = context,
                    ValidationMode = FieldsConfig.ValidationModes.TryGetValue((FieldsConfig.OperationDistance, typeof(T)), out V mode) ? mode : V.Standard,
                    OperationName = $"Fields.DistanceField.{typeof(T).Name}",
                    EnableDiagnostics = false,
                }).Map(results => results[0]),
            false => ResultFactory.Create<(Point3d[], double[])>(
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {typeof(T).Name}")),
        };

    /// <summary>Compute gradient field: geometry → (grid points[], gradient vectors[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d[] Grid, Vector3d[] Gradients)> GradientField<T>(
        T geometry,
        FieldSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        DistanceField(geometry: geometry, spec: spec, context: context).Bind(distanceField =>
            FieldsCompute.ComputeGradient(
                distances: distanceField.Distances,
                grid: distanceField.Grid,
                resolution: spec.Resolution,
                context: context));

    /// <summary>Trace streamlines along vector field: (field, seeds) → curves[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Curve[]> Streamlines(
        Vector3d[] vectorField,
        Point3d[] gridPoints,
        Point3d[] seeds,
        FieldSpec spec,
        IGeometryContext context) =>
        (vectorField.Length == gridPoints.Length, seeds.Length > 0) switch {
            (false, _) => ResultFactory.Create<Curve[]>(
                error: E.Geometry.InvalidScalarField.WithContext("Vector field length must match grid points")),
            (_, false) => ResultFactory.Create<Curve[]>(
                error: E.Geometry.InvalidStreamlineSeeds),
            (true, true) => FieldsCompute.IntegrateStreamlines(
                vectorField: vectorField,
                gridPoints: gridPoints,
                seeds: seeds,
                stepSize: spec.StepSize,
                integrationMethod: FieldsConfig.IntegrationRK4,
                context: context),
        };

    /// <summary>Extract isosurfaces from scalar field: (field, isovalues) → meshes[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Mesh[]> Isosurfaces(
        double[] scalarField,
        Point3d[] gridPoints,
        FieldSpec spec,
        double[] isovalues,
        IGeometryContext context) =>
        (scalarField.Length == gridPoints.Length, isovalues.Length > 0, isovalues.All(v => RhinoMath.IsValidDouble(v))) switch {
            (false, _, _) => ResultFactory.Create<Mesh[]>(
                error: E.Geometry.InvalidScalarField.WithContext("Scalar field length must match grid points")),
            (_, false, _) => ResultFactory.Create<Mesh[]>(
                error: E.Geometry.InvalidIsovalue.WithContext("At least one isovalue required")),
            (_, _, false) => ResultFactory.Create<Mesh[]>(
                error: E.Geometry.InvalidIsovalue.WithContext("All isovalues must be valid doubles")),
            (true, true, true) => FieldsCompute.ExtractIsosurfaces(
                scalarField: scalarField,
                gridPoints: gridPoints,
                resolution: spec.Resolution,
                isovalues: isovalues,
                context: context),
        };
}
```

## LOC: 92

## Key Patterns Demonstrated
1. **Struct with readonly fields** - FieldSpec encapsulates configuration
2. **Byte-based dispatch** - FieldsConfig.OperationDistance constant, NOT enum
3. **FrozenDictionary lookup** - FieldsCore.OperationRegistry.TryGetValue
4. **UnifiedOperation integration** - Wraps in IReadOnlyList for dispatch
5. **Named parameters** - `error:`, `value:`, `operation:`, `config:`
6. **Pattern matching switch** - NOT if/else statements
7. **Explicit types** - NO var anywhere
8. **K&R braces** - Opening brace on same line
9. **Trailing commas** - Multi-line collection literals
10. **Result monad** - Map, Bind for composition

## Integration Points
- **FieldsCore**: Registry lookup for operation dispatch
- **FieldsConfig**: Constants (OperationDistance, DefaultResolution, ValidationModes)
- **FieldsCompute**: Core algorithms (ComputeGradient, IntegrateStreamlines, ExtractIsosurfaces)
- **UnifiedOperation**: Polymorphic dispatch with validation
- **E.Geometry**: Error codes for field operations

## No Helper Methods
All logic inline with switch expressions and pattern matching. No private methods.
