---
name: review-csharp
description: Review C# code against CLAUDE.md standards
---

Review the specified code against Parametric Arsenal coding standards.

## MANDATORY CHECKS (analyzer-enforced, build failures)

### Syntax Violations
- `var` declarations → MUST use explicit types (IDE0007-0009)
- `if`/`else` statements → MUST use ternary, switch expressions, pattern matching
- `new List<T>()` or `new Dictionary<K,V>()` → MUST use collection expressions `[]` (IDE0300-0305)
- Missing trailing commas in multi-line collections
- `namespace X {` blocks → MUST use file-scoped `namespace X;` (IDE0001)
- Allman braces `\n{` → MUST use K&R `{` on same line (IDE0055)

### Architecture Violations
- `throw new Exception` → MUST return `Result<T>` with `E.*` errors
- Unnamed parameters → MUST use `parameter: value` for non-obvious args
- Multiple types in file → ONE type per file (CA1050)
- Helper method extraction → Inline or improve algorithm (300 LOC max)

### Organization Violations
- Method >300 LOC → Improve algorithm density
- >4 files per folder → Consolidate
- >10 types per folder → Restructure

## CORRECT PATTERNS

```csharp
// ✅ Result monad for errors
return count > 0
    ? ProcessItems(items)
    : ResultFactory.Create(error: E.Validation.Empty);

// ✅ Switch expression for multiple branches
return value switch {
    Point3d p => ProcessPoint(p),
    Curve c => ProcessCurve(c),
    _ => ResultFactory.Create(error: E.Geometry.UnsupportedType),
};

// ✅ Named parameters, trailing commas
ResultFactory.Create(errors: [
    E.Validation.GeometryInvalid,
    E.Validation.InputNull,
]);

// ✅ UnifiedOperation for polymorphic dispatch
UnifiedOperation.Apply(
    input: data,
    operation: ProcessItem,
    config: new OperationConfig<TIn, TOut> {
        Context = context,
        ValidationMode = V.Standard,
    });
```

## OUTPUT FORMAT

For each violation found:
1. File and line number
2. Current code snippet
3. Required fix
4. Reference to CLAUDE.md section

Conclude with: APPROVE if no violations, REQUEST_CHANGES if any found.
