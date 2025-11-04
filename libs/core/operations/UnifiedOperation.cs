namespace Arsenal.Core.Operations;

using System.Collections.Concurrent;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

/// <summary>Polymorphic single/batch operation execution with parameterized algebraic dispatch</summary>
public static class UnifiedOperation {

	public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull {

		Func<TIn, Result<IReadOnlyList<TOut>>> pipeline = item =>
			(config.InputFilter?.Invoke(item), config.ValidationMode != ValidationMode.None) switch {
				(false, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
				(_, false) => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item)).Bind(operation),
				(_, true) => ResultFactory.Create(value: item)
					.Validate(args: [config.Context, config.ValidationMode, .. config.ValidationArgs ?? []])
					.Match(
						onSuccess: validated => (config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated)).Bind(operation),
						onFailure: errors => config.SkipInvalid
							? (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item)).Bind(operation)
							: ResultFactory.Create<IReadOnlyList<TOut>>(errors: errors)),
			}
			.Match(
				onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
					(null, null) => ResultFactory.Create(value: outputs),
					(var filter, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => filter?.Invoke(o) ?? true).ToArray()),
					(var filter, var transform) => outputs.Where(o => filter?.Invoke(o) ?? true).Select(transform!).Aggregate(
						ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
						(acc, curr) => curr.IsSuccess
							? acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, c]))
							: config.SkipInvalid ? acc : ResultFactory.Create<IReadOnlyList<TOut>>(errors: curr.Errors)),
				},
				onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
					errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray()));

		Func<Result<IReadOnlyList<TOut>>, Result<IReadOnlyList<TOut>>, Result<IReadOnlyList<TOut>>> accumulate = (acc, curr) =>
			(config.ErrorStrategy, curr.IsSuccess) switch {
				(ErrorStrategy.FailFast, false) => curr,
				(ErrorStrategy.FailFast, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
				(ErrorStrategy.AccumulateAll, _) => acc.Apply(curr.Map(c => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => (IReadOnlyList<TOut>)[.. a, .. c]))),
				(ErrorStrategy.SkipFailed, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
				_ => acc,
			};

		return (input, config.EnableParallel) switch {
			(IReadOnlyList<TIn> { Count: 0 }, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(IReadOnlyList<TIn> { Count: 1 } list, _) => Apply(list[0], operation, config),
			(IReadOnlyList<TIn> list, true) => list.AsParallel()
				.WithDegreeOfParallelism(config.MaxDegreeOfParallelism)
				.Select(pipeline)
				.Aggregate(ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()), accumulate),
			(IReadOnlyList<TIn> list, false) => list.Aggregate(
				ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
				(acc, item) => pipeline(item) switch {
					var curr when !curr.IsSuccess && config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit => curr,
					var curr => accumulate(acc, curr),
				}),
			(IEnumerable<TIn> enumerable, _) => Apply(enumerable.ToList(), operation, config),
			_ => pipeline(input),
		};
	}

	public static Result<IReadOnlyList<TOut>> ApplyDeferred<TIn, TOut>(
		TIn input,
		Func<TIn, ValidationMode, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => operation(item, config.ValidationMode), config with { ValidationMode = ValidationMode.None });

	public static Result<IReadOnlyList<TOut>> ApplyFlat<TIn, TOut>(
		TIn input,
		Func<TIn, Result<Result<IReadOnlyList<TOut>>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => operation(item).Bind(nested => nested), config);

	public static Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(
		TIn input,
		Func<TIn, Result<TOut>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => operation(item).Map(single => (IReadOnlyList<TOut>)[single]), config);

	public static Result<IReadOnlyList<TOut>> Compose<TIn, TOut>(
		TIn input,
		IReadOnlyList<Func<TIn, Result<TOut>>> operations,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		operations.Aggregate(
			ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(acc, op) => acc.Apply(Traverse(input, op, config)
				.Map(results => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(
					existing => (IReadOnlyList<TOut>)[.. existing, .. results]))));

	public static Result<IReadOnlyList<TOut>> ApplyWhen<TIn, TOut>(
		TIn input,
		Func<TIn, bool> predicate,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		Apply(input, item => predicate(item) switch {
			true => operation(item),
			false => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
		}, config);

	public static Result<IReadOnlyList<TOut>> ApplyCached<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config,
		ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>? cache = null) where TIn : notnull =>
		(config.EnableCache, input, cache ?? new ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>()) switch {
			(false, _, _) => Apply(input, operation, config),
			(true, IReadOnlyList<TIn> list, var c) => Apply(
				list.Select(item => c.GetOrAdd(item, i => Apply(i, operation, config))).ToList(),
				r => r,
				config with { ValidationMode = ValidationMode.None, PreTransform = null, InputFilter = null }),
			(true, IEnumerable<TIn> enumerable, var c) => ApplyCached(enumerable.ToList(), operation, config, c),
			(true, var item, var c) => c.GetOrAdd(item, i => Apply(i, operation, config)),
		};
}
