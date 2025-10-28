using System.Collections.Generic;
using Grasshopper.Kernel;

namespace Arsenal.Grasshopper.Parameters;

/// <summary>Interface for managing reusable parameter definitions.</summary>
public interface IParameterCatalog
{
    /// <summary>Enumerates the registered definitions.</summary>
    IEnumerable<KeyValuePair<string, ParameterDefinition>> Definitions { get; }

    /// <summary>Gets a parameter definition by key.</summary>
    ParameterDefinition Get(string key);

    /// <summary>Creates a parameter instance using the specified key.</summary>
    IGH_Param CreateParameter(string key);
}
