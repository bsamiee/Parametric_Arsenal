# BooleanCompute.cs Implementation Blueprint

**File**: `libs/rhino/boolean/BooleanCompute.cs`  
**Purpose**: RhinoCommon SDK wrapper algorithms with null handling  
**Types**: 1 (BooleanCompute class only)  
**Estimated LOC**: 200-250

## File Structure

```csharp
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Boolean;

/// <summary>Dense boolean algorithm implementations wrapping RhinoCommon SDK.</summary>
[Pure]
internal static class BooleanCompute {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepUnion(
        Brep[] breps,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanUnion(breps, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Union returned null - verify input Breps are closed, valid, and have compatible tolerances")),
                    { Length: 0 } => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.EmptyResult.WithContext("Union produced empty result - Breps may not overlap or touch")),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps in result: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepIntersection(
        Brep[] firstSet,
        Brep[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanIntersection(firstSet, secondSet, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Intersection returned null - verify Breps are closed and overlap")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepDifference(
        Brep[] firstSet,
        Brep[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Brep.CreateBooleanDifference(
                    firstSet: firstSet,
                    secondSet: secondSet,
                    tolerance: tolerance,
                    manifoldOnly: options.ManifoldOnly) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Difference returned null - verify Breps are closed and overlap")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> BrepSplit(
        Brep brep,
        Brep splitter,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : brep.Split(splitter, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Split returned null - verify Breps intersect")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [brep,],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Brep[] results => options.ValidateResult
                        ? results.All(static b => b.IsValid)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: results,
                                Meshes: [],
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid Breps: {results.Count(static b => !b.IsValid)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: results,
                            Meshes: [],
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshUnion(
        Mesh[] meshes,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanUnion(
                    meshes: meshes,
                    tolerance: tolerance,
                    meshBooleanOptions: new MeshBooleanOptions {
                        CombineCoplanarFaces = options.CombineCoplanarFaces,
                    }) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh union returned null - ensure meshes are closed and manifold")),
                    { Length: 0 } => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.EmptyResult.WithContext("Mesh union produced empty result")),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshIntersection(
        Mesh[] firstSet,
        Mesh[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanIntersection(
                    firstSet: firstSet,
                    secondSet: secondSet,
                    tolerance: tolerance,
                    meshBooleanOptions: new MeshBooleanOptions {
                        CombineCoplanarFaces = options.CombineCoplanarFaces,
                    }) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh intersection returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshDifference(
        Mesh[] firstSet,
        Mesh[] secondSet,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanDifference(
                    firstSet: firstSet,
                    secondSet: secondSet,
                    tolerance: tolerance,
                    meshBooleanOptions: new MeshBooleanOptions {
                        CombineCoplanarFaces = options.CombineCoplanarFaces,
                    }) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh difference returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> MeshSplit(
        Mesh[] meshes,
        Mesh[] cutters,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanSplit(
                    meshes: meshes,
                    cutters: cutters,
                    tolerance: tolerance,
                    meshBooleanOptions: new MeshBooleanOptions {
                        CombineCoplanarFaces = options.CombineCoplanarFaces,
                    }) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.BooleanOps.OperationFailed.WithContext("Mesh split returned null")),
                    { Length: 0 } => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [.. meshes,],
                        Curves: [],
                        ToleranceUsed: tolerance)),
                    Mesh[] results => options.ValidateResult
                        ? results.All(static m => m.IsValid && m.IsClosed)
                            ? ResultFactory.Create(value: new Boolean.BooleanOutput(
                                Breps: [],
                                Meshes: results,
                                Curves: [],
                                ToleranceUsed: tolerance))
                            : ResultFactory.Create<Boolean.BooleanOutput>(
                                error: E.Validation.GeometryInvalid.WithContext(
                                    $"Invalid meshes: {results.Count(static m => !m.IsValid || !m.IsClosed)} of {results.Length}"))
                        : ResultFactory.Create(value: new Boolean.BooleanOutput(
                            Breps: [],
                            Meshes: results,
                            Curves: [],
                            ToleranceUsed: tolerance)),
                };
        }))();
}
```

## Key Design Notes

### CRITICAL: Type References
- **All methods return** `Result<Boolean.BooleanOutput>`
- **BooleanOutput is nested in Boolean class**, not BooleanCore
- **Matches pattern**: IntersectionCompute returns `Intersect.IntersectionOutput`
- **Single type per file**: Only BooleanCompute class at namespace level

### SDK Wrapping Pattern
Each method follows identical structure:
1. Tolerance validation (RhinoMath.IsValidDouble, > ZeroTolerance)
2. SDK call with named parameters and inline options construction
3. Null check with detailed error context
4. Empty result handling (length 0)
5. Optional result validation (IsValid, IsClosed for meshes)
6. Boolean.BooleanOutput construction with appropriate collection

### Null Handling
- **Pattern**: `SDK.Method() switch { null => error, ... }`
- **Error references**: E.Geometry.BooleanOps.* constants
- **Error context**: Specific guidance on failure cause
- **Examples**:
  - "verify input Breps are closed, valid, and have compatible tolerances"
  - "ensure meshes are closed and manifold"

### MeshBooleanOptions Construction
```csharp
Mesh.CreateBooleanUnion(
    meshes: meshes,
    tolerance: tolerance,
    meshBooleanOptions: new MeshBooleanOptions {
        CombineCoplanarFaces = options.CombineCoplanarFaces,
    })
```
- Constructed inline in SDK call parameter
- Maps from Boolean.BooleanOptions to SDK type
- Only CombineCoplanarFaces exposed (other options kept at defaults)

### Result Validation
- **Optional**: Controlled by `options.ValidateResult` boolean
- **Breps**: Check `IsValid`
- **Meshes**: Check `IsValid && IsClosed`
- **Error reporting**: Count of invalid items in result set

### Empty Result Handling
- **Union**: Empty result is ERROR (must produce geometry)
- **Intersection**: Empty result is VALID (no overlap)
- **Difference**: Empty result is VALID (nothing to subtract)
- **Split**: Empty result is VALID (no intersection to split on - return original geometry)

### RhinoMath Usage
- **RhinoMath.IsValidDouble**: Validate tolerance values
- **RhinoMath.ZeroTolerance**: Minimum tolerance threshold
- **NO magic numbers**: All comparisons use RhinoMath constants

### Pattern Matching
- **Switch expressions**: All SDK result handling
- **Ternary operators**: ValidateResult conditional
- **Named parameters**: All SDK calls and ResultFactory.Create

### LOC Breakdown per Method (~30-35 LOC each)
- BrepUnion: 30
- BrepIntersection: 30
- BrepDifference: 30
- BrepSplit: 30
- MeshUnion: 32 (inline MeshBooleanOptions)
- MeshIntersection: 32
- MeshDifference: 32
- MeshSplit: 32
- **Total**: 8 methods × ~31 LOC = ~248 LOC including using statements

## XML Documentation Standards
```csharp
/// <summary>Dense boolean algorithm implementations wrapping RhinoCommon SDK.</summary>
```

**NO** method-level documentation - internal implementations don't need XML comments.
