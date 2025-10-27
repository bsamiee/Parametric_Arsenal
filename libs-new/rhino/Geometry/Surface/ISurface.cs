using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Surface;

/// <summary>Surface geometry operations.</summary>
public interface ISurface
{
    /// <summary>Finds closest point on surface to test point.</summary>
    Result<SurfaceClosestPoint> ClosestPoint(global::Rhino.Geometry.Surface surface, global::Rhino.Geometry.Point3d testPoint, GeoContext context);
}
