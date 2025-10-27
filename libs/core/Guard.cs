using System;
using System.Collections.Generic;
using System.Linq;

namespace Arsenal.Core;

/// <summary>
/// Validation helpers that return Result&lt;T&gt; for expected validation failures.
/// </summary>
public static class Guard
{
    /// <summary>Validates that a reference type value is not null.</summary>
    public static Result<T> RequireNonNull<T>(T? value, string paramName) where T : class
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value switch
        {
            null => Result<T>.Fail($"{paramName} cannot be null"),
            _ => Result<T>.Success(value)
        };
    }

    /// <summary>Validates that a numeric value is non-negative.</summary>
    public static Result<double> RequireNonNegative(double value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value switch
        {
            < 0 => Result<double>.Fail($"{paramName} must be non-negative, but was {value}"),
            _ => Result<double>.Success(value)
        };
    }

    /// <summary>Validates that a numeric value is non-negative.</summary>
    public static Result<int> RequireNonNegative(int value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value switch
        {
            < 0 => Result<int>.Fail($"{paramName} must be non-negative, but was {value}"),
            _ => Result<int>.Success(value)
        };
    }

    /// <summary>Validates that a numeric value is positive.</summary>
    public static Result<double> RequirePositive(double value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value switch
        {
            <= 0 => Result<double>.Fail($"{paramName} must be positive, but was {value}"),
            _ => Result<double>.Success(value)
        };
    }

    /// <summary>Validates that a numeric value is positive.</summary>
    public static Result<int> RequirePositive(int value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value switch
        {
            <= 0 => Result<int>.Fail($"{paramName} must be positive, but was {value}"),
            _ => Result<int>.Success(value)
        };
    }

    /// <summary>Validates that a numeric value is within a specified range.</summary>
    public static Result<double> RequireInRange(double value, double min, double max, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value < min || value > max
            ? Result<double>.Fail($"{paramName} must be between {min} and {max}, but was {value}")
            : Result<double>.Success(value);
    }

    /// <summary>Validates that a numeric value is within a specified range.</summary>
    public static Result<int> RequireInRange(int value, int min, int max, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value < min || value > max
            ? Result<int>.Fail($"{paramName} must be between {min} and {max}, but was {value}")
            : Result<int>.Success(value);
    }

    /// <summary>Validates that a string is not null or empty.</summary>
    public static Result<string> RequireNonEmpty(string? value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return value switch
        {
            null or "" => Result<string>.Fail($"{paramName} cannot be null or empty"),
            _ => Result<string>.Success(value)
        };
    }

    /// <summary>Validates that a string is not null, empty, or whitespace.</summary>
    public static Result<string> RequireNonWhiteSpace(string? value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        return string.IsNullOrWhiteSpace(value) switch
        {
            true => Result<string>.Fail($"{paramName} cannot be null, empty, or whitespace"),
            false => Result<string>.Success(value)
        };
    }

    /// <summary>Validates that a collection is not null or empty.</summary>
    public static Result<IEnumerable<T>> RequireNonEmpty<T>(IEnumerable<T>? value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(paramName);

        if (value is null)
        {
            return Result<IEnumerable<T>>.Fail($"{paramName} cannot be null or empty");
        }

        IEnumerable<T> materialized = value as ICollection<T> ?? value.ToList();
        return !materialized.Any()
            ? Result<IEnumerable<T>>.Fail($"{paramName} cannot be null or empty")
            : Result<IEnumerable<T>>.Success(materialized);
    }

    /// <summary>Validates that a value satisfies a custom predicate.</summary>
    public static Result<T> Require<T>(T value, Func<T, bool> predicate, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorMessage);

        return predicate(value)
            ? Result<T>.Success(value)
            : Result<T>.Fail(errorMessage);
    }

    /// <summary>Validates multiple conditions and aggregates all validation errors.</summary>
    public static Result ValidateAll(params Result[] validations)
    {
        ArgumentNullException.ThrowIfNull(validations);

        List<string> errors = [];

        foreach (Result validation in validations)
        {
            if (validation is { Ok: false, Error: not null })
            {
                errors.Add(validation.Error);
            }
        }

        return errors.Count == 0
            ? Result.Success()
            : Result.Fail(string.Join("; ", errors));
    }

    /// <summary>Validates multiple conditions and aggregates all validation errors.</summary>
    public static Result ValidateAll(IEnumerable<Result> validations)
    {
        ArgumentNullException.ThrowIfNull(validations);

        List<string> errors = [];

        foreach (Result validation in validations)
        {
            if (validation is { Ok: false, Error: not null })
            {
                errors.Add(validation.Error);
            }
        }

        return errors.Count == 0
            ? Result.Success()
            : Result.Fail(string.Join("; ", errors));
    }

    /// <summary>Validates that a value passes all specified validators.</summary>
    public static Result<T> RequireAll<T>(T value, params Func<T, Result<T>>[] validators) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(validators);

        Result<T> current = Result<T>.Success(value);

        foreach (Func<T, Result<T>> validator in validators)
        {
            current = current.Bind(validator);
            if (!current.Ok)
            {
                return current;
            }
        }

        return current;
    }

    /// <summary>Validates that a value passes at least one of the specified validators.</summary>
    public static Result<T> RequireAny<T>(T value, params Func<T, Result<T>>[] validators) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(validators);

        if (validators.Length == 0)
        {
            return Result<T>.Fail("At least one validator must be provided");
        }

        List<string> errors = [];

        foreach (Func<T, Result<T>> validator in validators)
        {
            Result<T> result = validator(value);
            if (result.Ok)
            {
                return result;
            }

            if (result.Error is not null)
            {
                errors.Add(result.Error);
            }
        }

        return Result<T>.Fail($"All validators failed: {string.Join("; ", errors)}");
    }

    /// <summary>Validates each item in a sequence using the specified validator.</summary>
    public static Result<T[]> ValidateSequence<T>(IEnumerable<T> items, Func<T, Result<T>> validator) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(validator);

        List<T> validatedItems = [];
        List<string> errors = [];
        int index = 0;

        foreach (T item in items)
        {
            Result<T> result = validator(item);
            if (result is { Ok: true, Value: not null })
            {
                validatedItems.Add(result.Value);
            }
            else
            {
                errors.Add($"Item at index {index}: {result.Error ?? "Unknown error"}");
            }
            index++;
        }

        return errors.Count == 0
            ? Result<T[]>.Success(validatedItems.ToArray())
            : Result<T[]>.Fail(string.Join("; ", errors));
    }
}
