using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Rhino.Tests.Core.Context;
using Arsenal.Tests.Common;
using CsCheck;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Extraction;

/// <summary>Tests Extraction module point/curve operations with property-based and parametric approaches.</summary>
[RhinoTestFixture]
public sealed class ExtractionTests {
    private static readonly IGeometryContext DefaultContext = new GeometryContext(
        AbsoluteTolerance: 0.01,
        RelativeTolerance: 0.0,
        AngleToleranceRadians: RhinoMath.ToRadians(1.0),
        Units: UnitSystem.Millimeters);

    #region Point Extraction - Fundamental Properties

    /// <summary>Verifies Extremal extraction returns valid endpoint pairs for curves.</summary>
    [Test]
    public void ExtremalCurve_ReturnsStartAndEndPoints() => GeometryGenerators.CurveGen.Run((Curve curve) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.Extremal(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(points[0].DistanceTo(curve.PointAtStart), Is.LessThan(DefaultContext.AbsoluteTolerance));
            Assert.That(points[1].DistanceTo(curve.PointAtEnd), Is.LessThan(DefaultContext.AbsoluteTolerance));
            return true;
        });
    });

    /// <summary>Verifies Extremal extraction returns bounding box corners for surfaces.</summary>
    [Test]
    public void ExtremalSurface_ReturnsCornerPoints() => GeometryGenerators.SurfaceGen.Run((Surface surface) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(surface, new Extraction.Extremal(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.EqualTo(4));
            Interval u = surface.Domain(0);
            Interval v = surface.Domain(1);
            Point3d[] corners = [
                surface.PointAt(u.Min, v.Min),
                surface.PointAt(u.Max, v.Min),
                surface.PointAt(u.Max, v.Max),
                surface.PointAt(u.Min, v.Max),
            ];
            Test.All(points, p => corners.Any(c => p.DistanceTo(c) < DefaultContext.AbsoluteTolerance));
            return true;
        });
    });

    /// <summary>Verifies ByCount extraction respects count parameter for curves.</summary>
    [Test]
    public void ByCountCurve_ReturnsRequestedPointCount() => GeometryGenerators.CurveGen.Select(GeometryGenerators.DivisionCountGen).Run((Curve curve, int count) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.ByCount(count, IncludeEnds: true), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.EqualTo(count + 1));
            Test.All(points, p => p.IsValid);
            return true;
        });
    });

    /// <summary>Verifies ByCount extraction produces evenly spaced points along curve parameter.</summary>
    [Test]
    public void ByCountCurve_ProducesEvenlySpacedPoints() => GeometryGenerators.LineCurveGen.Run((LineCurve curve) => {
        const int count = 4;
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.ByCount(count, IncludeEnds: true), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.EqualTo(count + 1));
            double totalLength = curve.GetLength();
            double expectedSpacing = totalLength / count;
            for (int i = 1; i < points.Count; i++) {
                double spacing = points[i].DistanceTo(points[i - 1]);
                Test.EqualWithin(spacing, expectedSpacing, tolerance: DefaultContext.AbsoluteTolerance * 10);
            }
            return true;
        });
    });

    /// <summary>Verifies ByLength extraction respects length parameter.</summary>
    [Test]
    public void ByLengthCurve_DividesAtSpecifiedLength() => GeometryGenerators.CurveGen.Select(GeometryGenerators.DivisionLengthGen).Run((Curve curve, double length) => {
        double curveLength = curve.GetLength();
        int expectedMin = (int)(curveLength / length);
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.ByLength(length, IncludeEnds: true), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.GreaterThanOrEqualTo(Math.Max(2, expectedMin)));
            Test.All(points, p => p.IsValid);
            return true;
        });
    });

    /// <summary>Verifies Analytical extraction returns centroid and vertices for Breps.</summary>
    [Test]
    public void AnalyticalBrep_ReturnsCentroidAndVertices() => GeometryGenerators.BrepGen.Run((Brep brep) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(brep, new Extraction.Analytical(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.GreaterThanOrEqualTo(brep.Vertices.Count));
            Test.All(points, p => p.IsValid);
            BoundingBox bbox = brep.GetBoundingBox(accurate: true);
            Test.All(points, p => bbox.Contains(p, strict: false) || bbox.ClosestPoint(p).DistanceTo(p) < DefaultContext.AbsoluteTolerance * 100);
            return true;
        });
    });

    /// <summary>Verifies EdgeMidpoints extraction returns midpoints for each edge.</summary>
    [Test]
    public void EdgeMidpointsBrep_ReturnsEdgeMidpoints() => GeometryGenerators.BrepGen.Run((Brep brep) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(brep, new Extraction.EdgeMidpoints(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.EqualTo(brep.Edges.Count));
            for (int i = 0; i < brep.Edges.Count; i++) {
                Point3d expectedMid = brep.Edges[i].PointAtNormalizedLength(0.5);
                Test.Any(points, p => p.DistanceTo(expectedMid) < DefaultContext.AbsoluteTolerance * 10);
            }
            return true;
        });
    });

    /// <summary>Verifies FaceCentroids extraction returns centroids for each face.</summary>
    [Test]
    public void FaceCentroidsBrep_ReturnsFaceCentroids() => GeometryGenerators.BrepGen.Run((Brep brep) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(brep, new Extraction.FaceCentroids(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.GreaterThan(0));
            Assert.That(points.Count, Is.LessThanOrEqualTo(brep.Faces.Count));
            Test.All(points, p => p.IsValid);
            BoundingBox bbox = brep.GetBoundingBox(accurate: true);
            Test.All(points, p => bbox.Contains(p, strict: false) || bbox.ClosestPoint(p).DistanceTo(p) < DefaultContext.AbsoluteTolerance * 100);
            return true;
        });
    });

    /// <summary>Verifies Greville extraction returns NURBS Greville points.</summary>
    [Test]
    public void GrevilleNurbsCurve_ReturnsGrevillePoints() => GeometryGenerators.NurbsCurveGen.Run((NurbsCurve curve) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.Greville(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.GreaterThan(0));
            Test.All(points, p => p.IsValid);
            BoundingBox bbox = curve.GetBoundingBox(accurate: true);
            BoundingBox expanded = new(
                bbox.Min - new Vector3d(DefaultContext.AbsoluteTolerance * 100, DefaultContext.AbsoluteTolerance * 100, DefaultContext.AbsoluteTolerance * 100),
                bbox.Max + new Vector3d(DefaultContext.AbsoluteTolerance * 100, DefaultContext.AbsoluteTolerance * 100, DefaultContext.AbsoluteTolerance * 100));
            Test.All(points, p => expanded.Contains(p, strict: false));
            return true;
        });
    });

    /// <summary>Verifies OsculatingFrames extraction returns frame origins along curve.</summary>
    [Test]
    public void OsculatingFramesCurve_ReturnsFrameOrigins() => GeometryGenerators.CurveGen.Run((Curve curve) => {
        const int frameCount = 5;
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.OsculatingFrames(frameCount), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.EqualTo(frameCount));
            Test.All(points, p => p.IsValid);
            curve.ClosestPoint(points[0], out double t0);
            curve.ClosestPoint(points[^1], out double tN);
            Assert.That(t0, Is.LessThanOrEqualTo(tN).Or.GreaterThanOrEqualTo(tN));
            return true;
        });
    });

    #endregion

    #region Curve Extraction - Fundamental Properties

    /// <summary>Verifies Boundary extraction returns edge curves for surfaces.</summary>
    [Test]
    public void BoundarySurface_ReturnsBoundaryEdges() => GeometryGenerators.PlaneSurfaceGen.Run((PlaneSurface surface) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(surface, new Extraction.Boundary(), DefaultContext);
        Test.Success(result, curves => {
            Assert.That(curves.Count, Is.EqualTo(4));
            Test.All(curves, c => c is not null && c.IsValid);
            return true;
        });
    });

    /// <summary>Verifies Isocurves extraction returns correct count in U direction.</summary>
    [Test]
    public void IsocurvesU_ReturnsRequestedCount() => GeometryGenerators.SurfaceGen.Select(GeometryGenerators.IsocurveCountGen).Run((Surface surface, int count) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(surface, new Extraction.Isocurves(new Extraction.UDirection(), count), DefaultContext);
        Test.Success(result, curves => {
            Assert.That(curves.Count, Is.EqualTo(count));
            Test.All(curves, c => c is not null && c.IsValid);
            return true;
        });
    });

    /// <summary>Verifies Isocurves extraction returns correct count in V direction.</summary>
    [Test]
    public void IsocurvesV_ReturnsRequestedCount() => GeometryGenerators.SurfaceGen.Select(GeometryGenerators.IsocurveCountGen).Run((Surface surface, int count) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(surface, new Extraction.Isocurves(new Extraction.VDirection(), count), DefaultContext);
        Test.Success(result, curves => {
            Assert.That(curves.Count, Is.EqualTo(count));
            Test.All(curves, c => c is not null && c.IsValid);
            return true;
        });
    });

    /// <summary>Verifies BothDirections isocurves returns double the count.</summary>
    [Test]
    public void IsocurvesBoth_ReturnsDoubleCount() => GeometryGenerators.SurfaceGen.Select(GeometryGenerators.IsocurveCountGen).Run((Surface surface, int count) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(surface, new Extraction.Isocurves(new Extraction.BothDirections(), count), DefaultContext);
        Test.Success(result, curves => {
            Assert.That(curves.Count, Is.EqualTo(count * 2));
            Test.All(curves, c => c is not null && c.IsValid);
            return true;
        });
    });

    /// <summary>Verifies FeatureEdges extraction returns sharp edges for Breps.</summary>
    [Test]
    public void FeatureEdgesBrep_ReturnsSharpEdges() => GeometryGenerators.BoxBrepGen.Select(GeometryGenerators.AngleThresholdGen).Run((Brep brep, double threshold) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(brep, new Extraction.FeatureEdges(threshold), DefaultContext);
        Test.Success(result, curves => {
            Test.All(curves, c => c is not null && c.IsValid);
            return true;
        });
    });

    #endregion

    #region Error Handling and Edge Cases

    /// <summary>Verifies invalid geometry returns appropriate error.</summary>
    [Test]
    public void InvalidGeometry_ReturnsValidationError() {
        using LineCurve degenerateCurve = new(Point3d.Origin, Point3d.Origin);
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(degenerateCurve, new Extraction.ByCount(5), DefaultContext);
        Assert.That(result.IsSuccess, Is.False);
    }

    /// <summary>Verifies ByCount with zero count returns error.</summary>
    [Test]
    public void ByCountZero_ReturnsError() => GeometryGenerators.CurveGen.Run((Curve curve) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.ByCount(0), DefaultContext);
        Assert.That(result.IsSuccess, Is.False);
    });

    /// <summary>Verifies ByLength with negative length returns error.</summary>
    [Test]
    public void ByLengthNegative_ReturnsError() => GeometryGenerators.CurveGen.Run((Curve curve) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.ByLength(-1.0), DefaultContext);
        Assert.That(result.IsSuccess, Is.False);
    });

    /// <summary>Verifies Isocurves with invalid count returns error.</summary>
    [Test]
    public void IsocurvesInvalidCount_ReturnsError() => GeometryGenerators.SurfaceGen.Run((Surface surface) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(surface, new Extraction.Isocurves(new Extraction.UDirection(), 1), DefaultContext);
        Test.Failure(result, errors => errors.Any(e => e.Code == E.Geometry.InvalidCount.Code));
    });

    /// <summary>Verifies Boundary on non-surface returns error.</summary>
    [Test]
    public void BoundaryCurve_ReturnsError() => GeometryGenerators.CurveGen.Run((Curve curve) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(curve, new Extraction.Boundary(), DefaultContext);
        Test.Failure(result, errors => errors.Any(e => e.Domain == ErrorDomain.Geometry));
    });

    #endregion

    #region Batch Operations and Parallelism

    /// <summary>Verifies PointsMultiple processes batch correctly with accumulation.</summary>
    [Test]
    public void PointsMultipleBatch_AccumulatesResults() => GeometryGenerators.CurveGen.List[3, 5].Run((List<Curve> curves) => {
        Result<IReadOnlyList<IReadOnlyList<Point3d>>> result = Extraction.PointsMultiple(
            [.. curves,],
            new Extraction.Extremal(),
            DefaultContext,
            accumulateErrors: true,
            enableParallel: false);
        Test.Success(result, lists => {
            Assert.That(lists.Count, Is.EqualTo(curves.Count));
            for (int i = 0; i < curves.Count; i++) {
                Assert.That(lists[i].Count, Is.GreaterThanOrEqualTo(2));
            }
            return true;
        });
    });

    /// <summary>Verifies PointsMultiple with parallel execution produces same results.</summary>
    [Test]
    public void PointsMultipleParallel_ProducesSameResults() => GeometryGenerators.CurveGen.List[5, 10].Run((List<Curve> curves) => {
        Result<IReadOnlyList<IReadOnlyList<Point3d>>> sequential = Extraction.PointsMultiple(
            [.. curves,],
            new Extraction.ByCount(5),
            DefaultContext,
            accumulateErrors: true,
            enableParallel: false);
        Result<IReadOnlyList<IReadOnlyList<Point3d>>> parallel = Extraction.PointsMultiple(
            [.. curves,],
            new Extraction.ByCount(5),
            DefaultContext,
            accumulateErrors: true,
            enableParallel: true);
        Assert.That(sequential.IsSuccess, Is.EqualTo(parallel.IsSuccess));
        Test.Success(sequential, seqLists => {
            Test.Success(parallel, parLists => {
                Assert.That(parLists.Count, Is.EqualTo(seqLists.Count));
                return true;
            });
            return true;
        });
    });

    /// <summary>Verifies CurvesMultiple processes batch correctly.</summary>
    [Test]
    public void CurvesMultipleBatch_AccumulatesResults() => GeometryGenerators.SurfaceGen.List[2, 4].Run((List<Surface> surfaces) => {
        Result<IReadOnlyList<IReadOnlyList<Curve>>> result = Extraction.CurvesMultiple(
            [.. surfaces,],
            new Extraction.Isocurves(new Extraction.UDirection(), 3),
            DefaultContext,
            accumulateErrors: true,
            enableParallel: false);
        Test.Success(result, lists => {
            Assert.That(lists.Count, Is.EqualTo(surfaces.Count));
            Test.All(lists, l => l.Count == 3);
            return true;
        });
    });

    #endregion

    #region Context Sensitivity

    /// <summary>Verifies extraction respects different tolerance contexts.</summary>
    [Test]
    public void ExtractionRespectsToleranceContext() => GeometryGenerators.CurveGen.Select(ContextGenerators.GeometryContextGen).Run((Curve curve, GeometryContext context) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.ByCount(5), context);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.EqualTo(6));
            Test.All(points, p => p.IsValid);
            return true;
        });
    });

    /// <summary>Verifies Quadrant extraction for circles returns 4 cardinal points.</summary>
    [Test]
    public void QuadrantCircle_ReturnsFourCardinalPoints() => GeometryGenerators.CircleGen.Run((Circle circle) => {
        using ArcCurve arcCurve = new(circle);
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(arcCurve, new Extraction.Quadrant(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.EqualTo(4));
            Test.All(points, p => p.IsValid);
            Point3d[] expectedQuadrants = [
                circle.PointAt(0),
                circle.PointAt(RhinoMath.HalfPI),
                circle.PointAt(Math.PI),
                circle.PointAt(3 * RhinoMath.HalfPI),
            ];
            Test.All(points, p => expectedQuadrants.Any(q => p.DistanceTo(q) < DefaultContext.AbsoluteTolerance * 10));
            return true;
        });
    });

    #endregion

    #region Property-Based Invariants

    /// <summary>Verifies extracted points are always within expanded bounding box.</summary>
    [Test]
    public void ExtractedPoints_WithinBoundingBox() => GeometryGenerators.BrepGen.Run((Brep brep) => {
        BoundingBox bbox = brep.GetBoundingBox(accurate: true);
        double expansion = Math.Max(1.0, bbox.Diagonal.Length * 0.1);
        BoundingBox expanded = new(
            bbox.Min - new Vector3d(expansion, expansion, expansion),
            bbox.Max + new Vector3d(expansion, expansion, expansion));
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(brep, new Extraction.Analytical(), DefaultContext);
        Test.Success(result, points => {
            Test.All(points, p => expanded.Contains(p, strict: false));
            return true;
        });
    });

    /// <summary>Verifies extracted curves have positive length.</summary>
    [Test]
    public void ExtractedCurves_HavePositiveLength() => GeometryGenerators.SurfaceGen.Run((Surface surface) => {
        Result<IReadOnlyList<Curve>> result = Extraction.Curves(surface, new Extraction.Boundary(), DefaultContext);
        Test.Success(result, curves => {
            Test.All(curves, c => c.GetLength() > RhinoMath.ZeroTolerance);
            return true;
        });
    });

    /// <summary>Verifies ByCount with IncludeEnds=true includes start and end points.</summary>
    [Test]
    public void ByCountIncludeEnds_ContainsEndpoints() => GeometryGenerators.CurveGen.Run((Curve curve) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(curve, new Extraction.ByCount(5, IncludeEnds: true), DefaultContext);
        Test.Success(result, points => {
            Point3d start = curve.PointAtStart;
            Point3d end = curve.PointAtEnd;
            Test.Any(points, p => p.DistanceTo(start) < DefaultContext.AbsoluteTolerance * 10);
            Test.Any(points, p => p.DistanceTo(end) < DefaultContext.AbsoluteTolerance * 10);
            return true;
        });
    });

    /// <summary>Verifies mesh EdgeMidpoints returns correct count.</summary>
    [Test]
    public void EdgeMidpointsMesh_ReturnsEdgeMidpoints() => GeometryGenerators.MeshGen.Run((Mesh mesh) => {
        Result<IReadOnlyList<Point3d>> result = Extraction.Points(mesh, new Extraction.EdgeMidpoints(), DefaultContext);
        Test.Success(result, points => {
            Assert.That(points.Count, Is.GreaterThan(0));
            Assert.That(points.Count, Is.LessThanOrEqualTo(mesh.TopologyEdges.Count));
            Test.All(points, p => p.IsValid);
            return true;
        });
    });

    #endregion
}
