using System;
using Arsenal.Core;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>Determines the appropriate mass properties method for geometry classification.</summary>
public enum MassPropertiesMethod
{
    /// <summary>Use VolumeMassProperties for solid breps.</summary>
    Volume,

    /// <summary>Use AreaMassProperties for surfaces, non-solid breps, meshes, and closed planar curves.</summary>
    Area,

    /// <summary>Use LengthMassProperties for open curves.</summary>
    Length,

    /// <summary>Use fallback method (bounding box center) for unsupported geometry types.</summary>
    Fallback
}

/// <summary>Provides geometry type detection and classification for mass properties selection.</summary>
public static class GeometryDetection
{
    /// <summary>Determines the appropriate mass properties method for the given geometry.</summary>
    public static Result<MassPropertiesMethod> GetMassPropertiesMethod(GeometryBase? geometry)
    {
        if (geometry is null)
        {
            return Result<MassPropertiesMethod>.Fail("Geometry cannot be null");
        }

        if (!geometry.IsValid)
        {
            return Result<MassPropertiesMethod>.Fail("Geometry is not valid");
        }

        MassPropertiesMethod method = geometry switch
        {
            Brep brep when IsSolidBrep(brep) => MassPropertiesMethod.Volume,
            Brep => MassPropertiesMethod.Area,
            Surface => MassPropertiesMethod.Area,
            Mesh => MassPropertiesMethod.Area,
            Curve curve when IsClosedPlanarCurve(curve) => MassPropertiesMethod.Area,
            Curve => MassPropertiesMethod.Length,
            _ => MassPropertiesMethod.Fallback
        };

        return Result<MassPropertiesMethod>.Success(method);
    }

    /// <summary>Checks if a brep is solid using SDK method.</summary>
    public static bool IsSolidBrep(Brep brep)
    {
        ArgumentNullException.ThrowIfNull(brep);
        return brep.IsSolid;
    }

    /// <summary>Checks if a curve is closed and planar for area calculation.</summary>
    public static bool IsClosedPlanarCurve(Curve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return curve.IsClosed && curve.IsPlanar();
    }
}
