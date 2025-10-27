using Rhino.Geometry;
using RhinoVector3d = Rhino.Geometry.Vector3d;

namespace Arsenal.Rhino.Analysis.Vector;

/// <summary>Sample containing vector information at a point.</summary>
public readonly record struct VectorSample(
    Point3d Point,
    RhinoVector3d? Tangent,
    RhinoVector3d? Normal,
    RhinoVector3d? UDirection,
    RhinoVector3d? VDirection);
