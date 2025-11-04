# UnifiedOperation - Advanced Polymorphic Operation Execution

Dense, algebraically-sound operation system with single/batch unification, monadic composition, and advanced error handling.

## Core Capabilities

### 1. **Single/Batch Polymorphism**
Automatically handles:
- Single items: `GeometryBase`
- Collections: `IReadOnlyList<GeometryBase>`
- Enumerables: `IEnumerable<GeometryBase>`
- Empty collections (optimized)
- Single-item collections (unwrapped)

### 2. **Error Strategies**
```csharp
ErrorStrategy.FailFast       // Stop on first error (default)
ErrorStrategy.AccumulateAll  // Collect all errors
ErrorStrategy.SkipFailed     // Continue with valid results only
```

### 3. **Validation Integration**
```csharp
ValidationMode.Standard | ValidationMode.MassProperties
```
- Pre-operation validation with bitwise composition
- Deferred validation (inside operation)
- Skip invalid items option

### 4. **Transformation Pipeline**
```csharp
config.PreTransform   // TIn → Result<TIn>
config.PostTransform  // TOut → Result<TOut>
```
- Pre-operation transformation
- Post-operation transformation
- Monadic error propagation

### 5. **Filtering**
```csharp
config.InputFilter   // TIn → bool
config.OutputFilter  // TOut → bool
```
- Input filtering (before operation)
- Output filtering (after operation)
- Combines with SkipInvalid

### 6. **Parallel Execution**
```csharp
config.EnableParallel = true
config.MaxDegreeOfParallelism = Environment.ProcessorCount
```
- Automatic parallel dispatch
- Configurable degree of parallelism
- Thread-safe error accumulation

### 7. **Operation Caching**
```csharp
config.EnableCache = true
UnifiedOperation.ApplyCached(input, operation, config)
```
- Memoization for expensive operations
- ConcurrentDictionary-based
- Per-input caching

### 8. **Monadic Composition**

#### Apply (standard)
```csharp
UnifiedOperation.Apply(input, operation, config)
// TIn → Result<IReadOnlyList<TOut>>
```

#### Traverse (single-to-single)
```csharp
UnifiedOperation.Traverse(input, operation, config)
// TIn → Result<TOut> → Result<IReadOnlyList<TOut>>
```

#### Compose (multiple operations)
```csharp
UnifiedOperation.Compose(input, operations, config)
// TIn → [TIn → Result<TOut>] → Result<IReadOnlyList<TOut>>
```

#### ApplyWhen (conditional)
```csharp
UnifiedOperation.ApplyWhen(input, predicate, operation, config)
// TIn → (TIn → bool) → Result<IReadOnlyList<TOut>>
```

#### ApplyDeferred (validation inside)
```csharp
UnifiedOperation.ApplyDeferred(input, operation, config)
// TIn → (TIn, ValidationMode) → Result<IReadOnlyList<TOut>>
```

#### ApplyFlat (nested results)
```csharp
UnifiedOperation.ApplyFlat(input, operation, config)
// TIn → Result<Result<IReadOnlyList<TOut>>> → Result<IReadOnlyList<TOut>>
```

## Comparison: Before vs After

### Before (Manual Pattern Matching)
```csharp
public static Result<IReadOnlyList<Point3d>> Extract<T>(
    T input,
    ExtractionMethod method,
    IGeometryContext context) where T : notnull {

    return input switch {
        GeometryBase single => ResultFactory.Create(value: single)
            .Validate(args: [context, ValidationMode.Standard])
            .Bind(g => ExtractCore(g, method, context)),

        IReadOnlyList<GeometryBase> list when list.Count == 0 =>
            ResultFactory.Create(value: (IReadOnlyList<Point3d>)[]),

        IReadOnlyList<GeometryBase> list when list.Count == 1 =>
            Extract(list[0], method, context),

        IReadOnlyList<GeometryBase> list => list
            .Select(g => Extract(g, method, context))
            .Aggregate(
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[]),
                (acc, curr) => acc.Bind(a => curr.Map(c => [..a, ..c]))),

        IEnumerable<GeometryBase> enumerable =>
            Extract(enumerable.ToArray(), method, context),

        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ValidationErrors.Invalid),
    };
}
```

### After (Dense UnifiedOperation)
```csharp
public static Result<IReadOnlyList<Point3d>> Extract<T>(
    T input,
    ExtractionMethod method,
    IGeometryContext context) where T : notnull =>
    UnifiedOperation.Apply(
        input,
        geometry => ResultFactory.Create(value: geometry)
            .Validate(args: [context, ValidationMode.Standard])
            .Bind(g => ExtractCore(g, method, context)),
        OperationConfig<GeometryBase, Point3d>.Default(context));
```

**Reduction:** 25 lines → 7 lines (72% less code)

## Advanced Patterns

### Pattern 1: Resilient Batch Processing
```csharp
var config = new OperationConfig<GeometryBase, Point3d> {
    Context = context,
    ValidationMode = ValidationMode.Standard,
    ErrorStrategy = ErrorStrategy.SkipFailed,
    SkipInvalid = true,
    OutputFilter = pt => pt.IsValid,
};

// Processes all items, skips failures, returns only valid points
var result = UnifiedOperation.Apply(geometries, ExtractPoints, config);
```

### Pattern 2: Parallel with Error Accumulation
```csharp
var config = OperationConfig<GeometryBase, Point3d>.Parallel(context);
config = config with { ErrorStrategy = ErrorStrategy.AccumulateAll };

// Processes in parallel, collects ALL errors from all threads
var result = UnifiedOperation.Apply(geometries, ExtractPoints, config);

// Access all accumulated errors
if (!result.IsSuccess) {
    foreach (var error in result.Errors) {
        Console.WriteLine($"{error.Domain}: {error.Message}");
    }
}
```

### Pattern 3: Transformation Pipeline
```csharp
var config = OperationConfig<GeometryBase, Point3d>.WithTransforms(
    context,
    preTransform: geom => {
        // Scale geometry before processing
        geom.Transform(Transform.Scale(Point3d.Origin, 2.0));
        return ResultFactory.Create(value: geom);
    },
    postTransform: pt => {
        // Round points after extraction
        return ResultFactory.Create(value: new Point3d(
            Math.Round(pt.X, 3),
            Math.Round(pt.Y, 3),
            Math.Round(pt.Z, 3)
        ));
    });

var result = UnifiedOperation.Apply(geometries, ExtractPoints, config);
```

### Pattern 4: Conditional Execution
```csharp
var config = new OperationConfig<GeometryBase, Point3d> {
    Context = context,
    InputFilter = geom => geom is Curve { IsClosed: true },
    OutputFilter = pt => pt.Z > 0,
};

// Only processes closed curves, only returns points above Z=0
var result = UnifiedOperation.Apply(geometries, ExtractPoints, config);
```

### Pattern 5: Cached Expensive Operations
```csharp
var config = new OperationConfig<GeometryBase, Point3d> {
    Context = context,
    EnableCache = true,
};

var cache = new ConcurrentDictionary<GeometryBase, Result<IReadOnlyList<Point3d>>>();

// First call: computes
var result1 = UnifiedOperation.ApplyCached(geometries, ExtractPoints, config, cache);

// Second call with same geometries: returns cached
var result2 = UnifiedOperation.ApplyCached(geometries, ExtractPoints, config, cache);
```

### Pattern 6: Multiple Operations with Applicative Composition
```csharp
var operations = new List<Func<GeometryBase, Result<Point3d>>> {
    ExtractCentroid,
    ExtractStartPoint,
    ExtractEndPoint,
    ExtractMidPoint,
};

var config = new OperationConfig<GeometryBase, Point3d> {
    Context = context,
    ErrorStrategy = ErrorStrategy.AccumulateAll, // Collect errors from ALL operations
};

// Applies all operations, accumulates ALL results and errors
var result = UnifiedOperation.Compose(geometry, operations, config);
```

## Why This is Better

### 1. **Zero Boilerplate Repetition**
Every engine that needs single/batch handling just calls `UnifiedOperation.Apply()` - no copy/paste.

### 2. **Algebraically Sound**
- Respects functor laws (Map)
- Respects monad laws (Bind)
- Respects applicative laws (Apply)
- Composable transformations

### 3. **Performance Optimizations**
- Empty collection optimization
- Single-item unwrapping
- AggressiveInlining on hot paths
- Parallel execution for large batches

### 4. **Error Handling Flexibility**
Three strategies cover all use cases:
- `FailFast`: Traditional exception-like behavior
- `AccumulateAll`: Functional error accumulation
- `SkipFailed`: Resilient processing

### 5. **Extensibility**
Add new capabilities by extending `OperationConfig` - no changes to `UnifiedOperation` needed.

### 6. **Type Safety**
Full generic type inference, compile-time checking, no runtime type checks.

### 7. **Testability**
All configuration is explicit and mockable. Operations are pure functions.

## Integration with Existing Code

### Update PointExtractionEngine
```csharp
public static Result<IReadOnlyList<Point3d>> Extract<T>(
    T input,
    ExtractionMethod method,
    IGeometryContext context,
    int? count = null,
    double? length = null,
    bool includeEnds = true) where T : notnull =>
    UnifiedOperation.Apply(
        input,
        geometry => ResultFactory.Create(value: geometry)
            .Validate(args: [context, method switch {
                ExtractionMethod.Analytical => ValidationMode.Standard | ValidationMode.MassProperties,
                _ => ValidationMode.Standard,
            }])
            .Bind(g => ExtractionStrategies.Extract(g, method, context, count, length, includeEnds)),
        OperationConfig<GeometryBase, Point3d>.Default(context));
```

### Update SpatialEngine
```csharp
public static Result<IReadOnlyList<T>> Query<T>(
    T input,
    SpatialMethod method,
    IGeometryContext context,
    double? range = null) where T : notnull =>
    UnifiedOperation.Apply(
        input,
        source => SpatialStrategies.Query(source, method, context, range),
        OperationConfig<T, T>.WithValidation(
            context,
            ValidationMode.Standard | ValidationMode.BoundingBox));
```

## Summary

`UnifiedOperation` provides:
- **Dense**: 72% code reduction
- **Polymorphic**: Handles single/batch/enumerable uniformly
- **Algebraic**: Monadic composition with functor/monad/applicative
- **Flexible**: 8 execution modes (Apply, Traverse, Compose, etc.)
- **Configurable**: 12+ configuration options
- **Performant**: Parallel execution, caching, optimizations
- **Type-safe**: Full generic inference
- **Composable**: Transformation pipelines
- **Resilient**: 3 error strategies

Your existing `Result<T>` monad is fully leveraged. This is the missing piece that unifies all your engine patterns into one dense, flexible abstraction.
