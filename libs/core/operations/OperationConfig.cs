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
    /// <summary>Geometry context for validation and tolerance.</summary>
    public required IGeometryContext Context { get; init; }

    /// <summary>Validation mode flags to apply before execution.</summary>
    public V ValidationMode { get; init; } = V.None;

    /// <summary>Additional validation arguments for ValidationRules.</summary>
    public object[]? ValidationArgs { get; init; }

    /// <summary>Accumulate all errors vs fail-fast (applicative vs monadic).</summary>
    public bool AccumulateErrors { get; init; }

    /// <summary>Transform applied to inputs before operation.</summary>
    public Func<TIn, Result<TIn>>? PreTransform { get; init; }

    /// <summary>Transform applied to outputs after operation.</summary>
    public Func<TOut, Result<TOut>>? PostTransform { get; init; }

    /// <summary>Predicate to filter inputs before operation.</summary>
    public Func<TIn, bool>? InputFilter { get; init; }

    /// <summary>Predicate to filter outputs after operation.</summary>
    public Func<TOut, bool>? OutputFilter { get; init; }

    /// <summary>Enable parallel execution for collections.</summary>
    public bool EnableParallel { get; init; }

    /// <summary>Max parallelism (-1 for default).</summary>
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>Skip invalid inputs vs fail entire operation.</summary>
    public bool SkipInvalid { get; init; }

    /// <summary>Flatten nested Result&lt;Result&lt;T&gt;&gt; automatically.</summary>
    public bool AutoFlatten { get; init; } = true;

    /// <summary>Stop on first error vs process all.</summary>
    public bool ShortCircuit { get; init; } = true;

    /// <summary>Enable memoization caching.</summary>
    public bool EnableCache { get; init; }

    /// <summary>Prefix for error messages.</summary>
    public string? ErrorPrefix { get; init; }

    /// <summary>Operation name for diagnostics.</summary>
    public string? OperationName { get; init; }

    /// <summary>Capture timing and allocation diagnostics (DEBUG only).</summary>
    public bool EnableDiagnostics { get; init; }

    [Pure]
    private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
        $"Op:{this.OperationName ?? "unnamed"} | Val:{this.ValidationMode}{(this.EnableCache ? " [cached]" : string.Empty)}{(this.EnableParallel ? " [parallel]" : string.Empty)}{(this.EnableDiagnostics ? " [diag]" : string.Empty)}");
}
