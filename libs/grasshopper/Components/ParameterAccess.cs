using System.Collections.Generic;
using Arsenal.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Arsenal.Grasshopper.Components;

/// <summary>Type-safe parameter access helpers with Result pattern for Grasshopper components.</summary>
public static class ParameterAccess
{
    /// <summary>Gets single item from parameter with type conversion.</summary>
    public static Result<T> GetItem<T>(IGH_DataAccess? DA, int index) where T : class
    {
        Result<IGH_DataAccess> validation = Guard.RequireNonNull(DA, nameof(DA));
        if (!validation.Ok)
        {
            return Result<T>.Fail(validation.Error!);
        }

        Result<int> indexValidation = Guard.RequireNonNegative(index, nameof(index));
        if (!indexValidation.Ok)
        {
            return Result<T>.Fail(indexValidation.Error!);
        }

        T? data = null;
        bool success = DA!.GetData(index, ref data);

        if (!success)
        {
            return Result<T>.Fail($"Failed to retrieve data from parameter at index {index}");
        }

        return data is null
            ? Result<T>.Fail($"Parameter at index {index} is null")
            : Result<T>.Success(data);
    }

    /// <summary>Gets list of items from parameter with type conversion.</summary>
    public static Result<List<T>> GetList<T>(IGH_DataAccess? DA, int index) where T : class
    {
        Result<IGH_DataAccess> validation = Guard.RequireNonNull(DA, nameof(DA));
        if (!validation.Ok)
        {
            return Result<List<T>>.Fail(validation.Error!);
        }

        Result<int> indexValidation = Guard.RequireNonNegative(index, nameof(index));
        if (!indexValidation.Ok)
        {
            return Result<List<T>>.Fail(indexValidation.Error!);
        }

        List<T> list = [];
        bool success = DA!.GetDataList(index, list);

        return !success
            ? Result<List<T>>.Fail($"Failed to retrieve data list from parameter at index {index}")
            : Result<List<T>>.Success(list);
    }

    /// <summary>Gets data tree from parameter.</summary>
    public static Result<GH_Structure<T>> GetTree<T>(IGH_DataAccess? DA, int index) where T : IGH_Goo
    {
        Result<IGH_DataAccess> validation = Guard.RequireNonNull(DA, nameof(DA));
        if (!validation.Ok)
        {
            return Result<GH_Structure<T>>.Fail(validation.Error!);
        }

        Result<int> indexValidation = Guard.RequireNonNegative(index, nameof(index));
        if (!indexValidation.Ok)
        {
            return Result<GH_Structure<T>>.Fail(indexValidation.Error!);
        }

        bool success = DA!.GetDataTree(index, out GH_Structure<T> tree);

        return !success
            ? Result<GH_Structure<T>>.Fail($"Failed to retrieve data tree from parameter at index {index}")
            : Result<GH_Structure<T>>.Success(tree);
    }

    /// <summary>Gets optional parameter value with default fallback.</summary>
    public static Result<T> GetOptionalItem<T>(IGH_DataAccess? DA, int index, T defaultValue) where T : class
    {
        Result<IGH_DataAccess> validation = Guard.RequireNonNull(DA, nameof(DA));
        if (!validation.Ok)
        {
            return Result<T>.Fail(validation.Error!);
        }

        Result<int> indexValidation = Guard.RequireNonNegative(index, nameof(index));
        if (!indexValidation.Ok)
        {
            return Result<T>.Fail(indexValidation.Error!);
        }

        T? data = defaultValue;
        DA!.GetData(index, ref data);

        // For optional parameters, failure to get data is acceptable - use default
        return Result<T>.Success(data ?? defaultValue);
    }

    /// <summary>Gets value type parameter.</summary>
    public static Result<T> GetValue<T>(IGH_DataAccess? DA, int index) where T : struct
    {
        Result<IGH_DataAccess> validation = Guard.RequireNonNull(DA, nameof(DA));
        if (!validation.Ok)
        {
            return Result<T>.Fail(validation.Error!);
        }

        Result<int> indexValidation = Guard.RequireNonNegative(index, nameof(index));
        if (!indexValidation.Ok)
        {
            return Result<T>.Fail(indexValidation.Error!);
        }

        T data = default;
        bool success = DA!.GetData(index, ref data);

        return !success
            ? Result<T>.Fail($"Failed to retrieve value from parameter at index {index}")
            : Result<T>.Success(data);
    }

    /// <summary>Gets optional value type parameter with default fallback.</summary>
    public static Result<T> GetOptionalValue<T>(IGH_DataAccess? DA, int index, T defaultValue) where T : struct
    {
        Result<IGH_DataAccess> validation = Guard.RequireNonNull(DA, nameof(DA));
        if (!validation.Ok)
        {
            return Result<T>.Fail(validation.Error!);
        }

        Result<int> indexValidation = Guard.RequireNonNegative(index, nameof(index));
        if (!indexValidation.Ok)
        {
            return Result<T>.Fail(indexValidation.Error!);
        }

        T data = defaultValue;
        DA!.GetData(index, ref data);

        // For optional parameters, always return success with either retrieved or default value
        return Result<T>.Success(data);
    }

    /// <summary>Gets parameter with comprehensive validation and user-friendly error messages.</summary>
    public static Result<T> GetValidatedItem<T>(
        IGH_DataAccess? DA,
        int index,
        string parameterName,
        System.Func<T, bool>? validator = null,
        string? validationMessage = null) where T : class
    {
        Result<T> getResult = GetItem<T>(DA, index);
        if (!getResult.Ok)
        {
            return Result<T>.Fail($"Parameter '{parameterName}': {getResult.Error}");
        }

        if (validator is not null && !validator(getResult.Value!))
        {
            string message = validationMessage ?? $"Parameter '{parameterName}' failed validation";
            return Result<T>.Fail(message);
        }

        return getResult;
    }

    /// <summary>Gets list parameter with comprehensive validation and user-friendly error messages.</summary>
    public static Result<List<T>> GetValidatedList<T>(
        IGH_DataAccess? DA,
        int index,
        string parameterName,
        int? minCount = null,
        int? maxCount = null) where T : class
    {
        Result<List<T>> getResult = GetList<T>(DA, index);
        if (!getResult.Ok)
        {
            return Result<List<T>>.Fail($"Parameter '{parameterName}': {getResult.Error}");
        }

        List<T> list = getResult.Value!;

        if (minCount.HasValue && list.Count < minCount.Value)
        {
            return Result<List<T>>.Fail(
                $"Parameter '{parameterName}' requires at least {minCount.Value} items, got {list.Count}");
        }

        if (maxCount.HasValue && list.Count > maxCount.Value)
        {
            return Result<List<T>>.Fail(
                $"Parameter '{parameterName}' accepts at most {maxCount.Value} items, got {list.Count}");
        }

        return getResult;
    }

    /// <summary>Accumulates multiple validation errors for bulk reporting.</summary>
    public static Result<List<string>> ValidateMultipleParameters(params System.Func<Result>[] validations)
    {
        List<string> errors = [];

        foreach (System.Func<Result> validation in validations)
        {
            Result result = validation();
            if (result is { Ok: false, Error: not null })
            {
                errors.Add(result.Error);
            }
        }

        return errors.Count > 0
            ? Result<List<string>>.Fail($"Validation failed: {string.Join("; ", errors)}")
            : Result<List<string>>.Success(errors);
    }
}
