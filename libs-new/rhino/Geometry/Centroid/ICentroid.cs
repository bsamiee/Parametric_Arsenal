using Arsenal.Core.Result;
using Arsenal.Rhino.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Centroid;

/// <summary>Centroid calculations for supported geometry types.</summary>
public interface ICentroid
{
    /// <summary>Computes the centroid of the specified geometry.</summary>
    /// <param name="geometry">The geometry to compute the centroid for.</param>
    /// <param name="context">The geometric context containing tolerance information.</param>
    /// <returns>A result containing the centroid point or a failure.</returns>
    Result<Point3d> Compute(GeometryBase geometry, GeoContext context);
}
