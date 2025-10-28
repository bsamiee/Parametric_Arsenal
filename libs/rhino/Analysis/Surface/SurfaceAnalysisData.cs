using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis.Surface;

/// <summary>Surface frame containing tangents and normal at a UV location.</summary>
public readonly record struct SurfaceFrame(Point3d Point, Vector3d TangentU, Vector3d TangentV, Vector3d Normal);
