using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Results;

/// <summary>Polymorphic factory for creating and manipulating Result instances.</summary>
public static class ResultFactory {
    /// <summary>Creates Result using polymorphic parameter detection with explicit value semantics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Create<T>(
        T? value = default,
        SystemError[]? errors = null,
        SystemError? error = null,
        Func<Result<T>>? deferred = null,
        (Func<T, bool> Condition, SystemError Error)[]? conditionals = null,
        Result<Result<T>>? nested = null) =>
        (value, errors, error, deferred, conditionals, nested) switch {
            (var v, null, null, null, null, null) when v is not null => new Result<T>(isSuccess: true, v, [], deferred: null),
            (_, var e, null, null, null, null) when e?.Length > 0 => new Result<T>(isSuccess: false, default!, e, deferred: null),
            (_, null, var e, null, null, null) when e.HasValue => new Result<T>(isSuccess: false, default!, [e.Value,], deferred: null),
            (_, null, null, var d, null, null) when d is not null => new Result<T>(isSuccess: false, default!, [], deferred: d),
            (var v, null, null, null, var conds, null) when v is not null && conds is not null => new Result<T>(isSuccess: true, v, [], deferred: null).Ensure([.. conds]),
            (_, null, null, null, null, var n) when n.HasValue => n.Value.Match(onSuccess: inner => inner, onFailure: errs => new Result<T>(isSuccess: false, default!, errs, deferred: null)),
            (_, null, null, null, null, null) => new Result<T>(isSuccess: false, default!, [ErrorRegistry.Results.NoValueProvided,], deferred: null),
            _ => throw new ArgumentException(ErrorRegistry.Results.InvalidCreateParameters.Message, nameof(value)),
        };

    /// <summary>Validates Result using polymorphic parameter detection with unified validation semantics.</summary>
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
            (null, null, null, [IGeometryContext ctx, ValidationConfig config]) when IsGeometryType(typeof(T)) => result.Bind(g => ValidationRules.GetOrCompileValidator(g!.GetType(), config)(g, ctx) switch { { Length: 0 } => Create(value: g),
                var errs => Create<T>(errors: errs),
            }),
            (null, null, null, [IGeometryContext ctx]) when IsGeometryType(typeof(T)) => result.Bind(g => ValidationRules.GetOrCompileValidator(g!.GetType(), ValidationConfig.Standard)(g, ctx) switch { { Length: 0 } => Create(value: g),
                var errs => Create<T>(errors: errs),
            }),
            (null, null, null, [Func<T, bool> p, SystemError e]) => result.Ensure(p, e),
            _ => result,
        };

    /// <summary>Lifts functions into Result context with partial application and Result unwrapping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object Lift<TResult>(Delegate func, params object[] args) {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(args);
        return (func.Method.GetParameters().Length, args.Count(x => x.GetType() is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(Result<>)), args.Count(x => !(x.GetType() is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(Result<>))), args) switch {
            (var ar, 0, var nrc, var a) when ar > a.Length && nrc == a.Length =>
                Create<Func<object[], TResult>>(value: remaining => (TResult)func.DynamicInvoke([.. a, .. remaining])!),
            (var ar, var rc, 0, var a) when ar == a.Length && rc == ar =>
                a.Cast<Result<object>>().Aggregate(Create<IReadOnlyList<object>>(value: new List<object>().AsReadOnly()),
                    (acc, curr) => acc.Accumulate(curr))
                .Map(values => (TResult)func.DynamicInvoke([.. values])!),
            (var ar, var rc, 0, var a) when rc == a.Length && ar >= 3 && ar > a.Length =>
                a.Aggregate(Create<IReadOnlyList<object>>(value: new List<object>().AsReadOnly()),
                    (acc, arg) => arg.GetType() is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(Result<>) ?
                        ((bool)t.GetProperty(nameof(Result<object>.IsSuccess))!.GetValue(arg)!, t.GetProperty(nameof(Result<object>.Value))!.GetValue(arg), ((IReadOnlyList<SystemError>)t.GetProperty(nameof(Result<object>.Errors))!.GetValue(arg)!).ToArray()) switch {
                            (true, var v, _) when acc.IsSuccess => Create<IReadOnlyList<object>>(value: [.. acc.Value, v!,]),
                            (false, _, var errs) => Create<IReadOnlyList<object>>(errors: errs),
                            _ => acc,
                        } : acc.Map(list => (IReadOnlyList<object>)[.. list, arg,]))
                .Map(unwrapped => (Func<object[], TResult>)(remaining => (TResult)func.DynamicInvoke([.. unwrapped, .. remaining])!)),
            (var ar, var rc, var nrc, var a) when rc > 0 && nrc > 0 && ar > a.Length =>
                a.Aggregate(Create<IReadOnlyList<object>>(value: new List<object>().AsReadOnly()),
                    (acc, arg) => arg.GetType() is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(Result<>) ?
                        ((bool)t.GetProperty(nameof(Result<object>.IsSuccess))!.GetValue(arg)!, t.GetProperty(nameof(Result<object>.Value))!.GetValue(arg), ((IReadOnlyList<SystemError>)t.GetProperty(nameof(Result<object>.Errors))!.GetValue(arg)!).ToArray()) switch {
                            (true, var v, _) when acc.IsSuccess => Create<IReadOnlyList<object>>(value: [.. acc.Value, v!,]),
                            (false, _, var errs) => Create<IReadOnlyList<object>>(errors: errs),
                            _ => acc,
                        } : acc.Map(list => (IReadOnlyList<object>)[.. list, arg,]))
                .Map(unwrapped => (Func<object[], TResult>)(remaining => (TResult)func.DynamicInvoke([.. unwrapped, .. remaining])!)),
            (var ar, var rc, _, var a) => throw new ArgumentException(string.Create(CultureInfo.InvariantCulture,
                $"{ErrorRegistry.Results.InvalidLiftParameters.Message}: arity={ar.ToString(CultureInfo.InvariantCulture)}, results={rc.ToString(CultureInfo.InvariantCulture)}, args={a.Length.ToString(CultureInfo.InvariantCulture)}"), nameof(args)),
        };
    }

    /// <summary>Traverses IEnumerable elements with monadic transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<TOut>> TraverseElements<TIn, TOut>(this Result<IEnumerable<TIn>> result, Func<TIn, Result<TOut>> selector) {
        ArgumentNullException.ThrowIfNull(selector);
        return result.Bind(items => items.Aggregate(Create<IReadOnlyList<TOut>>(value: new List<TOut>().AsReadOnly()),
            (acc, item) => acc.Bind(list => selector(item).Map(val => (IReadOnlyList<TOut>)((List<TOut>)[.. list, val,]).AsReadOnly()))));
    }

    /// <summary>Accumulates item into Result list using applicative error composition and parallel validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> Accumulate<T>(this Result<IReadOnlyList<T>> accumulator, Result<T> item) =>
        accumulator.Apply(item.Map<Func<IReadOnlyList<T>, IReadOnlyList<T>>>(v => list => [.. list, v]));

    /// <summary>Checks if type is Geometry without loading Rhino assembly using string comparison.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGeometryType(Type type) =>
        type.FullName?.StartsWith("Rhino.Geometry.", StringComparison.Ordinal) ?? false;
}
