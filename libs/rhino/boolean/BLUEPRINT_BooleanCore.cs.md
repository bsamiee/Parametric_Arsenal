# BooleanCore.cs Implementation Blueprint

**File**: `libs/rhino/boolean/BooleanCore.cs`  
**Purpose**: FrozenDictionary dispatch registry and execution routing  
**Types**: 2 (BooleanCore class + BooleanOutput record)  
**Estimated LOC**: 150-200

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

/// <summary>FrozenDictionary dispatch with type-based operation routing.</summary>
[Pure]
internal static class BooleanCore {
    /// <summary>Type-operation-specific dispatch registry mapping to executors and validation modes.</summary>
    internal static readonly FrozenDictionary<(Type T1, Type T2, Boolean.OperationType Op), (V Mode, Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<BooleanOutput>> Executor)> OperationRegistry =
        new Dictionary<(Type T1, Type T2, Boolean.OperationType Op), (V Mode, Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<BooleanOutput>> Executor)> {
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
            [(typeof(Mesh[]), typeof(Mesh[]), Boolean.OperationType.Split)] = (V.Standard | V.MeshSpecific, MakeMeshArrayExecutor()),
            [(typeof(Curve[]), typeof(Plane), Boolean.OperationType.Union)] = (V.AreaCentroid, MakeCurveExecutor()),
        }.ToFrozenDictionary();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<BooleanOutput>> MakeBrepExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepBoolean((Brep)a, (Brep)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<BooleanOutput>> MakeBrepArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteBrepArrayBoolean((Brep[])a, (Brep[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<BooleanOutput>> MakeMeshExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshBoolean((Mesh)a, (Mesh)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<BooleanOutput>> MakeMeshArrayExecutor() =>
        (a, b, op, opts, ctx) => ExecuteMeshArrayBoolean((Mesh[])a, (Mesh[])b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<object, object, Boolean.OperationType, Boolean.BooleanOptions, IGeometryContext, Result<BooleanOutput>> MakeCurveExecutor() =>
        (a, b, op, opts, ctx) => ExecuteCurveBoolean((Curve[])a, (Plane)b, op, opts, ctx);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<BooleanOutput> ExecuteBrepBoolean(
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
            _ => ResultFactory.Create<BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Brep operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<BooleanOutput> ExecuteBrepArrayBoolean(
        Brep[] brepsA,
        Brep[] brepsB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.BrepUnion([.. brepsA, .. brepsB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.BrepIntersection(brepsA, brepsB, options, context),
            Boolean.OperationType.Difference => BooleanCompute.BrepDifference(brepsA, brepsB, options, context),
            _ => ResultFactory.Create<BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Brep[] operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<BooleanOutput> ExecuteMeshBoolean(
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
            _ => ResultFactory.Create<BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Mesh operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<BooleanOutput> ExecuteMeshArrayBoolean(
        Mesh[] meshesA,
        Mesh[] meshesB,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.MeshUnion([.. meshesA, .. meshesB,], options, context),
            Boolean.OperationType.Intersection => BooleanCompute.MeshIntersection(meshesA, meshesB, options, context),
            Boolean.OperationType.Difference => BooleanCompute.MeshDifference(meshesA, meshesB, options, context),
            Boolean.OperationType.Split => BooleanCompute.MeshSplit(meshesA, meshesB, options, context),
            _ => ResultFactory.Create<BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Mesh[] operation: {operation}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<BooleanOutput> ExecuteCurveBoolean(
        Curve[] curves,
        Plane plane,
        Boolean.OperationType operation,
        Boolean.BooleanOptions options,
        IGeometryContext context) =>
        operation switch {
            Boolean.OperationType.Union => BooleanCompute.CurveRegions(curves, plane, combineRegions: true, options, context),
            _ => ResultFactory.Create<BooleanOutput>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Curve[] operation: {operation}")),
        };
}
```

## Key Design Notes

### BooleanOutput Record
- **Public**: Accessible from Boolean.Execute return type
- **StructLayout.Auto**: Let runtime optimize struct layout
- **IReadOnlyList<T>**: Immutable collections for Breps, Meshes, Curves
- **ToleranceUsed**: Track actual tolerance applied (may differ from input)
- **Empty**: Static readonly for zero-allocation default

### Registry Structure
- **FrozenDictionary**: O(1) lookup, compiled at startup
- **Tuple key**: `(Type T1, Type T2, OperationType)` for precise dispatch
- **Tuple value**: `(V ValidationMode, Executor func)`
- **16 registry entries**: 
  - 4 Brep-Brep (single)
  - 3 Brep[]-Brep[] (arrays)
  - 4 Mesh-Mesh (single)
  - 4 Mesh[]-Mesh[] (arrays)
  - 1 Curve[]-Plane

### Executor Factories
- **MakeBrepExecutor**: Creates closure with type cast to Brep
- **MakeMeshExecutor**: Creates closure with type cast to Mesh
- **MakeCurveExecutor**: Creates closure with type cast to Curve[] + Plane
- **Pattern**: Avoids dynamic dispatch overhead, provides type safety

### Routing Methods
- **ExecuteBrepBoolean**: Single Brep operations
- **ExecuteBrepArrayBoolean**: Array Brep operations (union combines all)
- **ExecuteMeshBoolean**: Single Mesh operations
- **ExecuteMeshArrayBoolean**: Array Mesh operations
- **ExecuteCurveBoolean**: Curve region extraction
- **All use switch expressions**: NO if/else statements

### Pattern Matching
- Switch expression for operation type routing
- Named parameters in all calls
- Trailing commas in array literals

### LOC Breakdown
- Using statements: 10
- BooleanOutput record: 10
- BooleanCore class declaration: 2
- OperationRegistry: 20 (16 entries + ToFrozenDictionary)
- Executor factories (5): 15
- Routing methods (5): 60
- Total: ~117 LOC + XML comments

## XML Documentation Standards
```csharp
/// <summary>Boolean operation result containing geometry arrays and metadata.</summary>
/// <summary>Empty result for non-intersecting or failed operations.</summary>
/// <summary>FrozenDictionary dispatch with type-based operation routing.</summary>
/// <summary>Type-operation-specific dispatch registry mapping to executors and validation modes.</summary>
```

**NO** parameter tags or verbose descriptions - single-line summaries only.
