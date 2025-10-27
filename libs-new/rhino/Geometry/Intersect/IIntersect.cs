using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Intersect;

/// <summary>Geometry intersection operations.</summary>
public interface IIntersect
{
    /// <summary>Computes curve-curve intersections.</summary>
    Result<IReadOnlyList<CurveCurveHit>> CurveCurve(IEnumerable<global::Rhino.Geometry.Curve> curves, GeoContext context,
        bool includeSelf = false);

    /// <summary>Computes mesh-ray intersections.</summary>
    Result<IReadOnlyList<MeshRayHit>> MeshRay(global::Rhino.Geometry.Mesh mesh, IEnumerable<global::Rhino.Geometry.Ray3d> rays, GeoContext context);

    /// <summary>Computes surface-curve intersections.</summary>
    Result<IReadOnlyList<SurfaceCurveHit>> SurfaceCurve(IEnumerable<global::Rhino.Geometry.Surface> surfaces, IEnumerable<global::Rhino.Geometry.Curve> curves,
        GeoContext context);
}
