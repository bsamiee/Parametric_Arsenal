using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Brep;

/// <summary>RhinoCommon-backed brep operations.</summary>
public sealed class BrepOperations : IBrep
{
    /// <summary>Extracts all vertices from the brep.</summary>
    /// <param name="brep">The brep to extract vertices from.</param>
    /// <returns>A result containing the vertices or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> Vertices(global::Rhino.Geometry.Brep brep)
    {
        Result<global::Rhino.Geometry.Brep> brepResult = ValidateBrep(brep);
        if (!brepResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(brepResult.Failure!);
        }

        Point3d[] vertices = brep.DuplicateVertices();
        return Result<IReadOnlyList<Point3d>>.Success(vertices);
    }

    /// <summary>Extracts all edges from the brep.</summary>
    /// <param name="brep">The brep to extract edges from.</param>
    /// <returns>A result containing the edges or a failure.</returns>
    public Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(global::Rhino.Geometry.Brep brep)
    {
        Result<global::Rhino.Geometry.Brep> brepResult = ValidateBrep(brep);
        if (!brepResult.IsSuccess)
        {
            return Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Fail(brepResult.Failure!);
        }

        global::Rhino.Geometry.Curve[] edges = brep.DuplicateEdgeCurves() ?? [];
        return Result<IReadOnlyList<global::Rhino.Geometry.Curve>>.Success(edges);
    }

    /// <summary>Extracts all faces from the brep.</summary>
    /// <param name="brep">The brep to extract faces from.</param>
    /// <returns>A result containing the faces or a failure.</returns>
    public Result<IReadOnlyList<GeometryBase>> Faces(global::Rhino.Geometry.Brep brep)
    {
        Result<global::Rhino.Geometry.Brep> brepResult = ValidateBrep(brep);
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

    /// <summary>Computes the midpoints of all edges in the brep.</summary>
    /// <param name="brep">The brep to compute edge midpoints for.</param>
    /// <returns>A result containing the edge midpoints or a failure.</returns>
    public Result<IReadOnlyList<Point3d>> EdgeMidpoints(global::Rhino.Geometry.Brep brep)
    {
        Result<global::Rhino.Geometry.Brep> brepResult = ValidateBrep(brep);
        if (!brepResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(brepResult.Failure!);
        }

        global::Rhino.Geometry.Curve[]? edges = brep.DuplicateEdgeCurves();
        if (edges is null || edges.Length == 0)
        {
            return Result<IReadOnlyList<Point3d>>.Success([]);
        }

        Point3d[] midpoints = new Point3d[edges.Length];
        for (int i = 0; i < edges.Length; i++)
        {
            global::Rhino.Geometry.Curve edge = edges[i];
            midpoints[i] = edge.PointAt(edge.Domain.Mid);
        }

        return Result<IReadOnlyList<Point3d>>.Success(midpoints);
    }

    private static Result<global::Rhino.Geometry.Brep> ValidateBrep(global::Rhino.Geometry.Brep? brep)
    {
        Result<global::Rhino.Geometry.Brep> guard = Guard.AgainstNull(brep, nameof(brep));
        if (!guard.IsSuccess)
        {
            return guard;
        }

        if (!guard.Value!.IsValid)
        {
            return Result<global::Rhino.Geometry.Brep>.Fail(new Failure("brep.invalid", "Brep is not valid."));
        }

        return guard;
    }
}
