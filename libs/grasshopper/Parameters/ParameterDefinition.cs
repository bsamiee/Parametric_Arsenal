using System;
using Grasshopper.Kernel;

namespace Arsenal.Grasshopper.Parameters;

/// <summary>Describes a reusable Grasshopper parameter definition.</summary>
public sealed record ParameterDefinition
{
    private readonly Func<IGH_Param> _factory;

    /// <summary>Initializes a new instance of the <see cref="ParameterDefinition"/> class.</summary>
    public ParameterDefinition(
        string key,
        Func<IGH_Param> factory,
        string name,
        string nickname,
        string description,
        GH_ParamAccess access,
        bool isOptional = false,
        string? pathHint = null)
    {
        Key = string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException("Parameter key cannot be null or whitespace.", nameof(key))
            : key;

        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Parameter name cannot be null or whitespace.", nameof(name))
            : name;

        Nickname = string.IsNullOrWhiteSpace(nickname)
            ? throw new ArgumentException("Parameter nickname cannot be null or whitespace.", nameof(nickname))
            : nickname;

        Description = string.IsNullOrWhiteSpace(description)
            ? throw new ArgumentException("Parameter description cannot be null or whitespace.", nameof(description))
            : description;

        Access = access;
        IsOptional = isOptional;
        PathHint = pathHint;
    }

    /// <summary>Unique key for lookup.</summary>
    public string Key { get; }

    /// <summary>Parameter display name.</summary>
    public string Name { get; }

    /// <summary>Parameter nickname.</summary>
    public string Nickname { get; }

    /// <summary>Parameter description shown to users.</summary>
    public string Description { get; }

    /// <summary>Grasshopper access pattern.</summary>
    public GH_ParamAccess Access { get; }

    /// <summary>Indicates whether the parameter is optional.</summary>
    public bool IsOptional { get; }

    /// <summary>Optional data tree path semantic hint.</summary>
    public string? PathHint { get; }

    /// <summary>Creates a configured parameter instance.</summary>
    public IGH_Param Create()
    {
        IGH_Param parameter = _factory();
        parameter.Name = Name;
        parameter.NickName = Nickname;
        parameter.Description = Description;
        parameter.Access = Access;
        parameter.Optional = IsOptional;
        return parameter;
    }
}
