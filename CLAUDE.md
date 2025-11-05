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

### Dense Code Patterns

**UnifiedOperation Pattern** - Always use for polymorphic dispatch:
```csharp
UnifiedOperation.Apply(
    input,
    (Func<object, Result<IReadOnlyList<T>>>)(item => item switch {
        GeometryBase g => Strategies.Process(g, method, context),
        _ => ResultFactory.Create<IReadOnlyList<T>>(error: ValidationErrors.Geometry.Invalid),
    }),
    new OperationConfig<object, T> { Context = context, ValidationMode = ValidationMode.None })
```

**FrozenDictionary Configuration** - Compile-time lookups:
```csharp
private static readonly FrozenDictionary<(SpatialMethod, Type), (ValidationMode Mode, Func<object, RTree?>? TreeFactory)> _config =
    new Dictionary<(SpatialMethod, Type), (ValidationMode, Func<object, RTree?>?)> {
        [(SpatialMethod.PointsRange, typeof(Point3d[]))] = (ValidationMode.Standard, s => RTree.CreateFromPointArray((Point3d[])s)),
        [(SpatialMethod.MeshOverlap, typeof(ValueTuple<Mesh, Mesh>))] = (ValidationMode.MeshSpecific, null),
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
```csharp
Result<T> // Lazy evaluation, monadic composition
ResultFactory.Create(value: x) // Never new Result
.Map(x => transform)           // Functor transform
.Bind(x => Result<Y>)          // Monadic chain
.Apply(Result<Func>)           // Applicative parallel
.Filter(predicate, error)      // Validation
.OnError(recover: x => value)  // Recovery
.Traverse(transform)           // Collection inside Result
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

### Error Pattern (Each folder has own errors)
```csharp
// libs/core/validation/ValidationErrors.cs
public static class ValidationErrors {
    public static class Geometry {
        public static readonly SystemError Invalid = new(ErrorDomain.Validation, 3001, "...");
    }
}

// libs/rhino/spatial/SpatialErrors.cs - Folder-specific errors
public static class SpatialErrors {
    public static class Parameters {
        public static readonly SystemError InvalidCount = new(ErrorDomain.Geometry, 2221, "...");
    }
}
```

### Analyzers Enforced
- MA0051: Method length max 60 lines
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