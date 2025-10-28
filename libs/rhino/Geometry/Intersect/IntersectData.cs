using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Intersect;

/// <summary>Result of curve-curve intersection.</summary>
public readonly record struct CurveCurveHit(
    int CurveIndexA,
    int CurveIndexB,
    Point3d Point,
    double ParameterA,
    double ParameterB,
    bool IsOverlap);

/// <summary>Result of mesh-ray intersection.</summary>
public readonly record struct MeshRayHit(
    bool Hit,
    Point3d Point,
    double RayParameter,
    int FaceIndex,
    Vector3d FaceNormal);

/// <summary>Result of surface-curve intersection.</summary>
public readonly record struct SurfaceCurveHit(
    int SurfaceIndex,
    int CurveIndex,
    Point3d Point,
    double CurveParameter,
    double SurfaceU,
    double SurfaceV,
    bool IsOverlap);
