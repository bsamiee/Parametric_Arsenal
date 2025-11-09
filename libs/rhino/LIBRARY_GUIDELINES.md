# Library Guidelines for libs/rhino/ Functionality Folders

**Purpose**: This document defines the architectural patterns, file organization, and implementation guidelines for creating new functionality folders in `libs/rhino/`. All folders must follow these patterns to maintain consistency, leverage `libs/core/` infrastructure, and achieve the same level of sophistication as existing implementations.

**Audience**: Developers creating new geometry operation libraries

**Last Updated**: 2025-11-09

**Prerequisites**: Read `/CLAUDE.md` and `/.github/copilot-instructions.md` for foundational coding standards.

---

## Table of Contents

1. [Three-File Architecture Pattern](#three-file-architecture-pattern)
2. [File Naming and Structure](#file-naming-and-structure)
3. [Public API Design (Main File)](#public-api-design-main-file)
4. [Configuration and Constants (Config File)](#configuration-and-constants-config-file)
5. [Core Implementation Logic (Core File)](#core-implementation-logic-core-file)
6. [FrozenDictionary Dispatch Systems](#frozendictionary-dispatch-systems)
7. [UnifiedOperation Integration](#unifiedoperation-integration)
8. [Validation Mode Mapping](#validation-mode-mapping)
9. [Error Handling Patterns](#error-handling-patterns)
10. [Memory Optimization Techniques](#memory-optimization-techniques)
11. [Type-Based Polymorphic Dispatch](#type-based-polymorphic-dispatch)
12. [Common Implementation Patterns](#common-implementation-patterns)
13. [Complete Folder Examples](#complete-folder-examples)
14. [Pitfalls and Solutions](#pitfalls-and-solutions)
15. [Checklist for New Folders](#checklist-for-new-folders)

---

## Three-File Architecture Pattern

Every functionality folder in `libs/rhino/` MUST follow this three-file structure:

```
libs/rhino/myfolder/
├── MyFolder.cs        # Public API - thin orchestration layer
├── MyFolderConfig.cs  # Constants, FrozenDictionary configs, validation mapping
└── MyFolderCore.cs    # Implementation logic, algorithms, dispatch handlers
```

### File Responsibilities

**Main File (`MyFolder.cs`)**:
- Public API surface with explicit method signatures
- Thin orchestration calling UnifiedOperation or Core
- Pure functions marked with `[Pure]` and `[MethodImpl(AggressiveInlining)]`
- Method overloads for type-specific ergonomics
- XML documentation for all public members
- NO implementation logic - delegate to Core

**Config File (`MyFolderConfig.cs`)**:
- `internal static class` containing constants
- FrozenDictionary mappings for validation modes
- FrozenDictionary mappings for type dispatch
- Buffer sizes, thresholds, sampling parameters
- NO logic - only data structures

**Core File (`MyFolderCore.cs`)**:
- `internal static class` with actual algorithms
- FrozenDictionary dispatch handlers
- Factory methods for creating executors
- Complex computation logic
- ArrayPool buffer management
- ConditionalWeakTable caching
- All marked `internal` - never exposed publicly

### Why This Pattern?

1. **Separation of Concerns**: API, configuration, and implementation are isolated
2. **Testability**: Core logic can be tested independently
3. **Maintainability**: Changes to algorithms don't affect public API
4. **Discoverability**: Users see clean API without implementation complexity
5. **Consistency**: All folders follow identical structure
6. **File Count Compliance**: Stays within 3-file ideal target

---

## File Naming and Structure

### Namespace Convention

```csharp
namespace Arsenal.Rhino.{FolderName};
```

Example: `Arsenal.Rhino.Spatial`, `Arsenal.Rhino.Analysis`, `Arsenal.Rhino.Extraction`

### File Organization

```csharp
// Main File: Spatial.cs
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace")]
public static class Spatial {
    // Public API methods
}

// Config File: SpatialConfig.cs
namespace Arsenal.Rhino.Spatial;

internal static class SpatialConfig {
    // Constants and mappings
}

// Core File: SpatialCore.cs
using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

internal static class SpatialCore {
    // Implementation logic
}
```

### Suppression Attributes

Main file MUST include this suppression when class name matches namespace:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "{Name} is the primary API entry point for the {Name} namespace")]
public static class MyFolder { }
```

---

## Public API Design (Main File)

### Core Principles

1. **Explicit types** - No `var` ever
2. **Named parameters** - For all non-obvious arguments
3. **Pure functions** - Mark with `[Pure]` attribute
4. **Aggressive inlining** - Hot paths get `[MethodImpl(AggressiveInlining)]`
5. **Return Result<T>** - All failable operations use Result monad
6. **Thin orchestration** - Delegate to Core or UnifiedOperation

### Standard Method Signature Pattern

```csharp
/// <summary>Operation description with polymorphic behavior explained.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<TOutput> MethodName<TInput, TQuery>(
    TInput input,
    TQuery query,
    IGeometryContext context,
    int? bufferSize = null,
    bool enableDiagnostics = false) where TInput : notnull where TQuery : notnull =>
    CoreClass.OperationRegistry.TryGetValue((typeof(TInput), typeof(TQuery)), out var config) switch {
        true => UnifiedOperation.Apply(
            input: input,
            operation: (Func<TInput, Result<TOutput>>)(item => config.execute(item, query, context)),
            config: new OperationConfig<TInput, TOutput> {
                Context = context,
                ValidationMode = config.mode,
                OperationName = $"MyFolder.{typeof(TInput).Name}",
                EnableDiagnostics = enableDiagnostics,
            }),
        false => ResultFactory.Create<TOutput>(
            error: E.MyDomain.UnsupportedTypeCombo.WithContext(
                $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
    };
```

### Type-Specific Overloads

Provide ergonomic overloads for common scenarios:

```csharp
// Analysis.cs example
public static Result<CurveData> Analyze(
    Curve curve,
    IGeometryContext context,
    double? parameter = null,
    int derivativeOrder = 2,
    bool enableDiagnostics = false) =>
    AnalysisCore.Execute(curve, context, t: parameter, uv: null, /* ... */)
        .Map(results => (CurveData)results[0]);

public static Result<SurfaceData> Analyze(
    Surface surface,
    IGeometryContext context,
    (double u, double v)? uvParameter = null,
    int derivativeOrder = 2,
    bool enableDiagnostics = false) =>
    AnalysisCore.Execute(surface, context, t: null, uv: uvParameter, /* ... */)
        .Map(results => (SurfaceData)results[0]);
```

### Multi-Item Operations

```csharp
public static Result<IReadOnlyList<TResult>> AnalyzeMultiple<T>(
    IReadOnlyList<T> geometries,
    IGeometryContext context,
    bool enableDiagnostics = false) where T : notnull =>
    UnifiedOperation.Apply(
        geometries,
        (Func<object, Result<IReadOnlyList<TResult>>>)(item =>
            MyFolderCore.Execute(item, context, enableDiagnostics: enableDiagnostics)),
        new OperationConfig<object, TResult> {
            Context = context,
            ValidationMode = V.None,
            EnableCache = true,
            AccumulateErrors = false,
            OperationName = "MyFolder.Multiple",
            EnableDiagnostics = enableDiagnostics,
        });
```

### Custom Result Types

Define domain-specific result types with `IResult` marker interface:

```csharp
public static class MyFolder {
    /// <summary>Marker interface for polymorphic result discrimination.</summary>
    public interface IResult {
        Point3d Location { get; }
    }

    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MyData(
        Point3d Location,
        Vector3d Normal,
        double Area) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MyData @ {this.Location} | A={this.Area:F3}");
    }
}
```

### Custom Configuration Types

Use readonly structs for operation-specific configuration:

```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct MyOptions(
    double? Tolerance = null,
    int? MaxIterations = null,
    bool Sorted = false);
```

---

## Configuration and Constants (Config File)

### Structure

```csharp
namespace Arsenal.Rhino.MyFolder;

internal static class MyFolderConfig {
    // Constants first
    internal const int DefaultBufferSize = 2048;
    internal const int LargeBufferSize = 4096;
    internal const int MaxIterations = 100;
    internal const double DefaultTolerance = 0.001;

    // FrozenDictionary mappings second
    internal static readonly FrozenDictionary<Type, V> ValidationModes = /* ... */;
    internal static readonly FrozenDictionary<(Type, Type), (V, int)> TypeConfigs = /* ... */;
}
```

### Validation Mode Mapping

ALWAYS use FrozenDictionary for validation mode lookups:

```csharp
internal static readonly FrozenDictionary<Type, V> ValidationModes =
    new Dictionary<Type, V> {
        [typeof(Curve)] = V.Standard | V.Degeneracy,
        [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
        [typeof(Surface)] = V.Standard | V.UVDomain,
        [typeof(Brep)] = V.Standard | V.Topology,
        [typeof(Mesh)] = V.Standard | V.MeshSpecific,
    }.ToFrozenDictionary();
```

### Type-Pair Configuration

For operations on two geometry types:

```csharp
internal static readonly FrozenDictionary<(Type, Type), V> ValidationModes =
    new Dictionary<(Type, Type), V> {
        [(typeof(Curve), typeof(Curve))] = V.Standard | V.Degeneracy,
        [(typeof(Curve), typeof(Surface))] = V.Standard,
        [(typeof(Brep), typeof(Brep))] = V.Standard | V.Topology,
        [(typeof(Mesh), typeof(Plane))] = V.MeshSpecific,
    }.ToFrozenDictionary();
```

### Buffer Size Constants

```csharp
internal const int DefaultBufferSize = 2048;  // Basic operations
internal const int LargeBufferSize = 4096;    // Complex operations
internal const int MaxDiscontinuities = 20;   // Curve analysis
internal const int CurveFrameSampleCount = 5; // Frame sampling
```

### Type Inheritance Fallback

For semantic extraction or extensible type systems:

```csharp
internal static V GetValidationMode(byte kind, Type geometryType) =>
    ValidationModes.TryGetValue((kind, geometryType), out V exact)
        ? exact
        : ValidationModes
            .Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType))
            .OrderByDescending(kv => kv.Key.GeometryType, 
                Comparer<Type>.Create(static (a, b) => 
                    a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
            .Select(kv => kv.Value)
            .DefaultIfEmpty(V.Standard)
            .First();
```

---

## Core Implementation Logic (Core File)

### Structure

```csharp
namespace Arsenal.Rhino.MyFolder;

internal static class MyFolderCore {
    // Factory methods for creating tree/cache structures
    private static readonly Func<object, RTree> _pointArrayFactory = /* ... */;
    
    // FrozenDictionary dispatch registry
    internal static readonly FrozenDictionary<(Type, Type), Config> OperationRegistry = /* ... */;
    
    // Private helper methods
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> HelperMethod(...) { }
    
    // Internal execution methods
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> Execute(...) { }
}
```

### Factory Method Pattern

For RTree or expensive object construction:

```csharp
private static readonly Func<object, RTree> _pointArrayFactory = 
    s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree();

private static readonly Func<object, RTree> _meshFactory = 
    s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree();

private static readonly Func<object, RTree> _curveArrayFactory = 
    s => BuildGeometryArrayTree((Curve[])s);

[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
    RTree tree = new();
    for (int i = 0; i < geometries.Length; i++) {
        _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
    }
    return tree;
}
```

### Executor Factory Pattern

Create specialized executors for different type combinations:

```csharp
private static Func<object, object, IGeometryContext, int, Result<T>> MakeExecutor<TInput>(
    Func<object, RTree> factory) where TInput : notnull =>
    (i, q, _, b) => GetTree(source: (TInput)i, factory: factory)
        .Bind(tree => ExecuteRangeSearch(tree: tree, queryShape: q, bufferSize: b));
```

### ConditionalWeakTable Caching

Use for automatic cache with GC-aware weak references:

```csharp
internal static readonly ConditionalWeakTable<object, RTree> TreeCache = [];

[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static Result<RTree> GetTree<T>(T source, Func<object, RTree> factory) where T : notnull =>
    ResultFactory.Create(value: TreeCache.GetValue(
        key: source,
        createValueCallback: _ => factory(source!)));
```

### ArrayPool Buffer Management

ALWAYS use ArrayPool for temporary buffers:

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static Result<IReadOnlyList<int>> ExecuteWithBuffer(int bufferSize) {
    int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
    int count = 0;
    try {
        // Perform operation filling buffer[0..count]
        return ResultFactory.Create<IReadOnlyList<int>>(
            value: count > 0 ? [.. buffer[..count]] : []);
    } finally {
        ArrayPool<int>.Shared.Return(buffer, clearArray: true);
    }
}
```

### Validation-Then-Compute Pattern

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static Result<TOut> ValidateAndCompute<TGeom>(
    object geometry,
    IGeometryContext context,
    V mode,
    Func<TGeom, Result<TOut>> compute) =>
    ResultFactory.Create(value: (TGeom)geometry)
        .Validate(args: [context, mode])
        .Bind(compute);
```

---

## FrozenDictionary Dispatch Systems

### Why FrozenDictionary?

- **O(1) lookup** - Immutable perfect hashing
- **Zero allocation** - No runtime construction overhead
- **Thread-safe** - Immutable after creation
- **Type safety** - Compile-time key validation
- **Performance** - Faster than Dictionary for read-heavy workloads

### Basic Dispatch Registry

```csharp
internal static readonly FrozenDictionary<(Type Input, Type Query), (V Mode, Func<object, object, Result<T>> Execute)> OperationRegistry =
    new Dictionary<(Type, Type), (V, Func<object, object, Result<T>>)> {
        [(typeof(Point3d[]), typeof(Sphere))] = (V.None, ExecutePointSphere),
        [(typeof(Curve), typeof(Plane))] = (V.Standard, ExecuteCurvePlane),
        [(typeof(Mesh), typeof(Ray3d))] = (V.MeshSpecific, ExecuteMeshRay),
    }.ToFrozenDictionary();
```

### Complex Configuration Dispatch

Spatial.cs example with full configuration tuple:

```csharp
internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
    new (Type Input, Type Query, Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)[] {
        (typeof(Point3d[]), typeof(Sphere), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
        (typeof(PointCloud), typeof(BoundingBox), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory)),
        (typeof(Mesh), typeof(Sphere), _meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
    }.ToFrozenDictionary(
        static entry => (entry.Input, entry.Query),
        static entry => (entry.Factory, entry.Mode, entry.BufferSize, entry.Execute));
```

### Strategy Pattern Dispatch

Analysis.cs example with validation and compute function:

```csharp
private static readonly FrozenDictionary<Type, (V Mode, Func<object, IGeometryContext, Result<IResult>> Compute)> _strategies =
    new Dictionary<Type, (V, Func<object, IGeometryContext, Result<IResult>>)> {
        [typeof(Curve)] = (Modes[typeof(Curve)], (g, ctx) => 
            ValidateAndCompute<Curve>(g, ctx, Modes[typeof(Curve)], cv => CurveLogic(cv, ctx))),
        [typeof(Surface)] = (Modes[typeof(Surface)], (g, ctx) => 
            ValidateAndCompute<Surface>(g, ctx, Modes[typeof(Surface)], sf => SurfaceLogic(sf, ctx))),
    }.ToFrozenDictionary();
```

### Handler Registry Dispatch

Extraction.cs example with type-kind mapping:

```csharp
private static readonly FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, object?, bool, IGeometryContext, Point3d[]>> _handlers =
    new Dictionary<(byte, Type), Func<GeometryBase, object?, bool, IGeometryContext, Point3d[]>> {
        [(1, typeof(Curve))] = static (g, _, _, _) => g is Curve c 
            ? [c.PointAtStart, c.PointAtNormalizedLength(0.5), c.PointAtEnd] 
            : [],
        [(2, typeof(Surface))] = static (g, _, _, _) => g is Surface s 
            ? [s.PointAt(s.Domain(0).Mid, s.Domain(1).Mid)] 
            : [],
    }.ToFrozenDictionary();
```

### Lookup with Fallback

```csharp
internal static Result<T> Execute(Type inputType, Type queryType) =>
    OperationRegistry.TryGetValue((inputType, queryType), out var config) switch {
        true => config.Execute(),
        false => ResultFactory.Create<T>(
            error: E.MyDomain.UnsupportedTypeCombo.WithContext(
                $"Input: {inputType.Name}, Query: {queryType.Name}")),
    };
```

---

## UnifiedOperation Integration

### When to Use UnifiedOperation

USE when:
- Processing collections or single items uniformly
- Need validation, diagnostics, or caching
- Want configurable error handling (accumulate vs fail-fast)
- Enable parallel execution for collections
- Need pre/post transforms or filtering

DON'T USE when:
- Trivial single-item operation
- Performance is absolutely critical and validation is unnecessary
- Direct implementation is clearer

### Basic Integration

```csharp
public static Result<IReadOnlyList<T>> Process<TInput>(
    TInput input,
    IGeometryContext context) where TInput : notnull =>
    UnifiedOperation.Apply(
        input: input,
        operation: (Func<TInput, Result<IReadOnlyList<T>>>)(item => 
            MyFolderCore.Execute(item, context)),
        config: new OperationConfig<TInput, T> {
            Context = context,
            ValidationMode = V.Standard,
        });
```

### Full Configuration

```csharp
Result<IReadOnlyList<TOut>> result = UnifiedOperation.Apply(
    input: data,
    operation: (Func<TIn, Result<IReadOnlyList<TOut>>>)(item => ProcessItem(item)),
    config: new OperationConfig<TIn, TOut> {
        Context = context,
        ValidationMode = V.Standard | V.Degeneracy,
        AccumulateErrors = true,           // Applicative error handling
        EnableParallel = false,            // Parallel for collections
        MaxDegreeOfParallelism = -1,       // Default parallelism
        SkipInvalid = false,               // Skip vs fail on invalid
        EnableCache = false,               // Memoization
        EnableDiagnostics = false,         // DEBUG instrumentation
        OperationName = "MyFolder.Op",     // Diagnostic name
        PreTransform = x => Transform(x),  // Pre-operation
        PostTransform = x => Transform(x), // Post-operation
        InputFilter = x => ShouldProcess(x),
        OutputFilter = x => ShouldKeep(x),
    });
```

### Polymorphic Dispatch via UnifiedOperation

```csharp
public static Result<IReadOnlyList<T>> Process<TInput>(
    TInput input,
    MethodSpec method,
    IGeometryContext context) where TInput : notnull =>
    UnifiedOperation.Apply(
        input,
        (Func<TInput, Result<IReadOnlyList<T>>>)(item => item switch {
            Point3d p => ComputePoint(p, method, context),
            Curve c => ComputeCurve(c, method, context),
            Surface s => ComputeSurface(s, method, context),
            _ => ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.UnsupportedAnalysis.WithContext(
                    $"Type: {item.GetType().Name}")),
        }),
        new OperationConfig<TInput, T> {
            Context = context,
            ValidationMode = V.Standard,
        });
```

---

## Validation Mode Mapping

### Validation Flag Combinations

Use bitwise OR to combine validation modes:

```csharp
V mode = V.Standard | V.Degeneracy;              // Curve operations
V mode = V.Standard | V.Topology;                // Brep operations
V mode = V.Standard | V.MeshSpecific;            // Mesh operations
V mode = V.Standard | V.UVDomain;                // Surface operations
V mode = V.Standard | V.NurbsGeometry;           // NURBS operations
```

### Available Validation Modes

```csharp
V.None                  // Skip validation
V.Standard              // IsValid check
V.AreaCentroid          // IsClosed, IsPlanar
V.BoundingBox           // GetBoundingBox
V.MassProperties        // IsSolid, IsClosed
V.Topology              // IsManifold, IsClosed, IsSolid
V.Degeneracy            // IsPeriodic, IsDegenerate, IsShort
V.Tolerance             // IsPlanar, IsLinear within tolerance
V.SelfIntersection      // SelfIntersections check
V.MeshSpecific          // Mesh-specific validations
V.SurfaceContinuity     // Continuity checks
V.NurbsGeometry         // NURBS-specific checks
V.UVDomain              // UV domain validity
V.PolycurveStructure    // Polycurve segment checks
V.ExtrusionGeometry     // Extrusion-specific checks
V.All                   // All validations combined
```

### Type-Specific Mapping

```csharp
internal static readonly FrozenDictionary<Type, V> ValidationModes =
    new Dictionary<Type, V> {
        [typeof(Curve)] = V.Standard | V.Degeneracy,
        [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
        [typeof(PolyCurve)] = V.Standard | V.Degeneracy | V.PolycurveStructure,
        [typeof(Surface)] = V.Standard | V.UVDomain,
        [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
        [typeof(Brep)] = V.Standard | V.Topology,
        [typeof(Extrusion)] = V.Standard | V.Topology | V.ExtrusionGeometry,
        [typeof(Mesh)] = V.Standard | V.MeshSpecific,
    }.ToFrozenDictionary();
```

### Checking Flags

```csharp
bool hasStandard = mode.Has(V.Standard);
bool hasDegeneracy = mode.Has(V.Degeneracy);
```

---

## Error Handling Patterns

### Using E.* Constants

ALWAYS use `E.*` error constants from `libs/core/errors/E.cs`:

```csharp
// CORRECT
return ResultFactory.Create<T>(error: E.Geometry.InvalidCount);
return ResultFactory.Create<T>(error: E.Spatial.UnsupportedTypeCombo);
return ResultFactory.Create<T>(error: E.Validation.GeometryInvalid);

// WRONG - Never construct directly
return ResultFactory.Create<T>(error: new SystemError(ErrorDomain.Geometry, 2001, "Invalid"));
```

### Adding Context to Errors

```csharp
E.Geometry.UnsupportedAnalysis.WithContext($"Type: {typeof(T).Name}")
E.Spatial.UnsupportedTypeCombo.WithContext($"Input: {typeof(TIn).Name}, Query: {typeof(TQuery).Name}")
E.Geometry.InvalidCount.WithContext($"Expected: 5, Got: {count}")
```

### Error Code Ranges

- **1000-1999**: Results system errors (`E.Results.*`)
- **2000-2999**: Geometry operation errors (`E.Geometry.*`)
- **3000-3999**: Validation errors (`E.Validation.*`)
- **4000-4999**: Spatial indexing errors (`E.Spatial.*`)
- **5000-5999**: Topology errors (`E.Topology.*`)

### Common Errors per Domain

**Geometry Errors** (`E.Geometry.*`):
```csharp
E.Geometry.InvalidCount              // Count <= 0 or out of range
E.Geometry.InvalidLength             // Length <= 0
E.Geometry.InvalidDirection          // Direction vector too short
E.Geometry.UnsupportedAnalysis       // Type not supported for operation
E.Geometry.CurveAnalysisFailed       // Curve operation failed
E.Geometry.SurfaceAnalysisFailed     // Surface operation failed
E.Geometry.InvalidExtraction         // Extraction spec invalid
```

**Spatial Errors** (`E.Spatial.*`):
```csharp
E.Spatial.UnsupportedTypeCombo       // Input/query type combination not supported
E.Spatial.ProximityFailed            // Proximity search failed
E.Spatial.InvalidK                   // k <= 0 for k-nearest
E.Spatial.InvalidDistance            // distance <= 0 for distance-limited
```

**Validation Errors** (`E.Validation.*`):
```csharp
E.Validation.GeometryInvalid         // Geometry failed validation
E.Validation.Empty                   // Collection is empty
E.Validation.InvalidRange            // Value outside acceptable range
```

---

## Memory Optimization Techniques

### 1. ConditionalWeakTable for Caching

Automatic GC-aware cache for expensive computations:

```csharp
internal static readonly ConditionalWeakTable<object, RTree> TreeCache = [];

private static Result<RTree> GetTree<T>(T source, Func<object, RTree> factory) where T : notnull =>
    ResultFactory.Create(value: TreeCache.GetValue(
        key: source,
        createValueCallback: _ => factory(source!)));
```

### 2. ArrayPool for Buffers

Zero-allocation temporary buffers:

```csharp
int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
try {
    // Use buffer[0..count]
    return [.. buffer[..count]];
} finally {
    ArrayPool<int>.Shared.Return(buffer, clearArray: true);
}
```

### 3. FrozenDictionary for Lookups

Immutable O(1) lookups compiled at startup:

```csharp
private static readonly FrozenDictionary<Type, Handler> _handlers =
    new Dictionary<Type, Handler> { /* ... */ }.ToFrozenDictionary();
```

### 4. StructLayout(LayoutKind.Auto)

Let runtime optimize struct layout:

```csharp
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct MyOptions(int value) {
    internal readonly int Value = value;
}
```

### 5. readonly struct

Immutable value types for thread safety and copy optimization:

```csharp
public readonly record struct MyOptions(
    double? Tolerance = null,
    bool Sorted = false);
```

### 6. MethodImpl(AggressiveInlining)

Force inlining for hot paths:

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static Result<T> FastOperation(...) { }
```

### 7. Static Lambdas

Avoid closure allocation:

```csharp
// GOOD - static lambda, no closure
.Select(static x => x.Value)

// BAD - captures closure
int multiplier = 5;
.Select(x => x.Value * multiplier)
```

### 8. Collection Expressions

Modern syntax with potential optimization:

```csharp
// GOOD
Point3d[] points = [p1, p2, p3,];
IReadOnlyList<int> indices = [.. buffer[..count]];

// OLD
Point3d[] points = new[] { p1, p2, p3 };
IReadOnlyList<int> indices = buffer[..count].ToArray();
```

---

## Type-Based Polymorphic Dispatch

### Pattern 1: Switch Expression on Type

```csharp
return geometry switch {
    Point3d p => ProcessPoint(p),
    Curve c => ProcessCurve(c),
    Surface s => ProcessSurface(s),
    Brep b => ProcessBrep(b),
    Mesh m => ProcessMesh(m),
    _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedAnalysis),
};
```

### Pattern 2: FrozenDictionary Lookup

```csharp
private static readonly FrozenDictionary<Type, Func<object, Result<T>>> _handlers = /* ... */;

internal static Result<T> Execute(object geometry) =>
    _handlers.TryGetValue(geometry.GetType(), out Func<object, Result<T>>? handler) switch {
        true => handler(geometry),
        false => ResultFactory.Create<T>(error: E.Geometry.UnsupportedAnalysis),
    };
```

### Pattern 3: Type Tuple Dispatch

```csharp
internal static Result<T> Execute(object input, object query) =>
    OperationRegistry.TryGetValue((input.GetType(), query.GetType()), out var config) switch {
        true => config.Execute(input, query),
        false => ResultFactory.Create<T>(error: E.Spatial.UnsupportedTypeCombo),
    };
```

### Pattern 4: Type Inheritance Fallback

```csharp
private static Handler? GetHandler(Type type) =>
    _handlers.TryGetValue(type, out Handler? exact)
        ? exact
        : _handlers
            .Where(kv => kv.Key.IsAssignableFrom(type))
            .OrderByDescending(kv => kv.Key, 
                Comparer<Type>.Create(static (a, b) => 
                    a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
            .Select(kv => kv.Value)
            .FirstOrDefault();
```

### Pattern 5: Normalized Dispatch

Handle type conversions before dispatch:

```csharp
private static Result<T> ExecuteWithNormalization(GeometryBase geometry) {
    (GeometryBase normalized, bool shouldDispose) = geometry switch {
        Extrusion ext => (ext.ToBrep(splitKinkyFaces: true), true),
        SubD sd => (sd.ToBrep(), true),
        GeometryBase g => (g, false),
    };

    try {
        return DispatchToHandler(normalized);
    } finally {
        if (shouldDispose) {
            (normalized as IDisposable)?.Dispose();
        }
    }
}
```

---

## Common Implementation Patterns

### Pattern 1: Compute Logic with Mass Properties

```csharp
private static readonly Func<Curve, IGeometryContext, Result<IResult>> CurveLogic = 
    (cv, ctx) => {
        double param = cv.Domain.Mid;
        return cv.FrameAt(param, out Plane frame) && AreaMassProperties.Compute(cv) is { } amp
            ? ResultFactory.Create(value: (IResult)new CurveData(
                cv.PointAt(param),
                cv.DerivativeAt(param, order: 2) ?? [],
                cv.CurvatureAt(param).Length,
                frame,
                amp.Centroid,
                cv.GetLength()))
            : ResultFactory.Create<IResult>(error: E.Geometry.CurveAnalysisFailed);
    };
```

### Pattern 2: Discontinuity Detection

```csharp
double[] buffer = ArrayPool<double>.Shared.Rent(MaxDiscontinuities);
try {
    (int discCount, double s) = (0, curve.Domain.Min);
    while (discCount < MaxDiscontinuities && 
           curve.GetNextDiscontinuity(Continuity.C1_continuous, s, curve.Domain.Max, out double td)) {
        buffer[discCount++] = td;
        s = td + context.AbsoluteTolerance;
    }
    double[] discontinuities = [.. buffer[..discCount]];
    // Process discontinuities...
} finally {
    ArrayPool<double>.Shared.Return(buffer, clearArray: true);
}
```

### Pattern 3: Proximity Search Dispatch

```csharp
private static Result<IReadOnlyList<int>> ExecuteProximitySearch<T>(
    T source,
    Point3d[] needles,
    object limit,
    Func<T, Point3d[], int, IEnumerable<int[]>> kNearest,
    Func<T, Point3d[], double, IEnumerable<int[]>> distLimited) where T : notnull =>
    limit switch {
        int k when k > 0 => kNearest(source, needles, k).ToArray() is int[][] results
            ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        double d when d > 0 => distLimited(source, needles, d).ToArray() is int[][] results
            ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        int => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK),
        double => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance),
        _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
    };
```

### Pattern 4: RTree Range Search

```csharp
private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, object queryShape, int bufferSize) {
    int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
    int count = 0;
    try {
        Action search = queryShape switch {
            Sphere sphere => () => tree.Search(sphere, (_, args) => { 
                if (count < buffer.Length) { buffer[count++] = args.Id; } 
            }),
            BoundingBox box => () => tree.Search(box, (_, args) => { 
                if (count < buffer.Length) { buffer[count++] = args.Id; } 
            }),
            _ => () => { },
        };
        search();
        return ResultFactory.Create<IReadOnlyList<int>>(
            value: count > 0 ? [.. buffer[..count]] : []);
    } finally {
        ArrayPool<int>.Shared.Return(buffer, clearArray: true);
    }
}
```

### Pattern 5: Mesh Overlap Detection

```csharp
private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeMeshOverlapExecutor() =>
    (i, q, c, b) => i is (Mesh m1, Mesh m2) && q is double tolerance
        ? GetTree(source: m1, factory: _meshFactory)
            .Bind(t1 => GetTree(source: m2, factory: _meshFactory)
                .Bind(t2 => ExecuteOverlapSearch(tree1: t1, tree2: t2, tolerance: c.AbsoluteTolerance + tolerance, bufferSize: b)))
        : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo);
```

---

## Complete Folder Examples

### Example 1: Spatial Indexing (Simple)

**Spatial.cs**:
```csharp
namespace Arsenal.Rhino.Spatial;

[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace")]
public static class Spatial {
    internal static readonly ConditionalWeakTable<object, RTree> TreeCache = [];

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        int? bufferSize = null,
        bool enableDiagnostics = false) where TInput : notnull where TQuery : notnull =>
        SpatialCore.OperationRegistry.TryGetValue((typeof(TInput), typeof(TQuery)), out var config) switch {
            true => UnifiedOperation.Apply(
                input: input,
                operation: (Func<TInput, Result<IReadOnlyList<int>>>)(item => 
                    config.execute(item, query, context, bufferSize ?? config.bufferSize)),
                config: new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.mode,
                    OperationName = $"Spatial.{typeof(TInput).Name}.{typeof(TQuery).Name}",
                    EnableDiagnostics = enableDiagnostics,
                }),
            false => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };
}
```

**SpatialConfig.cs**:
```csharp
namespace Arsenal.Rhino.Spatial;

internal static class SpatialConfig {
    internal const int DefaultBufferSize = 2048;
    internal const int LargeBufferSize = 4096;
}
```

**SpatialCore.cs**:
```csharp
namespace Arsenal.Rhino.Spatial;

internal static class SpatialCore {
    private static readonly Func<object, RTree> _pointArrayFactory = 
        s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree();
    private static readonly Func<object, RTree> _meshFactory = 
        s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree();

    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new (Type, Type, Func<object, RTree>?, V, int, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>>)[] {
            (typeof(Point3d[]), typeof(Sphere), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
            (typeof(Mesh), typeof(BoundingBox), _meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
        }.ToFrozenDictionary(
            static entry => (entry.Item1, entry.Item2),
            static entry => (entry.Item3, entry.Item4, entry.Item5, entry.Item6));

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeExecutor<TInput>(
        Func<object, RTree> factory) where TInput : notnull =>
        (i, q, _, b) => GetTree(source: (TInput)i, factory: factory)
            .Bind(tree => ExecuteRangeSearch(tree: tree, queryShape: q, bufferSize: b));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<RTree> GetTree<T>(T source, Func<object, RTree> factory) where T : notnull =>
        ResultFactory.Create(value: Spatial.TreeCache.GetValue(
            key: source,
            createValueCallback: _ => factory(source!)));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, object queryShape, int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            Action search = queryShape switch {
                Sphere sphere => () => tree.Search(sphere, (_, args) => { 
                    if (count < buffer.Length) { buffer[count++] = args.Id; } 
                }),
                BoundingBox box => () => tree.Search(box, (_, args) => { 
                    if (count < buffer.Length) { buffer[count++] = args.Id; } 
                }),
                _ => () => { },
            };
            search();
            return ResultFactory.Create<IReadOnlyList<int>>(
                value: count > 0 ? [.. buffer[..count]] : []);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }
}
```

### Example 2: Analysis (Complex with Custom Types)

**Analysis.cs**:
```csharp
namespace Arsenal.Rhino.Analysis;

[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace")]
public static class Analysis {
    public interface IResult {
        public Point3d Location { get; }
    }

    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record CurveData(
        Point3d Location,
        Vector3d[] Derivatives,
        double Curvature,
        Plane Frame,
        double Length,
        Point3d Centroid) : IResult {
        [Pure] 
        private string DebuggerDisplay => 
            string.Create(CultureInfo.InvariantCulture, 
                $"Curve @ {this.Location} | κ={this.Curvature:F3} | L={this.Length:F3}");
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveData> Analyze(
        Curve curve,
        IGeometryContext context,
        double? parameter = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCore.Execute(curve, context, t: parameter, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics)
            .Map(results => (CurveData)results[0]);
}
```

**AnalysisConfig.cs**:
```csharp
namespace Arsenal.Rhino.Analysis;

internal static class AnalysisConfig {
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(Surface)] = V.Standard | V.UVDomain,
        }.ToFrozenDictionary();

    internal const int DefaultDerivativeOrder = 2;
}
```

**AnalysisCore.cs**:
```csharp
namespace Arsenal.Rhino.Analysis;

internal static class AnalysisCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.IResult> ValidateAndCompute<TGeom>(
        object geometry,
        IGeometryContext context,
        V mode,
        Func<TGeom, Result<Analysis.IResult>> compute) =>
        ResultFactory.Create(value: (TGeom)geometry)
            .Validate(args: [context, mode])
            .Bind(compute);

    private static readonly Func<Curve, IGeometryContext, double?, int, Result<Analysis.IResult>> CurveLogic = 
        (cv, ctx, t, order) => {
            double param = t ?? cv.Domain.Mid;
            return cv.FrameAt(param, out Plane frame) && 
                   AreaMassProperties.Compute(cv) is { } amp
                ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.CurveData(
                    cv.PointAt(param),
                    cv.DerivativeAt(param, order) ?? [],
                    cv.CurvatureAt(param).Length,
                    frame,
                    cv.GetLength(),
                    amp.Centroid))
                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.CurveAnalysisFailed);
        };

    private static readonly FrozenDictionary<Type, V> Modes = AnalysisConfig.ValidationModes;

    private static readonly FrozenDictionary<Type, (V Mode, Func<object, IGeometryContext, double?, int, Result<Analysis.IResult>> Compute)> _strategies =
        new Dictionary<Type, (V, Func<object, IGeometryContext, double?, int, Result<Analysis.IResult>>)> {
            [typeof(Curve)] = (Modes[typeof(Curve)], 
                (g, ctx, t, order) => ValidateAndCompute<Curve>(g, ctx, Modes[typeof(Curve)], 
                    cv => CurveLogic(cv, ctx, t, order))),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> Execute(
        object geometry,
        IGeometryContext context,
        double? t,
        int derivativeOrder,
        bool enableDiagnostics = false) =>
        _strategies.TryGetValue(geometry.GetType(), out var strategy)
            ? strategy.Compute(geometry, context, t, derivativeOrder)
                .Map(r => (IReadOnlyList<Analysis.IResult>)[r])
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(
                error: E.Geometry.UnsupportedAnalysis.WithContext(geometry.GetType().Name));
}
```

---

## Pitfalls and Solutions

### Pitfall 1: Multiple Types in One File

**WRONG**:
```csharp
// MyFolder.cs
public static class MyFolder { }
public record MyData(...);  // WRONG - CA1050 violation
```

**CORRECT**:
```csharp
// MyFolder.cs
public static class MyFolder { }

// MyData.cs (separate file)
public sealed record MyData(...);
```

### Pitfall 2: Missing Trailing Commas

**WRONG**:
```csharp
new Dictionary<Type, V> {
    [typeof(Curve)] = V.Standard,
    [typeof(Surface)] = V.Standard  // WRONG - missing comma
}.ToFrozenDictionary();
```

**CORRECT**:
```csharp
new Dictionary<Type, V> {
    [typeof(Curve)] = V.Standard,
    [typeof(Surface)] = V.Standard,  // CORRECT - trailing comma
}.ToFrozenDictionary();
```

### Pitfall 3: Using if/else Instead of Expressions

**WRONG**:
```csharp
if (count > 0) {
    return ProcessItems(items);
} else {
    return ResultFactory.Create(error: E.Validation.Empty);
}
```

**CORRECT**:
```csharp
return count > 0
    ? ProcessItems(items)
    : ResultFactory.Create(error: E.Validation.Empty);
```

### Pitfall 4: Forgetting Named Parameters

**WRONG**:
```csharp
ResultFactory.Create(E.Geometry.InvalidCount)  // WRONG - unnamed error
.Ensure(predicate, E.Validation.Range)         // WRONG - unnamed error
```

**CORRECT**:
```csharp
ResultFactory.Create(error: E.Geometry.InvalidCount)  // CORRECT
.Ensure(predicate, error: E.Validation.Range)         // CORRECT
```

### Pitfall 5: Not Using ArrayPool

**WRONG**:
```csharp
int[] buffer = new int[2048];  // WRONG - allocation
// Use buffer
```

**CORRECT**:
```csharp
int[] buffer = ArrayPool<int>.Shared.Rent(2048);
try {
    // Use buffer
} finally {
    ArrayPool<int>.Shared.Return(buffer, clearArray: true);
}
```

### Pitfall 6: Direct SystemError Construction

**WRONG**:
```csharp
return ResultFactory.Create<T>(
    error: new SystemError(ErrorDomain.Geometry, 2001, "Invalid"));  // WRONG
```

**CORRECT**:
```csharp
return ResultFactory.Create<T>(error: E.Geometry.InvalidCount);  // CORRECT
```

### Pitfall 7: Not Disposing Converted Geometry

**WRONG**:
```csharp
Brep normalized = extrusion.ToBrep(splitKinkyFaces: true);
return Process(normalized);  // WRONG - memory leak
```

**CORRECT**:
```csharp
(GeometryBase normalized, bool shouldDispose) = geometry switch {
    Extrusion ext => (ext.ToBrep(splitKinkyFaces: true), true),
    GeometryBase g => (g, false),
};

try {
    return Process(normalized);
} finally {
    if (shouldDispose) {
        (normalized as IDisposable)?.Dispose();
    }
}
```

### Pitfall 8: Using var Instead of Explicit Types

**WRONG**:
```csharp
var result = ResultFactory.Create(value: point);  // WRONG
var count = GetCount();                           // WRONG
```

**CORRECT**:
```csharp
Result<Point3d> result = ResultFactory.Create(value: point);  // CORRECT
int count = GetCount();                                       // CORRECT
```

### Pitfall 9: Forgetting Validation

**WRONG**:
```csharp
public static Result<T> Process(Curve curve) =>
    ProcessCurve(curve);  // WRONG - no validation
```

**CORRECT**:
```csharp
public static Result<T> Process(Curve curve, IGeometryContext context) =>
    ResultFactory.Create(value: curve)
        .Validate(args: [context, V.Standard | V.Degeneracy])
        .Bind(c => ProcessCurve(c));
```

### Pitfall 10: Hitting File/Type Limits

**WRONG**:
```
libs/rhino/myfolder/
├── MyFolder.cs
├── MyFolderConfig.cs
├── MyFolderCore.cs
├── MyFolderHelpers.cs      // WRONG - 4 files is the MAX
├── MyFolderUtilities.cs    // WRONG - exceeds limit
```

**CORRECT**: Consolidate into 3 files or improve algorithmic density.

---

## Checklist for New Folders

### File Structure
- [ ] Three files: `MyFolder.cs`, `MyFolderConfig.cs`, `MyFolderCore.cs`
- [ ] File names match type names exactly
- [ ] One type per file (CA1050 compliance)
- [ ] File-scoped namespaces (`namespace Arsenal.Rhino.MyFolder;`)
- [ ] Proper using statements ordered alphabetically

### Main File (Public API)
- [ ] Public static class with namespace suppression attribute
- [ ] XML documentation on all public members
- [ ] Methods marked `[Pure, MethodImpl(AggressiveInlining)]`
- [ ] All methods return `Result<T>` for failable operations
- [ ] Type-specific overloads for ergonomics
- [ ] Delegates to Core or UnifiedOperation - no implementation logic
- [ ] Custom result types with `IResult` marker interface if needed
- [ ] Custom option structs with `StructLayout(LayoutKind.Auto)` if needed

### Config File
- [ ] Internal static class
- [ ] Constants at top (buffer sizes, thresholds, etc.)
- [ ] FrozenDictionary for validation modes
- [ ] FrozenDictionary for type dispatch configurations
- [ ] All multi-line collections have trailing commas
- [ ] No logic - only data structures

### Core File
- [ ] Internal static class
- [ ] Factory methods for RTree/expensive objects
- [ ] FrozenDictionary dispatch registries
- [ ] Private helper methods marked internal
- [ ] ArrayPool for temporary buffers
- [ ] ConditionalWeakTable for caching if needed
- [ ] All methods marked `internal` and `[Pure, MethodImpl(AggressiveInlining)]`
- [ ] Dispose pattern for converted geometry

### Code Quality
- [ ] No `var` usage - explicit types everywhere
- [ ] No `if`/`else` statements - use ternary/switch/pattern matching
- [ ] Named parameters for all non-obvious arguments
- [ ] Trailing commas in all multi-line collections
- [ ] K&R brace style (opening brace on same line)
- [ ] Target-typed `new()` where applicable
- [ ] Collection expressions `[]` instead of `new List<T>()`

### Architecture
- [ ] UnifiedOperation for polymorphic dispatch
- [ ] Result monad for all error handling
- [ ] E.* constants for all errors
- [ ] Validation modes via V.* flags
- [ ] ValidationRules integration for geometry validation
- [ ] No handrolled validation logic
- [ ] Type-based dispatch via FrozenDictionary

### Performance
- [ ] ArrayPool for temporary buffers
- [ ] ConditionalWeakTable for caching
- [ ] FrozenDictionary for lookups
- [ ] Static lambdas to avoid closures
- [ ] MethodImpl(AggressiveInlining) on hot paths
- [ ] readonly struct for options
- [ ] Disposal of converted geometry

### Error Handling
- [ ] Using E.* error constants
- [ ] WithContext() for additional error details
- [ ] Proper error codes within domain ranges
- [ ] Result<T> for all failable operations
- [ ] No exceptions for control flow

### Testing
- [ ] Unit tests for public API
- [ ] Property-based tests for mathematical invariants (if applicable)
- [ ] Integration tests with RhinoCommon (if needed)

### Documentation
- [ ] XML documentation on all public members
- [ ] DebuggerDisplay attributes on result types
- [ ] Clear operation descriptions
- [ ] Parameter documentation

### Build Compliance
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No analyzer violations
- [ ] Stays within file/type limits (3 files ideal, 4 max)
- [ ] Member LOC ≤ 300 lines

---

## Quick Reference

### Standard Imports

```csharp
// Main File
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

// Core File (add these)
using System.Buffers;
using System.Collections.Frozen;
```

### Standard Method Template

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<TOut> MethodName<TIn>(
    TIn input,
    IGeometryContext context,
    bool enableDiagnostics = false) where TIn : notnull =>
    UnifiedOperation.Apply(
        input: input,
        operation: (Func<TIn, Result<IReadOnlyList<TOut>>>)(item => 
            MyFolderCore.Execute(item, context)),
        config: new OperationConfig<TIn, TOut> {
            Context = context,
            ValidationMode = V.Standard,
            OperationName = "MyFolder.Op",
            EnableDiagnostics = enableDiagnostics,
        });
```

### Standard Error Return

```csharp
return ResultFactory.Create<T>(
    error: E.MyDomain.ErrorName.WithContext($"Details: {info}"));
```

### Standard Validation

```csharp
return ResultFactory.Create(value: geometry)
    .Validate(args: [context, V.Standard | V.Degeneracy])
    .Bind(g => ProcessGeometry(g));
```

---

**Remember**: Every new folder MUST follow these patterns. When in doubt, examine existing folders (spatial, analysis, extraction) as references. Consistency is non-negotiable.
