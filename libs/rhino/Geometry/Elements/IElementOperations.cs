using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Elements;

/// <summary>Element extraction operations interface.</summary>
public interface IElementOperations
{
    /// <summary>Extracts vertices from geometry.</summary>
    Result<IReadOnlyList<Point3d>> Vertices(GeometryBase geometry, GeoContext context);

    /// <summary>Extracts edges from geometry.</summary>
    Result<IReadOnlyList<Curve>> Edges(GeometryBase geometry, GeoContext context);

    /// <summary>Extracts faces from geometry.</summary>
    Result<IReadOnlyList<GeometryBase>> Faces(GeometryBase geometry, GeoContext context);

    /// <summary>Computes edge midpoints from geometry.</summary>
    Result<IReadOnlyList<Point3d>> EdgeMidpoints(GeometryBase geometry, GeoContext context);

    /// <summary>Computes geometry centroid.</summary>
    Result<Point3d> Centroid(GeometryBase geometry, GeoContext context);
}
