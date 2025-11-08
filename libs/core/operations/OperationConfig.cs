using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Operations;

/// <summary>Operation configuration for composable transformations and error handling.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record OperationConfig<TIn, TOut> {
    /// <summary>Geometry context providing validation and tolerance contracts.</summary>
    public required IGeometryContext Context { get; init; }

    /// <summary>Validation mode applied before operation execution.</summary>
    public V ValidationMode { get; init; } = V.None;

    /// <summary>Additional validation arguments passed to validation contracts.</summary>
    public object[]? ValidationArgs { get; init; }

    /// <summary>Accumulate all errors using applicative functor semantics.</summary>
    public bool AccumulateErrors { get; init; }

    /// <summary>Pre-operation transformation applied to inputs.</summary>
    public Func<TIn, Result<TIn>>? PreTransform { get; init; }

    /// <summary>Post-operation transformation applied to outputs.</summary>
    public Func<TOut, Result<TOut>>? PostTransform { get; init; }

    /// <summary>Input filtering predicate for selective operation execution.</summary>
    public Func<TIn, bool>? InputFilter { get; init; }

    /// <summary>Output filtering predicate for result refinement.</summary>
    public Func<TOut, bool>? OutputFilter { get; init; }

    /// <summary>Enable parallel execution for batch operations.</summary>
    public bool EnableParallel { get; init; }

    /// <summary>Maximum degree of parallelism for concurrent execution.</summary>
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>Skip invalid inputs instead of failing operation.</summary>
    public bool SkipInvalid { get; init; }

    /// <summary>Automatically flatten nested monadic results.</summary>
    public bool AutoFlatten { get; init; } = true;

    /// <summary>Short-circuit execution on first error in fail-fast mode.</summary>
    public bool ShortCircuit { get; init; } = true;

    /// <summary>Enable memoization caching for operation results.</summary>
    public bool EnableCache { get; init; }

    /// <summary>Custom error message prefix for error accumulation.</summary>
    public string? ErrorPrefix { get; init; }

    /// <summary>Operation name for diagnostic instrumentation.</summary>
    public string? OperationName { get; init; }

    /// <summary>Enable diagnostic capture for allocation and timing instrumentation.</summary>
    public bool EnableDiagnostics { get; init; }

    [Pure]
    private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
        $"Op:{this.OperationName ?? "unnamed"} | Val:{this.ValidationMode}{(this.EnableCache ? " [cached]" : string.Empty)}{(this.EnableParallel ? " [parallel]" : string.Empty)}{(this.EnableDiagnostics ? " [diag]" : string.Empty)}");
}
