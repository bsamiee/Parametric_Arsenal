using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Results;

/// <summary>Factory for creating and manipulating Result.</summary>
public static class ResultFactory {
    /// <summary>True if type is Rhino.Geometry.*.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGeometryType(Type type) =>
        type.FullName?.StartsWith("Rhino.Geometry.", StringComparison.Ordinal) ?? false;

    /// <summary>Adds item to Result list via applicative composition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> Accumulate<T>(this Result<IReadOnlyList<T>> accumulator, Result<T> item) =>
        accumulator.Apply(item.Map<Func<IReadOnlyList<T>, IReadOnlyList<T>>>(v => list => [.. list, v]));

    /// <summary>Maps IEnumerable elements through Result function.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<TOut>> TraverseElements<TIn, TOut>(this Result<IEnumerable<TIn>> result, Func<TIn, Result<TOut>> selector) {
        return selector is null
            ? Create<IReadOnlyList<TOut>>(error: E.Results.NoValueProvided.WithContext("selector"))
            : result.Bind(items => items.Aggregate(Create<IReadOnlyList<TOut>>(value: []),
                (acc, item) => acc.Bind(list => selector(item).Map(val => (IReadOnlyList<TOut>)[.. list, val,]))));
    }

    /// <summary>Creates Result via polymorphic dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Create<T>(
        T? value = default,
        SystemError[]? errors = null,
        SystemError? error = null,
        Func<Result<T>>? deferred = null,
        (Func<T, bool> Condition, SystemError Error)[]? conditionals = null,
        Result<Result<T>>? nested = null) =>
        (value, errors, error, deferred, conditionals, nested) switch {
            (T v, null, null, null, null, null) when v is not null => new Result<T>(isSuccess: true, v, [], deferred: null),
            (_, SystemError[] e, null, null, null, null) when e.Length > 0 => new Result<T>(isSuccess: false, default!, e, deferred: null),
            (_, null, SystemError e, null, null, null) => new Result<T>(isSuccess: false, default!, [e,], deferred: null),
            (_, null, null, Func<Result<T>> d, null, null) when d is not null => new Result<T>(isSuccess: false, default!, [], deferred: d),
            (T v, null, null, null, (Func<T, bool>, SystemError)[] conds, null) when v is not null && conds is not null => new Result<T>(isSuccess: true, v, [], deferred: null).Ensure([.. conds]),
            (_, null, null, null, null, Result<Result<T>> nestedResult) => nestedResult.Match(onSuccess: inner => inner, onFailure: errs => new Result<T>(isSuccess: false, default!, errs, deferred: null)),
            (_, null, null, null, null, null) => new Result<T>(isSuccess: false, default!, [E.Results.NoValueProvided,], deferred: null),
            _ => Create<T>(errors: [E.Results.InvalidCreate.WithContext(nameof(value)),]),
        };

    /// <summary>Validates Result via polymorphic dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Validate<T>(
        this Result<T> result,
        Func<T, bool>? predicate = null,
        SystemError? error = null,
        Func<T, Result<T>>? validation = null,
        bool? unless = null,
        Func<T, bool>? premise = null,
        Func<T, bool>? conclusion = null,
        (Func<T, bool>, SystemError)[]? validations = null,
        object[]? args = null) =>
        (predicate ?? premise, validation, validations, args) switch {
            (Func<T, bool> p, null, null, _) when error.HasValue => result.Ensure(unless is true ? x => !p(x) : conclusion is not null ? x => !p(x) || conclusion(x) : p, error.Value),
            (Func<T, bool> p, Func<T, Result<T>> v, null, _) => result.Bind(value => (unless is true ? !p(value) : p(value)) ? v(value) : Create(value: value)),
            (null, null, (Func<T, bool>, SystemError)[] vs, _) when vs?.Length > 0 => result.Ensure(vs),
            (null, null, null, [IGeometryContext ctx, V mode]) when IsGeometryType(typeof(T)) => result.Bind(g => ValidationRules.GetOrCompileValidator(g!.GetType(), mode)(g, ctx) switch { { Length: 0 } => Create(value: g),
                SystemError[] errs => Create<T>(errors: errs),
            }),
            (null, null, null, [IGeometryContext ctx]) when IsGeometryType(typeof(T)) => result.Bind(g => ValidationRules.GetOrCompileValidator(g!.GetType(), V.Standard)(g, ctx) switch { { Length: 0 } => Create(value: g),
                SystemError[] errs => Create<T>(errors: errs),
            }),
            (null, null, null, [Func<T, bool> p, SystemError e]) => result.Ensure(p, e),
            _ => result,
        };

    /// <summary>Lifts function into Result context with partial application.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object Lift<TResult>(Delegate func, params object[] args) =>
        (func, args) switch {
            (null, _) => Create<TResult>(error: E.Results.NoValueProvided.WithContext("func")),
            (_, null) => Create<TResult>(error: E.Results.NoValueProvided.WithContext("args")),
            (Delegate actual, object[] actualArgs) => (
                actual.Method.GetParameters().Length,
                actualArgs.Count(argument => argument.GetType() is { IsGenericType: true } resultType && resultType.GetGenericTypeDefinition() == typeof(Result<>)),
                actualArgs.Count(argument => !(argument.GetType() is { IsGenericType: true } resultType && resultType.GetGenericTypeDefinition() == typeof(Result<>))),
                actualArgs) switch {
                    (int arity, 0, int nonResultCount, object[] argArray) when arity > argArray.Length && nonResultCount == argArray.Length =>
                        Create<Func<object[], TResult>>(value: remaining => (TResult)actual.DynamicInvoke([.. argArray, .. remaining])!),
                    (int arity, int resultCount, 0, object[] argArray) when arity == argArray.Length && resultCount == arity =>
                        argArray.Aggregate(Create<IReadOnlyList<object>>(value: []),
                            (acc, arg) => {
                                Type argType = arg.GetType();
                                return argType is { IsGenericType: true } genericType && genericType.GetGenericTypeDefinition() == typeof(Result<>) ?
                                    ((bool)genericType.GetProperty("IsSuccess")!.GetValue(arg)!, genericType.GetProperty("Value")!.GetValue(arg), (IReadOnlyList<SystemError>)genericType.GetProperty("Errors")!.GetValue(arg)!) switch {
                                        (true, object resultValue, _) when acc.IsSuccess => Create<IReadOnlyList<object>>(value: [.. acc.Value, resultValue,]),
                                        (false, _, IReadOnlyList<SystemError> resultErrors) when acc.IsSuccess => Create<IReadOnlyList<object>>(errors: [.. resultErrors,]),
                                        (false, _, IReadOnlyList<SystemError> resultErrors) => Create<IReadOnlyList<object>>(errors: [.. acc.Errors, .. resultErrors,]),
                                        _ => acc,
                                    } : acc.Map(list => (IReadOnlyList<object>)[.. list, arg,]);
                            })
                        .Map(values => (TResult)actual.DynamicInvoke([.. values])!),
                    (int arity, int resultCount, 0, object[] argArray) when resultCount == argArray.Length && arity >= 3 && arity > argArray.Length =>
                        argArray.Aggregate(Create<IReadOnlyList<object>>(value: []),
                            (acc, arg) => arg.GetType() is { IsGenericType: true } genericType && genericType.GetGenericTypeDefinition() == typeof(Result<>) ?
                                ((bool)genericType.GetProperty("IsSuccess")!.GetValue(arg)!, genericType.GetProperty("Value")!.GetValue(arg), ((IReadOnlyList<SystemError>)genericType.GetProperty("Errors")!.GetValue(arg)!).ToArray()) switch {
                                    (true, object resultValue, _) when acc.IsSuccess => Create<IReadOnlyList<object>>(value: [.. acc.Value, resultValue,]),
                                    (false, _, SystemError[] resultErrors) => Create<IReadOnlyList<object>>(errors: resultErrors),
                                    _ => acc,
                                } : acc.Map(list => (IReadOnlyList<object>)[.. list, arg,]))
                        .Map(unwrapped => (Func<object[], TResult>)(remaining => (TResult)actual.DynamicInvoke([.. unwrapped, .. remaining])!)),
                    (int arity, int resultCount, int nonResultCount, object[] argArray) when resultCount > 0 && nonResultCount > 0 && arity > argArray.Length =>
                        argArray.Aggregate(Create<IReadOnlyList<object>>(value: []),
                            (acc, arg) => arg.GetType() is { IsGenericType: true } genericType && genericType.GetGenericTypeDefinition() == typeof(Result<>) ?
                                ((bool)genericType.GetProperty("IsSuccess")!.GetValue(arg)!, genericType.GetProperty("Value")!.GetValue(arg), ((IReadOnlyList<SystemError>)genericType.GetProperty("Errors")!.GetValue(arg)!).ToArray()) switch {
                                    (true, object resultValue, _) when acc.IsSuccess => Create<IReadOnlyList<object>>(value: [.. acc.Value, resultValue,]),
                                    (false, _, SystemError[] resultErrors) => Create<IReadOnlyList<object>>(errors: resultErrors),
                                    _ => acc,
                                } : acc.Map(list => (IReadOnlyList<object>)[.. list, arg,]))
                        .Map(unwrapped => (Func<object[], TResult>)(remaining => (TResult)actual.DynamicInvoke([.. unwrapped, .. remaining])!)),
                    (int arity, int resultCount, _, object[] argArray) => Create<TResult>(errors: [E.Results.InvalidLift.WithContext(string.Create(CultureInfo.InvariantCulture,
                        $"{E.Results.InvalidLift.Message}: arity={arity.ToString(CultureInfo.InvariantCulture)}, results={resultCount.ToString(CultureInfo.InvariantCulture)}, args={argArray.Length.ToString(CultureInfo.InvariantCulture)}")),
                    ]),
                },
        };
}
