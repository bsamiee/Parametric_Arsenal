using System;
using System.Collections.Generic;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Arsenal.Rhino.Geometry.Brep;
using Arsenal.Rhino.Geometry.Core;
using Arsenal.Rhino.Geometry.Mesh;
using Rhino.Geometry;
using RhinoBrep = Rhino.Geometry.Brep;
using RhinoMesh = Rhino.Geometry.Mesh;

namespace Arsenal.Rhino.Geometry.Elements;

/// <summary>Element extraction operations using existing Brep and Mesh operations.</summary>
public sealed class ElementOperations : IElementOperations
{
    private readonly IBrep _brepOperations;
    private readonly IMesh _meshOperations;
    private readonly ICentroid _centroid;

    /// <summary>Initializes element operations with Brep, Mesh, and Centroid operations.</summary>
    public ElementOperations(IBrep brepOperations, IMesh meshOperations, ICentroid centroid)
    {
        _brepOperations = brepOperations ?? throw new ArgumentNullException(nameof(brepOperations));
        _meshOperations = meshOperations ?? throw new ArgumentNullException(nameof(meshOperations));
        _centroid = centroid ?? throw new ArgumentNullException(nameof(centroid));
    }

    /// <summary>Extracts vertices from geometry.</summary>
    public Result<IReadOnlyList<Point3d>> Vertices(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<GeometryBase> geometryResult = Guard.AgainstNull(geometry, nameof(geometry));
        if (!geometryResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(geometryResult.Failure!);
        }

        return geometry switch
        {
            RhinoBrep brep => _brepOperations.Vertices(brep),
            RhinoMesh mesh => _meshOperations.Vertices(mesh),
            _ => Result<IReadOnlyList<Point3d>>.Fail(
                new Failure("geometry.unsupported", $"Vertex extraction not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Extracts edges from geometry.</summary>
    public Result<IReadOnlyList<Curve>> Edges(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<GeometryBase> geometryResult = Guard.AgainstNull(geometry, nameof(geometry));
        if (!geometryResult.IsSuccess)
        {
            return Result<IReadOnlyList<Curve>>.Fail(geometryResult.Failure!);
        }

        return geometry switch
        {
            RhinoBrep brep => _brepOperations.Edges(brep),
            RhinoMesh mesh => _meshOperations.Edges(mesh),
            _ => Result<IReadOnlyList<Curve>>.Fail(
                new Failure("geometry.unsupported", $"Edge extraction not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Extracts faces from geometry.</summary>
    public Result<IReadOnlyList<GeometryBase>> Faces(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<GeometryBase> geometryResult = Guard.AgainstNull(geometry, nameof(geometry));
        if (!geometryResult.IsSuccess)
        {
            return Result<IReadOnlyList<GeometryBase>>.Fail(geometryResult.Failure!);
        }

        return geometry switch
        {
            RhinoBrep brep => _brepOperations.Faces(brep),
            RhinoMesh => Result<IReadOnlyList<GeometryBase>>.Fail(
                new Failure("geometry.unsupported", "Face extraction not supported for mesh geometry.")),
            _ => Result<IReadOnlyList<GeometryBase>>.Fail(
                new Failure("geometry.unsupported", $"Face extraction not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Computes edge midpoints from geometry.</summary>
    public Result<IReadOnlyList<Point3d>> EdgeMidpoints(GeometryBase geometry, GeoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<GeometryBase> geometryResult = Guard.AgainstNull(geometry, nameof(geometry));
        if (!geometryResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(geometryResult.Failure!);
        }

        return geometry switch
        {
            RhinoBrep brep => _brepOperations.EdgeMidpoints(brep),
            RhinoMesh mesh => _meshOperations.EdgeMidpoints(mesh),
            _ => Result<IReadOnlyList<Point3d>>.Fail(
                new Failure("geometry.unsupported", $"Edge midpoint computation not supported for geometry type {geometry.ObjectType}."))
        };
    }

    /// <summary>Computes geometry centroid.</summary>
    public Result<Point3d> Centroid(GeometryBase geometry, GeoContext context)
    {
        return _centroid.Compute(geometry, context);
    }
}
