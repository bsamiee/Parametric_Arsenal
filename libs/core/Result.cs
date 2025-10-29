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
    public TU Match<TU>(Func<T, TU> onSuccess, Func<ErrorInfo, TU> onError) =>
        IsSuccess ? onSuccess(Value!) : onError(Error!.Value);

    /// <summary>Maps success value to new type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TU> Map<TU>(Func<T, TU> transform)
    {
        if (IsSuccess)
        {
            return Result.Ok(transform(Value!));
        }

        ErrorInfo err = Error!.Value;
        return err.Exception is not null
            ? Result.Err<TU>(err.Code, err.Message, err.Exception)
            : Result.Err<TU>(err.Code, err.Message);
    }

    /// <summary>Chains result-returning operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TU> Bind<TU>(Func<T, Result<TU>> next)
    {
        if (IsSuccess)
        {
            return next(Value!);
        }

        ErrorInfo err = Error!.Value;
        return err.Exception is not null
            ? Result.Err<TU>(err.Code, err.Message, err.Exception)
            : Result.Err<TU>(err.Code, err.Message);
    }

    /// <summary>Transforms error if failed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> MapError(Func<ErrorInfo, ErrorInfo> transform)
    {
        if (IsFailure)
        {
            ErrorInfo transformed = transform(Error!.Value);
            return transformed.Exception is not null
                ? Result.Err<T>(transformed.Code, transformed.Message, transformed.Exception)
                : Result.Err<T>(transformed.Code, transformed.Message);
        }

        return this;
    }

    /// <summary>Applies wrapped function to wrapped value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TU> Apply<TU>(Result<Func<T, TU>> resultFunc)
    {
        if (resultFunc.IsSuccess && IsSuccess)
        {
            return Result.Ok(resultFunc.Value!(Value!));
        }

        if (resultFunc.IsFailure)
        {
            Result<Func<T, TU>>.ErrorInfo err = resultFunc.Error!.Value;
            return err.Exception is not null
                ? Result.Err<TU>(err.Code, err.Message, err.Exception)
                : Result.Err<TU>(err.Code, err.Message);
        }

        ErrorInfo error = Error!.Value;
        return error.Exception is not null
            ? Result.Err<TU>(error.Code, error.Message, error.Exception)
            : Result.Err<TU>(error.Code, error.Message);
    }

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
    public Result<TU> BiMap<TU>(Func<T, TU> onSuccess, Func<ErrorInfo, ErrorInfo> onError)
    {
        if (IsSuccess)
        {
            return Result.Ok(onSuccess(Value!));
        }

        ErrorInfo transformed = onError(Error!.Value);
        return transformed.Exception is not null
            ? Result.Err<TU>(transformed.Code, transformed.Message, transformed.Exception)
            : Result.Err<TU>(transformed.Code, transformed.Message);
    }

    /// <summary>Tries alternative result if failed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> OrElse(Func<ErrorInfo, Result<T>> alternative)
    {
        return IsFailure ? alternative(Error!.Value) : this;
    }

    /// <summary>Executes side effects without transforming the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Tap(Action<T> onSuccess)
    {
        if (IsSuccess)
        {
            onSuccess(Value!);
        }

        return this;
    }

    /// <summary>Executes side effects without transforming the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T> Tap(Action<T> onSuccess, Action<ErrorInfo> onError)
    {
        if (IsSuccess)
        {
            onSuccess(Value!);
        }
        else
        {
            onError(Error!.Value);
        }

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
    public static Result<T> Err<T>(string code, string message)
        => new(false, default, new Result<T>.ErrorInfo(code, message));

    /// <summary>Creates failed result.</summary>
    public static Result<T> Err<T>(string code, string message, Exception ex)
        => new(false, default, new Result<T>.ErrorInfo(code, message, ex));

    /// <summary>Map each item with a resultful function, aggregate or short-circuit.</summary>
    public static Result<ImmutableArray<TU>> Traverse<T, TU>(IEnumerable<T> items, Func<T, Result<TU>> transform)
    {
        ImmutableArray<TU>.Builder builder = ImmutableArray.CreateBuilder<TU>();
        foreach (T item in items)
        {
            Result<TU> result = transform(item);
            if (result.IsFailure)
            {
                Result<TU>.ErrorInfo err = result.Error!.Value;
                return err.Exception is not null
                    ? Err<ImmutableArray<TU>>(err.Code, err.Message, err.Exception)
                    : Err<ImmutableArray<TU>>(err.Code, err.Message);
            }

            builder.Add(result.Value!);
        }

        return Ok(builder.ToImmutable());
    }

    /// <summary>Aggregate successes or short-circuit on first error.</summary>
    public static Result<ImmutableArray<T>> Combine<T>(IEnumerable<Result<T>> results)
        => Traverse(results, r => r);
}
