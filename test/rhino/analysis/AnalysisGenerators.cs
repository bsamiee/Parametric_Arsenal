using System.Diagnostics.Contracts;
using Arsenal.Rhino.Tests.Extraction;
using CsCheck;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Analysis;

/// <summary>Analysis-specific generators for property-based testing with Rhino headless.</summary>
public static class AnalysisGenerators {
    private static readonly int[] ValidVertexIndices = [0, 1, 2, 3, 4,];
    private static readonly int[] ValidFaceIndices = [0, 1, 2,];
    private static readonly int[] ValidDerivativeOrders = [1, 2, 3,];

    /// <summary>Generates valid derivative order values for analysis operations.</summary>
    [Pure]
    public static Gen<int> DerivativeOrderGen => Gen.OneOfConst(ValidDerivativeOrders);

    /// <summary>Generates valid vertex indices for mesh analysis.</summary>
    [Pure]
    public static Gen<int> VertexIndexGen => Gen.OneOfConst(ValidVertexIndices);

    /// <summary>Generates valid face indices for Brep analysis.</summary>
    [Pure]
    public static Gen<int> FaceIndexGen => Gen.OneOfConst(ValidFaceIndices);

    /// <summary>Generates valid UV parameter tuples within [0,1] range.</summary>
    [Pure]
    public static Gen<(double U, double V)> UVParameterGen =>
        Gen.Double[0.1, 0.9].SelectMany(u =>
            Gen.Double[0.1, 0.9].Select(v =>
                (u, v)));

    /// <summary>Generates valid curve parameter values within [0,1] normalized range.</summary>
    [Pure]
    public static Gen<double> CurveParameterGen => Gen.Double[0.1, 0.9];

    /// <summary>Generates smooth curves suitable for curvature analysis (NurbsCurves with degree 3+).</summary>
    [Pure]
    public static Gen<NurbsCurve> SmoothCurveGen =>
        Gen.Int[5, 8].SelectMany(count =>
            GeometryGenerators.Point3dGen.List[count, count].Select(points =>
                Curve.CreateInterpolatedCurve([.. points,], degree: 3) as NurbsCurve))
        .Where(static c => c is not null && c.IsValid && c.GetLength() > RhinoMath.ZeroTolerance && c.Degree >= 3)
        .Select(static c => c!);

    /// <summary>Generates smooth surfaces suitable for curvature analysis via lofting.</summary>
    [Pure]
    public static Gen<NurbsSurface> SmoothSurfaceGen =>
        Gen.Int[3, 4].SelectMany(curveCount =>
            SmoothCurveGen.List[curveCount, curveCount].Select(curves => {
                Brep[] lofts = Brep.CreateFromLoft([.. curves,], Point3d.Unset, Point3d.Unset, LoftType.Normal, closed: false);
                return lofts?.Length > 0 && lofts[0].Faces.Count > 0 ? lofts[0].Faces[0].ToNurbsSurface() : null;
            }))
        .Where(static s => s is not null && s.IsValid)
        .Select(static s => s!);

    /// <summary>Generates well-formed meshes with valid topology for FEA analysis.</summary>
    [Pure]
    public static Gen<Mesh> AnalysisMeshGen =>
        GeometryGenerators.BrepGen.Select(brep => {
            MeshingParameters meshParams = new() { SimplePlanes = true, RefineGrid = true, MinimumEdgeLength = 0.1, };
            Mesh[] meshes = Mesh.CreateFromBrep(brep, meshParams);
            Mesh? combined = meshes?.Length > 0 ? meshes.Aggregate(static (a, b) => { a.Append(b); return a; }) : null;
            combined?.Normals.ComputeNormals();
            combined?.Compact();
            return combined;
        })
        .Where(static m => m is not null && m.IsValid && m.Vertices.Count >= 4 && m.Faces.Count >= 2)
        .Select(static m => m!);

    /// <summary>Generates valid extrusion geometry for extrusion analysis.</summary>
    [Pure]
    public static Gen<Extrusion> ExtrusionGen =>
        GeometryGenerators.PlaneGen.SelectMany(plane =>
            Gen.Double[1.0, 10.0].SelectMany(width =>
                Gen.Double[1.0, 10.0].SelectMany(height =>
                    Gen.Double[1.0, 20.0].Select(extrusionHeight => {
                        Rectangle3d rect = new(plane, width, height);
                        Extrusion? extrusion = Extrusion.Create(rect.ToNurbsCurve(), extrusionHeight, cap: true);
                        return extrusion;
                    }))))
        .Where(static e => e is not null && e.IsValid)
        .Select(static e => e!);

    /// <summary>Generates valid test points near geometry for closest point analysis.</summary>
    [Pure]
    public static Gen<Point3d> TestPointGen =>
        GeometryGenerators.Point3dGen.Select(p => new Point3d(p.X * 0.5, p.Y * 0.5, p.Z * 0.5));

    /// <summary>Generates curves with known discontinuities (polyline curves).</summary>
    [Pure]
    public static Gen<Curve> DiscontinuousCurveGen =>
        GeometryGenerators.PolylineCurveGen.Select(static c => (Curve)c);

    /// <summary>Generates geometry lists for batch analysis testing.</summary>
    [Pure]
    public static Gen<IReadOnlyList<Curve>> CurveBatchGen =>
        GeometryGenerators.CurveGen.List[2, 5].Select(static list => (IReadOnlyList<Curve>)[.. list,]);

    /// <summary>Generates geometry lists for batch surface analysis testing.</summary>
    [Pure]
    public static Gen<IReadOnlyList<Surface>> SurfaceBatchGen =>
        GeometryGenerators.SurfaceGen.List[2, 4].Select(static list => (IReadOnlyList<Surface>)[.. list,]);

    /// <summary>Generates geometry lists for batch Brep analysis testing.</summary>
    [Pure]
    public static Gen<IReadOnlyList<Brep>> BrepBatchGen =>
        GeometryGenerators.BrepGen.List[2, 3].Select(static list => (IReadOnlyList<Brep>)[.. list,]);

    /// <summary>Generates geometry lists for batch Mesh analysis testing.</summary>
    [Pure]
    public static Gen<IReadOnlyList<Mesh>> MeshBatchGen =>
        GeometryGenerators.MeshGen.List[2, 3].Select(static list => (IReadOnlyList<Mesh>)[.. list,]);
}
