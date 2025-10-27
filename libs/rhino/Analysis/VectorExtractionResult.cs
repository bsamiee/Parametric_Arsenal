using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Vector extraction result containing point and directional vectors extracted from geometry.</summary>
public readonly record struct VectorExtractionResult(
    Point3d ExtractionPoint,
    Vector3d? TangentVector,
    Vector3d? NormalVector,
    Vector3d? UDirection,
    Vector3d? VDirection
);
