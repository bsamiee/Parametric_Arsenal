using System.Collections.Generic;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Rhino.Geometry;
using RhinoBrep = Rhino.Geometry.Brep;
using RhinoCurve = Rhino.Geometry.Curve;

namespace Arsenal.Rhino.Geometry.Brep;

/// <summary>Brep operations using RhinoCommon.</summary>
public sealed class BrepOperations : IBrep
{
    /// <summary>Extracts all vertices from the brep.</summary>
    public Result<IReadOnlyList<Point3d>> Vertices(RhinoBrep brep)
    {
        Result<RhinoBrep> brepResult = ValidateBrep(brep);
        if (!brepResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(brepResult.Failure!);
        }

        Point3d[] vertices = brep.DuplicateVertices();
        return Result<IReadOnlyList<Point3d>>.Success(vertices);
    }

    /// <summary>Extracts all edges from the brep.</summary>
    public Result<IReadOnlyList<Curve>> Edges(RhinoBrep brep)
    {
        Result<RhinoBrep> brepResult = ValidateBrep(brep);
        if (!brepResult.IsSuccess)
        {
            return Result<IReadOnlyList<Curve>>.Fail(brepResult.Failure!);
        }

        Curve[] edges = brep.DuplicateEdgeCurves() ?? [];
        return Result<IReadOnlyList<Curve>>.Success(edges);
    }

    /// <summary>Extracts all faces from the brep.</summary>
    public Result<IReadOnlyList<GeometryBase>> Faces(RhinoBrep brep)
    {
        Result<RhinoBrep> brepResult = ValidateBrep(brep);
        if (!brepResult.IsSuccess)
        {
            return Result<IReadOnlyList<GeometryBase>>.Fail(brepResult.Failure!);
        }

        List<GeometryBase> faces = new(brep.Faces.Count);

        foreach (BrepFace face in brep.Faces)
        {
            GeometryBase? duplicate = face.DuplicateFace(true);
            if (duplicate is not null)
            {
                faces.Add(duplicate);
            }
        }

        return Result<IReadOnlyList<GeometryBase>>.Success(faces);
    }

    /// <summary>Computes midpoints of all brep edges.</summary>
    public Result<IReadOnlyList<Point3d>> EdgeMidpoints(RhinoBrep brep)
    {
        Result<RhinoBrep> brepResult = ValidateBrep(brep);
        if (!brepResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(brepResult.Failure!);
        }

        Curve[]? edges = brep.DuplicateEdgeCurves();
        if (edges is null || edges.Length == 0)
        {
            return Result<IReadOnlyList<Point3d>>.Success([]);
        }

        Point3d[] midpoints = new Point3d[edges.Length];
        for (int i = 0; i < edges.Length; i++)
        {
            Curve edge = edges[i];
            midpoints[i] = edge.PointAt(edge.Domain.Mid);
        }

        return Result<IReadOnlyList<Point3d>>.Success(midpoints);
    }

    private static Result<RhinoBrep> ValidateBrep(RhinoBrep? brep)
    {
        Result<RhinoBrep> guard = Guard.AgainstNull(brep, nameof(brep));
        if (!guard.IsSuccess)
        {
            return guard;
        }

        if (!guard.Value!.IsValid)
        {
            return Result<RhinoBrep>.Fail(new Failure("brep.invalid", "Brep is not valid."));
        }

        return guard;
    }
}
