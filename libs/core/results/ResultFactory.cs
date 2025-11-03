using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Errors;

namespace Arsenal.Core.Results;

/// <summary>Polymorphic factory for creating and manipulating Result instances with advanced composition strategies.</summary>
public static class ResultFactory {
    /// <summary>Creates Result using polymorphic parameter detection for values, errors, deferred execution, conditionals, or nested Results.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Create<T>(
        T? value = default,
        SystemError[]? errors = null,
        SystemError? error = null,
        Func<Result<T>>? deferred = null,
        (Func<T, bool> Condition, SystemError Error)[]? conditionals = null,
        Result<Result<T>>? nested = null) =>
        (value, errors, error, deferred, conditionals, nested) switch {
            (var v, null, null, null, null, null) when v is not null =>
                new Result<T>(isSuccess: true, v, [], deferred: null),
            (null, var e, null, null, null, null) when e?.Length > 0 =>
                new Result<T>(isSuccess: false, default!, e, deferred: null),
            (null, null, var e, null, null, null) when e.HasValue =>
                new Result<T>(isSuccess: false, default!, [e.Value], deferred: null),
            (null, null, null, var d, null, null) when d is not null =>
                new Result<T>(isSuccess: false, default!, [], deferred: d),
            (var v, null, null, null, var conds, null) when v is not null && conds is not null =>
                new Result<T>(isSuccess: true, v, [], deferred: null).Ensure([.. conds]),
            (null, null, null, null, null, var n) when n.HasValue =>
                n.Value.Match(
                    onSuccess: inner => inner,
                    onFailure: errors => new Result<T>(isSuccess: false, default!, errors, deferred: null)),
            (null, null, null, null, null, null) =>
                new Result<T>(isSuccess: false, default!, [ResultErrors.Factory.NoValueProvided], deferred: null),
            _ => throw new ArgumentException(ResultErrors.Factory.InvalidCreateParameters.Message, nameof(value)),
        };

    /// <summary>Validates Result using polymorphic parameter detection for predicates, conditionals, implications, and batch operations.</summary>
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
        (predicate, error, validation, unless, premise, conclusion, validations, args) switch {
            (var p, var e, null, null, null, null, null, null) when p is not null && e.HasValue =>
                result.Ensure(p, e.Value),
            (var p, null, var v, var u, null, null, null, null) when p is not null && v is not null =>
                result.Bind(value => (u is true ? !p(value) : p(value)) ? v(value) : Create(value: value)),
            (null, var e, null, null, var pr, var co, null, null) when pr is not null && co is not null && e.HasValue =>
                result.Ensure((Func<T, bool>)(x => !pr(x) || co(x)), e.Value),
            var (_, _, _, _, _, _, vs, a) when vs?.Length > 0 || (a?.Length > 0 && a.All(x => x is (Func<T, bool>, SystemError))) =>
                result.Ensure([.. (vs ?? a?.Cast<(Func<T, bool>, SystemError)>().ToArray() ?? [])]),
            (null, null, null, null, null, null, null, var a) when a?.Length > 0 =>
                a switch {
                    [Func<T, bool> p, SystemError e] => result.Ensure(p, e),
                    _ => throw new ArgumentException(ResultErrors.Factory.InvalidValidateParameters.Message, nameof(args)),
                },
            (null, null, null, null, null, null, null, null) => result,
            _ => throw new ArgumentException(ResultErrors.Factory.InvalidValidateResult.Message, nameof(result)),
        };

    /// <summary>Lifts functions into Result context with partial application and monadic lifting based on argument type analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object Lift<TResult>(Delegate func, params object[] args) {
        ArgumentNullException.ThrowIfNull(func);
        ArgumentNullException.ThrowIfNull(args);

        return (func.Method.GetParameters().Length, args.Count(x => x.GetType().IsGenericType && x.GetType().GetGenericTypeDefinition() == typeof(Result<>)), args) switch {
            (var arity, 0, var a) when arity > a.Length =>
                Create<Func<object[], TResult>>(value: remaining => (TResult)func.DynamicInvoke([.. a, .. remaining])!),
            (var arity, var resultCount, var a) when arity == a.Length && resultCount == arity =>
                a.Cast<Result<object>>().Aggregate(
                    Create<IReadOnlyList<object>>(value: new List<object>().AsReadOnly()),
                    (acc, curr) => acc.Apply(curr.Map<Func<IReadOnlyList<object>, IReadOnlyList<object>>>(
                        value => list => [.. list, value])))
                .Map(values => (TResult)func.DynamicInvoke([.. values])!),
            _ => throw new ArgumentException($"{ResultErrors.Factory.InvalidLiftParameters.Message}: arity={func.Method.GetParameters().Length}, results={args.Count(x => x.GetType().IsGenericType && x.GetType().GetGenericTypeDefinition() == typeof(Result<>))}, args={args.Length}", nameof(args)),
        };
    }
}
