using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Surfaces;

/// <summary>Closest point information for surface projection results.</summary>
public readonly record struct SurfaceClosestPoint(Point3d Point, double U, double V, double Distance);
