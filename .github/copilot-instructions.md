# GitHub Copilot Instructions for Parametric Arsenal

**Context**: .NET 8.0 monorepo with C# libraries for Rhino 8/Grasshopper parametric design + Python scripting. Extremely strict code quality standards enforced by analyzers.

**Primary Reference**: For detailed patterns and examples, see `/CLAUDE.md`

---

## 🚫 IMMEDIATE BLOCKERS (Fix Before Proceeding)

These violations fail the build. Check for and fix immediately:

1. ❌ **Multiple types in one file** → Split into separate files (CA1050)
2. ❌ **Missing trailing commas** in multi-line collections → Add `,` at end
3. ❌ **Unnamed parameters** in non-obvious calls → Add `parameter: value`
4. ❌ **Using `var`** → Replace with explicit type
5. ❌ **Using `if`/`else` statements** → Replace with ternary (binary), switch expression (multiple), or pattern matching (type discrimination)
6. ❌ **Redundant `new Type()`** → Use target-typed `new()`
7. ❌ **Old collection syntax** → Use collection expressions `[]`
8. ❌ **Folder has >4 files** → Consolidate into 2-3 files
9. ❌ **Folder has >10 types** → Consolidate into 6-8 types
10. ❌ **Member has >300 LOC** → Improve algorithm, don't extract helpers

---

## 📐 Organizational Limits (STRICTLY ENFORCED)

These limits force better, denser code:

**ABSOLUTE MAXIMUMS** (violations are unacceptable):
- **4 files maximum** per folder
- **10 types maximum** per folder
- **300 LOC maximum** per member

**IDEAL TARGETS** (aim for these):
- **2-3 files** per folder (preferred)
- **6-8 types** per folder (optimal)
- **150-250 LOC** per member (dense but readable)

**PURPOSE**: Force identification of better members instead of low-quality sprawl. Every type must justify existence. Every line must be algorithmically valuable.

---

## 📋 Quick Reference: Mandatory Patterns

### Result Monad (All Error Handling)

```csharp
// Creating Results - use named parameters
ResultFactory.Create(value: x)                 // Success
ResultFactory.Create(error: E.Validation.X)    // Single error
ResultFactory.Create(errors: [e1, e2,])        // Multiple errors + trailing comma

// Chaining operations
result
    .Map(x => Transform(x))                    // Transform value
    .Bind(x => ComputeResult(x))               // Chain operations
    .Ensure(pred, error: E.Validation.Y)       // Validate (named error)
    .Match(onSuccess: v => Use(v), onFailure: e => Handle(e))
```

### UnifiedOperation (Polymorphic Dispatch)

```csharp
UnifiedOperation.Apply(
    input: data,
    operation: (Func<TIn, Result<IReadOnlyList<TOut>>>)(item => item switch {
        Point3d p => Process(p),
        Curve c => Process(c),
        _ => ResultFactory.Create<IReadOnlyList<TOut>>(
            error: E.Geometry.UnsupportedAnalysis),
    }),
    config: new OperationConfig<TIn, TOut> {
        Context = context,
        ValidationMode = V.Standard,
        AccumulateErrors = false,
    });
```

### FrozenDictionary Configuration

```csharp
private static readonly FrozenDictionary<Key, Value> _config =
    new Dictionary<Key, Value> {
        [key1] = value1,
        [key2] = value2,  // ✅ Trailing comma required
    }.ToFrozenDictionary();
```

### Validation Modes (Bitwise Flags)

```csharp
V.None | V.Standard | V.Degeneracy           // Combine with |
V.All                                         // All validations
mode.Has(V.Standard)                          // Check flag
```

---

## 🎯 Core Principles

### 1. Code Density
- **Study exemplars** before writing code:
  - `libs/core/validation/ValidationRules.cs` - Expression trees
  - `libs/core/results/ResultFactory.cs` - Polymorphic patterns
  - `libs/core/operations/UnifiedOperation.cs` - Dispatch engine
- **300 LOC hard limit** per member - improve logic, don't extract helpers
- **Inline complex expressions** - no convenience methods

### 2. Conditional Expressions (Not Statements)
```csharp
// ✅ CORRECT - Ternary for binary choice
return count > 0
    ? ProcessItems(items)
    : ResultFactory.Create(error: E.Validation.Empty);

// ✅ CORRECT - Switch expression for multiple branches
return value switch {
    null => ResultFactory.Create(error: E.X),
    var v when v.IsValid => ResultFactory.Create(value: v),
    _ => ResultFactory.Create(error: E.Y),
};

// ✅ CORRECT - Pattern matching for type discrimination
return geometry switch {
    Point3d p => ProcessPoint(p),
    Curve c => ProcessCurve(c),
    Surface s => ProcessSurface(s),
    _ => ResultFactory.Create(error: E.Geometry.Unsupported),
};

// ❌ WRONG - Never use if/else statements
if (value == null) return ResultFactory.Create(error: E.X);
else if (value.IsValid) return ResultFactory.Create(value: value);
else return ResultFactory.Create(error: E.Y);
```

### 3. Explicit Types Always
```csharp
// ✅ CORRECT
int count = GetCount();
Result<Point3d> result = ResultFactory.Create(value: point);
Dictionary<string, int> dict = new();

// ❌ WRONG
var count = GetCount();
var result = ResultFactory.Create(value: point);
var dict = new Dictionary<string, int>();
```

### 4. Target-Typed New
```csharp
// ✅ CORRECT - Type on left, target-typed new on right
Dictionary<string, int> dict = new();
List<Point3d> points = [];
SystemError error = new(domain, code, message);

// ❌ WRONG - Redundant type
Dictionary<string, int> dict = new Dictionary<string, int>();
List<Point3d> points = new List<Point3d>();
```

### 5. Named Parameters for Clarity
```csharp
// ✅ CORRECT - Non-obvious parameters are named
ResultFactory.Create(error: E.Validation.Invalid)
.Ensure(predicate, error: E.Validation.Range)
.Validate(args: [context, V.Standard,])

// ❌ WRONG - Ambiguous unnamed parameters
ResultFactory.Create(E.Validation.Invalid)
.Ensure(predicate, E.Validation.Range)
.Validate([context, V.Standard])
```

### 6. Trailing Commas Required
```csharp
// ✅ CORRECT - All multi-line collections end with comma
[
    item1,
    item2,
    item3,
]

new Dictionary<K, V> {
    [key1] = value1,
    [key2] = value2,
}

// ❌ WRONG - Missing trailing comma
[item1, item2, item3]
new Dictionary<K, V> { [key1] = value1, [key2] = value2 }
```

---

## 📁 Project Structure

```
libs/
├── core/              # Result monad, validation, operations, errors
│   ├── results/       # Result<T>, ResultFactory
│   ├── validation/    # V (flags), ValidationRules (expression trees)
│   ├── operations/    # UnifiedOperation, OperationConfig
│   ├── errors/        # E (registry), SystemError, ErrorDomain
│   └── context/       # IGeometryContext, GeometryContext
├── rhino/             # RhinoCommon geometry operations
│   ├── spatial/       # Spatial indexing with RTree
│   ├── extraction/    # Point extraction
│   ├── intersection/  # Intersection algorithms
│   └── analysis/      # Geometric analysis
└── grasshopper/       # Grasshopper components

test/
├── core/              # xUnit + CsCheck property tests
├── rhino/             # NUnit + Rhino.Testing
└── shared/            # Shared test utilities
```

---

## 🔨 Build Commands

```bash
dotnet restore                              # Restore packages
dotnet build                                # Build solution
dotnet test                                 # Run all tests
dotnet test --filter "Name~Result"          # Run specific tests
dotnet clean                                # Clean artifacts

# Python (from root)
uv run ruff format .                        # Format Python
uv run ruff check .                         # Lint Python
uv run mypy .                               # Type check
```

---

## 🏗️ Architecture Patterns

### Error Management
- **All errors in** `libs/core/errors/E.cs`
- **Code ranges**: 1000-1999 (Results), 2000-2999 (Geometry), 3000-3999 (Validation), 4000-4999 (Spatial)
- **Usage**: `E.Validation.GeometryInvalid`, `E.Geometry.InvalidCount.WithContext("msg")`
- **Never construct directly**: Use `E.*` constants only

### Validation System
- **ValidationRules** compiles expression trees at runtime
- **Don't call directly** - used by `Result.Validate()` and `UnifiedOperation`
- **Validation modes**: Combine with `|` operator: `V.Standard | V.Degeneracy`

### Operation Configuration
- **UnifiedOperation** for all polymorphic operations
- **OperationConfig<TIn, TOut>** controls validation, parallelism, error handling
- **Never handroll** dispatch logic - use UnifiedOperation

---

## 🎓 Learning Resources

1. **Study exemplar files first** (see Core Principles section)
2. **Read `/CLAUDE.md`** for detailed patterns and examples
3. **Check `.editorconfig`** for enforced style rules
4. **Review `Directory.Build.props`** for analyzer configuration

---

## ⚠️ Common Pitfalls

1. **Don't create helper methods** - Improve the algorithm instead
2. **Don't use exceptions for control flow** - Use Result<T>
3. **Don't skip validation** - Use V.None explicitly if skipping
4. **Don't mix validation modes** - Be intentional about V.* flags
5. **Don't put multiple types in one file** - CA1050 enforces this
6. **Don't forget trailing commas** - Required for all multi-line collections
7. **Don't use unnamed parameters** - Name all non-obvious arguments

---

## 🐍 Python Standards (Brief)

- **Type annotations required** on all functions
- **Ruff formatting** via `uv run ruff format`
- **Rhino interop**: Use `Rhino.*` types correctly (they're .NET assemblies)
- **Module structure**: Keep Rhino imports separate from standard library

---

## 🚀 Quick Start Checklist

Before writing any C# code:

- [ ] Read `/CLAUDE.md` thoroughly
- [ ] Study the 5 exemplar files listed above
- [ ] Understand Result<T> monad pattern
- [ ] Understand UnifiedOperation dispatch pattern
- [ ] Know the validation flags (V.*)
- [ ] Know the error registry (E.*)
- [ ] Verify you understand: no var, no if/else, named params, trailing commas
- [ ] Run `dotnet build` to ensure clean starting state

---

**Remember**: These standards are enforced by build analyzers. Violations = build failures. When in doubt, check exemplar files and `/CLAUDE.md`.
