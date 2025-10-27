using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Vector;

/// <summary>Sample containing vector information at a point.</summary>
public readonly record struct VectorSample(
    Point3d Point,
    Vector3d? Tangent,
    Vector3d? Normal,
    Vector3d? UDirection,
    Vector3d? VDirection);
