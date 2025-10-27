using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Curve;

/// <summary>Operations available for Rhino curves.</summary>
public interface ICurve
{
    /// <summary>Finds the closest point on the curve to the test point.</summary>
    Result<CurveClosestPoint> ClosestPoint(global::Rhino.Geometry.Curve curve, global::Rhino.Geometry.Point3d testPoint, GeoContext context);

    /// <summary>Calculates the tangent vector at the specified parameter.</summary>
    Result<global::Rhino.Geometry.Vector3d> TangentAt(global::Rhino.Geometry.Curve curve, double parameter);

    /// <summary>Extracts quadrant points from the curve.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> QuadrantPoints(global::Rhino.Geometry.Curve curve, GeoContext context);

    /// <summary>Calculates the midpoint of the curve.</summary>
    Result<global::Rhino.Geometry.Point3d> Midpoint(global::Rhino.Geometry.Curve curve);
}
