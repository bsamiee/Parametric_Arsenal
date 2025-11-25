using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Rhino.Analysis;
using Arsenal.Rhino.Tests.Core.Context;
using Arsenal.Rhino.Tests.Extraction;
using Arsenal.Tests.Common;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Analysis;

/// <summary>Tests Analysis module differential geometry and quality operations with property-based approaches.</summary>
[RhinoTestFixture]
public sealed class AnalysisTests {
    private const double ToleranceMultiplier = 10.0;

    private static readonly IGeometryContext DefaultContext = new GeometryContext(
        AbsoluteTolerance: 0.01, RelativeTolerance: 0.0, AngleToleranceRadians: RhinoMath.ToRadians(1.0), Units: UnitSystem.Millimeters);

    #region Curve Differential Geometry

    [Test]
    public void AnalyzeCurve_ValidInput_ReturnsSuccess() => GeometryGenerators.CurveGen.Run((Curve curve) => {
        Result<Analysis.CurveData> result = Analysis.Analyze(curve, DefaultContext);
        Test.Success(result, data => data.Length > 0 && data.Curvature >= 0 && data.Frame.IsValid && data.Location.IsValid);
    });

    [Test]
    public void AnalyzeCurve_SpecificParameter_ReturnsCorrectLocation() =>
        GeometryGenerators.CurveGen.Select(AnalysisGenerators.CurveParameterGen).Run((Curve curve, double t) => {
            double parameter = curve.Domain.ParameterAt(t);
            Result<Analysis.CurveData> result = Analysis.Analyze(curve, DefaultContext, parameter: parameter);
            Test.Success(result, data => data.Location.DistanceTo(curve.PointAt(parameter)) < DefaultContext.AbsoluteTolerance * ToleranceMultiplier);
        });

    [Test]
    public void AnalyzeCurve_DerivativeOrder_ReturnsMatchingDerivatives() =>
        GeometryGenerators.CurveGen.Select(AnalysisGenerators.DerivativeOrderGen).Run((Curve curve, int order) => {
            Result<Analysis.CurveData> result = Analysis.Analyze(curve, DefaultContext, derivativeOrder: order);
            Test.Success(result, data => data.Derivatives.Length >= 1);
        });

    [Test]
    public void AnalyzeCurve_Discontinuous_ReportsDiscontinuities() => AnalysisGenerators.DiscontinuousCurveGen.Run((Curve curve) => {
        Result<Analysis.CurveData> result = Analysis.Analyze(curve, DefaultContext);
        Test.Success(result, data => data.DiscontinuityParameters is not null && data.DiscontinuityTypes is not null);
    });

    #endregion

    #region Surface Differential Geometry

    [Test]
    public void AnalyzeSurface_ValidInput_ReturnsSuccess() => GeometryGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface, DefaultContext);
        Test.Success(result, data => data.Frame.IsValid && data.Location.IsValid && data.Normal.IsValid && data.Area > 0);
    });

    [Test]
    public void AnalyzeSurface_SpecificUV_ReturnsCorrectLocation() =>
        GeometryGenerators.SurfaceGen.Select(AnalysisGenerators.UVParameterGen).Run((Surface surface, (double U, double V) uv) => {
            (double u, double v) = (surface.Domain(0).ParameterAt(uv.U), surface.Domain(1).ParameterAt(uv.V));
            Result<Analysis.SurfaceData> result = Analysis.Analyze(surface, DefaultContext, uvParameter: (u, v));
            Test.Success(result, data => data.Location.DistanceTo(surface.PointAt(u, v)) < DefaultContext.AbsoluteTolerance * ToleranceMultiplier);
        });

    [Test]
    public void AnalyzeSurface_PrincipalDirections_AreOrthogonal() => AnalysisGenerators.SmoothSurfaceGen.Run((NurbsSurface surface) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface, DefaultContext);
        Test.Success(result, data => Math.Abs(data.PrincipalDir1 * data.PrincipalDir2) < 0.1);
    });

    #endregion

    #region Brep Differential Geometry

    [Test]
    public void AnalyzeBrep_ValidInput_ReturnsSuccess() => GeometryGenerators.BrepGen.Run((Brep brep) => {
        Result<Analysis.BrepData> result = Analysis.Analyze(brep, DefaultContext);
        Test.Success(result, data => data.Frame.IsValid && data.Location.IsValid && data.Vertices.Length > 0 && data.Edges.Length > 0 && data.Area > 0);
    });

    [Test]
    public void AnalyzeBrep_ClosestPoint_ReturnsValidDistance() =>
        GeometryGenerators.BrepGen.Select(AnalysisGenerators.TestPointGen).Run((Brep brep, Point3d testPoint) => {
            Result<Analysis.BrepData> result = Analysis.Analyze(brep, DefaultContext, testPoint: testPoint);
            Test.Success(result, data => data.ClosestPoint.IsValid && data.Distance >= 0);
        });

    [Test]
    public void AnalyzeBrep_SolidGeometry_ReportsIsSolid() => GeometryGenerators.BoxBrepGen.Run((Brep brep) => {
        Result<Analysis.BrepData> result = Analysis.Analyze(brep, DefaultContext);
        Test.Success(result, data => data.IsSolid && data.Volume > 0);
    });

    #endregion

    #region Mesh Differential Geometry

    [Test]
    public void AnalyzeMesh_ValidInput_ReturnsSuccess() => GeometryGenerators.MeshGen.Run((Mesh mesh) => {
        Result<Analysis.MeshData> result = Analysis.Analyze(mesh, DefaultContext);
        Test.Success(result, data => data.Frame.IsValid && data.Location.IsValid && data.Normal.IsValid && data.TopologyVertices.Length > 0 && data.TopologyEdges.Length > 0 && data.Area > 0);
    });

    [Test]
    public void AnalyzeMesh_SpecificVertex_ReturnsCorrectLocation() =>
        GeometryGenerators.MeshGen.Select(AnalysisGenerators.VertexIndexGen).Run((Mesh mesh, int vertexIndex) => {
            int idx = Math.Min(vertexIndex, mesh.Vertices.Count - 1);
            Result<Analysis.MeshData> result = Analysis.Analyze(mesh, DefaultContext, vertexIndex: idx);
            Test.Success(result, data => data.Location.DistanceTo(mesh.Vertices[idx]) < DefaultContext.AbsoluteTolerance * ToleranceMultiplier);
        });

    #endregion

    #region Quality Analysis

    [Test]
    public void AnalyzeSurfaceQuality_ValidInput_ReturnsSuccess() => GeometryGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceQualityResult> result = Analysis.AnalyzeSurfaceQuality(surface, DefaultContext);
        Test.Success(result, data => data.GaussianCurvatures.Length > 0 && data.MeanCurvatures.Length > 0 && data.UniformityScore >= 0.0 && data.UniformityScore <= 1.0);
    });

    [Test]
    public void AnalyzeSurfaceQuality_PlanarSurface_HasLowGaussianCurvature() => GeometryGenerators.PlaneSurfaceGen.Run((PlaneSurface surface) => {
        Result<Analysis.SurfaceQualityResult> result = Analysis.AnalyzeSurfaceQuality(surface, DefaultContext);
        Test.Success(result, data => data.GaussianCurvatures.Select(Math.Abs).Max() < DefaultContext.AbsoluteTolerance);
    });

    [Test]
    public void AnalyzeCurveFairness_ValidInput_ReturnsSuccess() => AnalysisGenerators.SmoothCurveGen.Run((NurbsCurve curve) => {
        Result<Analysis.CurveFairnessResult> result = Analysis.AnalyzeCurveFairness(curve, DefaultContext);
        Test.Success(result, data => data.SmoothnessScore >= 0.0 && data.SmoothnessScore <= 1.0 && data.CurvatureValues.Length > 0 && data.BendingEnergy >= 0);
    });

    [Test]
    public void AnalyzeCurveFairness_LineCurve_HasHighSmoothness() => GeometryGenerators.LineCurveGen.Run((LineCurve curve) => {
        Result<Analysis.CurveFairnessResult> result = Analysis.AnalyzeCurveFairness(curve, DefaultContext);
        Test.Success(result, data => data.SmoothnessScore >= 0.9 && data.InflectionPoints.Length == 0);
    });

    [Test]
    public void AnalyzeMeshForFEA_ValidInput_ReturnsSuccess() => AnalysisGenerators.AnalysisMeshGen.Run((Mesh mesh) => {
        Result<Analysis.MeshQualityResult> result = Analysis.AnalyzeMeshForFEA(mesh, DefaultContext);
        Test.Success(result, data => data.AspectRatios.Length == mesh.Faces.Count && data.Skewness.Length == mesh.Faces.Count && data.Jacobians.Length == mesh.Faces.Count && data.AspectRatios.All(ar => ar >= 1.0) && data.Skewness.All(s => s >= 0.0));
    });

    [Test]
    public void AnalyzeMeshForFEA_QualityFlags_NonNegative() => AnalysisGenerators.AnalysisMeshGen.Run((Mesh mesh) => {
        Result<Analysis.MeshQualityResult> result = Analysis.AnalyzeMeshForFEA(mesh, DefaultContext);
        Test.Success(result, data => data.QualityFlags.Warning >= 0 && data.QualityFlags.Critical >= 0);
    });

    #endregion

    #region Batch Operations

    [Test]
    public void AnalyzeMultipleCurves_ValidInput_ReturnsAllResults() => AnalysisGenerators.CurveBatchGen.Run((IReadOnlyList<Curve> curves) => {
        Result<IReadOnlyList<Analysis.IResult>> result = Analysis.AnalyzeMultiple(curves, DefaultContext);
        Test.Success(result, results => results.Count == curves.Count && results.All(r => r is Analysis.CurveData));
    });

    [Test]
    public void AnalyzeMultipleSurfaces_ValidInput_ReturnsAllResults() => AnalysisGenerators.SurfaceBatchGen.Run((IReadOnlyList<Surface> surfaces) => {
        Result<IReadOnlyList<Analysis.IResult>> result = Analysis.AnalyzeMultiple(surfaces, DefaultContext);
        Test.Success(result, results => results.Count == surfaces.Count && results.All(r => r is Analysis.SurfaceData));
    });

    [Test]
    public void AnalyzeMultipleBreps_ValidInput_ReturnsAllResults() => AnalysisGenerators.BrepBatchGen.Run((IReadOnlyList<Brep> breps) => {
        Result<IReadOnlyList<Analysis.IResult>> result = Analysis.AnalyzeMultiple(breps, DefaultContext);
        Test.Success(result, results => results.Count == breps.Count && results.All(r => r is Analysis.BrepData));
    });

    #endregion

    #region Error Handling and Context

    [Test]
    public void AnalyzeCurve_DegenerateCurve_ReturnsFailure() {
        using LineCurve degenerateCurve = new(Point3d.Origin, Point3d.Origin);
        Result<Analysis.CurveData> result = Analysis.Analyze(degenerateCurve, DefaultContext);
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void AnalyzeMeshForFEA_EmptyMesh_ReturnsFailure() {
        Mesh emptyMesh = new();
        Result<Analysis.MeshQualityResult> result = Analysis.AnalyzeMeshForFEA(emptyMesh, DefaultContext);
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void AnalysisRespectsToleranceContext() => GeometryGenerators.CurveGen.Select(ContextGenerators.GeometryContextGen).Run((Curve curve, GeometryContext context) => {
        Result<Analysis.CurveData> result = Analysis.Analyze(curve, context);
        Test.Success(result, data => data.Location.IsValid && data.Frame.IsValid);
    });

    #endregion
}
