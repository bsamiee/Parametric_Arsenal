using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Base;

/// <summary>Composite geometry operations interface.</summary>
public interface IOperations
{
    /// <summary>Extracts vertices from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> Vertices(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Extracts edges from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Curve>> Edges(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Extracts faces from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.GeometryBase>> Faces(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Computes edge midpoints from geometry.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> EdgeMidpoints(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);

    /// <summary>Computes geometry centroid.</summary>
    Result<global::Rhino.Geometry.Point3d> Centroid(global::Rhino.Geometry.GeometryBase geometry, GeoContext context);
}
