using System;
using Arsenal.Core.Result;
using Arsenal.Rhino.Geometry.Base;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Base;

/// <summary>RhinoCommon-backed geometry classification utilities.</summary>
public sealed class Classifier : IClassifier
{
    /// <summary>Determines the appropriate mass property calculation mode for the geometry.</summary>
    /// <param name="geometry">The geometry to classify.</param>
    /// <returns>A result containing the mass property mode or a failure.</returns>
    public Result<MassPropertyMode> MassPropertyMode(GeometryBase geometry)
    {
        if (geometry is null)
        {
            return Result<MassPropertyMode>.Fail(new Failure("geometry.null", "Geometry cannot be null."));
        }

        if (!geometry.IsValid)
        {
            return Result<MassPropertyMode>.Fail(new Failure("geometry.invalid", "Geometry is not valid."));
        }

        return geometry switch
        {
            global::Rhino.Geometry.Brep brep when brep.IsSolid => Result<MassPropertyMode>.Success(MassPropertyMode.Volume),
            global::Rhino.Geometry.Brep => Result<MassPropertyMode>.Success(MassPropertyMode.Area),
            global::Rhino.Geometry.Surface => Result<MassPropertyMode>.Success(MassPropertyMode.Area),
            global::Rhino.Geometry.Mesh => Result<MassPropertyMode>.Success(MassPropertyMode.Area),
            global::Rhino.Geometry.Curve curve when curve.IsClosed && curve.IsPlanar() => Result<MassPropertyMode>.Success(MassPropertyMode.Area),
            global::Rhino.Geometry.Curve => Result<MassPropertyMode>.Success(MassPropertyMode.Length),
            _ => Result<MassPropertyMode>.Success(MassPropertyMode.Fallback)
        };
    }
}
