using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core;

/// <summary>Allocation-free success/failure container with typed errors.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[StructLayout(LayoutKind.Auto)]
public readonly record struct Result<T>(bool IsSuccess, T? Value, Result<T>.ErrorInfo? Error)
{
    /// <summary>Lightweight typed error with code, message, and optional exception.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct ErrorInfo(string Code, string Message, Exception? Exception = null)
    {
        /// <summary>Returns formatted error string with code and message.</summary>
        public override string ToString() => $"{Code}: {Message}";
    }

    /// <summary>True if operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets value if successful.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(out T value)
    {
        if (IsSuccess && Value is not null)
        {
            value = Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Gets value or fallback if failed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValueOr(T fallback) => IsSuccess ? Value! : fallback;

    /// <summary>Transforms result using success or error function.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public U Match<U>(Func<T, U> onSuccess, Func<ErrorInfo, U> onError) =>
        IsSuccess ? onSuccess(Value!) : onError(Error!.Value);

    /// <summary>Maps success value to new type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<U> Map<U>(Func<T, U> transform)
    {
        if (IsSuccess) return Result.Ok(transform(Value!));
        ErrorInfo err = Error!.Value;
        return Result.Err<U>(err.Code, err.Message, err.Exception);
    }

    /// <summary>Chains result-returning operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<U> Bind<U>(Func<T, Result<U>> next)
    {
        if (IsSuccess) return next(Value!);
        ErrorInfo err = Error!.Value;
        return Result.Err<U>(err.Code, err.Message, err.Exception);
    }

    /// <summary>Transforms error if failed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> MapError(Func<ErrorInfo, ErrorInfo> transform)
    {
        if (IsFailure)
        {
            ErrorInfo transformed = transform(Error!.Value);
            return Result.Err<T>(transformed.Code, transformed.Message, transformed.Exception);
        }

        return this;
    }

    /// <summary>Applies wrapped function to wrapped value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<U> Apply<U>(Result<Func<T, U>> resultFunc) =>
        resultFunc.IsSuccess && IsSuccess
            ? Result.Ok(resultFunc.Value!(Value!))
            : resultFunc.IsFailure
                ? Result.Err<U>(resultFunc.Error!.Value.Code, resultFunc.Error!.Value.Message,
                    resultFunc.Error!.Value.Exception)
                : Result.Err<U>(Error!.Value.Code, Error!.Value.Message, Error!.Value.Exception);

    /// <summary>Recovers from failure with fallback function.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Recover(Func<ErrorInfo, T> recovery) =>
        IsFailure ? Result.Ok(recovery(Error!.Value)) : this;

    /// <summary>Validates success value with predicate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Ensure(Func<T, bool> predicate, string code, string message) =>
        IsSuccess && !predicate(Value!) ? Result.Err<T>(code, message) : this;

    /// <summary>Transforms both success and error paths.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<U> BiMap<U>(Func<T, U> onSuccess, Func<ErrorInfo, ErrorInfo> onError)
    {
        if (IsSuccess) return Result.Ok(onSuccess(Value!));
        ErrorInfo transformed = onError(Error!.Value);
        return Result.Err<U>(transformed.Code, transformed.Message, transformed.Exception);
    }

    /// <summary>Tries alternative result if failed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> OrElse(Func<ErrorInfo, Result<T>> alternative) =>
        IsFailure ? alternative(Error!.Value) : this;

    /// <summary>Executes side effects without transforming the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Tap(Action<T> onSuccess, Action<ErrorInfo>? onError = null)
    {
        if (IsSuccess) onSuccess(Value!);
        else if (onError is not null) onError(Error!.Value);
        return this;
    }

    /// <summary>Implicit conversion from value to success result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<T>(T value) => Result.Ok(value);

    /// <summary>Implicit conversion from error to failure result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<T>(ErrorInfo error) => new(false, default, error);

    private string DebuggerDisplay => IsSuccess ? $"Ok({Value})" : $"Err({Error})";
}

/// <summary>Helpers for batching, traversal, and construction.</summary>
public static class Result
{
    /// <summary>Creates successful result.</summary>
    public static Result<T> Ok<T>(T value) => new(true, value, null);

    /// <summary>Creates failed result.</summary>
    public static Result<T> Err<T>(string code, string message, Exception? ex = null)
        => new(false, default, new Result<T>.ErrorInfo(code, message, ex));

    /// <summary>Map each item with a resultful function, aggregate or short-circuit.</summary>
    public static Result<ImmutableArray<U>> Traverse<T, U>(IEnumerable<T> items, Func<T, Result<U>> transform)
    {
        ImmutableArray<U>.Builder builder = ImmutableArray.CreateBuilder<U>();
        foreach (T item in items)
        {
            Result<U> result = transform(item);
            if (result.IsFailure)
            {
                return Err<ImmutableArray<U>>(result.Error!.Value.Code, result.Error!.Value.Message,
                    result.Error!.Value.Exception);
            }

            builder.Add(result.Value!);
        }

        return Ok(builder.ToImmutable());
    }

    /// <summary>Aggregate successes or short-circuit on first error.</summary>
    public static Result<ImmutableArray<T>> Combine<T>(IEnumerable<Result<T>> results)
        => Traverse(results, r => r);
}
