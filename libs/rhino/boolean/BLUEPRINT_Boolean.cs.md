# Boolean.cs Implementation Blueprint

**File**: `libs/rhino/boolean/Boolean.cs`  
**Purpose**: Public API surface with unified `Execute<T1, T2>` entry point  
**Types**: 1 (Boolean class with 3 nested types)  
**Estimated LOC**: 140-180

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

    /// <summary>Boolean operation result containing geometry arrays and metadata.</summary>
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
}
```

## Key Design Notes

### Type Nesting (CRITICAL)
- **Boolean** static class: Main API container - ONLY type at namespace level
- **OperationType** enum: Nested in Boolean class (byte enum for memory efficiency)
- **BooleanOptions** record struct: Nested in Boolean class with StructLayout.Auto
- **BooleanOutput** record struct: Nested in Boolean class (NOT in BooleanCore - matches Intersect.IntersectionOutput pattern)

### Namespace Suppression
- **REQUIRED**: `[SuppressMessage("MA0049")]` attribute on Boolean class
- **Justification**: Class name matches namespace (Arsenal.Rhino.Boolean.Boolean)
- **Rule**: This is the ONLY file that can use a suppression

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
- Using statements: 10
- Namespace + class declaration: 3
- OperationType enum: 6
- BooleanOptions record: 7
- BooleanOutput record: 10
- Execute<T1, T2> method: 30
- Total: ~66 LOC base + XML comments

### Why BooleanOutput is Here (Not in BooleanCore)
- **Pattern match**: Intersect.IntersectionOutput is nested in Intersect (public API class)
- **Rule**: Only ONE type per file at namespace level (no mixed types in bracket)
- **BooleanCore.cs must have ONLY BooleanCore class** at namespace level
- **Public output types belong with public API**, not internal implementation

## XML Documentation Standards
```csharp
/// <summary>Unified boolean operations for Brep, Mesh, and planar Curve geometry.</summary>
/// <summary>Boolean operation type selector.</summary>
/// <summary>Boolean operation configuration options.</summary>
/// <summary>Executes type-detected boolean operation with automatic validation and dispatch.</summary>
```

**NO** additional frills, parameter tags, or remarks - single-line summaries only.
