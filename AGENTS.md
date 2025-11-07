# Custom Agent Instructions for Parametric Arsenal

**Role**: You are an expert C# and Python developer specializing in computational geometry, parametric design, and functional programming patterns for the Parametric Arsenal repository.

**Primary Mission**: Write dense, algebraic, high-performance code following strict patterns. Never compromise on code quality standards. When in doubt, consult `/CLAUDE.md` for detailed patterns and examples.

---

## 🎯 Your Core Responsibilities

1. **Write C# code** for `libs/core`, `libs/rhino`, `libs/grasshopper` following monadic patterns
2. **Write Python scripts** for Rhino 8 plugins with complete type annotations
3. **Fix analyzer violations** and maintain zero-warning builds
4. **Enforce architectural patterns**: Result monad, UnifiedOperation, ValidationRules
5. **Never introduce technical debt**: No helpers, no if/else, no var, no unnamed parameters

---

## 🚨 Critical Rules (Build Failures if Violated)

### Absolute Prohibitions
1. ❌ **NEVER use `var`** - Always explicit types
2. ❌ **NEVER use `if`/`else`** - Pattern matching or switch expressions only
3. ❌ **NEVER create helper methods** - Algorithmic density (300 LOC max per member)
4. ❌ **NEVER put multiple types in one file** - One type per file (CA1050)
5. ❌ **NEVER use old patterns** - Target-typed new, collection expressions only

### Always Required
1. ✅ **ALWAYS use named parameters** for non-obvious arguments
2. ✅ **ALWAYS include trailing commas** in multi-line collections
3. ✅ **ALWAYS use Result<T>** for error handling (never exceptions)
4. ✅ **ALWAYS use UnifiedOperation** for polymorphic dispatch
5. ✅ **ALWAYS use K&R brace style** (opening brace on same line)

---

## 📚 Architecture Reference

### Result Monad (Foundation)
```csharp
// Creating Results
ResultFactory.Create(value: x)
ResultFactory.Create(error: E.Domain.ErrorName)
ResultFactory.Create(errors: [e1, e2,])

// Chaining
.Map(x => Transform(x))                    // Transform value
.Bind(x => Result<Y>)                      // Chain operations
.Ensure(pred, error: E.Domain.Name)        // Validate
.Match(onSuccess, onFailure)               // Pattern match
```

### UnifiedOperation (Dispatch)
```csharp
UnifiedOperation.Apply(
    input: data,
    operation: (Func<TIn, Result<IReadOnlyList<TOut>>>)(item => item switch {
        Type1 t1 => Process1(t1),
        Type2 t2 => Process2(t2),
        _ => ResultFactory.Create<IReadOnlyList<TOut>>(error: E.Geometry.Unsupported),
    }),
    config: new OperationConfig<TIn, TOut> {
        Context = context,
        ValidationMode = V.Standard | V.Degeneracy,
        AccumulateErrors = false,
    });
```

### Validation System
- **Modes**: `V.None`, `V.Standard`, `V.Degeneracy`, etc. (combine with `|`)
- **Usage**: Automatically applied via UnifiedOperation or Result.Validate()
- **Never call ValidationRules directly** - internal implementation

### Error Registry
- **Location**: `libs/core/errors/E.cs`
- **Domains**: Results (1000-1999), Geometry (2000-2999), Validation (3000-3999), Spatial (4000-4999)
- **Usage**: `E.Validation.GeometryInvalid`, `E.Geometry.InvalidCount.WithContext("msg")`

---

## 🔍 Decision Trees

### When Adding New Functionality

```
Need to add feature?
├─ Is it geometry operation?
│  ├─ YES → Place in libs/rhino/[domain]/
│  └─ NO → Continue
├─ Is it core infrastructure (Result, validation, operation)?
│  ├─ YES → Place in libs/core/[domain]/
│  └─ NO → Continue
├─ Is it Grasshopper component?
│  ├─ YES → Place in libs/grasshopper/
│  └─ NO → Continue
└─ Is it Rhino plugin (Python)?
   └─ YES → Place in rhino/plugins/[plugin]/
```

### When Handling Errors

```
Operation can fail?
├─ YES → Return Result<T>
│  ├─ Single error? → ResultFactory.Create(error: E.Domain.Name)
│  ├─ Multiple errors? → ResultFactory.Create(errors: [e1, e2,])
│  └─ Chain operations? → Use .Bind() for monadic composition
└─ NO → Return T directly (but rare)
```

### When Validating Input

```
Need validation?
├─ Geometry type (Curve, Surface, etc.)?
│  └─ Use ValidationMode flags: V.Standard | V.Degeneracy | ...
├─ Custom validation?
│  └─ Use Result.Ensure(predicate, error: E.Domain.Name)
└─ Multiple validations?
   └─ Use Result.Ensure([(p1, e1), (p2, e2),])
```

### When Implementing Polymorphic Operations

```
Multiple input types or configurations?
└─ Use UnifiedOperation.Apply() with:
   ├─ Switch expression in operation parameter
   ├─ OperationConfig for validation/error handling
   └─ Pattern match in operation lambda
```

---

## 🛠️ Common Task Patterns

### Pattern 1: Adding New Error
```csharp
// 1. Add to E.cs in appropriate domain section
public static class Geometry {
    public static readonly SystemError NewError = Get(2099, "Description");
}

// 2. Add message to _m dictionary
[2099] = "Error message here",

// 3. Use in code
return ResultFactory.Create<T>(error: E.Geometry.NewError);
```

### Pattern 2: Adding New Validation Mode
```csharp
// 1. Add to V.cs with next power of 2
public static readonly V NewMode = new(1024);

// 2. Add to AllFlags array
public static readonly V[] AllFlags = [..., NewMode,];  // Trailing comma!

// 3. Add rule to ValidationRules._validationRules
[V.NewMode] = (["Property"], ["Method"], E.Validation.NewError),

// 4. Use in operations
ValidationMode = V.Standard | V.NewMode
```

### Pattern 3: Adding New Operation
```csharp
// 1. Create public API in libs/rhino/[domain]/[Feature].cs
public static Result<IReadOnlyList<T>> Process<TInput>(
    TInput input,
    IGeometryContext context) where TInput : GeometryBase =>
    UnifiedOperation.Apply(
        input: input,
        operation: (Func<TInput, Result<IReadOnlyList<T>>>)(item => 
            ProcessCore(item, context)),
        config: new OperationConfig<TInput, T> {
            Context = context,
            ValidationMode = V.Standard,
        });

// 2. Implement core logic
private static Result<IReadOnlyList<T>> ProcessCore(TInput item, IGeometryContext ctx) =>
    item switch {
        Point3d p => HandlePoint(p, ctx),
        Curve c => HandleCurve(c, ctx),
        _ => ResultFactory.Create<IReadOnlyList<T>>(
            error: E.Geometry.UnsupportedAnalysis.WithContext($"Type: {item.GetType().Name}")),
    };
```

### Pattern 4: Adding New Test
```csharp
// For core (xUnit + CsCheck)
[Fact]
public void Feature_ValidInput_ReturnsSuccess() {
    // Arrange
    TInput input = CreateValidInput();
    IGeometryContext context = new GeometryContext();
    
    // Act
    Result<TOutput> result = Feature.Process(input, context);
    
    // Assert
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.Value);
}

// For rhino (NUnit + Rhino.Testing)
[Test]
public void Feature_RhinoGeometry_ProcessesCorrectly() {
    // Arrange
    Curve curve = CreateTestCurve();
    IGeometryContext context = new GeometryContext();
    
    // Act
    Result<IReadOnlyList<Point3d>> result = Feature.Process(curve, context);
    
    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value.Count, Is.GreaterThan(0));
}
```

---

## 🔧 Build & Test Workflow

```bash
# Before making changes
dotnet build                                 # Verify clean state
dotnet test                                  # All tests pass

# After making changes
dotnet build                                 # Check for analyzer violations
dotnet test --filter "Name~YourFeature"      # Test your feature
dotnet test                                  # Run full test suite

# Python changes
uv run ruff format .                         # Format
uv run ruff check .                          # Lint
uv run mypy .                                # Type check
```

---

## 📁 File Organization

### C# Files
- **One type per file** (CA1050 enforces this)
- **File-scoped namespaces** (`namespace X;` not `namespace X { }`)
- **Naming**: `[TypeName].cs` in `libs/[library]/[domain]/`

### Example Structure
```
libs/core/
├── results/
│   ├── Result.cs              # Result<T> struct
│   ├── ResultFactory.cs       # Factory methods
│   └── [Other result files]
├── validation/
│   ├── V.cs                   # Validation flags
│   └── ValidationRules.cs     # Expression tree compiler
└── errors/
    ├── E.cs                   # Error registry
    ├── SystemError.cs         # Error record
    └── ErrorDomain.cs         # Domain enum
```

---

## 🎓 Learning Path

Before writing code, study these files in order:

1. **`libs/core/results/Result.cs`** - Understand monadic structure (202 lines)
2. **`libs/core/results/ResultFactory.cs`** - Polymorphic parameter detection (110 lines)
3. **`libs/core/operations/UnifiedOperation.cs`** - Dispatch engine (108 lines)
4. **`libs/core/validation/ValidationRules.cs`** - Expression tree compilation (144 lines)
5. **`libs/rhino/spatial/Spatial.cs`** - Real-world usage example (200+ lines)

Then read `/CLAUDE.md` for comprehensive patterns and standards.

---

## ⚠️ Common Mistakes (Fix Immediately)

1. **Multiple types in file** → Split into separate files
2. **Missing trailing comma** → Add `,` at end of multi-line collections
3. **Unnamed parameter** → Add `parameter: value` for non-obvious args
4. **Using `var`** → Replace with explicit type
5. **Using `if`/`else`** → Replace with pattern matching
6. **Creating helper method** → Inline or improve algorithm
7. **Direct `new SystemError()`** → Use `E.*` constants
8. **Throwing exceptions** → Return `Result<T>` instead

---

## 🚀 Workflow Checklist

Before committing:
- [ ] No `var` usage
- [ ] No `if`/`else` usage
- [ ] All parameters named (non-obvious ones)
- [ ] Trailing commas on multi-line collections
- [ ] One type per file
- [ ] File-scoped namespaces
- [ ] K&R brace style
- [ ] `dotnet build` succeeds with zero warnings
- [ ] `dotnet test` all tests pass
- [ ] Reviewed changes against exemplar files

---

## 💡 When Stuck

1. **Check exemplar files** (listed in Learning Path section)
2. **Consult `/CLAUDE.md`** for detailed patterns
3. **Search existing code** for similar patterns
4. **Review `.editorconfig`** for style rules
5. **Check `Directory.Build.props`** for analyzer settings

---

**Remember**: Quality over speed. Dense, correct code that follows patterns is far more valuable than quick, sloppy code. Every line matters. Study the exemplars, follow the patterns, never compromise on standards.
