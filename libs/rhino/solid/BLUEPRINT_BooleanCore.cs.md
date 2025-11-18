# BooleanCore.cs Implementation Blueprint

**File**: `libs/rhino/boolean/BooleanCore.cs`  
**Purpose**: FrozenDictionary dispatch registry and execution routing  
**Types**: 1 (BooleanCore class ONLY - no nested types, no output types)  
**Estimated LOC**: 140-170

## File Structure

```csharp
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Boolean;

/// <summary>FrozenDictionary dispatch with type-based operation routing.</summary>
[Pure]
internal static class BooleanCore {
    /// <summary>Type-operation-specific dispatch registry mapping to executors and validation modes.</summary>
    internal static readonly FrozenDictionary<(Type T1, Type T2, Boolean.OperationType Op), (V Mode, Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> Executor)> OperationRegistry =
        new Dictionary<(Type T1, Type T2, Boolean.OperationType Op), (V Mode, Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> Executor)> {
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Union)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Intersection)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Difference)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep), typeof(Brep), Boolean.OperationType.Split)] = (V.Standard | V.Topology, MakeBrepExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), Boolean.OperationType.Union)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), Boolean.OperationType.Intersection)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Brep[]), typeof(Brep[]), Boolean.OperationType.Difference)] = (V.Standard | V.Topology, MakeBrepArrayExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Union)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Intersection)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Difference)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh), typeof(Mesh), Boolean.OperationType.Split)] = (V.Standard | V.MeshSpecific, MakeMeshExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), Boolean.OperationType.Union)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), Boolean.OperationType.Intersection)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
            [(typeof(Mesh[]), typeof(Mesh[]), Boolean.OperationType.Difference)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
        }.ToFrozenDictionary();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeBrepExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepBoolean((Brep)a, (Brep)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeBrepArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepArrayBoolean((Brep[])a, (Brep[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeMeshExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshBoolean((Mesh)a, (Mesh)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<Boolean.BooleanOutput>> MakeMeshArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshArrayBoolean((Mesh[])a, (Mesh[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteBrepBoolean(
        Brep brepA,
        Brep brepB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.BrepUnion([brepA, brepB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.BrepIntersection([brepA,], [brepB,], options, context),
            Boolean.OperationType.Difference => BooleanCompute.BrepDifference([brepA,], [brepB,], options, context),
            Boolean.OperationType.Split => BooleanCompute.BrepSplit(brepA, brepB, options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.BooleanOps.UnsupportedConfiguration.WithContext($"Brep operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteBrepArrayBoolean(
        Brep[] brepsA,
        Brep[] brepsB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.BrepUnion([.. brepsA, .. brepsB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.BrepIntersection(brepsA, brepsB, options, context),
            Boolean.OperationType.Difference => BooleanCompute.BrepDifference(brepsA, brepsB, options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.BooleanOps.UnsupportedConfiguration.WithContext($"Brep[] operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteMeshBoolean(
        Mesh meshA,
        Mesh meshB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.MeshUnion([meshA, meshB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.MeshIntersection([meshA,], [meshB,], options, context),
            Boolean.OperationType.Difference => BooleanCompute.MeshDifference([meshA,], [meshB,], options, context),
            Boolean.OperationType.Split => BooleanCompute.MeshSplit([meshA,], [meshB,], options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.BooleanOps.UnsupportedConfiguration.WithContext($"Mesh operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Boolean.BooleanOutput> ExecuteMeshArrayBoolean(
        Mesh[] meshesA,
        Mesh[] meshesB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.MeshUnion([.. meshesA, .. meshesB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.MeshIntersection(meshesA, meshesB, options, context),
            Boolean.OperationType.Difference => BooleanCompute.MeshDifference(meshesA, meshesB, options, context),
            _ => ResultFactory.Create<Boolean.BooleanOutput>(
                error: E.Geometry.BooleanOps.UnsupportedConfiguration.WithContext($"Mesh[] operation: {operation}")),
        };
}
```

## Key Design Notes

### CRITICAL: Single Type Per File
- **ONLY BooleanCore class at namespace level** - no nested types, no output types
- **BooleanOutput is in Boolean.cs** - nested in Boolean class, not in BooleanCore
- **Single responsibility**: BooleanCore ONLY handles dispatch and routing
- **No suppression needed** - only one type in the file
- **Pattern**: All Core files have exactly ONE type at namespace level

### Registry Structure
- **FrozenDictionary**: O(1) lookup, compiled at startup
- **Tuple key**: `(Type T1, Type T2, OperationType)` for precise dispatch
- **Tuple value**: `(V ValidationMode, Executor func)`
- **14 registry entries**: 
  - 4 Brep-Brep (Union, Intersection, Difference, Split)
  - 3 Brep[]-Brep[] (Union, Intersection, Difference - no Split for arrays)
  - 4 Mesh-Mesh (Union, Intersection, Difference, Split)
  - 3 Mesh[]-Mesh[] (Union, Intersection, Difference - no Split for arrays)

### Executor Factories
- **MakeBrepExecutor**: Creates closure with type cast to Brep
- **MakeBrepArrayExecutor**: Creates closure with type cast to Brep[]
- **MakeMeshExecutor**: Creates closure with type cast to Mesh
- **MakeMeshArrayExecutor**: Creates closure with type cast to Mesh[]
- **Pattern**: Avoids dynamic dispatch overhead, provides type safety
- **Count**: 4 executor factories total

### Routing Methods
- **ExecuteBrepBoolean**: Single Brep operations (4 operation types)
- **ExecuteBrepArrayBoolean**: Array Brep operations (3 operation types - no Split)
- **ExecuteMeshBoolean**: Single Mesh operations (4 operation types)
- **ExecuteMeshArrayBoolean**: Array Mesh operations (3 operation types - no Split)
- **All use switch expressions**: NO if/else statements
- **Count**: 4 routing methods total

### Validation Modes
- **Brep operations**: `V.Standard | V.Topology` - validate topology integrity
- **Mesh operations**: `V.Standard | V.MeshSpecific` - validate mesh structure
- **No curve validation** - no curve operations in this file

### LOC Breakdown
- Using statements: 10 lines
- BooleanCore class declaration: 2 lines
- OperationRegistry: 16 lines (14 entries + declaration + ToFrozenDictionary)
- Executor factories (4): 12 lines (4 factories × 3 lines each)
- Routing methods (4): 48 lines (4 methods × 12 lines average)
- Total: ~88 LOC core + comments/spacing = 140-170 LOC

### Type References
- **All `BooleanOutput` references MUST be qualified** as `Boolean.BooleanOutput`
- **BooleanOutput defined in Boolean.cs** - nested in Boolean class, NOT in BooleanCore
- **Pattern**: Matches IntersectionCore referencing `Intersect.IntersectionOutput`
- **Executor functions return**: `Result<Boolean.BooleanOutput>`

### Error References
- **All error references** use `E.Geometry.BooleanOps.*` namespace
- **Example**: `E.Geometry.BooleanOps.UnsupportedConfiguration`
- **NOT** `E.Geometry.Boolean.*` - incorrect namespace

## XML Documentation Standards
```csharp
/// <summary>FrozenDictionary dispatch with type-based operation routing.</summary>
/// <summary>Type-operation-specific dispatch registry mapping to executors and validation modes.</summary>
```

**NO** parameter tags or verbose descriptions - single-line summaries only.

## Adherence to Organizational Limits
- **Files**: 1 file (BooleanCore.cs) - ✅ Well under 4-file limit
- **Types**: 1 type (BooleanCore class) - ✅ Single type, no nested types
- **LOC**: 140-170 LOC per file - ✅ Within 300 LOC limit, optimal density
- **Single Responsibility**: Dispatch and routing ONLY - no output types, no configuration, no algorithms
