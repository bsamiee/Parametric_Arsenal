namespace Arsenal.Rhino.Intersection;

/// <summary>Geometric intersection algorithms using RhinoCommon Intersect SDK.</summary>
[Flags]
public enum IntersectionMethod {
    None = 0,
    CurveCurve = 1,
    CurveSurface = 2,
    CurveBrep = 4,
    BrepBrep = 8,
    MeshMesh = 16,
    MeshRay = 32,
    MeshPlane = 64,
    LineBox = 128,
    CurveSelf = 256,
}
