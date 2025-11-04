namespace Arsenal.Core.Operations;

using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

/// <summary>
/// Configuration for unified operation execution with composable transformations and validation
/// </summary>
/// <typeparam name="TIn">Input element type</typeparam>
/// <typeparam name="TOut">Output element type</typeparam>
public sealed class OperationConfig<TIn, TOut> {
	/// <summary>Geometry context for validation and tolerance</summary>
	public required IGeometryContext Context { get; init; }

	/// <summary>Validation mode applied before operation (default: None)</summary>
	public ValidationMode ValidationMode { get; init; } = ValidationMode.None;

	/// <summary>Additional validation arguments</summary>
	public object[]? ValidationArgs { get; init; }

	/// <summary>Error accumulation strategy</summary>
	public ErrorStrategy ErrorStrategy { get; init; } = ErrorStrategy.FailFast;

	/// <summary>Pre-operation transformation (identity if null)</summary>
	public Func<TIn, Result<TIn>>? PreTransform { get; init; }

	/// <summary>Post-operation transformation (identity if null)</summary>
	public Func<TOut, Result<TOut>>? PostTransform { get; init; }

	/// <summary>Predicate to filter inputs (all pass if null)</summary>
	public Func<TIn, bool>? InputFilter { get; init; }

	/// <summary>Predicate to filter outputs (all pass if null)</summary>
	public Func<TOut, bool>? OutputFilter { get; init; }

	/// <summary>Enable parallel execution for batches (default: false)</summary>
	public bool EnableParallel { get; init; }

	/// <summary>Maximum degree of parallelism (default: -1 = unlimited)</summary>
	public int MaxDegreeOfParallelism { get; init; } = -1;

	/// <summary>Skip invalid inputs instead of failing (default: false)</summary>
	public bool SkipInvalid { get; init; }

	/// <summary>Flatten nested results automatically (default: true)</summary>
	public bool AutoFlatten { get; init; } = true;

	/// <summary>Short-circuit on first error (fail-fast mode only)</summary>
	public bool ShortCircuit { get; init; } = true;

	/// <summary>Enable operation result caching (default: false)</summary>
	public bool EnableCache { get; init; }

	/// <summary>Custom error message prefix for accumulated errors</summary>
	public string? ErrorPrefix { get; init; }

	/// <summary>Create default configuration with context only</summary>
	public static OperationConfig<TIn, TOut> Default(IGeometryContext context) => new() {
		Context = context,
	};

	/// <summary>Create configuration with validation mode</summary>
	public static OperationConfig<TIn, TOut> WithValidation(
		IGeometryContext context,
		ValidationMode mode,
		params object[] args) => new() {
		Context = context,
		ValidationMode = mode,
		ValidationArgs = args,
	};

	/// <summary>Create configuration with transformations</summary>
	public static OperationConfig<TIn, TOut> WithTransforms(
		IGeometryContext context,
		Func<TIn, Result<TIn>>? preTransform = null,
		Func<TOut, Result<TOut>>? postTransform = null) => new() {
		Context = context,
		PreTransform = preTransform,
		PostTransform = postTransform,
	};

	/// <summary>Create configuration for parallel batch operations</summary>
	public static OperationConfig<TIn, TOut> Parallel(
		IGeometryContext context,
		int maxDegreeOfParallelism = -1) => new() {
		Context = context,
		EnableParallel = true,
		MaxDegreeOfParallelism = maxDegreeOfParallelism,
		ErrorStrategy = ErrorStrategy.AccumulateAll,
	};
}

/// <summary>Error handling strategy for batch operations</summary>
public enum ErrorStrategy {
	/// <summary>Stop on first error and return immediately</summary>
	FailFast,

	/// <summary>Continue processing and accumulate all errors</summary>
	AccumulateAll,

	/// <summary>Skip failed items and continue with valid results</summary>
	SkipFailed,
}
