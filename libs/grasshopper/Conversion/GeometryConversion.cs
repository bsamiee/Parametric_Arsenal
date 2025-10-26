using Arsenal.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Arsenal.Grasshopper.Conversion;

/// <summary>
/// Provides type-safe conversion methods for Grasshopper geometry objects using the SDK's GH_Convert.
/// Reduces boilerplate by wrapping GH_Convert calls in Result pattern.
/// </summary>
public static class GeometryConversion
{
    /// <summary>Converts IGH_Goo to Point3d using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Point3d> ToPoint3d(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Point3d>.Fail("Input cannot be null");
        }

        Point3d result = default;
        return GH_Convert.ToPoint3d(goo, ref result, GH_Conversion.Both)
            ? Result<Point3d>.Success(result)
            : Result<Point3d>.Fail($"Cannot convert {goo.TypeName} to Point3d");
    }

    /// <summary>Converts IGH_Goo to Vector3d using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Vector3d> ToVector3d(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Vector3d>.Fail("Input cannot be null");
        }

        Vector3d result = default;
        return GH_Convert.ToVector3d(goo, ref result, GH_Conversion.Both)
            ? Result<Vector3d>.Success(result)
            : Result<Vector3d>.Fail($"Cannot convert {goo.TypeName} to Vector3d");
    }

    /// <summary>Converts IGH_Goo to Plane using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Plane> ToPlane(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Plane>.Fail("Input cannot be null");
        }

        Plane result = default;
        return GH_Convert.ToPlane(goo, ref result, GH_Conversion.Both)
            ? Result<Plane>.Success(result)
            : Result<Plane>.Fail($"Cannot convert {goo.TypeName} to Plane");
    }

    /// <summary>Converts IGH_Goo to Line using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Line> ToLine(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Line>.Fail("Input cannot be null");
        }

        Line result = default;
        return GH_Convert.ToLine(goo, ref result, GH_Conversion.Both)
            ? Result<Line>.Success(result)
            : Result<Line>.Fail($"Cannot convert {goo.TypeName} to Line");
    }

    /// <summary>Converts IGH_Goo to Curve using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Curve> ToCurve(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Curve>.Fail("Input cannot be null");
        }

        Curve? result = null;
        return GH_Convert.ToCurve(goo, ref result, GH_Conversion.Both) && result is not null
            ? Result<Curve>.Success(result)
            : Result<Curve>.Fail($"Cannot convert {goo.TypeName} to Curve");
    }

    /// <summary>Converts IGH_Goo to Surface using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Surface> ToSurface(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Surface>.Fail("Input cannot be null");
        }

        Surface? result = null;
        return GH_Convert.ToSurface(goo, ref result, GH_Conversion.Both) && result is not null
            ? Result<Surface>.Success(result)
            : Result<Surface>.Fail($"Cannot convert {goo.TypeName} to Surface");
    }

    /// <summary>Converts IGH_Goo to Brep using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Brep> ToBrep(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Brep>.Fail("Input cannot be null");
        }

        Brep? result = null;
        return GH_Convert.ToBrep(goo, ref result, GH_Conversion.Both) && result is not null
            ? Result<Brep>.Success(result)
            : Result<Brep>.Fail($"Cannot convert {goo.TypeName} to Brep");
    }

    /// <summary>Converts IGH_Goo to Mesh using GH_Convert with both primary and secondary conversion.</summary>
    public static Result<Mesh> ToMesh(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<Mesh>.Fail("Input cannot be null");
        }

        Mesh? result = null;
        return GH_Convert.ToMesh(goo, ref result, GH_Conversion.Both) && result is not null
            ? Result<Mesh>.Success(result)
            : Result<Mesh>.Fail($"Cannot convert {goo.TypeName} to Mesh");
    }

    /// <summary>Converts IGH_Goo to GeometryBase using ScriptVariable and pattern matching.</summary>
    public static Result<GeometryBase> ToGeometryBase(IGH_Goo? goo)
    {
        if (goo is null)
        {
            return Result<GeometryBase>.Fail("Input cannot be null");
        }

        return goo.ScriptVariable() is GeometryBase geom
            ? Result<GeometryBase>.Success(geom)
            : Result<GeometryBase>.Fail($"Cannot convert {goo.TypeName} to GeometryBase");
    }
}