using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Type-safe optional parameters for intersection operations.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct IntersectionOptions(
    double? Tolerance = null,
    Vector3d? ProjectionDirection = null,
    int? MaxHits = null,
    bool WithIndices = false,
    bool Sorted = false);
