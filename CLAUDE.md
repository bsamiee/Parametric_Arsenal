# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## CRITICAL: Code Density Requirements

**MANDATORY**: Super dense algebraic code only. Study these exemplars:

- `libs/core/validation/ValidationRules.cs` - Expression tree compilation, zero allocations
- `libs/core/results/ResultFactory.cs` - Polymorphic parameter detection patterns
- `libs/core/operations/UnifiedOperation.cs` - 90 lines handling all dispatch logic

### Strict Rules

- **NO if/else** - Pattern matching and switch expressions only
- **NO var** - Explicit types always
- **NO helpers/extracting** - Improve logic instead (300 LOC hard limit)
- **NO old patterns** - Target-typed new, collection expressions, tuple deconstruction
- **K&R brace style** - Opening braces on same line
- **Always leverage libs/** - Never handroll what Result monad provides
- **ALWAYS named parameters** - Use `parameter: value` for all non-obvious arguments (errors, values, configs)
- **ALWAYS target-typed new** - Use `new(...)` not `new Type(...)` when type is known from context

### Common Mistakes (ELIMINATE)

```csharp
// ❌ Missing trailing commas
["key"] = 1]  // Wrong
["key"] = 1,] // Correct

// ❌ Unnamed parameters  
ResultFactory.Create(error)                          // Wrong
ResultFactory.Create(error: ValidationErrors.X)      // Correct

// ❌ Redundant type in new
new Dictionary<K, V>()                               // Wrong
new()                                                // Correct
```

### Dense Code Patterns

**UnifiedOperation Pattern** - Always use for polymorphic dispatch:

```csharp
UnifiedOperation.Apply(
    input,
    (Func<object, Result<IReadOnlyList<T>>>)(item => item switch {
        GeometryBase g => Strategies.Process(g, method, context),
        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Validation.GeometryInvalid),
    }),
    new OperationConfig<object, T> { Context = context, ValidationMode = V.None })
```

**FrozenDictionary Configuration** - Compile-time lookups with trailing commas:

```csharp
private static readonly FrozenDictionary<(SpatialMethod, Type), (V Mode, Func<object, RTree?>? TreeFactory)> _config =
    new Dictionary<(SpatialMethod, Type), (V, Func<object, RTree?>?)> {
        [(SpatialMethod.PointsRange, typeof(Point3d[]))] = (V.Standard, s => RTree.CreateFromPointArray((Point3d[])s)),
        [(SpatialMethod.MeshOverlap, typeof(ValueTuple<Mesh, Mesh>))] = (V.MeshSpecific, null),  // ✅ Trailing comma
    }.ToFrozenDictionary();
```

**Inline tuple operations** - Never extract:

```csharp
// Cache with inline tree construction
s => _treeCache.GetValue(s, _ => { RTree t = new(); _ = ((Curve[])s).Select((c, i) => (t.Insert(c.GetBoundingBox(true), i), 0).Item2).ToArray(); return t; })
```

## Build and Development Commands

```bash
dotnet build                                      # Build entire solution
dotnet build libs/core/Core.csproj               # Build specific project
dotnet test                                       # Run all tests
dotnet test --filter "FullyQualifiedName~Result" # Run specific test
```

## Core Architecture

### Result Monad (ALWAYS USE)

**CRITICAL**: Always use named parameters for ResultFactory methods:

```csharp
Result<T>                                      // Lazy evaluation, monadic composition
ResultFactory.Create(value: x)                 // ✅ Named parameter, never new Result
ResultFactory.Create(error: err)               // ✅ Named parameter for errors
ResultFactory.Create(errors: [err1, err2,])    // ✅ Named + trailing comma
.Map(x => transform)                           // Functor transform
.Bind(x => Result<Y>)                          // Monadic chain
.Ensure(predicate, error: E.Validation.X)      // ✅ Validation with named error parameter
.Match(onSuccess, onFailure)                   // Pattern match exhaustive
.Tap(onSuccess, onFailure)                     // Side effects, preserves state
.Apply(Result<Func>)                           // Applicative parallel validation
.OnError((Func<SystemError[], T>)recover)      // ✅ Explicit overload for recovery
.OnError((Func<SystemError[], SystemError[]>)map) // ✅ Explicit overload for error mapping
.OnError((Func<SystemError[], Result<T>>)recovWith) // ✅ Explicit overload for monadic recovery
.Traverse(transform)                           // Collection traversal with Result
```

### Polymorphic Patterns

```csharp
// Parameter detection through pattern matching
(value, errors, deferred, nested) switch {
    (var v, null, null, null) when v is not null => Create(v),
    (null, var e, null, null) when e?.Length > 0 => Create(e),
    (null, null, var d, null) when d is not null => Create(deferred: d),
    _ => throw new ArgumentException()
}
```

### Expression Tree Compilation

ValidationRules compiles validators at runtime using expression trees - never handwrite validation logic.

### Error System (E.cs Registry)

**CRITICAL**: Use E.Category.Error pattern for all errors. Domain computed from code ranges.

```csharp
// ✅ Use E.Category.Error pattern
return ResultFactory.Create<T>(error: E.Results.NoValueProvided);
return ResultFactory.Create<T>(error: E.Geometry.InvalidExtraction);
return ResultFactory.Create<T>(error: E.Validation.GeometryInvalid);
return ResultFactory.Create<T>(error: E.Spatial.InvalidK);

// ✅ Add context when needed
return ResultFactory.Create<T>(error: E.Get(code: 1001, context: "MyMethod"));

// ❌ Never create SystemError directly
return ResultFactory.Create<T>(error: new SystemError(...));  // DON'T

// Error Code Ranges (domain computed automatically):
// 1000-1999: E.Results.*
// 2000-2999: E.Geometry.* (extraction, intersection, analysis)
// 3000-3999: E.Validation.*
// 4000-4099: E.Spatial.*
```

### Validation Modes (V Struct)

**CRITICAL**: Use V struct with bitwise operations for validation modes.

```csharp
// ✅ Combine with | operator
V mode = V.Standard | V.Topology | V.Degeneracy;

// ✅ Check with Has method
if (mode.Has(flag: V.Standard)) { ... }

// ✅ Compare with ==
if (mode == V.None) { ... }

// ✅ Pass to operations
config: new OperationConfig<TIn, TOut> {
    Context = context,
    ValidationMode = V.Standard | V.Topology,
}

// ❌ Don't use is pattern for struct comparison
if (mode is V.None) { ... }  // WRONG
```

### Legacy Error Pattern (Backward Compatibility)

**Note**: ValidationErrors, ResultErrors, and specific error files (ExtractionErrors, IntersectionErrors, etc.) are maintained as aliases for backward compatibility during transition. New code should use E.* pattern.

### Analyzers Enforced

- IDE0301-0305: Collection expressions required
- IDE0290: Primary constructors required
- File-scoped namespaces mandatory

### Key Techniques

- **ConditionalWeakTable** for auto-memory managed caching
- **ArrayPool<T>** for zero-allocation buffers
- **Expression.Compile()** for runtime validator generation
- **FrozenDictionary** for compile-time lookups
- **ValueTuple patterns** for multi-value dispatch

### Platform

- .NET 8.0, C# preview, Rhino 8 SDK
- xUnit + CsCheck (core), NUnit + Rhino.Testing (rhino)
- Artifacts to /artifacts/
