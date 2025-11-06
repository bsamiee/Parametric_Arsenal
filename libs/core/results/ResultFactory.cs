using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Results;

/// <summary>Polymorphic factory for creating and manipulating Result instances.</summary>
public static class ResultFactory {
    /// <summary>Creates Result using polymorphic input detection with unified semantics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Create<T>(object? input = null) =>
        input switch {
            T value => new Result<T>(isSuccess: true, value, [], deferred: null),
            SystemError err => new Result<T>(isSuccess: false, default!, [err,], deferred: null),
            SystemError[] errs when errs.Length > 0 => new Result<T>(isSuccess: false, default!, errs, deferred: null),
            Func<Result<T>> deferred => new Result<T>(isSuccess: false, default!, [], deferred: deferred),
            Result<Result<T>> nested => nested.Match(onSuccess: inner => inner, onFailure: errs => new Result<T>(isSuccess: false, default!, errs, deferred: null)),
            Result<T> existing => existing,
            null => new Result<T>(isSuccess: false, default!, [ResultErrors.Factory.NoValueProvided,], deferred: null),
            _ => throw new ArgumentException(ResultErrors.Factory.InvalidCreateParameters.Message, nameof(input)),
        };

    /// <summary>Extension for geometry validation using compiled validators.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ValidateGeometry<T>(
        this Result<T> result,
        IGeometryContext context,
        ValidationMode mode = ValidationMode.Standard) where T : notnull =>
        IsGeometryType(typeof(T)) switch {
            true => result.Bind(g => ValidationRules.GetOrCompileValidator(g!.GetType(), mode)(g, context) switch { { Length: 0 } => new Result<T>(isSuccess: true, g, [], deferred: null),
                var errs => new Result<T>(isSuccess: false, default!, errs, deferred: null),
            }),
            false => result,
        };

    /// <summary>Combines multiple Results into single Result using applicative composition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2) =>
        (r1.IsSuccess, r2.IsSuccess) switch {
            (true, true) => new Result<(T1, T2)>(isSuccess: true, (r1.Value, r2.Value), [], deferred: null),
            _ => new Result<(T1, T2)>(isSuccess: false, default!, [.. r2.Errors, .. r1.Errors,], deferred: null),
        };

    /// <summary>Combines multiple Results into single Result using applicative composition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> r1, Result<T2> r2, Result<T3> r3) =>
        (r1.IsSuccess, r2.IsSuccess, r3.IsSuccess) switch {
            (true, true, true) => new Result<(T1, T2, T3)>(isSuccess: true, (r1.Value, r2.Value, r3.Value), [], deferred: null),
            _ => new Result<(T1, T2, T3)>(isSuccess: false, default!, [.. r3.Errors, .. r2.Errors, .. r1.Errors,], deferred: null),
        };

    /// <summary>Combines multiple Results into single Result using applicative composition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(T1, T2, T3, T4)> Combine<T1, T2, T3, T4>(Result<T1> r1, Result<T2> r2, Result<T3> r3, Result<T4> r4) =>
        (r1.IsSuccess, r2.IsSuccess, r3.IsSuccess, r4.IsSuccess) switch {
            (true, true, true, true) => new Result<(T1, T2, T3, T4)>(isSuccess: true, (r1.Value, r2.Value, r3.Value, r4.Value), [], deferred: null),
            _ => new Result<(T1, T2, T3, T4)>(isSuccess: false, default!, [.. r4.Errors, .. r3.Errors, .. r2.Errors, .. r1.Errors,], deferred: null),
        };

    /// <summary>Traverses collection with monadic or applicative composition based on accumulation mode.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(
        this IEnumerable<TIn> items,
        Func<TIn, Result<TOut>> selector,
        bool accumulateErrors = false) {
        ArgumentNullException.ThrowIfNull(selector);
        IReadOnlyList<TOut> empty = Array.Empty<TOut>();
        return accumulateErrors switch {
            true => items.Select(selector).Aggregate(
                new Result<IReadOnlyList<TOut>>(isSuccess: true, empty, [], deferred: null),
                (acc, curr) => acc.Apply(curr.Map<Func<IReadOnlyList<TOut>, IReadOnlyList<TOut>>>(v => list => [.. list, v]))),
            false => items.Aggregate(
                new Result<IReadOnlyList<TOut>>(isSuccess: true, empty, [], deferred: null),
                (acc, item) => acc.Bind(list => selector(item).Map(val => (IReadOnlyList<TOut>)[.. list, val]))),
        };
    }

    /// <summary>Accumulates item into Result list using applicative composition with error accumulation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> Accumulate<T>(this Result<IReadOnlyList<T>> accumulator, Result<T> item) =>
        accumulator.Apply(item.Map<Func<IReadOnlyList<T>, IReadOnlyList<T>>>(v => list => [.. list, v]));

    /// <summary>Checks if type is Geometry without loading Rhino assembly using string comparison.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGeometryType(Type type) =>
        type.FullName?.StartsWith("Rhino.Geometry.", StringComparison.Ordinal) ?? false;
}
