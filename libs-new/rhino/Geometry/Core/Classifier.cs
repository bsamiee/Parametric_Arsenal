using Arsenal.Core.Result;
using Rhino.Geometry;
using RhinoBrep = Rhino.Geometry.Brep;
using RhinoCurve = Rhino.Geometry.Curve;
using RhinoMesh = Rhino.Geometry.Mesh;
using RhinoSurface = Rhino.Geometry.Surface;

namespace Arsenal.Rhino.Geometry.Core;

/// <summary>Geometry classification using RhinoCommon.</summary>
public sealed class Classifier : IClassifier
{
    /// <summary>Determines appropriate mass property calculation mode for geometry.</summary>
    public Result<MassPropertyMode> MassPropertyMode(GeometryBase? geometry)
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
            RhinoBrep { IsSolid: true } => Result<MassPropertyMode>.Success(Core.MassPropertyMode.Volume),
            RhinoBrep => Result<MassPropertyMode>.Success(Core.MassPropertyMode.Area),
            RhinoSurface => Result<MassPropertyMode>.Success(Core.MassPropertyMode.Area),
            RhinoMesh => Result<MassPropertyMode>.Success(Core.MassPropertyMode.Area),
            RhinoCurve { IsClosed: true } curve when curve.IsPlanar() => Result<MassPropertyMode>.Success(Core.MassPropertyMode.Area),
            RhinoCurve => Result<MassPropertyMode>.Success(Core.MassPropertyMode.Length),
            _ => Result<MassPropertyMode>.Success(Core.MassPropertyMode.Fallback)
        };
    }
}
