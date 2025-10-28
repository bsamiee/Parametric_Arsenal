using System;
using System.Collections.Generic;
using Arsenal.Core.Result;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Arsenal.Grasshopper.Parameters;

/// <summary>Result-returning helpers for reading inputs and writing outputs.</summary>
public static class DataAccessReader
{
    /// <summary>Reads a required reference type input.</summary>
    public static Result<T> GetRequired<T>(IGH_DataAccess dataAccess, int index, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        T? value = null;
        if (!dataAccess.GetData(index, ref value) || value is null)
        {
            return Result<T>.Fail(new Failure(
                "grasshopper.input.missing",
                $"Parameter '{parameterName}' is required."));
        }

        return Result<T>.Success(value);
    }

    /// <summary>Reads an optional reference type input with a default fallback.</summary>
    public static Result<T> GetOptional<T>(IGH_DataAccess dataAccess, int index, string parameterName, T defaultValue)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        T? value = defaultValue;
        dataAccess.GetData(index, ref value);
        return Result<T>.Success(value ?? defaultValue);
    }

    /// <summary>Reads a required value type input.</summary>
    public static Result<T> GetRequiredValue<T>(IGH_DataAccess dataAccess, int index, string parameterName)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        T value = default;
        if (!dataAccess.GetData(index, ref value))
        {
            return Result<T>.Fail(new Failure(
                "grasshopper.input.missing",
                $"Parameter '{parameterName}' is required."));
        }

        return Result<T>.Success(value);
    }

    /// <summary>Reads an optional value type input with a default fallback.</summary>
    public static Result<T> GetOptionalValue<T>(IGH_DataAccess dataAccess, int index, string parameterName, T defaultValue)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        T value = defaultValue;
        dataAccess.GetData(index, ref value);
        return Result<T>.Success(value);
    }

    /// <summary>Reads a required list input.</summary>
    public static Result<IReadOnlyList<T>> GetList<T>(IGH_DataAccess dataAccess, int index, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        List<T> buffer = [];
        if (!dataAccess.GetDataList(index, buffer) || buffer.Count == 0)
        {
            return Result<IReadOnlyList<T>>.Fail(new Failure(
                "grasshopper.input.listMissing",
                $"Parameter '{parameterName}' requires at least one item."));
        }

        return Result<IReadOnlyList<T>>.Success(buffer);
    }

    /// <summary>Reads an optional list input that may be empty.</summary>
    public static Result<IReadOnlyList<T>> GetOptionalList<T>(IGH_DataAccess dataAccess, int index, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        List<T> buffer = [];
        dataAccess.GetDataList(index, buffer);
        return Result<IReadOnlyList<T>>.Success(buffer);
    }

    /// <summary>Reads a GH data tree input.</summary>
    public static Result<GH_Structure<T>> GetTree<T>(IGH_DataAccess dataAccess, int index, string parameterName)
        where T : IGH_Goo
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        bool success = dataAccess.GetDataTree(index, out GH_Structure<T> tree);
        return success
            ? Result<GH_Structure<T>>.Success(tree)
            : Result<GH_Structure<T>>.Fail(new Failure(
                "grasshopper.input.treeMissing",
                $"Parameter '{parameterName}' requires a valid data tree."));
    }

    /// <summary>Writes a value to an output parameter.</summary>
    public static Result SetData<T>(IGH_DataAccess dataAccess, int index, T value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        bool success = dataAccess.SetData(index, value);
        return success
            ? Result.Success()
            : Result.Fail(new Failure(
                "grasshopper.output.writeFailed",
                $"Failed to set output '{parameterName}'."));
    }

    /// <summary>Writes a list to an output parameter.</summary>
    public static Result SetDataList<T>(IGH_DataAccess dataAccess, int index, IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(dataAccess);
        ValidateIndex(index, parameterName);

        ArgumentNullException.ThrowIfNull(values);
        bool success = dataAccess.SetDataList(index, values);
        return success
            ? Result.Success()
            : Result.Fail(new Failure(
                "grasshopper.output.writeFailed",
                $"Failed to set output '{parameterName}'."));
    }

    private static void ValidateIndex(int index, string parameterName)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Parameter '{parameterName}' index must be non-negative.");
        }
    }
}
