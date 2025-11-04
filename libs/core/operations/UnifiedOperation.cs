namespace Arsenal.Core.Operations;

using System.Collections.Concurrent;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

/// <summary>Polymorphic single/batch operation execution with parameterized algebraic dispatch</summary>
public static class UnifiedOperation {

	public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull {

		var empty = ResultFactory.Create(value: (IReadOnlyList<TOut>)[]);

		Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>, IReadOnlyList<TOut>> combine = (a, c) => (IReadOnlyList<TOut>)[.. a, .. c];

		Func<TIn, Result<IReadOnlyList<TOut>>> pipeline = item =>
			((config.InputFilter?.Invoke(item), config.ValidationMode != ValidationMode.None, config.SkipInvalid) switch {
				(false, _, _) => empty,
				(_, false, _) => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item)).Bind(operation),
				(_, true, false) => ResultFactory.Create(value: item)
					.Validate(args: [config.Context, config.ValidationMode, .. config.ValidationArgs ?? []])
					.Bind(validated => (config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated)).Bind(operation)),
				(_, true, true) => ResultFactory.Create(value: item)
					.Validate(args: [config.Context, config.ValidationMode, .. config.ValidationArgs ?? []])
					.Match(
						onSuccess: validated => (config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated)).Bind(operation),
						onFailure: _ => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item)).Bind(operation)),
			})
			.Match(
				onSuccess: outputs => (config.OutputFilter, config.PostTransform, config.SkipInvalid) switch {
					(null, null, _) => ResultFactory.Create(value: outputs),
					(var f, null, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
					(var f, var t, var skip) => outputs
						.Where(o => f?.Invoke(o) ?? true)
						.Aggregate(empty, (acc, o) => t(o) switch {
							{ IsSuccess: true } r => acc.Bind(a => r.Map(c => combine(a, [c]))),
							{ IsSuccess: false } r when !skip => ResultFactory.Create<IReadOnlyList<TOut>>(errors: r.Errors.ToArray()),
							_ => acc,
						}),
				},
				onFailure: errors => (config.ErrorPrefix, errors) switch {
					(null, var e) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: e.ToArray()),
					(var prefix, var e) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: e.Select(err => new SystemError(err.Domain, err.Code, $"{prefix}: {err.Message}")).ToArray()),
				});

		Func<Result<IReadOnlyList<TOut>>, Result<IReadOnlyList<TOut>>, Result<IReadOnlyList<TOut>>> accumulate = (acc, curr) =>
			(config.ErrorStrategy, curr.IsSuccess) switch {
				(ErrorStrategy.FailFast, false) => curr,
				(ErrorStrategy.FailFast, true) => acc.Bind(a => curr.Map(c => combine(a, c))),
				(ErrorStrategy.AccumulateAll, _) => acc.Apply(curr.Map(c => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => combine(a, c)))),
				(ErrorStrategy.SkipFailed, true) => acc.Bind(a => curr.Map(c => combine(a, c))),
				_ => acc,
			};

		return (input, config.EnableParallel) switch {
			(IReadOnlyList<TIn> { Count: 0 }, _) => empty,
			(IReadOnlyList<TIn> { Count: 1 } list, _) => Apply(list[0], operation, config),
			(IReadOnlyList<TIn> list, true) => list.AsParallel()
				.WithDegreeOfParallelism(config.MaxDegreeOfParallelism)
				.Aggregate(empty, (acc, item) => accumulate(acc, pipeline(item)), x => x),
			(IReadOnlyList<TIn> list, false) when config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit => list.Aggregate(
				empty,
				(acc, item) => acc.IsSuccess switch {
					true => pipeline(item) switch {
						{ IsSuccess: true } curr => accumulate(acc, curr),
						var curr => curr,
					},
					false => acc,
				}),
			(IReadOnlyList<TIn> list, false) => list.Aggregate(empty, (acc, item) => accumulate(acc, pipeline(item))),
			(IEnumerable<TIn> enumerable, _) => Apply((TIn)(object)enumerable.ToList(), operation, config),
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
			ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
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
			false => ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
		}, config);

	public static Result<IReadOnlyList<TOut>> ApplyCached<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config,
		ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>? cache = null) where TIn : notnull =>
		(config.EnableCache, input, cache ?? new ConcurrentDictionary<TIn, Result<IReadOnlyList<TOut>>>()) switch {
			(false, var i, _) => Apply(i, operation, config),
			(true, IReadOnlyList<TIn> list, var c) => list.Aggregate(
				ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
				(acc, item) => (config.ErrorStrategy, c.GetOrAdd(item, i => Apply(i, operation, config))) switch {
					(ErrorStrategy.FailFast, { IsSuccess: false } curr) => curr,
					(ErrorStrategy.FailFast, { IsSuccess: true } curr) => acc.Bind(a => curr.Map(x => (IReadOnlyList<TOut>)[.. a, .. x])),
					(ErrorStrategy.AccumulateAll, var curr) => acc.Apply(curr.Map(x => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => (IReadOnlyList<TOut>)[.. a, .. x]))),
					(ErrorStrategy.SkipFailed, { IsSuccess: true } curr) => acc.Bind(a => curr.Map(x => (IReadOnlyList<TOut>)[.. a, .. x])),
					_ => acc,
				}),
			(true, IEnumerable<TIn> enumerable, var c) => ApplyCached((TIn)(object)enumerable.ToList(), operation, config, c),
			(true, var item, var c) => c.GetOrAdd(item, i => Apply(i, operation, config)),
		};
}
