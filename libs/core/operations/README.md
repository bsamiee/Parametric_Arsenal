# UnifiedOperation - Polymorphic Operation Execution

Dense, algebraic operation system with single/batch unification, monadic composition, and error strategies.

## Core Capabilities

### Single/Batch Polymorphism
Automatically handles single items, collections, and enumerables with optimizations for empty and single-item collections.

### Error Strategies
```csharp
ErrorStrategy.FailFast       // Stop on first error
ErrorStrategy.AccumulateAll  // Collect all errors
ErrorStrategy.SkipFailed     // Continue with valid results only
```

### Execution Modes

| Mode | Signature | Purpose |
|------|-----------|---------|
| `Apply` | `TIn → (TIn → Result<List<TOut>>) → Result<List<TOut>>` | Standard execution |
| `Traverse` | `TIn → (TIn → Result<TOut>) → Result<List<TOut>>` | Single-to-single mapping |
| `Compose` | `TIn → [TIn → Result<TOut>] → Result<List<TOut>>` | Multiple operations |
| `ApplyWhen` | `TIn → Predicate → Operation → Result` | Conditional execution |
| `ApplyDeferred` | `TIn → (TIn, ValidationMode) → Result` | Deferred validation |
| `ApplyFlat` | `TIn → Result<Result<List<TOut>>> → Result` | Nested flattening |
| `ApplyCached` | `TIn → Operation → Cache → Result` | Memoization |

## Configuration

```csharp
var config = new OperationConfig<GeometryBase, Point3d> {
    Context = context,
    ValidationMode = ValidationMode.Standard | ValidationMode.MassProperties,
    ErrorStrategy = ErrorStrategy.AccumulateAll,
    EnableParallel = true,
    MaxDegreeOfParallelism = Environment.ProcessorCount,
    PreTransform = geom => Transform(geom),
    PostTransform = pt => Round(pt),
    InputFilter = geom => geom is Curve,
    OutputFilter = pt => pt.IsValid,
    SkipInvalid = true,
    EnableCache = true,
    ErrorPrefix = "Operation",
};
```

## Usage

### Before (Manual Pattern Matching)
```csharp
public static Result<IReadOnlyList<Point3d>> Extract<T>(
    T input, ExtractionMethod method, IGeometryContext context) where T : notnull {

    return input switch {
        GeometryBase single => ResultFactory.Create(value: single)
            .Validate(args: [context, ValidationMode.Standard])
            .Bind(g => ExtractCore(g, method, context)),
        IReadOnlyList<GeometryBase> { Count: 0 } =>
            ResultFactory.Create(value: (IReadOnlyList<Point3d>)[]),
        IReadOnlyList<GeometryBase> { Count: 1 } list =>
            Extract(list[0], method, context),
        IReadOnlyList<GeometryBase> list => list
            .Select(g => Extract(g, method, context))
            .Aggregate(/* ... */),
        IEnumerable<GeometryBase> enumerable =>
            Extract(enumerable.ToArray(), method, context),
        _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: /* ... */),
    };
}
```

### After (UnifiedOperation)
```csharp
public static Result<IReadOnlyList<Point3d>> Extract<T>(
    T input, ExtractionMethod method, IGeometryContext context) where T : notnull =>
    UnifiedOperation.Apply(
        input,
        geometry => ResultFactory.Create(value: geometry)
            .Validate(args: [context, ValidationMode.Standard])
            .Bind(g => ExtractCore(g, method, context)),
        OperationConfig<GeometryBase, Point3d>.Default(context));
```

**Reduction:** 25 lines → 7 lines (72%)

## Patterns

### Parallel Processing
```csharp
var config = OperationConfig<GeometryBase, Point3d>.Parallel(context);
var result = UnifiedOperation.Apply(geometries, ExtractPoints, config);
```

### Transformation Pipeline
```csharp
var config = OperationConfig<GeometryBase, Point3d>.WithTransforms(
    context,
    preTransform: geom => Scale(geom, 2.0),
    postTransform: pt => Round(pt, 3));
```

### Resilient Processing
```csharp
var config = new OperationConfig<GeometryBase, Point3d> {
    Context = context,
    ErrorStrategy = ErrorStrategy.SkipFailed,
    SkipInvalid = true,
};
```

### Multiple Operations
```csharp
var operations = new[] { ExtractCentroid, ExtractStart, ExtractEnd };
var result = UnifiedOperation.Compose(geometry, operations, config);
```

### Caching
```csharp
var config = new OperationConfig<GeometryBase, Point3d> {
    Context = context,
    EnableCache = true,
};
var result = UnifiedOperation.ApplyCached(geometries, ExtractPoints, config);
```

## Architecture

All code uses pattern matching with zero if/else statements. Heavily optimized with `AggressiveInlining` on hot paths. Respects functor/monad/applicative laws for algebraic soundness.
