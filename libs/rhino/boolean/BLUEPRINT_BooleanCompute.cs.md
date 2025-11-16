# BooleanCompute.cs Implementation Blueprint

**File**: `libs/rhino/boolean/BooleanCompute.cs`  
**Purpose**: RhinoCommon SDK wrapper algorithms with null handling  
**Types**: 1 (BooleanCompute class only)  
**Estimated LOC**: 220-280

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
                        error: E.Geometry.InvalidGeometryType.WithContext("Union returned null - verify input Breps are closed, valid, and have compatible tolerances")),
                    { Length: 0 } => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Union produced empty result - Breps may not overlap or touch")),
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
                        error: E.Geometry.InvalidGeometryType.WithContext("Intersection returned null - verify Breps are closed and overlap")),
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
                        error: E.Geometry.InvalidGeometryType.WithContext("Difference returned null - verify Breps are closed and overlap")),
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
                        error: E.Geometry.InvalidGeometryType.WithContext("Split returned null - verify Breps intersect")),
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
            MeshBooleanOptions meshOptions = new() {
                CombineCoplanarFaces = options.CombineCoplanarFaces,
            };
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanUnion(meshes, tolerance, meshOptions) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Mesh union returned null - ensure meshes are closed and manifold")),
                    { Length: 0 } => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Mesh union produced empty result")),
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
            MeshBooleanOptions meshOptions = new() {
                CombineCoplanarFaces = options.CombineCoplanarFaces,
            };
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanIntersection(firstSet, secondSet, tolerance, meshOptions) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Mesh intersection returned null")),
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
            MeshBooleanOptions meshOptions = new() {
                CombineCoplanarFaces = options.CombineCoplanarFaces,
            };
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanDifference(firstSet, secondSet, tolerance, meshOptions) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Mesh difference returned null")),
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
            MeshBooleanOptions meshOptions = new() {
                CombineCoplanarFaces = options.CombineCoplanarFaces,
            };
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Mesh.CreateBooleanSplit(meshes, cutters, tolerance, meshOptions) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Mesh split returned null")),
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Boolean.BooleanOutput> CurveRegions(
        Curve[] curves,
        Plane plane,
        bool combineRegions,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        ((Func<Result<Boolean.BooleanOutput>>)(() => {
            double tolerance = options.ToleranceOverride ?? context.AbsoluteTolerance;
            
            return !RhinoMath.IsValidDouble(tolerance) || tolerance <= RhinoMath.ZeroTolerance
                ? ResultFactory.Create<Boolean.BooleanOutput>(error: E.Validation.ToleranceAbsoluteInvalid)
                : Curve.CreateBooleanRegions(curves, plane, combineRegions, tolerance) switch {
                    null => ResultFactory.Create<Boolean.BooleanOutput>(
                        error: E.Geometry.InvalidGeometryType.WithContext("Curve region extraction returned null - verify curves are planar and coplanar")),
                    CurveBooleanRegions regions when regions.RegionCount > 0 => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
                        Curves: [.. Enumerable.Range(0, regions.RegionCount)
                            .Select(i => regions.RegionCurve(i))
                            .Where(static c => c is not null),
                        ],
                        ToleranceUsed: tolerance)),
                    _ => ResultFactory.Create(value: new Boolean.BooleanOutput(
                        Breps: [],
                        Meshes: [],
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
2. SDK call with named parameters
3. Null check with detailed error context
4. Empty result handling (length 0)
5. Optional result validation (IsValid, IsClosed for meshes)
6. BooleanOutput construction with appropriate collection

### Null Handling
- **Pattern**: `SDK.Method() switch { null => error, ... }`
- **Error context**: Specific guidance on failure cause
- **Examples**:
  - "verify input Breps are closed, valid, and have compatible tolerances"
  - "ensure meshes are closed and manifold"
  - "verify curves are planar and coplanar"

### MeshBooleanOptions Configuration
```csharp
MeshBooleanOptions meshOptions = new() {
    CombineCoplanarFaces = options.CombineCoplanarFaces,
};
```
- Created inline per method
- Maps from Boolean.BooleanOptions to SDK type
- Only CombineCoplanarFaces exposed (other options kept at defaults)

### Result Validation
- **Optional**: Controlled by `options.ValidateResult` boolean
- **Breps**: Check `IsValid`
- **Meshes**: Check `IsValid && IsClosed`
- **Error reporting**: Count of invalid items in result set

### Empty Result Handling
- **Union/Intersection/Difference**: Empty array is error for Union, valid for Intersection/Difference
- **Split**: Empty means no split occurred, return original
- **Regions**: Empty is valid (no regions found)

### RhinoMath Usage
- **RhinoMath.IsValidDouble**: Validate tolerance values
- **RhinoMath.ZeroTolerance**: Minimum tolerance threshold
- **NO magic numbers**: All comparisons use RhinoMath constants

### Pattern Matching
- **Switch expressions**: All SDK result handling
- **Ternary operators**: ValidateResult conditional
- **Named parameters**: All SDK calls and ResultFactory.Create

### LOC Breakdown per Method (~35 LOC each)
- BrepUnion: 35
- BrepIntersection: 35
- BrepDifference: 35
- BrepSplit: 35
- MeshUnion: 38 (MeshBooleanOptions construction)
- MeshIntersection: 38
- MeshDifference: 38
- MeshSplit: 38
- CurveRegions: 28 (CurveBooleanRegions extraction)
- **Total**: ~315 LOC including using statements

## XML Documentation Standards
```csharp
/// <summary>Dense boolean algorithm implementations wrapping RhinoCommon SDK.</summary>
```

**NO** method-level documentation - internal implementations don't need XML comments.
