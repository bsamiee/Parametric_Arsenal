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

/// <summary>Unified intersection output with zero nullable fields.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct IntersectionOutput(
    IReadOnlyList<Point3d> Points,
    IReadOnlyList<Curve> Curves,
    IReadOnlyList<double> ParametersA,
    IReadOnlyList<double> ParametersB,
    IReadOnlyList<int> FaceIndices,
    IReadOnlyList<Polyline> Sections) {
    public static readonly IntersectionOutput Empty = new([], [], [], [], [], []);
}
