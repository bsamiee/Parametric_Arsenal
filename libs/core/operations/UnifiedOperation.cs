namespace Arsenal.Core.Operations;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

/// <summary>
/// Polymorphic single/batch operation execution with validation, transformation,
/// error strategies, and monadic composition
/// </summary>
public static class UnifiedOperation {

	/// <summary>Execute operation with automatic single/batch dispatch</summary>
	public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		input switch {
			IReadOnlyList<TIn> { Count: 0 } => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			IReadOnlyList<TIn> { Count: 1 } list => Apply(list[0], operation, config),
			IReadOnlyList<TIn> list => config.EnableParallel
				? ApplyParallel(list, operation, config)
				: ApplySequential(list, operation, config),
			IEnumerable<TIn> enumerable => Apply(enumerable.ToList(), operation, config),
			_ => ApplySingle(input, operation, config),
		};

	/// <summary>Execute operation with deferred validation</summary>
	public static Result<IReadOnlyList<TOut>> ApplyDeferred<TIn, TOut>(
		TIn input,
		Func<TIn, ValidationMode, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => operation(item, config.ValidationMode), config with { ValidationMode = ValidationMode.None });

	/// <summary>Execute operation with nested result flattening</summary>
	public static Result<IReadOnlyList<TOut>> ApplyFlat<TIn, TOut>(
		TIn input,
		Func<TIn, Result<Result<IReadOnlyList<TOut>>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => operation(item).Bind(nested => nested), config);

	/// <summary>Execute operation with monadic traversal</summary>
	public static Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(
		TIn input,
		Func<TIn, Result<TOut>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => operation(item).Map(single => (IReadOnlyList<TOut>)[single]), config);

	/// <summary>Execute multiple operations with applicative composition</summary>
	public static Result<IReadOnlyList<TOut>> Compose<TIn, TOut>(
		TIn input,
		IReadOnlyList<Func<TIn, Result<TOut>>> operations,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		operations.Aggregate(
			ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(acc, op) => acc.Apply(Traverse(input, op, config)
				.Map(results => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(
					existing => (IReadOnlyList<TOut>)[.. existing, .. results]))));

	/// <summary>Execute operation conditionally based on predicate</summary>
	public static Result<IReadOnlyList<TOut>> ApplyWhen<TIn, TOut>(
		TIn input,
		Func<TIn, bool> predicate,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => predicate(item) switch {
			true => operation(item),
			false => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
		}, config);

	/// <summary>Execute operation with memoization</summary>
	public static Result<IReadOnlyList<TOut>> ApplyCached<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config,
		ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>? cache = null) where TIn : notnull =>
		(config.EnableCache, input, cache ?? new ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>()) switch {
			(false, _, _) => Apply(input, operation, config),
			(true, IReadOnlyList<TIn> list, var c) => list
				.Select(item => c.GetOrAdd(item, operation))
				.Aggregate(
					ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
					(acc, curr) => AccumulateResult(acc, curr, config)),
			(true, IEnumerable<TIn> enumerable, var c) => ApplyCached(enumerable.ToList(), operation, config, c),
			(true, var item, var c) => c.GetOrAdd(item, operation),
		};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Result<IReadOnlyList<TOut>> ApplySingle<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		(config.InputFilter?.Invoke(input), config.ValidationMode, config.PreTransform) switch {
			(false, _, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(_, var mode, var pre) => Validate(input, mode, config)
				.Bind(validated => pre?.Invoke(validated) ?? ResultFactory.Create(value: validated))
				.Bind(transformed => operation(transformed))
				.Match(
					onSuccess: outputs => PostProcess(outputs, config),
					onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
						errors: config.ErrorPrefix switch {
							null => errors,
							var prefix => errors.Select(e => e with { Message = $"{prefix}: {e.Message}" }).ToArray(),
						})),
		};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Result<TIn> Validate<TIn>(TIn input, ValidationMode mode, OperationConfig<TIn, object> config) =>
		(mode, config.SkipInvalid) switch {
			(ValidationMode.None, _) => ResultFactory.Create(value: input),
			(var m, false) => ResultFactory.Create(value: input)
				.Validate(args: [config.Context, m, .. config.ValidationArgs ?? []]),
			(var m, true) => ResultFactory.Create(value: input)
				.Validate(args: [config.Context, m, .. config.ValidationArgs ?? []])
				.Match(
					onSuccess: v => ResultFactory.Create(value: v),
					onFailure: _ => ResultFactory.Create(value: input)),
		};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Result<IReadOnlyList<TOut>> PostProcess<TOut>(
		IReadOnlyList<TOut> outputs,
		OperationConfig<object, TOut> config) =>
		(config.OutputFilter, config.PostTransform) switch {
			(null, null) => ResultFactory.Create(value: outputs),
			(var filter, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs
				.Where(o => filter?.Invoke(o) ?? true)
				.ToArray()),
			(var filter, var transform) => outputs
				.Where(o => filter?.Invoke(o) ?? true)
				.Select(o => transform!(o))
				.Aggregate(
					ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
					(acc, curr) => (curr.IsSuccess, config.SkipInvalid) switch {
						(true, _) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, c])),
						(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: curr.Errors),
						(false, true) => acc,
					}),
		};

	private static Result<IReadOnlyList<TOut>> ApplySequential<TIn, TOut>(
		IReadOnlyList<TIn> inputs,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		inputs.Aggregate(
			ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(acc, input) => ApplySingle(input, operation, config) switch {
				var current when !current.IsSuccess && config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit
					=> current,
				var current => AccumulateResult(acc, current, config),
			});

	private static Result<IReadOnlyList<TOut>> ApplyParallel<TIn, TOut>(
		IReadOnlyList<TIn> inputs,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull {
		ConcurrentBag<Result<IReadOnlyList<TOut>>> results = [];
		Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = config.MaxDegreeOfParallelism },
			input => results.Add(ApplySingle(input, operation, config)));
		return results.Aggregate(
			ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(acc, curr) => AccumulateResult(acc, curr, config));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Result<IReadOnlyList<TOut>> AccumulateResult<TOut>(
		Result<IReadOnlyList<TOut>> accumulator,
		Result<IReadOnlyList<TOut>> current,
		OperationConfig<object, TOut> config) =>
		(config.ErrorStrategy, current.IsSuccess) switch {
			(ErrorStrategy.FailFast, false) => current,
			(ErrorStrategy.FailFast, true) => accumulator.Bind(acc => current.Map(curr => (IReadOnlyList<TOut>)[.. acc, .. curr])),
			(ErrorStrategy.AccumulateAll, _) => accumulator.Apply(current.Map(curr =>
				new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(acc => (IReadOnlyList<TOut>)[.. acc, .. curr]))),
			(ErrorStrategy.SkipFailed, true) => accumulator.Bind(acc => current.Map(curr => (IReadOnlyList<TOut>)[.. acc, .. curr])),
			(ErrorStrategy.SkipFailed, false) => accumulator,
			_ => accumulator,
		};
}
