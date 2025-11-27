using System.Diagnostics.Contracts;
using CsCheck;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Extraction;

/// <summary>Geometry generators for property-based testing with Rhino headless.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Test generators used for property-based testing")]
public static class GeometryGenerators {
    private static readonly double[] ValidDomainRange = [0.1, 100.0,];
    private static readonly int[] ValidPointCounts = [2, 3, 4, 5, 6, 8, 10,];
    private static readonly int[] DivisionCounts = [2, 3, 4, 5, 6, 8, 10, 15, 20,];

    /// <summary>Generates valid 3D points within a reasonable bounding region.</summary>
    [Pure] public static Gen<Point3d> Point3dGen =>
        Gen.Double[-100.0, 100.0].SelectMany(x =>
            Gen.Double[-100.0, 100.0].SelectMany(y =>
                Gen.Double[-100.0, 100.0].Select(z =>
                    new Point3d(x, y, z))))
        .Where(static p => p.IsValid);

    /// <summary>Generates valid 3D vectors with non-zero length.</summary>
    [Pure] public static Gen<Vector3d> Vector3dGen =>
        Gen.Double[-10.0, 10.0].SelectMany(x =>
            Gen.Double[-10.0, 10.0].SelectMany(y =>
                Gen.Double[-10.0, 10.0].Select(z =>
                    new Vector3d(x, y, z))))
        .Where(static v => v.IsValid && v.Length > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid planes at random positions and orientations.</summary>
    [Pure] public static Gen<Plane> PlaneGen =>
        Point3dGen.SelectMany(origin =>
            Vector3dGen.Select(normal => {
                Vector3d n = normal;
                n.Unitize();
                return new Plane(origin, n);
            }))
        .Where(static p => p.IsValid);

    /// <summary>Generates valid line curves between two distinct points.</summary>
    [Pure] public static Gen<LineCurve> LineCurveGen =>
        Point3dGen.SelectMany(p1 =>
            Point3dGen.Select(p2 => new LineCurve(p1, p2)))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid circles with positive radius.</summary>
    [Pure] public static Gen<Circle> CircleGen =>
        PlaneGen.SelectMany(plane =>
            Gen.Double[0.1, 50.0].Select(radius =>
                new Circle(plane, radius)))
        .Where(static c => c.IsValid && c.Radius > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid arc curves (partial circles).</summary>
    [Pure] public static Gen<ArcCurve> ArcCurveGen =>
        CircleGen.SelectMany(circle =>
            Gen.Double[RhinoMath.ToRadians(30.0), RhinoMath.ToRadians(330.0)].Select(angle =>
                new ArcCurve(new Arc(circle, angle))))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid polyline curves with random vertices.</summary>
    [Pure] public static Gen<PolylineCurve> PolylineCurveGen =>
        Gen.OneOfConst(ValidPointCounts).SelectMany(count =>
            Point3dGen.List[count, count].Select(points =>
                new PolylineCurve([.. points,])))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid NURBS curves via interpolation.</summary>
    [Pure] public static Gen<NurbsCurve> NurbsCurveGen =>
        Gen.OneOfConst(ValidPointCounts).SelectMany(count =>
            Point3dGen.List[count, count].Select(points =>
                Curve.CreateInterpolatedCurve([.. points,], degree: 3) as NurbsCurve))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance)
        .Select(static c => c!);

    /// <summary>Generates any valid curve type polymorphically.</summary>
    [Pure] public static Gen<Curve> CurveGen =>
        Gen.Frequency([
            (3, (IGen<Curve>)LineCurveGen.Select(static c => (Curve)c)),
            (2, (IGen<Curve>)ArcCurveGen.Select(static c => (Curve)c)),
            (2, (IGen<Curve>)PolylineCurveGen.Select(static c => (Curve)c)),
            (3, (IGen<Curve>)NurbsCurveGen.Select(static c => (Curve)c)),
        ]);

    /// <summary>Generates valid planar surfaces from rectangles.</summary>
    [Pure] public static Gen<PlaneSurface> PlaneSurfaceGen =>
        PlaneGen.SelectMany(plane =>
            Gen.Double[ValidDomainRange[0], ValidDomainRange[1]].SelectMany(width =>
                Gen.Double[ValidDomainRange[0], ValidDomainRange[1]].Select(height =>
                    new PlaneSurface(plane, new Interval(0, width), new Interval(0, height)))))
        .Where(static s => s is not null && s.IsValid);

    /// <summary>Generates valid NURBS surfaces via lofting curves.</summary>
    [Pure] public static Gen<NurbsSurface> NurbsSurfaceGen =>
        Gen.Int[3, 5].SelectMany(curveCount =>
            NurbsCurveGen.List[curveCount, curveCount].Select(curves => {
                Brep[] lofts = Brep.CreateFromLoft([.. curves,], Point3d.Unset, Point3d.Unset, LoftType.Normal, closed: false);
                return lofts?.Length > 0 && lofts[0].Faces.Count > 0
                    ? lofts[0].Faces[0].ToNurbsSurface()
                    : null;
            }))
        .Where(static s => s is not null && s.IsValid)
        .Select(static s => s!);

    /// <summary>Generates any valid surface type polymorphically.</summary>
    [Pure] public static Gen<Surface> SurfaceGen =>
        Gen.Frequency([
            (3, (IGen<Surface>)PlaneSurfaceGen.Select(static s => (Surface)s)),
            (2, (IGen<Surface>)NurbsSurfaceGen.Select(static s => (Surface)s)),
        ]);

    /// <summary>Generates valid box Breps.</summary>
    [Pure] public static Gen<Brep> BoxBrepGen =>
        PlaneGen.SelectMany(plane =>
            Gen.Double[1.0, 20.0].SelectMany(x =>
                Gen.Double[1.0, 20.0].SelectMany(y =>
                    Gen.Double[1.0, 20.0].Select(z =>
                        Brep.CreateFromBox(new Box(plane, new Interval(0, x), new Interval(0, y), new Interval(0, z)))))))
        .Where(static b => b is not null && b.IsValid);

    /// <summary>Generates valid sphere Breps.</summary>
    [Pure] public static Gen<Brep> SphereBrepGen =>
        Point3dGen.SelectMany(center =>
            Gen.Double[1.0, 20.0].Select(radius =>
                Brep.CreateFromSphere(new Sphere(center, radius))))
        .Where(static b => b is not null && b.IsValid);

    /// <summary>Generates valid cylinder Breps.</summary>
    [Pure] public static Gen<Brep> CylinderBrepGen =>
        PlaneGen.SelectMany(plane =>
            Gen.Double[1.0, 10.0].SelectMany(radius =>
                Gen.Double[1.0, 20.0].Select(height =>
                    Brep.CreateFromCylinder(new Cylinder(new Circle(plane, radius), height), capBottom: true, capTop: true))))
        .Where(static b => b is not null && b.IsValid);

    /// <summary>Generates any valid Brep type polymorphically.</summary>
    [Pure] public static Gen<Brep> BrepGen =>
        Gen.Frequency([
            (3, (IGen<Brep>)BoxBrepGen),
            (2, (IGen<Brep>)SphereBrepGen),
            (2, (IGen<Brep>)CylinderBrepGen),
        ]);

    /// <summary>Generates valid meshes from Breps.</summary>
    [Pure] public static Gen<Mesh> MeshGen =>
        BrepGen.Select(brep => {
            Mesh[] meshes = Mesh.CreateFromBrep(brep, MeshingParameters.Default);
            Mesh? combined = meshes?.Length > 0 ? meshes.Aggregate(static (a, b) => { a.Append(b); return a; }) : null;
            return combined;
        })
        .Where(static m => m is not null && m.IsValid && m.Vertices.Count > 0 && m.Faces.Count > 0)
        .Select(static m => m!);

    /// <summary>Generates valid division counts for extraction operations.</summary>
    [Pure] public static Gen<int> DivisionCountGen => Gen.OneOfConst(DivisionCounts);

    /// <summary>Generates valid division lengths based on typical curve lengths.</summary>
    [Pure] public static Gen<double> DivisionLengthGen => Gen.Double[0.5, 10.0].Where(static d => d > RhinoMath.ZeroTolerance);

    /// <summary>Generates valid angle thresholds for feature edge detection (radians).</summary>
    [Pure] public static Gen<double> AngleThresholdGen => Gen.Double[RhinoMath.ToRadians(10.0), RhinoMath.ToRadians(60.0)];

    /// <summary>Generates valid isocurve counts.</summary>
    [Pure] public static Gen<int> IsocurveCountGen => Gen.Int[2, 20];
}
