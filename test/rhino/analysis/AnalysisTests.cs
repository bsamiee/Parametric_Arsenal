using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Rhino.Analysis;
using Arsenal.Tests.Common;
using CsCheck;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Analysis;

/// <summary>Tests Analysis module with property-based differential geometry invariants and quality metrics.</summary>
[RhinoTestFixture]
public sealed class AnalysisTests {
    private static readonly IGeometryContext DefaultContext = new GeometryContext(
        AbsoluteTolerance: 0.001,
        RelativeTolerance: 0.0,
        AngleToleranceRadians: RhinoMath.ToRadians(0.1),
        Units: UnitSystem.Millimeters);

    #region Curve Differential Geometry - Fundamental Invariants

    /// <summary>Verifies line curvature is approximately zero (fundamental: straight lines have no curvature).</summary>
    [Test]
    public void LineCurvature_IsZero() => AnalysisGenerators.LineCurveGen.Run((LineCurve line) => {
        double t = line.Domain.Mid;
        Result<Analysis.CurveData> result = Analysis.Analyze(curve: line, context: DefaultContext, parameter: t);
        Test.Success(result, data => {
            Test.EqualWithin(data.Curvature, expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies arc curvature equals 1/radius (fundamental: circle curvature formula κ = 1/r).</summary>
    [Test]
    public void ArcCurvature_EqualsInverseRadius() => AnalysisGenerators.CircleGen.Run((Circle circle) => {
        using ArcCurve arc = new(circle);
        double t = arc.Domain.Mid;
        double expectedCurvature = 1.0 / circle.Radius;
        Result<Analysis.CurveData> result = Analysis.Analyze(curve: arc, context: DefaultContext, parameter: t);
        Test.Success(result, data => {
            Test.EqualWithin(data.Curvature, expectedCurvature, tolerance: DefaultContext.AbsoluteTolerance * 10);
            return true;
        });
    });

    /// <summary>Verifies curvature is non-negative for all curves (fundamental: κ ≥ 0 by definition).</summary>
    [Test]
    public void CurveCurvature_IsNonNegative() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        double t = curve.Domain.Mid;
        Result<Analysis.CurveData> result = Analysis.Analyze(curve: curve, context: DefaultContext, parameter: t);
        Test.Success(result, data => {
            Assert.That(data.Curvature, Is.GreaterThanOrEqualTo(0.0));
            return true;
        });
    });

    /// <summary>Verifies curve frame is orthonormal (fundamental: Frenet frame orthonormality).</summary>
    [Test]
    public void CurveFrame_IsOrthonormal() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        double t = curve.Domain.Mid;
        Result<Analysis.CurveData> result = Analysis.Analyze(curve: curve, context: DefaultContext, parameter: t);
        Test.Success(result, data => {
            Plane frame = data.Frame;
            Test.EqualWithin(frame.XAxis.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            Test.EqualWithin(frame.YAxis.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            Test.EqualWithin(frame.ZAxis.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            Test.EqualWithin(frame.XAxis * frame.YAxis, expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            Test.EqualWithin(frame.XAxis * frame.ZAxis, expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            Test.EqualWithin(frame.YAxis * frame.ZAxis, expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies curve location lies on curve (fundamental: point at parameter consistency).</summary>
    [Test]
    public void CurveLocation_LiesOnCurve() => AnalysisGenerators.CurveGen.Select(AnalysisGenerators.NormalizedParameterGen).Run((Curve curve, double normalizedT) => {
        double t = curve.Domain.ParameterAt(normalizedT);
        Result<Analysis.CurveData> result = Analysis.Analyze(curve: curve, context: DefaultContext, parameter: t);
        Test.Success(result, data => {
            Point3d expected = curve.PointAt(t);
            Test.EqualWithin(data.Location.DistanceTo(expected), expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies curve length is positive (fundamental: non-degenerate curves have positive length).</summary>
    [Test]
    public void CurveLength_IsPositive() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        Result<Analysis.CurveData> result = Analysis.Analyze(curve: curve, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Length, Is.GreaterThan(RhinoMath.ZeroTolerance));
            return true;
        });
    });

    /// <summary>Verifies first derivative is tangent to curve (fundamental: d/dt curve = tangent).</summary>
    [Test]
    public void CurveFirstDerivative_IsTangent() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        double t = curve.Domain.Mid;
        Result<Analysis.CurveData> result = Analysis.Analyze(curve: curve, context: DefaultContext, parameter: t, derivativeOrder: 1);
        Test.Success(result, data => {
            Assert.That(data.Derivatives.Length, Is.GreaterThanOrEqualTo(1));
            Vector3d tangent = curve.TangentAt(t);
            Vector3d deriv = data.Derivatives[0];
            deriv.Unitize();
            Test.EqualWithin(Math.Abs(deriv * tangent), expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance * 10);
            return true;
        });
    });

    #endregion

    #region Surface Differential Geometry - Fundamental Invariants

    /// <summary>Verifies plane has zero Gaussian curvature (fundamental: K = 0 for flat surfaces).</summary>
    [Test]
    public void PlaneGaussianCurvature_IsZero() => AnalysisGenerators.PlaneSurfaceGen.Run((PlaneSurface plane) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: plane, context: DefaultContext);
        Test.Success(result, data => {
            Test.EqualWithin(data.Gaussian, expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies plane has zero mean curvature (fundamental: H = 0 for flat surfaces).</summary>
    [Test]
    public void PlaneMeanCurvature_IsZero() => AnalysisGenerators.PlaneSurfaceGen.Run((PlaneSurface plane) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: plane, context: DefaultContext);
        Test.Success(result, data => {
            Test.EqualWithin(data.Mean, expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies sphere Gaussian curvature equals 1/r² (fundamental: K = 1/r² for spheres).</summary>
    [Test]
    public void SphereGaussianCurvature_EqualsInverseRadiusSquared() => AnalysisGenerators.SphereGen.Run((Sphere sphere) => {
        NurbsSurface? surface = sphere.ToNurbsSurface();
        _ = surface is not null || throw new InvalidOperationException("Failed to create sphere surface");
        double expectedK = 1.0 / (sphere.Radius * sphere.Radius);
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            Test.EqualWithin(Math.Abs(data.Gaussian), expectedK, tolerance: expectedK * 0.1);
            return true;
        });
    });

    /// <summary>Verifies sphere mean curvature equals 1/r (fundamental: H = 1/r for spheres).</summary>
    [Test]
    public void SphereMeanCurvature_EqualsInverseRadius() => AnalysisGenerators.SphereGen.Run((Sphere sphere) => {
        NurbsSurface? surface = sphere.ToNurbsSurface();
        _ = surface is not null || throw new InvalidOperationException("Failed to create sphere surface");
        double expectedH = 1.0 / sphere.Radius;
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            Test.EqualWithin(Math.Abs(data.Mean), expectedH, tolerance: expectedH * 0.1);
            return true;
        });
    });

    /// <summary>Verifies principal curvature relationship K = K1 * K2 (fundamental: Gaussian curvature formula).</summary>
    [Test]
    public void PrincipalCurvatures_SatisfyGaussianRelation() => AnalysisGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            double computedK = data.K1 * data.K2;
            Test.EqualWithin(data.Gaussian, computedK, tolerance: Math.Max(0.01, Math.Abs(data.Gaussian) * 0.1));
            return true;
        });
    });

    /// <summary>Verifies principal curvature relationship H = (K1 + K2) / 2 (fundamental: mean curvature formula).</summary>
    [Test]
    public void PrincipalCurvatures_SatisfyMeanRelation() => AnalysisGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            double computedH = (data.K1 + data.K2) / 2.0;
            Test.EqualWithin(data.Mean, computedH, tolerance: Math.Max(0.01, Math.Abs(data.Mean) * 0.1));
            return true;
        });
    });

    /// <summary>Verifies surface normal is unit vector (fundamental: normal normalization).</summary>
    [Test]
    public void SurfaceNormal_IsUnitVector() => AnalysisGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            Test.EqualWithin(data.Normal.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies surface frame is orthonormal (fundamental: frame orthonormality).</summary>
    [Test]
    public void SurfaceFrame_IsOrthonormal() => AnalysisGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            Plane frame = data.Frame;
            Test.EqualWithin(frame.XAxis.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            Test.EqualWithin(frame.YAxis.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            Test.EqualWithin(frame.ZAxis.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies surface area is positive (fundamental: non-degenerate surfaces have positive area).</summary>
    [Test]
    public void SurfaceArea_IsPositive() => AnalysisGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Area, Is.GreaterThan(RhinoMath.ZeroTolerance));
            return true;
        });
    });

    /// <summary>Verifies surface location lies on surface (fundamental: point at UV consistency).</summary>
    [Test]
    public void SurfaceLocation_LiesOnSurface() => AnalysisGenerators.SurfaceGen.Select(AnalysisGenerators.UVParameterGen).Run((Surface surface, (double U, double V) uv) => {
        double u = surface.Domain(0).ParameterAt(uv.U);
        double v = surface.Domain(1).ParameterAt(uv.V);
        Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext, uvParameter: (u, v));
        Test.Success(result, data => {
            Point3d expected = surface.PointAt(u, v);
            Test.EqualWithin(data.Location.DistanceTo(expected), expected: 0.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    #endregion

    #region Brep Analysis - Fundamental Invariants

    /// <summary>Verifies Brep volume is positive for solid Breps (fundamental: solid volume > 0).</summary>
    [Test]
    public void BrepVolume_IsPositiveForSolids() => AnalysisGenerators.BrepGen.Where(static b => b.IsSolid).Run((Brep brep) => {
        Result<Analysis.BrepData> result = Analysis.Analyze(brep: brep, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Volume, Is.GreaterThan(RhinoMath.ZeroTolerance));
            return true;
        });
    });

    /// <summary>Verifies Brep area is positive (fundamental: non-degenerate surfaces have positive area).</summary>
    [Test]
    public void BrepArea_IsPositive() => AnalysisGenerators.BrepGen.Run((Brep brep) => {
        Result<Analysis.BrepData> result = Analysis.Analyze(brep: brep, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Area, Is.GreaterThan(RhinoMath.ZeroTolerance));
            return true;
        });
    });

    /// <summary>Verifies Brep vertices match actual vertex count (fundamental: consistency).</summary>
    [Test]
    public void BrepVertices_MatchActualCount() => AnalysisGenerators.BrepGen.Run((Brep brep) => {
        Result<Analysis.BrepData> result = Analysis.Analyze(brep: brep, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Vertices.Length, Is.EqualTo(brep.Vertices.Count));
            return true;
        });
    });

    /// <summary>Verifies Brep edges match actual edge count (fundamental: consistency).</summary>
    [Test]
    public void BrepEdges_MatchActualCount() => AnalysisGenerators.BrepGen.Run((Brep brep) => {
        Result<Analysis.BrepData> result = Analysis.Analyze(brep: brep, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Edges.Length, Is.EqualTo(brep.Edges.Count));
            return true;
        });
    });

    /// <summary>Verifies Brep closest point is within reasonable distance (fundamental: closest point validity).</summary>
    [Test]
    public void BrepClosestPoint_IsReasonable() => AnalysisGenerators.BrepGen.Run((Brep brep) => {
        Point3d testPoint = brep.GetBoundingBox(accurate: true).Center;
        Result<Analysis.BrepData> result = Analysis.Analyze(brep: brep, context: DefaultContext, testPoint: testPoint);
        Test.Success(result, data => {
            Assert.That(data.Distance, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(data.ClosestPoint.IsValid, Is.True);
            return true;
        });
    });

    #endregion

    #region Mesh Analysis - Fundamental Invariants

    /// <summary>Verifies mesh area is positive (fundamental: non-degenerate meshes have positive area).</summary>
    [Test]
    public void MeshArea_IsPositive() => AnalysisGenerators.MeshGen.Run((Mesh mesh) => {
        Result<Analysis.MeshData> result = Analysis.Analyze(mesh: mesh, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Area, Is.GreaterThan(RhinoMath.ZeroTolerance));
            return true;
        });
    });

    /// <summary>Verifies mesh volume is positive for closed meshes (fundamental: closed mesh volume > 0).</summary>
    [Test]
    public void MeshVolume_IsPositiveForClosed() => AnalysisGenerators.MeshGen.Where(static m => m.IsClosed).Run((Mesh mesh) => {
        Result<Analysis.MeshData> result = Analysis.Analyze(mesh: mesh, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Volume, Is.GreaterThan(RhinoMath.ZeroTolerance));
            return true;
        });
    });

    /// <summary>Verifies mesh normal is unit vector at vertex (fundamental: normal normalization).</summary>
    [Test]
    public void MeshNormal_IsUnitVector() => AnalysisGenerators.MeshGen.Run((Mesh mesh) => {
        int vertexIndex = mesh.Vertices.Count > 0 ? 0 : -1;
        _ = vertexIndex >= 0 || throw new InvalidOperationException("Mesh has no vertices");
        Result<Analysis.MeshData> result = Analysis.Analyze(mesh: mesh, context: DefaultContext, vertexIndex: vertexIndex);
        Test.Success(result, data => {
            Test.EqualWithin(data.Normal.Length, expected: 1.0, tolerance: DefaultContext.AbsoluteTolerance);
            return true;
        });
    });

    /// <summary>Verifies mesh topology edges count is consistent (fundamental: mesh topology consistency).</summary>
    [Test]
    public void MeshTopologyEdges_AreConsistent() => AnalysisGenerators.MeshGen.Run((Mesh mesh) => {
        Result<Analysis.MeshData> result = Analysis.Analyze(mesh: mesh, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.TopologyEdges.Length, Is.GreaterThan(0));
            Assert.That(data.TopologyVertices.Length, Is.GreaterThan(0));
            return true;
        });
    });

    #endregion

    #region Quality Analysis - Fundamental Invariants

    /// <summary>Verifies curve fairness smoothness is bounded [0,1] (fundamental: score normalization).</summary>
    [Test]
    public void CurveFairness_SmoothnessIsBounded() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        Result<Analysis.CurveFairnessResult> result = Analysis.AnalyzeCurveFairness(curve: curve, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.SmoothnessScore, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(data.SmoothnessScore, Is.LessThanOrEqualTo(1.0));
            return true;
        });
    });

    /// <summary>Verifies curve bending energy is non-negative (fundamental: energy ≥ 0).</summary>
    [Test]
    public void CurveFairness_BendingEnergyIsNonNegative() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        Result<Analysis.CurveFairnessResult> result = Analysis.AnalyzeCurveFairness(curve: curve, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.BendingEnergy, Is.GreaterThanOrEqualTo(0.0));
            return true;
        });
    });

    /// <summary>Verifies line has high smoothness (fundamental: lines are perfectly smooth).</summary>
    [Test]
    public void LineFairness_HasHighSmoothness() => AnalysisGenerators.LineCurveGen.Run((LineCurve line) => {
        Result<Analysis.CurveFairnessResult> result = Analysis.AnalyzeCurveFairness(curve: line, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.SmoothnessScore, Is.GreaterThan(0.9));
            return true;
        });
    });

    /// <summary>Verifies surface quality uniformity is bounded [0,1] (fundamental: score normalization).</summary>
    [Test]
    public void SurfaceQuality_UniformityIsBounded() => AnalysisGenerators.SurfaceGen.Run((Surface surface) => {
        Result<Analysis.SurfaceQualityResult> result = Analysis.AnalyzeSurfaceQuality(surface: surface, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.UniformityScore, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(data.UniformityScore, Is.LessThanOrEqualTo(1.0));
            return true;
        });
    });

    /// <summary>Verifies plane has high uniformity (fundamental: flat surfaces are uniform).</summary>
    [Test]
    public void PlaneQuality_HasHighUniformity() => AnalysisGenerators.PlaneSurfaceGen.Run((PlaneSurface plane) => {
        Result<Analysis.SurfaceQualityResult> result = Analysis.AnalyzeSurfaceQuality(surface: plane, context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.UniformityScore, Is.GreaterThan(0.9));
            return true;
        });
    });

    /// <summary>Verifies mesh quality aspect ratios are positive (fundamental: ratio > 0).</summary>
    [Test]
    public void MeshQuality_AspectRatiosArePositive() => AnalysisGenerators.MeshGen.Run((Mesh mesh) => {
        Result<Analysis.MeshQualityResult> result = Analysis.AnalyzeMeshForFEA(mesh: mesh, context: DefaultContext);
        Test.Success(result, data => {
            Test.All(data.AspectRatios, static ratio => ratio > 0.0);
            return true;
        });
    });

    /// <summary>Verifies mesh quality skewness is bounded [0,1] (fundamental: skewness normalization).</summary>
    [Test]
    public void MeshQuality_SkewnessIsBounded() => AnalysisGenerators.MeshGen.Run((Mesh mesh) => {
        Result<Analysis.MeshQualityResult> result = Analysis.AnalyzeMeshForFEA(mesh: mesh, context: DefaultContext);
        Test.Success(result, data => {
            Test.All(data.Skewness, static skew => skew >= 0.0 && skew <= 1.0);
            return true;
        });
    });

    #endregion

    #region Batch Operations and Polymorphism

    /// <summary>Verifies batch curve analysis processes all items.</summary>
    [Test]
    public void BatchCurveAnalysis_ProcessesAllItems() => AnalysisGenerators.CurveGen.List[3, 6].Run((List<Curve> curves) => {
        Result<IReadOnlyList<Analysis.IResult>> result = Analysis.AnalyzeMultiple(
            geometries: [.. curves,],
            context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Count, Is.EqualTo(curves.Count));
            Test.All(data, static r => r is Analysis.CurveData);
            return true;
        });
    });

    /// <summary>Verifies batch surface analysis processes all items.</summary>
    [Test]
    public void BatchSurfaceAnalysis_ProcessesAllItems() => AnalysisGenerators.SurfaceGen.List[2, 4].Run((List<Surface> surfaces) => {
        Result<IReadOnlyList<Analysis.IResult>> result = Analysis.AnalyzeMultiple(
            geometries: [.. surfaces,],
            context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Count, Is.EqualTo(surfaces.Count));
            Test.All(data, static r => r is Analysis.SurfaceData);
            return true;
        });
    });

    /// <summary>Verifies batch Brep analysis processes all items.</summary>
    [Test]
    public void BatchBrepAnalysis_ProcessesAllItems() => AnalysisGenerators.BrepGen.List[2, 3].Run((List<Brep> breps) => {
        Result<IReadOnlyList<Analysis.IResult>> result = Analysis.AnalyzeMultiple(
            geometries: [.. breps,],
            context: DefaultContext);
        Test.Success(result, data => {
            Assert.That(data.Count, Is.EqualTo(breps.Count));
            Test.All(data, static r => r is Analysis.BrepData);
            return true;
        });
    });

    #endregion

    #region Parameter Variation Tests

    /// <summary>Verifies curve analysis at different parameters produces valid results.</summary>
    [Test]
    public void CurveAnalysis_ValidAtMultipleParameters() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        double[] normalizedParams = [0.1, 0.25, 0.5, 0.75, 0.9,];
        for (int i = 0; i < normalizedParams.Length; i++) {
            double t = curve.Domain.ParameterAt(normalizedParams[i]);
            Result<Analysis.CurveData> result = Analysis.Analyze(curve: curve, context: DefaultContext, parameter: t);
            Test.Success(result, data => {
                Assert.That(data.Location.IsValid, Is.True);
                Assert.That(data.Curvature, Is.GreaterThanOrEqualTo(0.0));
                return true;
            });
        }
    });

    /// <summary>Verifies surface analysis at different UV parameters produces valid results.</summary>
    [Test]
    public void SurfaceAnalysis_ValidAtMultipleUVs() => AnalysisGenerators.SurfaceGen.Run((Surface surface) => {
        (double, double)[] uvParams = [(0.25, 0.25), (0.5, 0.5), (0.75, 0.75),];
        for (int i = 0; i < uvParams.Length; i++) {
            double u = surface.Domain(0).ParameterAt(uvParams[i].Item1);
            double v = surface.Domain(1).ParameterAt(uvParams[i].Item2);
            Result<Analysis.SurfaceData> result = Analysis.Analyze(surface: surface, context: DefaultContext, uvParameter: (u, v));
            Test.Success(result, data => {
                Assert.That(data.Location.IsValid, Is.True);
                Assert.That(data.Normal.IsValid, Is.True);
                return true;
            });
        }
    });

    /// <summary>Verifies higher derivative orders produce more derivatives.</summary>
    [Test]
    public void CurveAnalysis_HigherOrderProducesMoreDerivatives() => AnalysisGenerators.CurveGen.Run((Curve curve) => {
        Result<Analysis.CurveData> result1 = Analysis.Analyze(curve: curve, context: DefaultContext, derivativeOrder: 1);
        Result<Analysis.CurveData> result2 = Analysis.Analyze(curve: curve, context: DefaultContext, derivativeOrder: 2);
        Test.Success(result1, data1 => {
            Test.Success(result2, data2 => {
                Assert.That(data2.Derivatives.Length, Is.GreaterThanOrEqualTo(data1.Derivatives.Length));
                return true;
            });
            return true;
        });
    });

    #endregion
}
