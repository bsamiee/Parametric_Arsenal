using System.Collections.Generic;
using Arsenal.Core.Result;
using Arsenal.Rhino.Context;

namespace Arsenal.Rhino.Geometry.Intersect;

/// <summary>Intersection operations backed by RhinoCommon APIs.</summary>
public interface IIntersect
{
    /// <summary>Finds intersections between curves.</summary>
    Result<IReadOnlyList<CurveCurveHit>> CurveCurve(IEnumerable<global::Rhino.Geometry.Curve> curves, GeoContext context,
        bool includeSelf = false);

    /// <summary>Finds intersections between mesh and rays.</summary>
    Result<IReadOnlyList<MeshRayHit>> MeshRay(global::Rhino.Geometry.Mesh mesh, IEnumerable<global::Rhino.Geometry.Ray3d> rays, GeoContext context);

    /// <summary>Finds intersections between surfaces and curves.</summary>
    Result<IReadOnlyList<SurfaceCurveHit>> SurfaceCurve(IEnumerable<global::Rhino.Geometry.Surface> surfaces, IEnumerable<global::Rhino.Geometry.Curve> curves,
        GeoContext context);
}
