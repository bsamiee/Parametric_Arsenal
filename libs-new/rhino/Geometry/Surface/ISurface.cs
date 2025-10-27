using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Surface;

/// <summary>Operations available for Rhino surfaces.</summary>
public interface ISurface
{
    /// <summary>Finds the closest point on the surface to the test point.</summary>
    Result<SurfaceClosestPoint> ClosestPoint(global::Rhino.Geometry.Surface surface, global::Rhino.Geometry.Point3d testPoint, GeoContext context);

    /// <summary>Calculates the surface frame at the specified UV parameters.</summary>
    Result<SurfaceFrame> FrameAt(global::Rhino.Geometry.Surface surface, double u, double v, GeoContext context);
}
