# C# Coding Standards for Parametric Arsenal

**Purpose**: This document defines the mandatory coding standards, architectural patterns, and exemplar code for C# development in the Parametric Arsenal repository. These standards are enforced by build analyzers and are non-negotiable.

**Audience**: Claude AI, working via claude.ai/code

**Last Updated**: 2025-11-07

## CRITICAL: Code Density and Style Requirements

**MANDATORY**: Write super-dense, algebraic code only. Study these exemplars before editing ANY code:

- `libs/core/validation/ValidationRules.cs` - Expression tree compilation, zero allocations
- `libs/core/results/ResultFactory.cs` - Polymorphic parameter detection with complex pattern matching
- `libs/core/operations/UnifiedOperation.cs` - 108 lines handling complete polymorphic dispatch engine
- `libs/core/results/Result.cs` - Monadic composition with lazy evaluation
- `libs/rhino/spatial/Spatial.cs` - FrozenDictionary dispatch with algorithmic density

### Absolute Rules (ZERO TOLERANCE)

These rules are **enforced by analyzers** and violations **fail the build**:

1. **NO `if`/`else` STATEMENTS** - Use expressions that return values: ternary operators, switch expressions, pattern matching, or boolean composition. **Note**: `if` without `else` for guard clauses is acceptable when it returns/throws immediately.
2. **NO `var` EVER** - Explicit types always (`int`, `string`, `Result<T>`, etc.)
3. **NO helper methods or "Extract Method" refactoring** - Improve the logic algorithmically instead (hard 300 LOC limit per member)
4. **NO old C# patterns** - Use target-typed `new()`, collection expressions `[]`, tuple deconstruction
5. **K&R brace style ALWAYS** - Opening braces on same line: `void Foo() {` not `void Foo()\n{`
6. **ALWAYS leverage `libs/` primitives** - Never handroll what Result monad, UnifiedOperation, or ValidationRules provide
7. **ALWAYS use named parameters** - For ALL non-obvious arguments: `ResultFactory.Create(error: E.X)` not `ResultFactory.Create(E.X)`
8. **ALWAYS use target-typed new** - `new()` not `new Type()` when type is known from context
9. **ALWAYS include trailing commas** - Every multi-line array/collection/dictionary literal MUST end with `,`
10. **ONE type per file** - Never put multiple top-level types in same file (analyzer CA1050 enforces this)

### Organizational Limits (STRICTLY ENFORCED)

These limits force identification of better, denser members instead of low-quality code sprawl:

**ABSOLUTE MAXIMUMS** (violations are unacceptable):
- **4 files maximum** per folder (implementation or feature folder)
- **10 types maximum** per folder (across all files in that folder)
- **300 LOC maximum** per member (method, property, etc.)

**IDEAL TARGETS** (aim for these ranges):
- **2-3 files** per folder (preferred over 1 mega-file or hitting the 4-file limit)
- **6-8 types** per folder (sweet spot for maintainability and cohesion)
- **150-250 LOC** per member (dense but readable algorithms)

**PURPOSE**: These limits encourage dense, algorithmically sophisticated code. Every type must justify its existence. Every member must be valuable. If you hit the limits, you need better algorithms or better consolidation, not more files or extracted helpers.

### Common Critical Mistakes (FIX IMMEDIATELY)

```csharp
// ❌ WRONG - Missing trailing commas
[error1, error2]                                 // WRONG
new Dictionary<K, V> { ["key"] = value }         // WRONG

// ✅ CORRECT - Always trailing commas
[error1, error2,]                                // CORRECT
new Dictionary<K, V> { ["key"] = value, }        // CORRECT

// ❌ WRONG - Unnamed parameters
ResultFactory.Create(error)                      // WRONG
.Ensure(pred, err)                               // WRONG
new SystemError(domain, 1001, "msg")             // WRONG

// ✅ CORRECT - Named parameters for non-obvious args
ResultFactory.Create(error: E.Validation.X)      // CORRECT
.Ensure(pred, error: E.Validation.Y)             // CORRECT
new SystemError(Domain: domain, Code: 1001, Message: "msg")  // CORRECT when ambiguous

// ❌ WRONG - Redundant type in new
new Dictionary<string, int>()                    // WRONG
new List<Point3d> { p1, p2 }                    // WRONG

// ✅ CORRECT - Target-typed new
new()                                            // CORRECT
[p1, p2,]                                        // CORRECT

// ❌ WRONG - Multiple types in one file
// File: SystemError.cs
public record SystemError(...);
public enum ErrorDomain { ... }                  // WRONG - Move to ErrorDomain.cs

// ✅ CORRECT - One type per file
// File: SystemError.cs
public record SystemError(...);
// File: ErrorDomain.cs  
public enum ErrorDomain { ... }                  // CORRECT
```

### Algorithmic Density Patterns

**Pattern 1: Polymorphic Dispatch via UnifiedOperation**

Always use UnifiedOperation for operations on multiple input types/configurations:

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
                error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {item.GetType().Name}")),
        }),
        new OperationConfig<TInput, T> {
            Context = context,
            ValidationMode = V.Standard,
            EnableDiagnostics = false,
        });
```

**Pattern 2: FrozenDictionary Configuration**

Use FrozenDictionaries for compile-time constant lookups with trailing commas:

```csharp
private static readonly FrozenDictionary<(MethodType Method, Type Input), (V Mode, int Buffer)> _config =
    new Dictionary<(MethodType, Type), (V, int)> {
        [(MethodType.Analysis, typeof(Curve))] = (V.Degeneracy, 1024),
        [(MethodType.Analysis, typeof(Surface))] = (V.BoundingBox, 2048),
        [(MethodType.Spatial, typeof(Point3d[]))] = (V.None, 4096),  // ✅ Trailing comma required
    }.ToFrozenDictionary();
```

**Pattern 3: Complex Tuple Pattern Matching**

Use tuple patterns for polymorphic parameter detection:

```csharp
public static Result<T> Create<T>(
    T? value = default,
    SystemError[]? errors = null,
    SystemError? error = null,
    Func<Result<T>>? deferred = null) =>
    (value, errors, error, deferred) switch {
        (var v, null, null, null) when v is not null => 
            new Result<T>(isSuccess: true, v, [], deferred: null),
        (_, var e, null, null) when e?.Length > 0 => 
            new Result<T>(isSuccess: false, default!, e, deferred: null),
        (_, null, var e, null) when e.HasValue => 
            new Result<T>(isSuccess: false, default!, [e.Value,], deferred: null),
        (_, null, null, var d) when d is not null => 
            new Result<T>(isSuccess: false, default!, [], deferred: d),
        _ => throw new ArgumentException(E.Results.InvalidCreate.Message),
    };
```

**Pattern 4: Inline Complex Expressions**

Never extract helper methods - inline complex operations:

```csharp
// ✅ CORRECT - Dense inline expression
private static readonly ConditionalWeakTable<object, RTree> _cache = [];
private static RTree GetTree(Curve[] curves) =>
    _cache.GetValue(curves, static c => {
        RTree tree = new();
        _ = c.Select((curve, i) => (tree.Insert(curve.GetBoundingBox(accurate: true), i), 0).Item2)
            .ToArray();
        return tree;
    });

// ❌ WRONG - Don't extract helpers
private static RTree GetTree(Curve[] curves) => _cache.GetValue(curves, BuildTree);
private static RTree BuildTree(Curve[] curves) { ... }  // WRONG - No helpers
```

**Pattern 5: Expression Tree Compilation**

Use expression trees for runtime code generation (see ValidationRules.cs):

```csharp
private static Func<object, IGeometryContext, SystemError[]> CompileValidator(Type type, V mode) {
    ParameterExpression geometry = Expression.Parameter(typeof(object), "g");
    ParameterExpression context = Expression.Parameter(typeof(IGeometryContext), "c");
    
    Expression[] validations = [.. GetValidationRules(type, mode)
        .Select(rule => Expression.Condition(
            BuildPredicate(geometry, context, rule),
            Expression.Constant(rule.Error),
            Expression.Constant(null, typeof(SystemError?))))];
    
    return Expression.Lambda<Func<object, IGeometryContext, SystemError[]>>(
        Expression.Call(
            _toArrayMethod,
            Expression.Call(_whereMethod, Expression.NewArrayInit(typeof(SystemError?), validations),
                Expression.Lambda<Func<SystemError?, bool>>(
                    Expression.NotEqual(error, Expression.Constant(null)), error))),
        geometry, context).Compile();
}
```

## Build and Development Commands

```bash
dotnet restore                                    # Restore NuGet packages
dotnet build                                      # Build entire solution
dotnet build libs/core/Core.csproj               # Build specific project
dotnet test                                       # Run all tests
dotnet test --filter "FullyQualifiedName~Result" # Run specific test
dotnet clean                                      # Clean build artifacts

# All development is C# only - no Python tooling required
```

## Core Architecture

### Result Monad (ALWAYS USE)

**The Result monad is the foundation of all error handling.** Never use exceptions for control flow.

```csharp
// Core operations - ALL require named parameters for non-obvious args
Result<T>                                          // Lazy evaluation, monadic composition
ResultFactory.Create(value: x)                     // ✅ Success with value
ResultFactory.Create(error: E.Validation.X)        // ✅ Single error
ResultFactory.Create(errors: [e1, e2,])            // ✅ Multiple errors with trailing comma
ResultFactory.Create(deferred: () => Compute())    // Lazy evaluation

// Functor operations
.Map(x => transform(x))                            // Transform success value
.Map<TOut>(x => (TOut)transform(x))                // Explicit type for ambiguous cases

// Monad operations  
.Bind(x => Result<Y>)                              // Monadic chain (flatMap)
.Bind(x => ComputeResult(x, context))              // Chain with context

// Validation
.Ensure(predicate, error: E.Validation.X)          // Single validation with named error
.Ensure([(pred1, E.X), (pred2, E.Y),])            // Multiple validations with trailing comma
.Validate(args: [context, V.Standard,])            // Polymorphic validation with named args

// Pattern matching
.Match(
    onSuccess: value => ProcessValue(value),
    onFailure: errors => HandleErrors(errors))

// Side effects (preserves Result state)
.Tap(
    onSuccess: value => Log(value),
    onFailure: errors => LogErrors(errors))

// Applicative functor (parallel validation)
.Apply(Result<Func<T, TOut>>)                      // Apply function in Result context
.Accumulate(Result<T>)                             // Accumulate errors from multiple Results

// Error recovery
.OnError((Func<SystemError[], T>)recover)          // Recover with value
.OnError((Func<SystemError[], SystemError[]>)map)  // Map errors
.OnError((Func<SystemError[], Result<T>>)bind)     // Monadic error recovery

// Collection operations
.Traverse(item => Result<TOut>)                    // Traverse collection with Result
.TraverseElements(item => Result<TOut>)            // Traverse IEnumerable
```

### UnifiedOperation Pattern

**Use for ALL polymorphic operations.** Never handroll operation dispatch logic.

```csharp
Result<IReadOnlyList<TOut>> result = UnifiedOperation.Apply(
    input: data,
    operation: (Func<TIn, Result<IReadOnlyList<TOut>>>)ProcessItem,
    config: new OperationConfig<TIn, TOut> {
        Context = context,                          // Required: IGeometryContext
        ValidationMode = V.Standard,                // Validation mode (V.None to skip)
        AccumulateErrors = true,                    // Applicative vs monadic error handling
        EnableParallel = false,                     // Parallel execution for collections
        MaxDegreeOfParallelism = -1,               // -1 = default parallelism
        SkipInvalid = false,                        // Skip invalid inputs vs fail
        EnableCache = false,                        // Memoization caching
        EnableDiagnostics = false,                  // Instrumentation in DEBUG
        OperationName = "MyOperation",              // Name for diagnostics
        PreTransform = x => Transform(x),           // Pre-operation transform
        PostTransform = x => Transform(x),          // Post-operation transform
        InputFilter = x => ShouldProcess(x),        // Input filtering predicate
        OutputFilter = x => ShouldKeep(x),          // Output filtering predicate
    });
```

### Validation System

**Use ValidationRules for geometry validation.** Never handwrite validators.

```csharp
// V (ValidationMode) is a bitwise flag enum
V mode = V.Standard | V.Degeneracy;                 // Combine flags
bool hasFlag = mode.Has(V.Standard);                // Check flag

// Validation modes (combine with |)
V.None                                              // No validation
V.Standard                                          // IsValid check
V.AreaCentroid                                      // IsClosed, IsPlanar
V.BoundingBox                                       // GetBoundingBox
V.MassProperties                                    // IsSolid, IsClosed
V.Topology                                          // IsManifold, IsClosed, IsSolid
V.Degeneracy                                        // IsPeriodic, IsDegenerate, IsShort
V.Tolerance                                         // IsPlanar, IsLinear within tolerance
V.SelfIntersection                                  // SelfIntersections check
V.MeshSpecific                                      // Mesh-specific validations
V.SurfaceContinuity                                 // Continuity checks
V.All                                               // All validations combined

// ValidationRules compiles expression trees at runtime
Func<object, IGeometryContext, SystemError[]> validator = 
    ValidationRules.GetOrCompileValidator(typeof(Curve), V.Standard | V.Degeneracy);
SystemError[] errors = validator(curve, context);
```

### Error Management System

**Centralized error registry in `libs/core/errors/E.cs`**

Each domain has error codes:
- **1000-1999**: Results system errors (`E.Results.*`)
- **2000-2999**: Geometry operation errors (`E.Geometry.*`)
- **3000-3999**: Validation errors (`E.Validation.*`)
- **4000-4999**: Spatial indexing errors (`E.Spatial.*`)

```csharp
// Use E.* constants - never create SystemError directly
SystemError error = E.Validation.GeometryInvalid;
SystemError withContext = E.Geometry.InvalidCount.WithContext("Expected: 5");

// Error structure
public readonly record struct SystemError(
    ErrorDomain Domain,    // Categorization: Results, Geometry, Validation, Spatial
    int Code,              // Unique code within domain
    string Message);       // Human-readable message
```

**CRITICAL FILE ORGANIZATION**: Each type MUST be in its own file:
- `libs/core/errors/SystemError.cs` - SystemError record
- `libs/core/errors/ErrorDomain.cs` - ErrorDomain enum
- `libs/core/errors/E.cs` - Error constants registry

### Key Optimization Techniques

1. **ConditionalWeakTable<TKey, TValue>** - Automatic cache with GC-aware weak references
2. **ArrayPool<T>.Shared** - Zero-allocation buffer pooling for temporary arrays
3. **Expression.Compile()** - Runtime code generation for validators
4. **FrozenDictionary<K, V>** - Immutable O(1) lookups compiled at startup
5. **ValueTuple patterns** - Stack-allocated multi-value dispatch
6. **StructLayout(LayoutKind.Auto)** - Let runtime optimize struct layout
7. **MethodImpl(AggressiveInlining)** - Force inlining for hot paths
8. **readonly struct** - Immutable value types for thread safety
9. **Primary constructors** - Compact record/struct initialization
10. **File-scoped namespaces** - Reduce indentation and improve readability

### Analyzers Enforced (Build Failures)

These are **automatically enforced** - violations fail the build:

- **IDE0290**: Primary constructors required for records/classes
- **IDE0300-0305**: Collection expressions required (`[]` not `new List<T>()`)
- **IDE0007-0009**: Explicit types required (no `var`)
- **IDE0048**: Pattern matching required over `is`/`as`
- **CA1050**: One type per file (never multiple top-level types)
- **IDE0001**: File-scoped namespaces mandatory
- **IDE0055**: K&R brace style enforced

### Platform and Dependencies

- **.NET 8.0** with C# preview features enabled
- **RhinoCommon 8.24+** for geometry operations
- **Testing**: xUnit + CsCheck (libs/core), NUnit + Rhino.Testing (libs/rhino)
- **Build artifacts**: `/artifacts/[ProjectName]/debug/` (lowercase debug)
- **EditorConfig**: Enforces code style, indentation, naming conventions

---

## 📐 Architectural Philosophy and Rationale

### Why Result Monad?
**Problem**: Exceptions are expensive, unpredictable, and break functional composition.
**Solution**: Result<T> provides:
- **Explicit error handling** - Errors are part of the type signature
- **Lazy evaluation** - Defer computation until needed
- **Functional composition** - Map, Bind, Ensure chain seamlessly
- **Parallel validation** - Apply accumulates errors applicatively
- **Zero exceptions** - Control flow is explicit and predictable

### Why Pattern Matching Over if/else?
**Problem**: if/else creates branching complexity and is hard to reason about.
**Solution**: Pattern matching provides:
- **Exhaustiveness checking** - Compiler ensures all cases handled
- **Expression-based** - Returns values, enabling functional composition
- **Type discrimination** - Built-in type checking with `is`
- **Deconstruction** - Tuple and record patterns simplify complex conditions
- **Performance** - JIT optimizes pattern matching better than branching

### Why No `var`?
**Problem**: Type inference obscures intent and makes refactoring dangerous.
**Solution**: Explicit types provide:
- **Readability** - Intent is clear at declaration site
- **Maintainability** - Refactoring doesn't silently change semantics
- **Documentation** - Types are self-documenting contracts
- **Tool support** - Better IntelliSense and static analysis

### Why 300 LOC Hard Limit?
**Problem**: Long methods accumulate complexity and hide design flaws.
**Solution**: Hard limit forces:
- **Algorithmic thinking** - Improve the algorithm, don't hide complexity
- **Density** - Every line must earn its place
- **Composability** - Use libs/ primitives instead of handrolling
- **Functional style** - Chain operations rather than procedural steps

### Why Named Parameters?
**Problem**: Positional arguments become ambiguous and error-prone.
**Solution**: Named parameters provide:
- **Self-documentation** - Purpose is clear at call site
- **Refactoring safety** - Parameter reordering doesn't break calls
- **Disambiguation** - Multiple bool/int parameters are unambiguous
- **Intentional code** - Forces developer to think about meaning

### Why Trailing Commas?
**Problem**: Diffs are noisy when adding items, merge conflicts occur.
**Solution**: Trailing commas provide:
- **Clean diffs** - Adding an item is a one-line change
- **Fewer merge conflicts** - Last line doesn't change when adding items
- **Consistency** - All multi-line collections follow same pattern
- **Industry standard** - Common in modern languages (Rust, TypeScript, Python)

### Why UnifiedOperation?
**Problem**: Operation dispatch is repetitive and error-prone.
**Solution**: UnifiedOperation provides:
- **Single point of control** - Validation, caching, diagnostics in one place
- **Polymorphic dispatch** - Handles single items and collections uniformly
- **Configurable behavior** - Error handling, parallelism, filtering all configurable
- **Instrumentation** - Built-in diagnostics and performance tracking
- **Testing** - Single code path to test, not N implementations

### Why Expression Trees for Validation?
**Problem**: Handwritten validation is repetitive and slow.
**Solution**: Expression tree compilation provides:
- **Zero allocations** - Compiled validators are as fast as handwritten
- **Type safety** - Compilation ensures properties/methods exist
- **Centralized rules** - All validation logic in one place
- **Runtime adaptability** - Validators compile for actual runtime types
- **Composability** - Validation modes combine with bitwise flags

---

## 🗂️ File Organization Guidelines

### One Type Per File (CA1050)
**WHY**: Prevents analyzer violations and improves discoverability.

```
✅ CORRECT:
libs/core/errors/
├── E.cs              # Error registry with nested classes
├── SystemError.cs    # SystemError record
└── ErrorDomain.cs    # ErrorDomain enum

❌ WRONG:
libs/core/errors/
└── Errors.cs         # Contains E, SystemError, AND ErrorDomain (CA1050 violation!)
```

**Exception**: Nested types within a parent type are allowed (like `E.Validation`, `E.Geometry`).

### File-Scoped Namespaces
**WHY**: Reduces indentation, enforced by IDE0001.

```csharp
// ✅ CORRECT
namespace Arsenal.Core.Results;

public static class ResultFactory {
    // Implementation
}

// ❌ WRONG
namespace Arsenal.Core.Results {
    public static class ResultFactory {
        // Implementation
    }
}
```

### Naming Conventions
- **File name matches type name**: `ResultFactory.cs` contains `ResultFactory`
- **Folder matches namespace segment**: `libs/core/results/` → `Arsenal.Core.Results`
- **One domain per folder**: `libs/rhino/spatial/`, `libs/core/validation/`

---

## 🧪 Testing Patterns

### Property-Based Testing (xUnit + CsCheck)
Used in `test/core/` for pure functions and mathematical properties:

```csharp
[Fact]
public void Result_Map_Identity_Law() =>
    Gen.Int.Sample(x => {
        Result<int> result = ResultFactory.Create(value: x);
        Result<int> mapped = result.Map(v => v);
        Assert.Equal(result, mapped);
    });

[Fact]
public void Result_Bind_Associativity_Law() =>
    Gen.Int.Sample(x => {
        Result<int> result = ResultFactory.Create(value: x);
        Func<int, Result<int>> f = v => ResultFactory.Create(value: v + 1);
        Func<int, Result<int>> g = v => ResultFactory.Create(value: v * 2);
        
        Result<int> left = result.Bind(f).Bind(g);
        Result<int> right = result.Bind(v => f(v).Bind(g));
        
        Assert.Equal(left, right);
    });
```

### Integration Testing (NUnit + Rhino.Testing)
Used in `test/rhino/` for geometry operations requiring RhinoCommon:

```csharp
[Test]
public void Spatial_PointCloud_SphereQuery_ReturnsIndices() {
    // Arrange
    Point3d[] points = [
        new Point3d(0, 0, 0),
        new Point3d(1, 0, 0),
        new Point3d(10, 10, 10),
    ];
    PointCloud cloud = new(points);
    Sphere query = new(new Point3d(0, 0, 0), radius: 2.0);
    IGeometryContext context = new GeometryContext();
    
    // Act
    Result<IReadOnlyList<int>> result = Spatial.Analyze(cloud, query, context);
    
    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value.Count, Is.EqualTo(2));  // Points 0 and 1 are within sphere
}
```

---

## 🔍 Code Review Checklist

Before finalizing any code change, verify:

### Syntax and Style
- [ ] No `var` usage anywhere
- [ ] No `if`/`else` statements (pattern matching only)
- [ ] All non-obvious parameters are named
- [ ] All multi-line collections have trailing commas
- [ ] K&R brace style (opening brace on same line)
- [ ] File-scoped namespaces (`namespace X;`)
- [ ] Target-typed `new()` where applicable
- [ ] Collection expressions `[]` instead of `new List<T>()`

### Architecture
- [ ] Operations return `Result<T>` for failable operations
- [ ] Polymorphic operations use `UnifiedOperation.Apply()`
- [ ] Errors use `E.*` constants, never direct `new SystemError()`
- [ ] Validation uses `V.*` flags and Result.Validate()
- [ ] No handrolled validation logic (use ValidationRules)

### Organization
- [ ] One type per file (CA1050)
- [ ] File name matches type name
- [ ] Folder structure matches namespace
- [ ] Using statements outside namespace
- [ ] Primary constructors for records/classes

### Quality
- [ ] Method length ≤ 300 LOC
- [ ] No helper methods (inline or improve algorithm)
- [ ] Immutability where possible (`readonly`, `record`)
- [ ] Performance optimizations (ArrayPool, ConditionalWeakTable, FrozenDictionary)
- [ ] Pure functions marked with `[Pure]` attribute
- [ ] Hot paths marked with `[MethodImpl(AggressiveInlining)]`

### Testing
- [ ] Unit tests added for new public APIs
- [ ] Property-based tests for mathematical invariants
- [ ] Integration tests for RhinoCommon operations
- [ ] All tests pass locally
- [ ] No skipped or ignored tests without reason

### Build
- [ ] `dotnet build` succeeds with zero warnings
- [ ] `dotnet test` all tests pass
- [ ] No analyzer warnings or suppressions
- [ ] Artifacts build to correct location

---

## 🚦 When to Use What

### Result<T> vs Direct Return
```csharp
// Use Result<T> when:
// - Operation can fail (validation, computation, I/O)
// - Need to chain operations monadically
// - Want to accumulate errors
public static Result<Point3d> Centroid(Curve curve, IGeometryContext context) { ... }

// Use direct return when:
// - Operation cannot fail (pure computation, lookup)
// - Performance critical and failure is impossible
public static int Count(this IReadOnlyList<Point3d> points) => points.Count;
```

### UnifiedOperation vs Direct Implementation
```csharp
// Use UnifiedOperation when:
// - Polymorphic over input types
// - Need validation, caching, diagnostics
// - Processing collections or single items
// - Configuration is needed (parallel, error handling, etc.)
public static Result<IReadOnlyList<T>> Process(...) =>
    UnifiedOperation.Apply(...);

// Use direct implementation when:
// - Single, specific type
// - Trivial operation (lookup, conversion)
// - Performance is absolutely critical
private static Point3d Transform(Point3d p, Transform xform) => xform * p;
```

### Conditional Expression Hierarchy (Most Important)

**THE RULE**: Never use `if`/`else` STATEMENTS. Always use EXPRESSIONS that return values.

```csharp
// ❌ WRONG - if/else STATEMENTS (forbidden)
if (count > 0) {
    return ProcessItems(items);
} else {
    return ResultFactory.Create(error: E.Validation.Empty);
}

// ✅ CORRECT - Ternary operator (simple binary choice)
return count > 0
    ? ProcessItems(items)
    : ResultFactory.Create(error: E.Validation.Empty);

// ✅ CORRECT - Switch expression (multiple branches)
return count switch {
    0 => ResultFactory.Create(error: E.Validation.Empty),
    1 => ProcessSingle(items[0]),
    _ => ProcessMultiple(items),
};

// ✅ CORRECT - Pattern matching with switch (type discrimination)
return value switch {
    Point3d p => ProcessPoint(p),
    Curve c => ProcessCurve(c),
    Surface s => ProcessSurface(s),
    _ => ResultFactory.Create(error: E.Geometry.UnsupportedType),
};

// ✅ CORRECT - Boolean expression composition (guards)
return value is Point3d p && p.IsValid
    ? Process(p)
    : ResultFactory.Create(error: E.Validation.Invalid);

// ✅ CORRECT - Nested ternary for complex conditions (acceptable when readable)
return result.IsSuccess
    ? result.Value.Count > 0
        ? ProcessNonEmpty(result.Value)
        : ProcessEmpty()
    : HandleError(result.Errors);
```

**Decision Tree**:
1. **Simple binary (true/false)** → Ternary operator (`condition ? true : false`)
2. **Multiple distinct cases** → Switch expression (`value switch { ... }`)
3. **Type discrimination** → Pattern matching with switch (`value switch { Type t => ..., _ => ... }`)
4. **Complex boolean logic** → Ternary with composed predicates or switch on tuple
5. **Never** → `if`/`else` statements

### Pattern Matching vs Switch Expression
```csharp
// Use inline pattern matching when:
// - Single branch with early return
// - Guard clause pattern
return value is not Point3d p || !p.IsValid
    ? ResultFactory.Create(error: E.Validation.Invalid)
    : Process(p);

// Use switch expression when:
// - Multiple cases
// - Exhaustive matching
// - Returning different values per case
return value switch {
    Point3d p => ProcessPoint(p),
    Curve c => ProcessCurve(c),
    _ => Default,
};
```

---

## 📖 Additional Resources

### Internal Documentation
- `/CLAUDE.md` - This file (comprehensive standards)
- `/.github/copilot-instructions.md` - Quick reference for GitHub Copilot
- `/AGENTS.md` - Task-oriented guide for ChatGPT/Codex agents
- `/.editorconfig` - Enforced style rules
- `/Directory.Build.props` - Build configuration and analyzers

### External References
- [C# Pattern Matching](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/pattern-matching)
- [Expression Trees](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/expression-trees/)
- [RhinoCommon API](https://developer.rhino3d.com/api/RhinoCommon/)
- [Railway Oriented Programming](https://fsharpforfunandprofit.com/rop/) - Conceptual foundation for Result monad

---

## 🎓 Learning Progression

### Stage 1: Understanding Result Monad (Required)
1. Read `libs/core/results/Result.cs` - Understand the structure
2. Read `libs/core/results/ResultFactory.cs` - Learn creation patterns
3. Practice chaining: Map → Bind → Ensure → Match
4. Understand lazy evaluation with `deferred` parameter

### Stage 2: Mastering Patterns (Required)
1. Study `libs/core/operations/UnifiedOperation.cs` - Dispatch engine
2. Study `libs/core/validation/ValidationRules.cs` - Expression trees
3. Review `libs/rhino/spatial/Spatial.cs` - Real-world usage
4. Practice: Write operation using UnifiedOperation pattern

### Stage 3: Advanced Techniques (Recommended)
1. Learn FrozenDictionary for constant lookups
2. Understand ConditionalWeakTable for caching
3. Study ArrayPool for zero-allocation buffers
4. Master expression tree compilation for validators

### Stage 4: Contribution Ready (Required)
1. Can write operations using UnifiedOperation
2. Can add new validation modes to ValidationRules
3. Can add new error codes to E registry
4. Understand when to use Result<T> vs direct return
5. Can write property-based tests with CsCheck

---

**Remember**: This is a living document. As patterns evolve, update this file. Every line of code should exemplify these standards. Quality is not negotiable.
