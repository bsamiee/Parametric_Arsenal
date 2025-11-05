using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Operations;

/// <summary>Polymorphic operation execution engine with unified dispatch and embedded caching.</summary>
public static class UnifiedOperation {
    private static readonly ThreadLocal<ConcurrentDictionary<(object, Type), object>> _threadCache = new(() => new());

    /// <summary>Executes polymorphic operations with automatic type detection, validation, and caching.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
        TIn input,
        object operation,
        OperationConfig<TIn, TOut> config,
        ConcurrentDictionary<(object, Type), object>? externalCache = null) where TIn : notnull {
        ConcurrentDictionary<(object, Type), object> cache = externalCache ?? (config.EnableCache ? _threadCache.Value! : new());

        Result<IReadOnlyList<TOut>> resolveOp(TIn item) => operation switch {
            Func<TIn, Result<IReadOnlyList<TOut>>> op => op(item),
            Func<TIn, ValidationMode, Result<IReadOnlyList<TOut>>> deferred => deferred(item, config.ValidationMode),
            Func<TIn, Result<Result<IReadOnlyList<TOut>>>> nested => nested(item).Bind(inner => inner),
            Func<TIn, Result<TOut>> single => single(item).Map(v => (IReadOnlyList<TOut>)[v]),
            IReadOnlyList<Func<TIn, Result<TOut>>> ops => ops.Aggregate(
                ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
                (acc, op) => (config.AccumulateErrors, op(item)) switch {
                    (true, Result<TOut> res) => acc.Apply(res.Map<Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>>(v => list => [.. list, v])),
                    (_, Result<TOut> res) => acc.Bind(list => res.Map(v => (IReadOnlyList<TOut>)[.. list, v])),
                }),
            (Func<TIn, bool> pred, Func<TIn, Result<IReadOnlyList<TOut>>> op) =>
                pred(item) ? op(item) : ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
            _ => ResultFactory.Create<IReadOnlyList<TOut>>(
                error: ValidationErrors.Operations.UnsupportedOperationType.WithContext($"Type: {operation.GetType()}")),
        };

        Result<IReadOnlyList<TOut>> execute(TIn item) {
            if (cache.TryGetValue((item, operation.GetType()), out object? cached) && config.EnableCache) {
                return (Result<IReadOnlyList<TOut>>)cached;
            }

            Result<IReadOnlyList<TOut>> computed = ResultFactory.Create(value: item)
                .Filter(config.InputFilter ?? (_ => true),
                    config.ErrorPrefix is null ? ValidationErrors.Operations.InputFiltered : ValidationErrors.Operations.InputFiltered.WithContext(config.ErrorPrefix))
                .Validate(args: config.ValidationMode is ValidationMode.None ? null :
                    [config.Context, config.ValidationMode, .. config.ValidationArgs ?? []])
                .OnError(recover: config.SkipInvalid ? _ => item : null)
                .Bind(config.PreTransform ?? (v => ResultFactory.Create(value: v)))
                .Bind(resolveOp)
                .Map(outputs => config.OutputFilter switch {
                    null => outputs,
                    Func<TOut, bool> filter => outputs.Where(filter).ToArray(),
                })
                .Bind(outputs => config.PostTransform switch {
                    null => ResultFactory.Create(value: outputs),
                    Func<TOut, Result<TOut>> transform => outputs.Aggregate(
                        ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
                        (acc, output) => (config.SkipInvalid, transform(output)) switch {
                            (true, { IsSuccess: false }) => acc,
                            (_, Result<TOut> res) => acc.Bind(list => res.Map(v => (IReadOnlyList<TOut>)[.. list, v])),
                        }),
                });

            return (Result<IReadOnlyList<TOut>>)cache.AddOrUpdate(
                (item, operation.GetType()),
                static (_, state) => state,
                static (_, existing, state) => state.config.EnableCache ? existing : state.computed,
                (config, computed));
        }

        return (input, config) switch {
            (IReadOnlyList<TIn> { Count: 0 }, _) => ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
            (IReadOnlyList<TIn> { Count: 1 } list, _) => execute(list[0]),
            (IReadOnlyList<TIn> list, { EnableParallel: true, AccumulateErrors: bool acc, SkipInvalid: bool skip, MaxDegreeOfParallelism: int max }) =>
                list.AsParallel().WithDegreeOfParallelism(max).Select(execute).Aggregate(
                    ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
                    (a, c) => (acc, skip && !c.IsSuccess) switch {
                        (true, _) => a.Apply(c.Map<Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>>(items => prev => [.. prev, .. items])),
                        (_, true) => a,
                        _ => a.Bind(prev => c.Map(items => (IReadOnlyList<TOut>)[.. prev, .. items])),
                    }),
            (IReadOnlyList<TIn> list, { ShortCircuit: true, AccumulateErrors: false }) =>
                list.Aggregate(ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
                    (a, item) => a.IsSuccess ? a.Bind(prev => execute(item).Map(items => (IReadOnlyList<TOut>)[.. prev, .. items])) : a),
            (IReadOnlyList<TIn> list, { AccumulateErrors: bool acc, SkipInvalid: bool skip }) =>
                list.Select(execute).Aggregate(
                    ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
                    (a, c) => (acc, skip && !c.IsSuccess) switch {
                        (true, _) => a.Apply(c.Map<Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>>(items => prev => [.. prev, .. items])),
                        (_, true) => a,
                        _ => a.Bind(prev => c.Map(items => (IReadOnlyList<TOut>)[.. prev, .. items])),
                    }),
            (IEnumerable<TIn> enumerable, _) => Apply((TIn)(object)enumerable.ToList(), operation, config, cache),
            _ => execute(input),
        };
    }
}
