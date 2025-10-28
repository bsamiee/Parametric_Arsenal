using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Curves;

/// <summary>Closest point information for curve projection results.</summary>
public readonly record struct CurveClosestPoint(Point3d Point, double Parameter, double Distance);
