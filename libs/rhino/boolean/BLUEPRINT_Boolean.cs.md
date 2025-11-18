# Boolean.cs Implementation Blueprint

**File**: `libs/rhino/boolean/Boolean.cs`  
**Purpose**: Public API surface with unified `Execute<T1, T2>` entry point + TrimSolid helper  
**Types**: 1 (Boolean class with 3 nested types)  
**Estimated LOC**: 85-95

## File Structure

```csharp
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Boolean;

/// <summary>Unified boolean operations for Brep, Mesh, and planar Curve geometry.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Boolean is the primary API entry point for the Boolean namespace")]
public static class Boolean {
    /// <summary>Boolean operation type selector.</summary>
    public enum OperationType : byte {
        Union = 0,
        Intersection = 1,
        Difference = 2,
        Split = 3,
    }

    /// <summary>Boolean operation configuration options.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct BooleanOptions(
        double? ToleranceOverride = null,
        bool ManifoldOnly = false,
        bool CombineCoplanarFaces = true,
        bool ValidateResult = true);

    /// <summary>Boolean operation result containing geometry arrays and metadata. Only ONE array populated per call.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct BooleanOutput(
        IReadOnlyList<Brep> Breps,
        IReadOnlyList<Mesh> Meshes,
        IReadOnlyList<Curve> Curves,
        double ToleranceUsed) {
        /// <summary>Empty result for non-intersecting or failed operations.</summary>
        public static readonly BooleanOutput Empty = new([], [], [], 0.0);
    }

    /// <summary>Executes type-detected boolean operation with automatic validation and dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BooleanOutput> Execute<T1, T2>(
        T1 geometryA,
        T2 geometryB,
        OperationType operation,
        IGeometryContext context,
        BooleanOptions? options = null) where T1 : notnull where T2 : notnull =>
        BooleanCore.OperationRegistry.TryGetValue(
            key: (typeof(T1), typeof(T2), operation),
            value: out (V ValidationMode, Func<object, object, OperationType, BooleanOptions, IGeometryContext, Result<BooleanOutput>> Executor) config) switch {
            true => UnifiedOperation.Apply(
                input: geometryA,
                operation: (Func<T1, Result<IReadOnlyList<BooleanOutput>>>)(itemA => config.Executor(
                    itemA,
                    geometryB,
                    operation,
                    options ?? new BooleanOptions(),
                    context)
                    .Map(output => (IReadOnlyList<BooleanOutput>)[output])),
                config: new OperationConfig<T1, BooleanOutput> {
                    Context = context,
                    ValidationMode = config.ValidationMode,
                    OperationName = $"Boolean.{operation}.{typeof(T1).Name}.{typeof(T2).Name}",
                    EnableDiagnostics = false,
                })
                .Map(outputs => outputs.Count > 0 ? outputs[0] : BooleanOutput.Empty),
            false => ResultFactory.Create<BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext(
                    $"Operation: {operation}, Types: {typeof(T1).Name}, {typeof(T2).Name}")),
        };

    /// <summary>Trims Brep using oriented cutter retaining portions inside cutter normal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BooleanOutput> TrimSolid(
        Brep target,
        Brep cutter,
        IGeometryContext context,
        BooleanOptions? options = null) =>
        UnifiedOperation.Apply(
            input: target,
            operation: (Func<Brep, Result<IReadOnlyList<BooleanOutput>>>)(item => BooleanCompute.BrepTrim(
                item,
                cutter,
                options ?? new BooleanOptions(),
                context)
                .Map(output => (IReadOnlyList<BooleanOutput>)[output])),
            config: new OperationConfig<Brep, BooleanOutput> {
                Context = context,
                ValidationMode = V.Standard | V.Topology,
                OperationName = "Boolean.TrimSolid",
                EnableDiagnostics = false,
            })
            .Map(outputs => outputs.Count > 0 ? outputs[0] : BooleanOutput.Empty);
}
```

## Key Design Notes

### Type Nesting (CRITICAL)
- **Boolean** static class: Main API container - ONLY type at namespace level (THIS IS THE ONLY FILE WITH NESTED TYPES)
- **OperationType** enum: Nested in Boolean class (byte enum for memory efficiency) - 4 operations only
- **BooleanOptions** record struct: Nested in Boolean class with StructLayout.Auto - 4 parameters
- **BooleanOutput** record struct: Nested in Boolean class (matches libs/rhino/intersection/Intersect.cs IntersectionOutput pattern)

### Namespace Suppression (REQUIRED)
- **REQUIRED**: `[SuppressMessage("MA0049")]` attribute on Boolean class
- **Justification**: Class name matches namespace (Arsenal.Rhino.Boolean.Boolean) - this is intentional for primary API entry point
- **Rule**: This is the ONLY file in the boolean/ folder that uses a suppression - absolutely required and justified

### Validation Modes
- Retrieved from `BooleanCore.OperationRegistry` FrozenDictionary
- V.Standard | V.Topology for Brep/Mesh solids
- No hardcoded validation logic

### UnifiedOperation Integration
- Wraps input geometryA only (geometryB passed through executor)
- OperationConfig specifies validation mode from registry
- EnableDiagnostics = false for performance

### Error Handling
- TryGetValue pattern matching on registry lookup
- Unsupported combinations return E.Geometry.UnsupportedConfiguration
- Context includes operation type and both geometry type names

### Pattern Matching
- **NO if/else statements**: Switch expression for registry lookup
- Ternary for output mapping: `outputs.Count > 0 ? outputs[0] : Empty`
- Named parameters throughout

### LOC Breakdown
- Using statements: 9
- Namespace + class declaration: 3
- OperationType enum: 5 lines (4 values + declaration)
- BooleanOptions record: 6 lines (4 parameters)
- BooleanOutput record: 9 lines (4 fields + Empty)
- Execute<T1, T2> method: 25-30 lines
- TrimSolid helper method: 14 lines
- Total: ~85-95 LOC (follows Intersect pattern with helper methods)

### Why BooleanOutput is Here (DEFINITIVELY)
- **Pattern match**: libs/rhino/intersection/Intersect.cs nests IntersectionOutput in Intersect (public API class)
- **Rule**: Only ONE type per file at namespace level (no mixed types in bracket)
- **BooleanCore.cs has ONLY BooleanCore class** at namespace level - no nested types
- **Public output types belong with public API** - BooleanOutput is definitively in Boolean.cs
- **Field usage**: Only ONE array populated per operation call (Breps for Brep ops, Meshes for Mesh ops)

## Error References
All error codes use `E.Geometry.BooleanOps.*` namespace (libs/core/errors/E.cs line 355):
- `E.Geometry.BooleanOps.OperationFailed` (2100)
- `E.Geometry.BooleanOps.NotClosedOrSolid` (2101)
- `E.Geometry.BooleanOps.NotPlanarOrCoplanar` (2102)
- `E.Geometry.BooleanOps.InsufficientMeshQuality` (2103)
- `E.Geometry.BooleanOps.DegenerateResult` (2104)
- `E.Geometry.BooleanOps.TrimFailed` (2105) (used by TrimSolid helper method)
- `E.Geometry.BooleanOps.SplitFailed` (2106)
- `E.Geometry.BooleanOps.RegionExtractionFailed` (2107) (unused - reserved for future curve operations)
- `E.Geometry.BooleanOps.ResultValidationFailed` (2108)

**Never reference**: `E.Geometry.Boolean.*` (incorrect namespace)

## XML Documentation Standards
```csharp
/// <summary>Unified boolean operations for Brep, Mesh, and planar Curve geometry.</summary>
/// <summary>Boolean operation type selector.</summary>
/// <summary>Boolean operation configuration options.</summary>
/// <summary>Boolean operation result containing geometry arrays and metadata. Only ONE array populated per call.</summary>
/// <summary>Executes type-detected boolean operation with automatic validation and dispatch.</summary>
```

**NO** additional frills, parameter tags, or remarks - single-line summaries only.
