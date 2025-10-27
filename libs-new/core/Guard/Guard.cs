using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Result;

namespace Arsenal.Core.Guard;

/// <summary>Guard utilities that promote failures instead of thrown exceptions.</summary>
public static class Guard
{
    /// <summary>Guards against null values.</summary>
    /// <typeparam name="T">The type of the value being validated.</typeparam>
    /// <param name="candidate">The value to validate.</param>
    /// <param name="name">The name of the parameter for error messages.</param>
    /// <returns>A result containing the value if not null, or a failure if null.</returns>
    public static Result<T> AgainstNull<T>(T? candidate, string name) where T : class
    {
        if (candidate is null)
        {
            return Result<T>.Fail(new Failure("validation.null", $"{name} cannot be null."));
        }

        return Result<T>.Success(candidate);
    }

    /// <summary>Guards against values that match a predicate condition.</summary>
    /// <typeparam name="T">The type of the value being validated.</typeparam>
    /// <param name="value">The value to validate.</param>
    /// <param name="predicate">The condition that indicates failure when true.</param>
    /// <param name="failureCode">The failure code to use if validation fails.</param>
    /// <param name="failureMessage">The failure message to use if validation fails.</param>
    /// <returns>A result containing the value if valid, or a failure if the predicate matches.</returns>
    public static Result<T> Against<T>(T value, Func<T, bool> predicate, string failureCode, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return predicate(value)
            ? Result<T>.Fail(new Failure(failureCode, failureMessage))
            : Result<T>.Success(value);
    }

    /// <summary>Applies multiple guard functions to a value, stopping at the first failure.</summary>
    /// <typeparam name="T">The type of the value being validated.</typeparam>
    /// <param name="value">The value to validate.</param>
    /// <param name="guards">The guard functions to apply in sequence.</param>
    /// <returns>A result containing the value if all guards pass, or the first failure encountered.</returns>
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
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="items">The collection to validate.</param>
    /// <param name="name">The name of the parameter for error messages.</param>
    /// <returns>A result containing the collection if not null or empty, or a failure otherwise.</returns>
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
