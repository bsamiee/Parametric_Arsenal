using System.Collections.Generic;
using Arsenal.Core.Result;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Arsenal.Grasshopper.Parameters;

/// <summary>Interface for reading Grasshopper component inputs and writing outputs using Result patterns.</summary>
public interface IDataAccessReader
{
    /// <summary>Reads a required reference type input.</summary>
    Result<T> GetRequired<T>(IGH_DataAccess dataAccess, int index, string parameterName) where T : class;

    /// <summary>Reads an optional reference type input with a default fallback.</summary>
    Result<T> GetOptional<T>(IGH_DataAccess dataAccess, int index, string parameterName, T defaultValue) where T : class;

    /// <summary>Reads a required value type input.</summary>
    Result<T> GetRequiredValue<T>(IGH_DataAccess dataAccess, int index, string parameterName) where T : struct;

    /// <summary>Reads an optional value type input with a default fallback.</summary>
    Result<T> GetOptionalValue<T>(IGH_DataAccess dataAccess, int index, string parameterName, T defaultValue) where T : struct;

    /// <summary>Reads a required list input.</summary>
    Result<IReadOnlyList<T>> GetList<T>(IGH_DataAccess dataAccess, int index, string parameterName);

    /// <summary>Reads an optional list input that may be empty.</summary>
    Result<IReadOnlyList<T>> GetOptionalList<T>(IGH_DataAccess dataAccess, int index, string parameterName);

    /// <summary>Reads a GH data tree input.</summary>
    Result<GH_Structure<T>> GetTree<T>(IGH_DataAccess dataAccess, int index, string parameterName) where T : IGH_Goo;

    /// <summary>Writes a value to an output parameter.</summary>
    Result SetData<T>(IGH_DataAccess dataAccess, int index, T value, string parameterName);

    /// <summary>Writes a list to an output parameter.</summary>
    Result SetDataList<T>(IGH_DataAccess dataAccess, int index, IEnumerable<T> values, string parameterName);
}
