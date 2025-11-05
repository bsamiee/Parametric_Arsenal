using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Core.Results;

/// <summary>Polymorphic factory for creating and manipulating Result instances.</summary>
public static class ResultFactory {
    /// <summary>Creates Result using polymorphic parameter detection.</summary>
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

    /// <summary>Validates Result using polymorphic parameter detection.</summary>
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
            // Predicate validation with logical composition
            (Func<T, bool> p, null, null, _) when error.HasValue =>
                result.Ensure(unless is true ? x => !p(x) : conclusion is not null ? x => !p(x) || conclusion(x) : p, error.Value),
            // Monadic bind with conditional logic
            (Func<T, bool> p, Func<T, Result<T>> v, null, _) =>
                result.Bind(value => (unless is true ? !p(value) : p(value)) ? v(value) : Create(value: value)),
            // Batch validation
            (null, null, (Func<T, bool>, SystemError)[] vs, _) when vs?.Length > 0 => result.Ensure([.. vs]),
            // Geometry validation
            (null, null, null, [IGeometryContext ctx, ValidationMode mode]) when typeof(T).IsAssignableTo(typeof(GeometryBase)) =>
                result.Bind(g => ValidationRules.GetOrCompileValidator(g!.GetType(), mode)(g, ctx) switch {
                    { Length: 0 } => Create(value: g), var errs => Create<T>(errors: errs), }),
            (null, null, null, [IGeometryContext ctx]) when typeof(T).IsAssignableTo(typeof(GeometryBase)) =>
                result.Bind(g => ValidationRules.GetOrCompileValidator(g!.GetType(), ValidationMode.Standard)(g, ctx) switch {
                    { Length: 0 } => Create(value: g), var errs => Create<T>(errors: errs), }),
            (null, null, null, [Func<T, bool> p, SystemError e]) => result.Ensure(p, e),
            // Identity fallback
            _ => result,
        };

    /// <summary>Lifts functions into Result context with partial application.</summary>
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

    /// <summary>Traverses IEnumerable elements with monadic transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<TOut>> TraverseElements<TIn, TOut>(this Result<IEnumerable<TIn>> result, Func<TIn, Result<TOut>> selector) {
        ArgumentNullException.ThrowIfNull(selector);
        return result.Bind(items => items.Aggregate(
            Create<IReadOnlyList<TOut>>(value: new List<TOut>().AsReadOnly()),
            (acc, item) => acc.Bind(list => selector(item).Map(val => {
                List<TOut> newList = [.. list, val];
                return (IReadOnlyList<TOut>)newList.AsReadOnly();
            }))));
    }
}
