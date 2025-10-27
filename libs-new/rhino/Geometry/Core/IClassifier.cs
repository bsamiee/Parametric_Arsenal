using Arsenal.Core.Result;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Core;

/// <summary>Geometry classification operations.</summary>
public interface IClassifier
{
    /// <summary>Determines appropriate mass property calculation mode for geometry.</summary>
    Result<MassPropertyMode> MassPropertyMode(GeometryBase geometry);
}

/// <summary>Mass property calculation modes for different geometry types.</summary>
public enum MassPropertyMode
{
    /// <summary>Volume-based calculations for solid geometry.</summary>
    Volume,
    /// <summary>Area-based calculations for surface geometry.</summary>
    Area,
    /// <summary>Length-based calculations for curve geometry.</summary>
    Length,
    /// <summary>Fallback mode for unsupported geometry types.</summary>
    Fallback
}
