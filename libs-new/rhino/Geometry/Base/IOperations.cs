using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Base;

/// <summary>High-level operations composed from individual geometry modules.</summary>
public interface IOperations
{
    /// <summary>Extracts vertices from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> Vertices(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Extracts edges from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Extracts faces from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.GeometryBase>> Faces(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Calculates edge midpoints from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> EdgeMidpoints(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Calculates centroid of geometry.</summary>
    Result<global::Rhino.Geometry.Point3d> Centroid(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);
}
