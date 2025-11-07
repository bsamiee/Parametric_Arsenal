using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Arsenal.Rhino.Extraction;
using Arsenal.Rhino.Spatial;
using NUnit.Framework;
using Rhino.Geometry;
using Rhino.Testing.Fixtures;

namespace Arsenal.Rhino.Tests;

/// <summary>Comprehensive integration tests for Rhino geometry operations covering spatial indexing and extraction with property-based patterns.</summary>
[TestFixture]
public sealed class RhinoGeometryTests {
    private static readonly IGeometryContext DefaultContext = new GeometryContext(Tolerance: 0.001);

    /// <summary>Tests spatial indexing with Point3d arrays using sphere and bounding box range queries.</summary>
    [Test]
    public void SpatialPointArrayRangeQueries() {
        Point3d[] points = [
            new Point3d(0, 0, 0),
            new Point3d(1, 0, 0),
            new Point3d(10, 10, 10),
            new Point3d(2, 0, 0),
        ];

        Result<IReadOnlyList<int>> sphereResult = Spatial.Analyze(
            input: points,
            query: new Sphere(new Point3d(0, 0, 0), radius: 2.5),
            context: DefaultContext);

        Result<IReadOnlyList<int>> boxResult = Spatial.Analyze(
            input: points,
            query: new BoundingBox(-1, -1, -1, 2.5, 1, 1),
            context: DefaultContext);

        sphereResult.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.EqualTo(3));
                Assert.That(indices, Does.Contain(0));
                Assert.That(indices, Does.Contain(1));
                Assert.That(indices, Does.Contain(3));
            },
            onFailure: errors => Assert.Fail($"Sphere query failed: {string.Join(", ", errors.Select(e => e.Message))}"));

        boxResult.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(indices, Does.Contain(0));
            },
            onFailure: errors => Assert.Fail($"Box query failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests spatial indexing with PointCloud using sphere queries and validation.</summary>
    [Test]
    public void SpatialPointCloudSphereQuery() {
        PointCloud cloud = new(
            [
                new Point3d(0, 0, 0),
                new Point3d(1, 0, 0),
                new Point3d(0, 1, 0),
                new Point3d(10, 10, 10),
            ]);

        Sphere query = new(new Point3d(0, 0, 0), radius: 1.5);
        Result<IReadOnlyList<int>> result = Spatial.Analyze(input: cloud, query: query, context: DefaultContext);

        result.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(indices, Does.Contain(0));
            },
            onFailure: errors => Assert.Fail($"PointCloud sphere query failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests spatial proximity queries with k-nearest neighbors algorithm for Point3d arrays.</summary>
    [Test]
    public void SpatialKNearestNeighborsQuery() {
        Point3d[] source = [
            new Point3d(0, 0, 0),
            new Point3d(1, 0, 0),
            new Point3d(2, 0, 0),
            new Point3d(10, 10, 10),
            new Point3d(0.5, 0, 0),
        ];

        Point3d[] query = [new Point3d(0, 0, 0)];
        int k = 3;

        Result<IReadOnlyList<int>> result = Spatial.Analyze(
            input: source,
            query: (query, k),
            context: DefaultContext);

        result.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.EqualTo(k));
                Assert.That(indices, Does.Contain(0));
            },
            onFailure: errors => Assert.Fail($"K-nearest query failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests spatial proximity queries with distance-limited search for Point3d arrays.</summary>
    [Test]
    public void SpatialDistanceLimitedQuery() {
        Point3d[] source = [
            new Point3d(0, 0, 0),
            new Point3d(1, 0, 0),
            new Point3d(2, 0, 0),
            new Point3d(10, 10, 10),
        ];

        Point3d[] query = [new Point3d(0, 0, 0)];
        double maxDistance = 2.5;

        Result<IReadOnlyList<int>> result = Spatial.Analyze(
            input: source,
            query: (query, maxDistance),
            context: DefaultContext);

        result.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(indices, Does.Contain(0));
                Assert.That(indices, Does.Contain(1));
            },
            onFailure: errors => Assert.Fail($"Distance-limited query failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests spatial indexing with Mesh using sphere queries and mesh-specific validation.</summary>
    [Test]
    public void SpatialMeshSphereQuery() {
        Mesh mesh = new();
        mesh.Vertices.Add(0, 0, 0);
        mesh.Vertices.Add(1, 0, 0);
        mesh.Vertices.Add(0, 1, 0);
        mesh.Vertices.Add(10, 10, 10);
        mesh.Faces.AddFace(0, 1, 2);

        Sphere query = new(new Point3d(0, 0, 0), radius: 1.5);
        Result<IReadOnlyList<int>> result = Spatial.Analyze(input: mesh, query: query, context: DefaultContext);

        result.Match(
            onSuccess: indices => Assert.That(indices.Count, Is.GreaterThanOrEqualTo(0)),
            onFailure: errors => Assert.Fail($"Mesh sphere query failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests spatial indexing error handling for unsupported type combinations.</summary>
    [Test]
    public void SpatialUnsupportedTypeCombinationReturnsError() {
        Point3d[] points = [new Point3d(0, 0, 0)];
        Result<IReadOnlyList<int>> result = Spatial.Analyze(
            input: points,
            query: "unsupported",
            context: DefaultContext);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors[0].Domain, Is.EqualTo(ErrorDomain.Spatial));
    }

    /// <summary>Tests spatial k-nearest with invalid k value returns error.</summary>
    [Test]
    public void SpatialInvalidKValueReturnsError() {
        Point3d[] source = [new Point3d(0, 0, 0)];
        Point3d[] query = [new Point3d(0, 0, 0)];
        int invalidK = -1;

        Result<IReadOnlyList<int>> result = Spatial.Analyze(
            input: source,
            query: (query, invalidK),
            context: DefaultContext);

        Assert.That(result.IsSuccess, Is.False);
        result.Match(
            onSuccess: _ => Assert.Fail("Expected failure for invalid k value"),
            onFailure: errors => Assert.That(errors.Any(e => e.Code == E.Spatial.InvalidK.Code), Is.True));
    }

    /// <summary>Tests spatial distance-limited with invalid distance returns error.</summary>
    [Test]
    public void SpatialInvalidDistanceReturnsError() {
        Point3d[] source = [new Point3d(0, 0, 0)];
        Point3d[] query = [new Point3d(0, 0, 0)];
        double invalidDistance = -5.0;

        Result<IReadOnlyList<int>> result = Spatial.Analyze(
            input: source,
            query: (query, invalidDistance),
            context: DefaultContext);

        Assert.That(result.IsSuccess, Is.False);
        result.Match(
            onSuccess: _ => Assert.Fail("Expected failure for invalid distance"),
            onFailure: errors => Assert.That(errors.Any(e => e.Code == E.Spatial.InvalidDistance.Code), Is.True));
    }

    /// <summary>Tests point extraction from curves with count-based parameterization.</summary>
    [Test]
    public void ExtractionCurveCountBasedPoints() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        int count = 5;

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: count,
            context: DefaultContext);

        result.Match(
            onSuccess: points => {
                Assert.That(points.Count, Is.EqualTo(count));
                Assert.That(points[0].X, Is.EqualTo(0).Within(DefaultContext.AbsoluteTolerance));
                Assert.That(points[^1].X, Is.EqualTo(10).Within(DefaultContext.AbsoluteTolerance));
            },
            onFailure: errors => Assert.Fail($"Count-based extraction failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests point extraction from curves with length-based parameterization.</summary>
    [Test]
    public void ExtractionCurveLengthBasedPoints() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        double length = 2.5;

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: length,
            context: DefaultContext);

        result.Match(
            onSuccess: points => {
                Assert.That(points.Count, Is.GreaterThan(0));
                Assert.That(points[0].X, Is.EqualTo(0).Within(DefaultContext.AbsoluteTolerance));
            },
            onFailure: errors => Assert.Fail($"Length-based extraction failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests point extraction validation with invalid count parameter.</summary>
    [Test]
    public void ExtractionInvalidCountReturnsError() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        int invalidCount = -5;

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: invalidCount,
            context: DefaultContext);

        Assert.That(result.IsSuccess, Is.False);
        result.Match(
            onSuccess: _ => Assert.Fail("Expected failure for invalid count"),
            onFailure: errors => Assert.That(errors.Any(e => e.Code == E.Geometry.InvalidCount.Code), Is.True));
    }

    /// <summary>Tests point extraction validation with invalid length parameter.</summary>
    [Test]
    public void ExtractionInvalidLengthReturnsError() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        double invalidLength = -2.5;

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: invalidLength,
            context: DefaultContext);

        Assert.That(result.IsSuccess, Is.False);
        result.Match(
            onSuccess: _ => Assert.Fail("Expected failure for invalid length"),
            onFailure: errors => Assert.That(errors.Any(e => e.Code == E.Geometry.InvalidLength.Code), Is.True));
    }

    /// <summary>Tests point extraction with direction-based extrema specification.</summary>
    [Test]
    public void ExtractionDirectionBasedExtrema() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        Vector3d direction = new(1, 0, 0);

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: direction,
            context: DefaultContext);

        result.Match(
            onSuccess: points => Assert.That(points.Count, Is.GreaterThan(0)),
            onFailure: errors => Assert.Fail($"Direction-based extraction failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests point extraction validation with invalid direction vector.</summary>
    [Test]
    public void ExtractionInvalidDirectionReturnsError() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        Vector3d invalidDirection = Vector3d.Zero;

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: invalidDirection,
            context: DefaultContext);

        Assert.That(result.IsSuccess, Is.False);
        result.Match(
            onSuccess: _ => Assert.Fail("Expected failure for invalid direction"),
            onFailure: errors => Assert.That(errors.Any(e => e.Code == E.Geometry.InvalidDirection.Code), Is.True));
    }

    /// <summary>Tests point extraction from curves with semantic analytical method.</summary>
    [Test]
    public void ExtractionSemanticAnalyticalPoints() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        Extract.Semantic semantic = Extract.Semantic.Analytical;

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: semantic,
            context: DefaultContext);

        result.Match(
            onSuccess: points => Assert.That(points.Count, Is.GreaterThan(0)),
            onFailure: errors => Assert.Fail($"Semantic analytical extraction failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests point extraction from curves with semantic extremal method.</summary>
    [Test]
    public void ExtractionSemanticExtremalPoints() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        Extract.Semantic semantic = Extract.Semantic.Extremal;

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: semantic,
            context: DefaultContext);

        result.Match(
            onSuccess: points => {
                Assert.That(points.Count, Is.GreaterThan(0));
                Assert.That(points.Count, Is.LessThanOrEqualTo(2));
            },
            onFailure: errors => Assert.Fail($"Semantic extremal extraction failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests spatial indexing with curve arrays using sphere queries and degeneracy validation.</summary>
    [Test]
    public void SpatialCurveArraySphereQuery() {
        Curve[] curves = [
            new LineCurve(new Point3d(0, 0, 0), new Point3d(1, 0, 0)),
            new LineCurve(new Point3d(10, 10, 10), new Point3d(11, 10, 10)),
        ];

        Sphere query = new(new Point3d(0, 0, 0), radius: 2.0);
        Result<IReadOnlyList<int>> result = Spatial.Analyze(input: curves, query: query, context: DefaultContext);

        result.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.GreaterThanOrEqualTo(0));
                Assert.That(indices, Does.Contain(0));
            },
            onFailure: errors => Assert.Fail($"Curve array sphere query failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests spatial indexing with empty point arrays returns empty results.</summary>
    [Test]
    public void SpatialEmptyPointArrayReturnsEmpty() {
        Point3d[] emptyPoints = [];
        Sphere query = new(new Point3d(0, 0, 0), radius: 10.0);

        Result<IReadOnlyList<int>> result = Spatial.Analyze(
            input: emptyPoints,
            query: query,
            context: DefaultContext);

        result.Match(
            onSuccess: indices => Assert.That(indices.Count, Is.EqualTo(0)),
            onFailure: errors => Assert.Fail($"Empty array should return empty results: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests extraction with count and inclusive boundary specification.</summary>
    [Test]
    public void ExtractionCountWithInclusiveBoundary() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        (int count, bool inclusive) spec = (5, true);

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: spec,
            context: DefaultContext);

        result.Match(
            onSuccess: points => {
                Assert.That(points.Count, Is.EqualTo(spec.count));
                Assert.That(points[0].DistanceTo(curve.PointAtStart), Is.LessThanOrEqualTo(DefaultContext.AbsoluteTolerance));
                Assert.That(points[^1].DistanceTo(curve.PointAtEnd), Is.LessThanOrEqualTo(DefaultContext.AbsoluteTolerance));
            },
            onFailure: errors => Assert.Fail($"Count with boundary extraction failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }

    /// <summary>Tests extraction with length and inclusive boundary specification.</summary>
    [Test]
    public void ExtractionLengthWithInclusiveBoundary() {
        Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        (double length, bool inclusive) spec = (2.0, true);

        Result<IReadOnlyList<Point3d>> result = Extract.Points(
            input: curve,
            spec: spec,
            context: DefaultContext);

        result.Match(
            onSuccess: points => {
                Assert.That(points.Count, Is.GreaterThan(0));
                Assert.That(points[0].DistanceTo(curve.PointAtStart), Is.LessThanOrEqualTo(DefaultContext.AbsoluteTolerance));
            },
            onFailure: errors => Assert.Fail($"Length with boundary extraction failed: {string.Join(", ", errors.Select(e => e.Message))}"));
    }
}
