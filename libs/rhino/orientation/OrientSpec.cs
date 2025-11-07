using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic specification for orientation target discrimination.</summary>
public readonly record struct OrientSpec {
    public required object Target { get; init; }
    public Plane? TargetPlane { get; init; }
    public Point3d? TargetPoint { get; init; }
    public Vector3d? TargetVector { get; init; }
    public Curve? TargetCurve { get; init; }
    public Surface? TargetSurface { get; init; }
    public double CurveParameter { get; init; }
    public (double u, double v) SurfaceUV { get; init; }

    public static OrientSpec Plane(Plane plane) => new() { Target = plane, TargetPlane = plane, };
    public static OrientSpec Point(Point3d point) => new() { Target = point, TargetPoint = point, };
    public static OrientSpec Vector(Vector3d vector) => new() { Target = vector, TargetVector = vector, };
    public static OrientSpec Curve(Curve curve, double t) => new() { Target = curve, TargetCurve = curve, CurveParameter = t, };
    public static OrientSpec Surface(Surface surface, double u, double v) => new() { Target = surface, TargetSurface = surface, SurfaceUV = (u, v), };
}
