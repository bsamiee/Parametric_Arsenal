using System.Diagnostics.Contracts;
using CsCheck;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Analysis;

/// <summary>Geometry generators for differential and quality analysis testing with polymorphic coverage.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Test generators used for property-based testing")]
public static class AnalysisGenerators {
    private static readonly double[] ValidRadii = [1.0, 2.0, 5.0, 10.0, 20.0,];
    private static readonly int[] ValidMeshDivisions = [4, 8, 12, 16,];
    private static readonly int[] ValidDegrees = [2, 3, 4, 5,];

    /// <summary>Generates valid 3D points within a bounded region avoiding degeneracy.</summary>
    [Pure]
    public static Gen<Point3d> Point3dGen =>
        Gen.Double[-50.0, 50.0].SelectMany(x =>
            Gen.Double[-50.0, 50.0].SelectMany(y =>
                Gen.Double[-50.0, 50.0].Select(z =>
                    new Point3d(x, y, z))))
        .Where(static p => p.IsValid);

    /// <summary>Generates valid planes at random positions and orientations.</summary>
    [Pure]
    public static Gen<Plane> PlaneGen =>
        Point3dGen.SelectMany(origin =>
            Gen.Double[-1.0, 1.0].SelectMany(nx =>
                Gen.Double[-1.0, 1.0].SelectMany(ny =>
                    Gen.Double[-1.0, 1.0].Select(nz => {
                        Vector3d n = new(nx, ny, nz);
                        return n.Length > RhinoMath.ZeroTolerance
                            ? new Plane(origin, n / n.Length)
                            : Plane.WorldXY;
                    }))))
        .Where(static p => p.IsValid);

    /// <summary>Generates valid circles with positive radius for curvature testing.</summary>
    [Pure]
    public static Gen<Circle> CircleGen =>
        PlaneGen.SelectMany(plane =>
            Gen.OneOfConst(ValidRadii).Select(radius =>
                new Circle(plane, radius)))
        .Where(static c => c.IsValid && c.Radius > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid line curves for zero-curvature baseline testing.</summary>
    [Pure]
    public static Gen<LineCurve> LineCurveGen =>
        Point3dGen.SelectMany(p1 =>
            Point3dGen.Select(p2 => new LineCurve(p1, p2)))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance * 10);

    /// <summary>Generates valid arc curves for known-curvature testing.</summary>
    [Pure]
    public static Gen<ArcCurve> ArcCurveGen =>
        CircleGen.SelectMany(circle =>
            Gen.Double[RhinoMath.ToRadians(45.0), RhinoMath.ToRadians(270.0)].Select(angle =>
                new ArcCurve(new Arc(circle, angle))))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid NURBS curves via interpolation for general curvature testing.</summary>
    [Pure]
    public static Gen<NurbsCurve> NurbsCurveGen =>
        Gen.OneOfConst(ValidDegrees).SelectMany(degree =>
            Point3dGen.List[degree + 2, degree + 5].Select(points =>
                Curve.CreateInterpolatedCurve([.. points,], degree) as NurbsCurve))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance)
        .Select(static c => c!);

    /// <summary>Generates any valid curve type polymorphically for comprehensive testing.</summary>
    [Pure]
    public static Gen<Curve> CurveGen =>
        Gen.Frequency([
            (2, (IGen<Curve>)LineCurveGen.Select(static c => (Curve)c)),
            (3, (IGen<Curve>)ArcCurveGen.Select(static c => (Curve)c)),
            (3, (IGen<Curve>)NurbsCurveGen.Select(static c => (Curve)c)),
        ]);

    /// <summary>Generates valid planar surfaces for zero Gaussian curvature testing.</summary>
    [Pure]
    public static Gen<PlaneSurface> PlaneSurfaceGen =>
        PlaneGen.SelectMany(plane =>
            Gen.Double[1.0, 20.0].SelectMany(width =>
                Gen.Double[1.0, 20.0].Select(height =>
                    new PlaneSurface(plane, new Interval(0, width), new Interval(0, height)))))
        .Where(static s => s is not null && s.IsValid);

    /// <summary>Generates valid spheres with positive radius for known curvature testing.</summary>
    [Pure]
    public static Gen<Sphere> SphereGen =>
        Point3dGen.SelectMany(center =>
            Gen.OneOfConst(ValidRadii).Select(radius =>
                new Sphere(center, radius)))
        .Where(static s => s.IsValid && s.Radius > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid sphere surfaces for known Gaussian/mean curvature testing.</summary>
    [Pure]
    public static Gen<NurbsSurface> SphereSurfaceGen =>
        SphereGen.Select(static sphere => sphere.ToNurbsSurface())
        .Where(static s => s is not null && s.IsValid)
        .Select(static s => s!);

    /// <summary>Generates valid NURBS surfaces via lofting for general curvature testing.</summary>
    [Pure]
    public static Gen<NurbsSurface> NurbsSurfaceGen =>
        Gen.Int[3, 4].SelectMany(curveCount =>
            NurbsCurveGen.List[curveCount, curveCount].Select(curves => {
                Brep[] lofts = Brep.CreateFromLoft([.. curves,], Point3d.Unset, Point3d.Unset, LoftType.Normal, closed: false);
                return lofts?.Length > 0 && lofts[0].Faces.Count > 0
                    ? lofts[0].Faces[0].ToNurbsSurface()
                    : null;
            }))
        .Where(static s => s is not null && s.IsValid)
        .Select(static s => s!);

    /// <summary>Generates any valid surface type polymorphically.</summary>
    [Pure]
    public static Gen<Surface> SurfaceGen =>
        Gen.Frequency([
            (2, (IGen<Surface>)PlaneSurfaceGen.Select(static s => (Surface)s)),
            (3, (IGen<Surface>)SphereSurfaceGen.Select(static s => (Surface)s)),
            (2, (IGen<Surface>)NurbsSurfaceGen.Select(static s => (Surface)s)),
        ]);

    /// <summary>Generates valid box Breps for testing.</summary>
    [Pure]
    public static Gen<Brep> BoxBrepGen =>
        PlaneGen.SelectMany(plane =>
            Gen.Double[2.0, 15.0].SelectMany(x =>
                Gen.Double[2.0, 15.0].SelectMany(y =>
                    Gen.Double[2.0, 15.0].Select(z =>
                        Brep.CreateFromBox(new Box(plane, new Interval(0, x), new Interval(0, y), new Interval(0, z)))))))
        .Where(static b => b is not null && b.IsValid);

    /// <summary>Generates valid sphere Breps for known curvature testing.</summary>
    [Pure]
    public static Gen<Brep> SphereBrepGen =>
        SphereGen.Select(static sphere => Brep.CreateFromSphere(sphere))
        .Where(static b => b is not null && b.IsValid);

    /// <summary>Generates valid cylinder Breps for known curvature testing.</summary>
    [Pure]
    public static Gen<Brep> CylinderBrepGen =>
        PlaneGen.SelectMany(plane =>
            Gen.OneOfConst(ValidRadii).SelectMany(radius =>
                Gen.Double[5.0, 20.0].Select(height =>
                    Brep.CreateFromCylinder(new Cylinder(new Circle(plane, radius), height), capBottom: true, capTop: true))))
        .Where(static b => b is not null && b.IsValid);

    /// <summary>Generates any valid Brep type polymorphically.</summary>
    [Pure]
    public static Gen<Brep> BrepGen =>
        Gen.Frequency([
            (3, (IGen<Brep>)BoxBrepGen),
            (3, (IGen<Brep>)SphereBrepGen),
            (2, (IGen<Brep>)CylinderBrepGen),
        ]);

    /// <summary>Generates valid meshes from sphere for quality testing.</summary>
    [Pure]
    public static Gen<Mesh> SphereMeshGen =>
        SphereGen.SelectMany(sphere =>
            Gen.OneOfConst(ValidMeshDivisions).Select(divs =>
                Mesh.CreateFromSphere(sphere, divs, divs)))
        .Where(static m => m is not null && m.IsValid && m.Vertices.Count > 0 && m.Faces.Count > 0);

    /// <summary>Generates valid meshes from Breps for quality testing.</summary>
    [Pure]
    public static Gen<Mesh> BrepMeshGen =>
        BrepGen.Select(brep => {
            Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.Default);
            Mesh? combined = meshes?.Length > 0 ? meshes.Aggregate(static (a, b) => { a.Append(b); return a; }) : null;
            return combined;
        })
        .Where(static m => m is not null && m.IsValid && m.Vertices.Count > 0 && m.Faces.Count > 0)
        .Select(static m => m!);

    /// <summary>Generates any valid mesh type polymorphically.</summary>
    [Pure]
    public static Gen<Mesh> MeshGen =>
        Gen.Frequency([
            (3, (IGen<Mesh>)SphereMeshGen),
            (2, (IGen<Mesh>)BrepMeshGen),
        ]);

    /// <summary>Generates valid derivative orders for analysis.</summary>
    [Pure]
    public static Gen<int> DerivativeOrderGen => Gen.Int[1, 3];

    /// <summary>Generates normalized parameter values in [0,1].</summary>
    [Pure]
    public static Gen<double> NormalizedParameterGen => Gen.Double[0.1, 0.9];

    /// <summary>Generates normalized UV parameter pairs in [0,1] x [0,1].</summary>
    [Pure]
    public static Gen<(double U, double V)> UVParameterGen =>
        NormalizedParameterGen.SelectMany(u =>
            NormalizedParameterGen.Select(v => (u, v)));
}
