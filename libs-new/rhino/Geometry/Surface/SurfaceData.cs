using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Surface;

/// <summary>Closest point information for surface projection results.</summary>
public readonly record struct SurfaceClosestPoint(Point3d Point, double U, double V, double Distance);

/// <summary>Surface frame containing tangents and normal at a UV location.</summary>
public readonly record struct SurfaceFrame(Point3d Point, Vector3d TangentU, Vector3d TangentV, Vector3d Normal);
