using System;

namespace Arsenal.Core.Result;

/// <summary>Functional result that carries either a value or a failure.</summary>
public readonly record struct Result<T>
{
    private Result(bool isSuccess, T? value, Failure? failure)
    {
        IsSuccess = isSuccess;
        Value = value;
        Failure = failure;
    }

    /// <summary>Gets a value indicating whether the result represents success.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the value if the result is successful.</summary>
    public T? Value { get; }

    /// <summary>Gets the failure if the result is unsuccessful.</summary>
    public Failure? Failure { get; }

    /// <summary>Creates a successful result with the specified value.</summary>
#pragma warning disable CA1000 // Do not declare static members on generic types - factory methods are acceptable
    public static Result<T> Success(T value) => new(true, value, null);

    /// <summary>Creates a failed result with the specified failure.</summary>
    public static Result<T> Fail(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Result<T>(false, default, failure);
    }
#pragma warning restore CA1000

    /// <summary>Attempts to get the value from the result.</summary>
    public bool TryGet(out T? value)
    {
        value = Value;
        return IsSuccess;
    }

    /// <summary>Executes side effects based on the result state without changing the result.</summary>
    public Result<T> Tap(Action<T> onSuccess, Action<Failure>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (IsSuccess && Value is not null)
        {
            onSuccess(Value);
        }
        else if (!IsSuccess && Failure is not null)
        {
            onFailure?.Invoke(Failure);
        }

        return this;
    }

    /// <summary>Transforms the value of a successful result using the specified function.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return IsSuccess && Value is not null
            ? Result<TOut>.Success(selector(Value))
            : Result<TOut>.Fail(Failure!);
    }

    /// <summary>Chains another result-returning operation to this result.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return IsSuccess && Value is not null
            ? binder(Value)
            : Result<TOut>.Fail(Failure!);
    }

    /// <summary>Matches the result state and returns a value based on success or failure.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Failure, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess && Value is not null
            ? onSuccess(Value)
            : onFailure(Failure!);
    }
}

/// <summary>Non-generic result state for operations that only signal success or failure.</summary>
public readonly record struct Result
{
    private Result(bool isSuccess, Failure? failure)
    {
        IsSuccess = isSuccess;
        Failure = failure;
    }

    /// <summary>Gets a value indicating whether the result represents success.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the failure if the result is unsuccessful.</summary>
    public Failure? Failure { get; }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, null);

    /// <summary>Creates a failed result with the specified failure.</summary>
    public static Result Fail(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Result(false, failure);
    }

    /// <summary>Converts this result to a generic result with the specified value.</summary>
    public Result<T> WithValue<T>(T value) =>
        IsSuccess ? Result<T>.Success(value) : Result<T>.Fail(Failure!);

    /// <summary>Matches the result state and returns a value based on success or failure.</summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Failure, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Failure!);
    }
}
