using System;
using System.Collections.Generic;

namespace Arsenal.Core;

/// <summary>
/// Operation result that either succeeds with a value or fails with an error message.
/// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types - factory methods are intentional for Result pattern
public readonly record struct Result<T>(bool Ok, T? Value, string? Error)
{
    /// <summary>Creates a successful result.</summary>
    public static Result<T> Success(T value) => new(true, value, null);

    /// <summary>Creates a failed result.</summary>
    public static Result<T> Fail(string error) => new(false, default, error);

    /// <summary>Gets the value if the result is successful.</summary>
    public bool TryGetValue(out T value)
    {
        if (Ok && Value is not null)
        {
            value = Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Maps the value of a successful result using the specified function.</summary>
    public Result<U> Map<U>(Func<T, U> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return Ok && Value is not null
            ? Result<U>.Success(mapper(Value))
            : Result<U>.Fail(Error!);
    }

    /// <summary>Binds the value of a successful result using the specified function.</summary>
    public Result<U> Bind<U>(Func<T, Result<U>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return Ok && Value is not null
            ? binder(Value)
            : Result<U>.Fail(Error!);
    }

    /// <summary>Executes the specified action if the result is successful.</summary>
    public Result<T> Tap(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Ok && Value is not null)
        {
            action(Value);
        }

        return this;
    }

    /// <summary>Ensures the value satisfies the specified condition.</summary>
    public Result<T> Ensure(Func<T, bool> predicate, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorMessage);

        return Ok && Value is not null && !predicate(Value)
            ? Result<T>.Fail(errorMessage)
            : this;
    }

    /// <summary>Matches the result against success and failure cases.</summary>
    public U Match<U>(Func<T, U> onSuccess, Func<string, U> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return Ok && Value is not null
            ? onSuccess(Value)
            : onFailure(Error!);
    }
}
#pragma warning restore CA1000

/// <summary>
/// Operation result that either succeeds or fails with an error message.
/// </summary>
public readonly record struct Result(bool Ok, string? Error)
{
    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, null);

    /// <summary>Creates a failed result.</summary>
    public static Result Fail(string error) => new(false, error);

    /// <summary>Converts a non-generic Result to a generic Result.</summary>
    public Result<T> WithValue<T>(T value)
    {
        return Ok
            ? Result<T>.Success(value)
            : Result<T>.Fail(Error!);
    }

    /// <summary>Executes the specified action if the result is successful.</summary>
    public Result Tap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Ok)
        {
            action();
        }

        return this;
    }

    /// <summary>Matches the result against success and failure cases.</summary>
    public T Match<T>(Func<T> onSuccess, Func<string, T> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return Ok
            ? onSuccess()
            : onFailure(Error!);
    }
}

/// <summary>
/// Helper methods for working with Result types.
/// </summary>
public static class Results
{
    /// <summary>Combines multiple results into a single result.</summary>
    public static Result<IReadOnlyList<T>> Combine<T>(params Result<T>[] results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<T> values = [];

        foreach (Result<T> result in results)
        {
            if (!result.Ok)
            {
                return Result<IReadOnlyList<T>>.Fail(result.Error!);
            }

            if (result.Value is not null)
            {
                values.Add(result.Value);
            }
        }

        return Result<IReadOnlyList<T>>.Success(values);
    }

    /// <summary>Combines multiple results into a single result.</summary>
    public static Result<IReadOnlyList<T>> Combine<T>(IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<T> values = [];

        foreach (Result<T> result in results)
        {
            if (!result.Ok)
            {
                return Result<IReadOnlyList<T>>.Fail(result.Error!);
            }

            if (result.Value is not null)
            {
                values.Add(result.Value);
            }
        }

        return Result<IReadOnlyList<T>>.Success(values);
    }

    /// <summary>Executes the specified function, catching exceptions and converting them to failures.</summary>
    public static Result<T> Try<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        try
        {
            T value = func();
            return Result<T>.Success(value);
        }
        catch (Exception ex)
        {
            return Result<T>.Fail(ex.Message);
        }
    }

    /// <summary>Executes the specified action, catching exceptions and converting them to failures.</summary>
    public static Result Try(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
    }
}
