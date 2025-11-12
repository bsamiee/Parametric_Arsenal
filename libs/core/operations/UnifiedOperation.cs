using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Diagnostics;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Operations;

/// <summary>Polymorphic operation engine with validation, caching, and parallelism.</summary>
public static class UnifiedOperation {
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<(Type Operation, Type Output), object>> _cache = [];

    /// <summary>Applies operation with validation, caching, and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<TOut>> Apply<TIn, TOut>(
        TIn input,
        object operation,
        OperationConfig<TIn, TOut> config,
        ConcurrentDictionary<(object, Type), object>? externalCache = null) where TIn : notnull {
        Type operationType = operation.GetType();

        Result<IReadOnlyList<TOut>> resolveOp(TIn item) => operation switch {
            Func<TIn, Result<IReadOnlyList<TOut>>> op => op(item),
            Func<TIn, V, Result<IReadOnlyList<TOut>>> deferred => deferred(item, config.ValidationMode),
            Func<TIn, Result<Result<IReadOnlyList<TOut>>>> nested => nested(item).Bind(inner => inner),
            Func<TIn, Result<TOut>> single => single(item).Map(v => (IReadOnlyList<TOut>)[v]),
            IReadOnlyList<Func<TIn, Result<TOut>>> ops => ops.Aggregate(
                ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
                (acc, op) => (config.AccumulateErrors, op(item)) switch {
                    (true, Result<TOut> res) => acc.Accumulate(res),
                    (_, Result<TOut> res) => acc.Bind(list => res.Map(v => (IReadOnlyList<TOut>)[.. list, v])),
                }),
            (Func<TIn, bool> pred, Func<TIn, Result<IReadOnlyList<TOut>>> op) =>
                pred(item) ? op(item) : ResultFactory.Create(value: (IReadOnlyList<TOut>)[]),
            _ => ResultFactory.Create<IReadOnlyList<TOut>>(
                error: E.Validation.UnsupportedOperationType.WithContext($"Type: {operationType}")),
        };

        Result<IReadOnlyList<TOut>> execute(TIn item) {
            Result<IReadOnlyList<TOut>> instrument(Result<IReadOnlyList<TOut>> r, bool hit) =>
                config.EnableDiagnostics && config.OperationName is not null
                    ? r.Capture(config.OperationName, validationApplied: config.ValidationMode, cacheHit: hit)
                    : r;

            Result<TIn> validated = ResultFactory.Create(value: item)
                .Ensure(config.InputFilter ?? (_ => true),
                    config.ErrorPrefix is null ? E.Validation.InputFiltered : E.Validation.InputFiltered.WithContext(config.ErrorPrefix))
                .Validate(args: config.ValidationMode == V.None ? null :
                    [config.Context, config.ValidationMode, .. config.ValidationArgs ?? []]);

            Result<IReadOnlyList<TOut>> compute() => (config.SkipInvalid ? validated.OnError(_ => item) : validated)
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

            (Result<IReadOnlyList<TOut>> Result, bool Hit) AcquireFromExternal(ConcurrentDictionary<(object, Type), object> dictionary) {
                object cacheKey = item!;
                (object, Type) key = (cacheKey, operationType);
                return dictionary.TryGetValue(key, out object? stored)
                    ? ((Result<IReadOnlyList<TOut>>)stored!, true)
                    : ((Result<IReadOnlyList<TOut>>)dictionary.GetOrAdd(
                        key,
                        static (_, factory) => factory(),
                        compute), false);
            }

            (Result<IReadOnlyList<TOut>> Result, bool Hit) AcquireFromInternal() {
                object cacheKey = item!;
                ConcurrentDictionary<(Type Operation, Type Output), object> dictionary = _cache.GetValue(cacheKey, static _ => new ConcurrentDictionary<(Type, Type), object>());
                (Type Operation, Type Output) key = (operationType, typeof(TOut));
                return dictionary.TryGetValue(key, out object? stored)
                    ? ((Result<IReadOnlyList<TOut>>)stored!, true)
                    : ((Result<IReadOnlyList<TOut>>)dictionary.GetOrAdd(
                        key,
                        static (_, factory) => factory(),
                        compute), false);
            }

            (Result<IReadOnlyList<TOut>> Result, bool Hit) resolved = externalCache is not null
                ? AcquireFromExternal(externalCache)
                : config.EnableCache
                    ? AcquireFromInternal()
                    : (compute(), false);

            return instrument(resolved.Result, resolved.Hit);
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
            (IEnumerable<TIn> enumerable, _) => Apply((TIn)(object)enumerable.ToList(), operation, config, externalCache),
            _ => execute(input),
        };
    }
}
