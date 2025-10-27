using Arsenal.Core.Result;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Base;

/// <summary>Determines appropriate geometric classifications for downstream operations.</summary>
public interface IClassifier
{
    /// <summary>Determines the appropriate mass property calculation mode for the geometry.</summary>
    /// <param name="geometry">The geometry to classify.</param>
    /// <returns>A result containing the mass property mode or a failure.</returns>
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
