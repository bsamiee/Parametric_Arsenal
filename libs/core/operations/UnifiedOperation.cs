namespace Arsenal.Core.Operations;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

/// <summary>
/// Advanced unified operation execution with polymorphic single/batch handling,
/// validation composition, error accumulation strategies, and transformation pipelines
/// </summary>
public static class UnifiedOperation {

	/// <summary>
	/// Execute operation with automatic single/batch dispatch and advanced composition
	/// </summary>
	/// <typeparam name="TIn">Input element type</typeparam>
	/// <typeparam name="TOut">Output element type</typeparam>
	/// <param name="input">Single item, collection, or enumerable</param>
	/// <param name="operation">Operation to apply to each element</param>
	/// <param name="config">Operation configuration</param>
	/// <returns>Result containing collection of outputs</returns>
	public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		input switch {
			// Empty collection - immediate success with empty result
			IReadOnlyList<TIn> { Count: 0 } =>
				ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),

			// Single-item collection - unwrap and recurse for optimization
			IReadOnlyList<TIn> { Count: 1 } list =>
				Apply(list[0], operation, config),

			// Multiple items - dispatch based on parallel/sequential strategy
			IReadOnlyList<TIn> list =>
				config.EnableParallel
					? ApplyParallel(list, operation, config)
					: ApplySequential(list, operation, config),

			// IEnumerable - materialize and recurse
			IEnumerable<TIn> enumerable =>
				Apply(enumerable.ToList(), operation, config),

			// Single item - apply full pipeline
			_ => ApplySingle(input, operation, config),
		};

	/// <summary>
	/// Execute operation with deferred validation (validation happens inside operation)
	/// </summary>
	public static Result<IReadOnlyList<TOut>> ApplyDeferred<TIn, TOut>(
		TIn input,
		Func<TIn, ValidationMode, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(
			input,
			item => operation(item, config.ValidationMode),
			config with { ValidationMode = ValidationMode.None });

	/// <summary>
	/// Execute operation with automatic flattening of nested results
	/// </summary>
	public static Result<IReadOnlyList<TOut>> ApplyFlat<TIn, TOut>(
		TIn input,
		Func<TIn, Result<Result<IReadOnlyList<TOut>>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => operation(item).Bind(nested => nested), config);

	/// <summary>
	/// Execute operation with monadic traversal (applies operation and flattens)
	/// </summary>
	public static Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(
		TIn input,
		Func<TIn, Result<TOut>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(
			input,
			item => operation(item).Map(single => (IReadOnlyList<TOut>)[single]),
			config);

	/// <summary>
	/// Execute operation with applicative composition (accumulate errors from multiple operations)
	/// </summary>
	public static Result<IReadOnlyList<TOut>> Compose<TIn, TOut>(
		TIn input,
		IReadOnlyList<Func<TIn, Result<TOut>>> operations,
		OperationConfig<TIn, TOut> config) where TIn : notnull {
		// Apply all operations and accumulate results using applicative composition
		Result<IReadOnlyList<TOut>> seed = ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>());

		return operations.Aggregate(
			seed,
			(acc, op) => acc.Apply(
				Traverse(input, op, config)
					.Map(results => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(
						existing => (IReadOnlyList<TOut>)[.. existing, .. results]))));
	}

	/// <summary>
	/// Execute operation with conditional execution based on predicate
	/// </summary>
	public static Result<IReadOnlyList<TOut>> ApplyWhen<TIn, TOut>(
		TIn input,
		Func<TIn, bool> predicate,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(
			input,
			item => predicate(item)
				? operation(item)
				: ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			config);

	/// <summary>
	/// Execute operation with caching for expensive computations
	/// </summary>
	public static Result<IReadOnlyList<TOut>> ApplyCached<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config,
		ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>? cache = null)
		where TIn : notnull {
		if (!config.EnableCache) {
			return Apply(input, operation, config);
		}

		cache ??= new ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>();

		return input switch {
			IReadOnlyList<TIn> list => list
				.Select(item => cache.GetOrAdd(item, operation))
				.Aggregate(
					ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
					(acc, curr) => AccumulateResult(acc, curr, config)),

			IEnumerable<TIn> enumerable =>
				ApplyCached(enumerable.ToList(), operation, config, cache),

			_ => cache.GetOrAdd(input, operation),
		};
	}

	// ==================== PRIVATE IMPLEMENTATION ====================

	/// <summary>Apply operation to single item with full pipeline</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Result<IReadOnlyList<TOut>> ApplySingle<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull {
		// Input filtering
		if (config.InputFilter is not null && !config.InputFilter(input)) {
			return ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>());
		}

		// Pre-validation
		Result<TIn> validated = config.ValidationMode != ValidationMode.None
			? ResultFactory.Create(value: input)
				.Validate(args: [config.Context, config.ValidationMode, .. config.ValidationArgs ?? []])
			: ResultFactory.Create(value: input);

		if (!validated.IsSuccess && !config.SkipInvalid) {
			return ResultFactory.Create<IReadOnlyList<TOut>>(errors: validated.Errors);
		}

		if (!validated.IsSuccess) {
			return ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>());
		}

		// Pre-transformation
		Result<TIn> preTransformed = config.PreTransform is not null
			? validated.Bind(config.PreTransform)
			: validated;

		if (!preTransformed.IsSuccess) {
			return ResultFactory.Create<IReadOnlyList<TOut>>(errors: preTransformed.Errors);
		}

		// Core operation
		Result<IReadOnlyList<TOut>> result = operation(preTransformed.Value);

		if (!result.IsSuccess) {
			return config.ErrorPrefix is not null
				? ResultFactory.Create<IReadOnlyList<TOut>>(
					errors: result.Errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())
				: result;
		}

		// Post-transformation and output filtering
		return result.Map(outputs => {
			IEnumerable<TOut> filtered = config.OutputFilter is not null
				? outputs.Where(config.OutputFilter)
				: outputs;

			if (config.PostTransform is null) {
				return (IReadOnlyList<TOut>)filtered.ToArray();
			}

			// Apply post-transform to each output
			List<TOut> transformed = [];
			foreach (TOut output in filtered) {
				Result<TOut> transformResult = config.PostTransform(output);
				if (transformResult.IsSuccess) {
					transformed.Add(transformResult.Value);
				} else if (!config.SkipInvalid) {
					return ResultFactory.Create<IReadOnlyList<TOut>>(errors: transformResult.Errors).Value;
				}
			}

			return (IReadOnlyList<TOut>)transformed;
		});
	}

	/// <summary>Apply operation to collection sequentially with error strategy</summary>
	private static Result<IReadOnlyList<TOut>> ApplySequential<TIn, TOut>(
		IReadOnlyList<TIn> inputs,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull {
		Result<IReadOnlyList<TOut>> accumulator = ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>());

		foreach (TIn input in inputs) {
			Result<IReadOnlyList<TOut>> current = ApplySingle(input, operation, config);

			// Short-circuit on first error in fail-fast mode
			if (!current.IsSuccess && config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit) {
				return current;
			}

			accumulator = AccumulateResult(accumulator, current, config);
		}

		return accumulator;
	}

	/// <summary>Apply operation to collection in parallel with error strategy</summary>
	private static Result<IReadOnlyList<TOut>> ApplyParallel<TIn, TOut>(
		IReadOnlyList<TIn> inputs,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull {
		ParallelOptions options = new() {
			MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
		};

		ConcurrentBag<Result<IReadOnlyList<TOut>>> results = [];

		Parallel.ForEach(inputs, options, input => {
			Result<IReadOnlyList<TOut>> result = ApplySingle(input, operation, config);
			results.Add(result);
		});

		// Aggregate results based on error strategy
		return results.Aggregate(
			ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(acc, curr) => AccumulateResult(acc, curr, config));
	}

	/// <summary>Accumulate result based on error strategy</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Result<IReadOnlyList<TOut>> AccumulateResult<TOut>(
		Result<IReadOnlyList<TOut>> accumulator,
		Result<IReadOnlyList<TOut>> current,
		OperationConfig<object, TOut> config) =>
		config.ErrorStrategy switch {
			// Fail-fast: first error propagates
			ErrorStrategy.FailFast when !current.IsSuccess => current,
			ErrorStrategy.FailFast => accumulator.Bind(acc =>
				current.Map(curr => (IReadOnlyList<TOut>)[.. acc, .. curr])),

			// Accumulate all errors using applicative composition
			ErrorStrategy.AccumulateAll => accumulator.Apply(
				current.Map(curr => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(
					acc => (IReadOnlyList<TOut>)[.. acc, .. curr]))),

			// Skip failed items, only accumulate successes
			ErrorStrategy.SkipFailed when current.IsSuccess => accumulator.Bind(acc =>
				current.Map(curr => (IReadOnlyList<TOut>)[.. acc, .. curr])),
			ErrorStrategy.SkipFailed => accumulator,

			_ => accumulator,
		};
}
