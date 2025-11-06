using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Operations;

/// <summary>Polymorphic operation configuration enabling composable transformations, validation, and error handling strategies.</summary>
public sealed record OperationConfig<TIn, TOut> {
    /// <summary>Polymorphic geometry context providing validation and tolerance contracts</summary>
    public required IGeometryContext Context { get; init; }

    /// <summary>Validation mode applied before operation execution</summary>
    public ulong ValidationMode { get; init; } = Modes.None;

    /// <summary>Additional validation arguments passed to validation contracts</summary>
    public object[]? ValidationArgs { get; init; }

    /// <summary>Accumulate all errors using applicative functor semantics instead of fail-fast monadic bind</summary>
    public bool AccumulateErrors { get; init; }

    /// <summary>Composable pre-operation transformation applied to inputs</summary>
    public Func<TIn, Result<TIn>>? PreTransform { get; init; }

    /// <summary>Composable post-operation transformation applied to outputs</summary>
    public Func<TOut, Result<TOut>>? PostTransform { get; init; }

    /// <summary>Input filtering predicate for selective operation execution</summary>
    public Func<TIn, bool>? InputFilter { get; init; }

    /// <summary>Output filtering predicate for result refinement</summary>
    public Func<TOut, bool>? OutputFilter { get; init; }

    /// <summary>Enable parallel execution for batch operations</summary>
    public bool EnableParallel { get; init; }

    /// <summary>Maximum degree of parallelism for concurrent execution</summary>
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>Skip invalid inputs instead of failing operation</summary>
    public bool SkipInvalid { get; init; }

    /// <summary>Automatically flatten nested monadic results</summary>
    public bool AutoFlatten { get; init; } = true;

    /// <summary>Short-circuit execution on first error in fail-fast mode</summary>
    public bool ShortCircuit { get; init; } = true;

    /// <summary>Enable memoization caching for operation results</summary>
    public bool EnableCache { get; init; }

    /// <summary>Custom error message prefix for error accumulation</summary>
    public string? ErrorPrefix { get; init; }

    /// <summary>Operation name for diagnostic instrumentation when diagnostics enabled</summary>
    public string? OperationName { get; init; }

    /// <summary>Enable diagnostic capture for allocation and timing instrumentation in DEBUG builds</summary>
    public bool EnableDiagnostics { get; init; }
}
