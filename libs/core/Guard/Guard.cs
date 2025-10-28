using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Result;

namespace Arsenal.Core.Guard;

/// <summary>Guard utilities that promote failures instead of thrown exceptions.</summary>
public static class Guard
{
    /// <summary>Guards against null values.</summary>
    public static Result<T> AgainstNull<T>(T? candidate, string name) where T : class
    {
        if (candidate is null)
        {
            return Result<T>.Fail(new Failure("validation.null", $"{name} cannot be null."));
        }

        return Result<T>.Success(candidate);
    }

    /// <summary>Guards against values that match a predicate condition.</summary>
    public static Result<T> Against<T>(T value, Func<T, bool> predicate, string failureCode, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return predicate(value)
            ? Result<T>.Fail(new Failure(failureCode, failureMessage))
            : Result<T>.Success(value);
    }

    /// <summary>Applies multiple guard functions to a value, stopping at the first failure.</summary>
    public static Result<T> RequireAll<T>(T value, params Func<T, Result<T>>[] guards)
    {
        ArgumentNullException.ThrowIfNull(guards);

        Result<T> current = Result<T>.Success(value);

        foreach (Func<T, Result<T>> guard in guards)
        {
            current = current.Bind(guard);
            if (!current.IsSuccess)
            {
                return current;
            }
        }

        return current;
    }

    /// <summary>Guards against null or empty collections.</summary>
    public static Result<IReadOnlyCollection<T>> AgainstEmpty<T>(IEnumerable<T>? items, string name)
    {
        if (items is null)
        {
            return Result<IReadOnlyCollection<T>>.Fail(new Failure("validation.null", $"{name} cannot be null."));
        }

        IReadOnlyCollection<T> collection = items as IReadOnlyCollection<T> ?? items.ToArray();
        return collection.Count == 0
            ? Result<IReadOnlyCollection<T>>.Fail(new Failure("validation.empty", $"{name} must contain at least one item."))
            : Result<IReadOnlyCollection<T>>.Success(collection);
    }
}
