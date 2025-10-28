using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace Arsenal.Grasshopper.Parameters;

/// <summary>Central registry of reusable parameter definitions.</summary>
public sealed class ParameterCatalog
{
    private readonly Dictionary<string, ParameterDefinition> _definitions;

    private ParameterCatalog()
    {
        _definitions = CreateDefinitions();
    }

    /// <summary>Gets the singleton catalog instance.</summary>
    public static ParameterCatalog Instance { get; } = new();

    /// <summary>Enumerates the registered definitions.</summary>
    public IEnumerable<KeyValuePair<string, ParameterDefinition>> Definitions => _definitions;

    /// <summary>Gets a parameter definition by key.</summary>
    public ParameterDefinition Get(string key)
    {
        if (!_definitions.TryGetValue(key, out ParameterDefinition? definition))
        {
            throw new ArgumentException($"Parameter definition '{key}' is not registered.", nameof(key));
        }

        return definition;
    }

    /// <summary>Creates a parameter instance using the specified key.</summary>
    public IGH_Param CreateParameter(string key)
    {
        ParameterDefinition definition = Get(key);
        return definition.Create();
    }

    private static Dictionary<string, ParameterDefinition> CreateDefinitions()
    {
        Dictionary<string, ParameterDefinition> definitions = new(StringComparer.Ordinal)
        {
            ["geometry.input"] = new ParameterDefinition(
                "geometry.input",
                () => new Param_Geometry(),
                "Geometry",
                "Geom",
                "Geometry to evaluate.",
                GH_ParamAccess.item),

            ["geometry.collection"] = new ParameterDefinition(
                "geometry.collection",
                () => new Param_Geometry(),
                "Geometry",
                "Geom",
                "Collection of geometry to evaluate.",
                GH_ParamAccess.list,
                pathHint: "{path;item}"),

            ["curve.input"] = new ParameterDefinition(
                "curve.input",
                () => new Param_Curve(),
                "Curve",
                "Crv",
                "Input curve.",
                GH_ParamAccess.item),

            ["point.output"] = new ParameterDefinition(
                "point.output",
                () => new Param_Point(),
                "Point",
                "Pt",
                "Computed point result.",
                GH_ParamAccess.item),

            ["vector.output"] = new ParameterDefinition(
                "vector.output",
                () => new Param_Vector(),
                "Vector",
                "Vec",
                "Computed vector result.",
                GH_ParamAccess.list,
                pathHint: "{branch}(i)"),

            ["number.tolerance"] = new ParameterDefinition(
                "number.tolerance",
                () => new Param_Number(),
                "Tolerance",
                "Tol",
                "Optional tolerance override in document units.",
                GH_ParamAccess.item,
                isOptional: true),

            ["text.remark"] = new ParameterDefinition(
                "text.remark",
                () => new Param_String(),
                "Remark",
                "Info",
                "Supplementary information for degraded results.",
                GH_ParamAccess.item,
                isOptional: true)
        };

        return definitions;
    }
}
