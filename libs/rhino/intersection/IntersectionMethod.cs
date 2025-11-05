namespace Arsenal.Rhino.Intersection;

/// <summary>Geometric intersection algorithms using RhinoCommon Intersect SDK.</summary>
[Flags]
public enum IntersectionMethod {
    None = 0,
    CurveCurve = 1,
    CurveSurface = 2,
    CurveBrep = 4,
    CurvePlane = 8,
    CurveLine = 16,
    BrepBrep = 32,
    BrepPlane = 64,
    SurfaceSurface = 128,
    MeshMesh = 256,
    MeshRay = 512,
    MeshPlane = 1024,
    MeshLine = 2048,
    LineBox = 4096,
    LinePlane = 8192,
    LineSphere = 16384,
    LineCylinder = 32768,
    PlanePlane = 65536,
    SphereSphere = 131072,
    CircleCircle = 262144,
    CurveSelf = 524288,
}
