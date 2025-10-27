using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Analysis.Surface;

/// <summary>Surface analysis operations for UV parameter analysis and frame computation.</summary>
public interface ISurfaceAnalysis
{
    /// <summary>Computes surface frame at UV parameters.</summary>
    Result<SurfaceFrame> FrameAt(global::Rhino.Geometry.Surface surface, double u, double v, GeoContext context);
}
