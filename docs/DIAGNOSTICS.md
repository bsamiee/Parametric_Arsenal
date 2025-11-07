# Diagnostics Infrastructure

This document describes the diagnostics and debugging infrastructure in Parametric Arsenal.

## Overview

The diagnostics system provides zero-allocation observability infrastructure with compile-time toggleable tracing. It consists of two main components:

1. **DiagnosticCapture** - Polymorphic diagnostic capture engine
2. **DebuggerDisplay** - Compile-time attributes for improved debugging experience

## DiagnosticCapture System

### Architecture

The diagnostics pipeline flows as follows:

```
UnifiedOperation.Apply() 
  → OperationConfig (EnableDiagnostics = true)
  → Result<T>.Capture() extension method
  → DiagnosticCapture stores metadata
  → ConditionalWeakTable for automatic cleanup
```

### DiagnosticContext

A readonly struct that captures:
- **Operation name** - Identifies the operation being performed
- **Elapsed time** - Execution duration in milliseconds
- **Allocations** - Bytes allocated during operation
- **Validation applied** - Which validation mode was used
- **Cache hit** - Whether result came from cache
- **Error count** - Number of errors if operation failed

```csharp
public readonly struct DiagnosticContext(
    string operation,
    TimeSpan elapsed,
    long allocations,
    V? validationApplied = null,
    bool? cacheHit = null,
    int? errorCount = null)
```

### Usage

Enable diagnostics in operation configuration:

```csharp
UnifiedOperation.Apply(
    input: data,
    operation: (Func<T, Result<R>>)MyOperation,
    config: new OperationConfig<T, R> {
        Context = context,
        OperationName = "MyOperation",
        EnableDiagnostics = true,  // ← Enable diagnostics
    });
```

Retrieve diagnostics from result:

```csharp
if (result.TryGetDiagnostics(out DiagnosticContext ctx)) {
    Console.WriteLine($"Operation: {ctx.Operation}");
    Console.WriteLine($"Time: {ctx.Elapsed.TotalMilliseconds}ms");
    Console.WriteLine($"Allocations: {ctx.Allocations} bytes");
    Console.WriteLine($"Cache: {ctx.CacheHit}");
}
```

### Compile-Time Control

Diagnostics are only active in DEBUG builds:
- `#if DEBUG` preprocessor directives eliminate all overhead in release builds
- `DiagnosticCapture.IsEnabled` property for runtime detection
- `ConditionalWeakTable` ensures no memory leaks

## DebuggerDisplay Attributes

DebuggerDisplay is a .NET attribute that customizes how types appear in debugger windows (Visual Studio, Rider, etc.). We've added it to key types for better debugging experience.

### Core Library Types

#### Result<T>
Shows success/error state without forcing evaluation:
- **Success**: `Success: 42`
- **Error**: `Error: [Validation:3000] Geometry must be valid`
- **Multiple errors**: `Errors(3): [Validation:3000] ...`
- **Deferred**: `Deferred<int>` (never forces evaluation)

```csharp
[DebuggerDisplay("{DebuggerDisplay}")]
public readonly struct Result<T> { ... }
```

#### SystemError
Shows domain, code, and message:
- Format: `[Domain:Code] Message`
- Example: `[Validation:3000] Geometry must be valid`

#### V (Validation Flags)
Shows validation mode:
- Single: `Standard`, `Topology`, `MeshSpecific`
- Combined: `Combined(17)` for bitwise combinations

#### OperationConfig<TIn, TOut>
Shows operation name and enabled features:
- Format: `Op:{name} | Val:{mode} [cached] [parallel] [diag]`
- Example: `Op:Spatial.Mesh.Sphere | Val:MeshSpecific [cached] [diag]`

### Rhino Library Types

#### CurveData
Shows location, curvature, length, discontinuities:
- Format: `Curve @ {x,y,z} | κ={curvature} | L={length} | Disc={count}`
- Example: `Curve @ {10.5,20.3,0} | κ=0.125 | L=125.3 | Disc=2`

#### SurfaceData
Shows location, Gaussian/mean curvature, area, singularities:
- Format: `Surface @ {x,y,z} | K={gaussian} | H={mean} | A={area} [singular]`
- Example: `Surface @ {0,0,0} | K=0.001 | H=0.050 | A=1250.5`

#### BrepData
Shows location, volume, area, topology flags:
- Format: `Brep @ {x,y,z} | V={volume} | A={area} [solid] [manifold]`
- Example: `Brep @ {5,5,5} | V=125.0 | A=150.0 [solid] [manifold]`

#### MeshData
Shows location, volume, area, topology flags:
- Format: `Mesh @ {x,y,z} | V={volume} | A={area} [closed] [manifold]`
- Example: `Mesh @ {0,0,0} | V=8.0 | A=24.0 [closed] [manifold]`

### Design Principles

1. **No side effects** - DebuggerDisplay never forces evaluation of deferred results
2. **InvariantCulture** - Consistent formatting regardless of locale
3. **Concise** - Show only essential information to avoid clutter
4. **Domain-specific** - Geometry types show geometry-relevant metrics
5. **Zero overhead** - Only evaluated by debugger, never in production code

## DiagnosticContext vs DebuggerDisplay

These are complementary but independent features:

| Feature | DiagnosticContext | DebuggerDisplay |
|---------|------------------|-----------------|
| **Purpose** | Runtime performance monitoring | Development-time debugging |
| **When active** | DEBUG builds only | Always (but only used by debugger) |
| **Storage** | ConditionalWeakTable | Compile-time attribute |
| **Access** | `TryGetDiagnostics()` method | Automatic in debugger |
| **Overhead** | Zero in RELEASE | Zero (debugger-only evaluation) |
| **Propagation** | Attached to Result instances | Compile-time, no propagation |

## Best Practices

### Enabling Diagnostics

Enable for performance-critical operations:
```csharp
config: new OperationConfig<T, R> {
    EnableDiagnostics = true,
    OperationName = "MyOp",  // Required for diagnostics
}
```

### Viewing in Debugger

1. Set breakpoint after operation
2. Hover over Result variable
3. See DebuggerDisplay summary
4. Expand to see full details
5. Use `TryGetDiagnostics()` for performance metrics

### Testing

See `test/core/Diagnostics/DebuggerDisplayTests.cs` for examples:
- Test DebuggerDisplay formatting
- Verify no evaluation side effects
- Validate deferred result handling

## Implementation Details

### ConditionalWeakTable

Used for diagnostic metadata storage:
- Weak references allow garbage collection
- Thread-safe dictionary semantics
- Automatic cleanup when Result is collected

### Activity Tracing

In DEBUG builds, creates Activity spans:
- Compatible with OpenTelemetry
- Includes operation name, timing, allocations
- Caller info via `[CallerMemberName]` etc.

### Memory Efficiency

- DiagnosticContext: 40-48 bytes per instance (struct)
- ConditionalWeakTable: O(n) memory for n captured results
- Automatic cleanup via weak references
- Zero overhead in RELEASE builds

## Future Enhancements

Potential improvements (not currently needed):
- [ ] Diagnostic aggregation/statistics
- [ ] Custom diagnostic providers
- [ ] Diagnostic middleware pipeline
- [ ] Performance counter integration
- [ ] Diagnostic export/serialization
