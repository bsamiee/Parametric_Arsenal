using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Core;

/// <summary>Geometry centroid operations.</summary>
public interface ICentroid
{
    /// <summary>Computes geometry centroid.</summary>
    Result<Point3d> Compute(GeometryBase geometry, GeoContext context);
}
