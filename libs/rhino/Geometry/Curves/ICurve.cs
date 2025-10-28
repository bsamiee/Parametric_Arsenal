using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Curves;

/// <summary>Curve geometry operations.</summary>
public interface ICurve
{
    /// <summary>Finds closest point on curve to test point.</summary>
    Result<CurveClosestPoint> ClosestPoint(global::Rhino.Geometry.Curve curve, global::Rhino.Geometry.Point3d testPoint, GeoContext context);

    /// <summary>Computes tangent vector at curve parameter.</summary>
    Result<global::Rhino.Geometry.Vector3d> TangentAt(global::Rhino.Geometry.Curve curve, double parameter);

    /// <summary>Extracts quadrant points for circular/elliptical curves.</summary>
    Result<IReadOnlyList<global::Rhino.Geometry.Point3d>> QuadrantPoints(global::Rhino.Geometry.Curve curve, GeoContext context);

    /// <summary>Computes curve midpoint.</summary>
    Result<global::Rhino.Geometry.Point3d> Midpoint(global::Rhino.Geometry.Curve curve);
}
