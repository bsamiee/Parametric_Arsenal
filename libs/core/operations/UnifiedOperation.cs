namespace Arsenal.Core.Operations;

using System.Collections.Concurrent;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

/// <summary>Polymorphic single/batch operation execution with single algebraic dispatch</summary>
public static class UnifiedOperation {

	public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
		TIn input,
		Func<TIn, Result<IReadOnlyList<TOut>>> operation,
		OperationConfig<TIn, TOut> config) where TIn : notnull =>
		(input, config.EnableParallel) switch {
			(IReadOnlyList<TIn> { Count: 0 }, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
			(IReadOnlyList<TIn> { Count: 1 } list, _) => Apply(list[0], operation, config),
			(IReadOnlyList<TIn> list, true) => list.AsParallel()
				.WithDegreeOfParallelism(config.MaxDegreeOfParallelism)
				.Select(item => (config.InputFilter?.Invoke(item), config.ValidationMode, config.SkipInvalid) switch {
					(false, _, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
					(_, ValidationMode.None, _) => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item))
						.Bind(operation)
						.Match(
							onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
								(null, null) => ResultFactory.Create(value: outputs),
								(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
								(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
									ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
									(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
										(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
										(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
										(false, true) => a,
									}),
							},
							onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
								errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())),
					(_, var mode, false) => ResultFactory.Create(value: item)
						.Validate(args: [config.Context, mode, .. config.ValidationArgs ?? []])
						.Bind(validated => config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated))
						.Bind(operation)
						.Match(
							onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
								(null, null) => ResultFactory.Create(value: outputs),
								(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
								(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
									ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
									(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
										(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
										(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
										(false, true) => a,
									}),
							},
							onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
								errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())),
					(_, var mode, true) => ResultFactory.Create(value: item)
						.Validate(args: [config.Context, mode, .. config.ValidationArgs ?? []])
						.Match(
							onSuccess: validated => (config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated))
								.Bind(operation)
								.Match(
									onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
										(null, null) => ResultFactory.Create(value: outputs),
										(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
										(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
											ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
											(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
												(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
												(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
												(false, true) => a,
											}),
									},
									onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
										errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())),
							onFailure: _ => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item))
								.Bind(operation)
								.Match(
									onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
										(null, null) => ResultFactory.Create(value: outputs),
										(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
										(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
											ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
											(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
												(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
												(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
												(false, true) => a,
											}),
									},
									onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
										errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray()))),
				})
				.Aggregate(
					ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
					(acc, curr) => (config.ErrorStrategy, curr.IsSuccess) switch {
						(ErrorStrategy.FailFast, false) => curr,
						(ErrorStrategy.FailFast, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
						(ErrorStrategy.AccumulateAll, _) => acc.Apply(curr.Map(c => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => (IReadOnlyList<TOut>)[.. a, .. c]))),
						(ErrorStrategy.SkipFailed, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
						(ErrorStrategy.SkipFailed, false) => acc,
						_ => acc,
					}),
			(IReadOnlyList<TIn> list, false) => list.Aggregate(
				ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
				(acc, item) => (config.InputFilter?.Invoke(item), config.ValidationMode, config.SkipInvalid) switch {
					(false, _, _) when config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit => acc,
					(false, _, _) => acc,
					(_, ValidationMode.None, _) => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item))
						.Bind(operation)
						.Match(
							onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
								(null, null) => ResultFactory.Create(value: outputs),
								(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
								(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
									ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
									(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
										(true, _) => a.Bind(inner => c.Map(v => (IReadOnlyList<TOut>)[.. inner, v])),
										(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
										(false, true) => a,
									}),
							},
							onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
								errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray()))
						switch {
							var curr when !curr.IsSuccess && config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit => curr,
							var curr => (config.ErrorStrategy, curr.IsSuccess) switch {
								(ErrorStrategy.FailFast, false) => curr,
								(ErrorStrategy.FailFast, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
								(ErrorStrategy.AccumulateAll, _) => acc.Apply(curr.Map(c => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => (IReadOnlyList<TOut>)[.. a, .. c]))),
								(ErrorStrategy.SkipFailed, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
								(ErrorStrategy.SkipFailed, false) => acc,
								_ => acc,
							},
						},
					(_, var mode, false) => ResultFactory.Create(value: item)
						.Validate(args: [config.Context, mode, .. config.ValidationArgs ?? []])
						.Bind(validated => config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated))
						.Bind(operation)
						.Match(
							onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
								(null, null) => ResultFactory.Create(value: outputs),
								(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
								(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
									ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
									(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
										(true, _) => a.Bind(inner => c.Map(v => (IReadOnlyList<TOut>)[.. inner, v])),
										(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
										(false, true) => a,
									}),
							},
							onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
								errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray()))
						switch {
							var curr when !curr.IsSuccess && config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit => curr,
							var curr => (config.ErrorStrategy, curr.IsSuccess) switch {
								(ErrorStrategy.FailFast, false) => curr,
								(ErrorStrategy.FailFast, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
								(ErrorStrategy.AccumulateAll, _) => acc.Apply(curr.Map(c => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => (IReadOnlyList<TOut>)[.. a, .. c]))),
								(ErrorStrategy.SkipFailed, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
								(ErrorStrategy.SkipFailed, false) => acc,
								_ => acc,
							},
						},
					(_, var mode, true) => ResultFactory.Create(value: item)
						.Validate(args: [config.Context, mode, .. config.ValidationArgs ?? []])
						.Match(
							onSuccess: validated => (config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated))
								.Bind(operation)
								.Match(
									onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
										(null, null) => ResultFactory.Create(value: outputs),
										(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
										(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
											ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
											(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
												(true, _) => a.Bind(inner => c.Map(v => (IReadOnlyList<TOut>)[.. inner, v])),
												(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
												(false, true) => a,
											}),
									},
									onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
										errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())),
							onFailure: _ => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item))
								.Bind(operation)
								.Match(
									onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
										(null, null) => ResultFactory.Create(value: outputs),
										(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
										(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
											ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
											(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
												(true, _) => a.Bind(inner => c.Map(v => (IReadOnlyList<TOut>)[.. inner, v])),
												(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
												(false, true) => a,
											}),
									},
									onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
										errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())))
						switch {
							var curr when !curr.IsSuccess && config.ErrorStrategy == ErrorStrategy.FailFast && config.ShortCircuit => curr,
							var curr => (config.ErrorStrategy, curr.IsSuccess) switch {
								(ErrorStrategy.FailFast, false) => curr,
								(ErrorStrategy.FailFast, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
								(ErrorStrategy.AccumulateAll, _) => acc.Apply(curr.Map(c => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => (IReadOnlyList<TOut>)[.. a, .. c]))),
								(ErrorStrategy.SkipFailed, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
								(ErrorStrategy.SkipFailed, false) => acc,
								_ => acc,
							},
						},
				}),
			(IEnumerable<TIn> enumerable, _) => Apply(enumerable.ToList(), operation, config),
			(var item, _) => (config.InputFilter?.Invoke(item), config.ValidationMode, config.SkipInvalid) switch {
				(false, _, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
				(_, ValidationMode.None, _) => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item))
					.Bind(operation)
					.Match(
						onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
							(null, null) => ResultFactory.Create(value: outputs),
							(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
							(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
								ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
								(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
									(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
									(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
									(false, true) => a,
								}),
						},
						onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
							errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())),
				(_, var mode, false) => ResultFactory.Create(value: item)
					.Validate(args: [config.Context, mode, .. config.ValidationArgs ?? []])
					.Bind(validated => config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated))
					.Bind(operation)
					.Match(
						onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
							(null, null) => ResultFactory.Create(value: outputs),
							(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
							(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
								ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
								(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
									(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
									(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
									(false, true) => a,
								}),
						},
						onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
							errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())),
				(_, var mode, true) => ResultFactory.Create(value: item)
					.Validate(args: [config.Context, mode, .. config.ValidationArgs ?? []])
					.Match(
						onSuccess: validated => (config.PreTransform?.Invoke(validated) ?? ResultFactory.Create(value: validated))
							.Bind(operation)
							.Match(
								onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
									(null, null) => ResultFactory.Create(value: outputs),
									(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
									(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
										ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
										(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
											(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
											(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
											(false, true) => a,
										}),
								},
								onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
									errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray())),
						onFailure: _ => (config.PreTransform?.Invoke(item) ?? ResultFactory.Create(value: item))
							.Bind(operation)
							.Match(
								onSuccess: outputs => (config.OutputFilter, config.PostTransform) switch {
									(null, null) => ResultFactory.Create(value: outputs),
									(var f, null) => ResultFactory.Create(value: (IReadOnlyList<TOut>)outputs.Where(o => f?.Invoke(o) ?? true).ToArray()),
									(var f, var t) => outputs.Where(o => f?.Invoke(o) ?? true).Select(t!).Aggregate(
										ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
										(a, c) => (c.IsSuccess, config.SkipInvalid) switch {
											(true, _) => a.Bind(acc => c.Map(v => (IReadOnlyList<TOut>)[.. acc, v])),
											(false, false) => ResultFactory.Create<IReadOnlyList<TOut>>(errors: c.Errors),
											(false, true) => a,
										}),
								},
								onFailure: errors => ResultFactory.Create<IReadOnlyList<TOut>>(
									errors: config.ErrorPrefix is null ? errors : errors.Select(e => e with { Message = $"{config.ErrorPrefix}: {e.Message}" }).ToArray()))),
			},
		};

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
			(true, IReadOnlyList<TIn> list, var c) => list
				.Select(item => c.GetOrAdd(item, i => Apply(i, operation, config)))
				.Aggregate(
					ResultFactory.Create(value: (IReadOnlyList<TOut>)Array.Empty<TOut>()),
					(acc, curr) => (config.ErrorStrategy, curr.IsSuccess) switch {
						(ErrorStrategy.FailFast, false) => curr,
						(ErrorStrategy.FailFast, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
						(ErrorStrategy.AccumulateAll, _) => acc.Apply(curr.Map(c => new Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>(a => (IReadOnlyList<TOut>)[.. a, .. c]))),
						(ErrorStrategy.SkipFailed, true) => acc.Bind(a => curr.Map(c => (IReadOnlyList<TOut>)[.. a, .. c])),
						(ErrorStrategy.SkipFailed, false) => acc,
						_ => acc,
					}),
			(true, IEnumerable<TIn> enumerable, var c) => ApplyCached(enumerable.ToList(), operation, config, c),
			(true, var item, var c) => c.GetOrAdd(item, i => Apply(i, operation, config)),
		};
}
