# UnifiedOperation - Parameterized Algebraic Dispatch

Dense polymorphic operation system with zero duplication. **136 lines total.**

## Architecture

**Parameterized behavior defined once, used everywhere:**

```csharp
public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
    TIn input, operation, config) {

    // Define pipeline ONCE (29 lines)
    Func<TIn, Result<IReadOnlyList<TOut>>> pipeline = item =>
        InputFilter → Validation → PreTransform → Operation → PostTransform;

    // Define accumulator ONCE (7 lines)
    Func<Result, Result, Result> accumulate = (acc, curr) =>
        ErrorStrategy dispatch;

    // Use parameterized behaviors (16 lines)
    return (input, parallel) switch {
        (empty, _) => empty,
        (single-item, _) => recurse,
        (list, true) => list.AsParallel().Select(pipeline).Aggregate(accumulate),
        (list, false) => list.Aggregate((acc, item) => accumulate(acc, pipeline(item))),
        (enumerable, _) => recurse,
        (single, _) => pipeline(input),
    };
}
```

**Key insight:** Parallel/sequential/single all use the **same pipeline and accumulator**. Zero duplication.

## Comparison

### Before (with helpers)
```
181 lines
- 6 private helper methods
- Deep call chains
- 47% helper code
```

### After (parameterized)
```
136 lines (25% reduction)
- 0 private helper methods
- 0 duplicated logic
- Local functions parameterize behavior
```

## Pattern

This follows the same pattern as `validation/`, `extraction/`, and `spatial/`:
- **Define behavior parametrically** (expression tree, lambda, config)
- **Dispatch to parameterized behavior** (pattern match, dictionary lookup)
- **Zero duplication** (behavior defined once, referenced many times)

## Usage

```csharp
// Basic
var result = UnifiedOperation.Apply(geometries, ExtractPoints, config);

// With validation
var config = OperationConfig<GeometryBase, Point3d>.WithValidation(
    context, ValidationMode.Standard);

// Parallel
var config = OperationConfig<GeometryBase, Point3d>.Parallel(
    context, maxDegreeOfParallelism: 8);

// Traverse (single → single)
var result = UnifiedOperation.Traverse(geometries, ExtractCentroid, config);

// Compose (multiple operations)
var ops = new[] { ExtractStart, ExtractEnd, ExtractMid };
var result = UnifiedOperation.Compose(geometry, ops, config);
```

## Methods

| Method | Purpose |
|--------|---------|
| `Apply` | Standard single/batch execution |
| `ApplyDeferred` | Validation happens inside operation |
| `ApplyFlat` | Flatten nested Result<Result<T>> |
| `Traverse` | Single-to-single mapping |
| `Compose` | Multiple operations with error accumulation |
| `ApplyWhen` | Conditional execution |
| `ApplyCached` | Memoization |

All methods are expression-bodied one-liners that call `Apply` with lambda adapters.

## Error Strategies

```csharp
ErrorStrategy.FailFast       // Stop on first error
ErrorStrategy.AccumulateAll  // Collect all errors
ErrorStrategy.SkipFailed     // Continue with valid results
```

## Philosophy

**Algebraic:** Respects functor/monad/applicative laws
**Parameterized:** Behavior defined once as lambdas
**Polymorphic:** Handles single/batch/enumerable uniformly
**Dense:** 136 LOC, zero boilerplate, zero duplication
