using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Spatial;

/// <summary>
/// Foundational tests for SpatialEngine using Rhino.Testing fixtures.
/// Demonstrates algebraic patterns: pattern matching, monadic composition, polymorphic dispatch.
/// </summary>
[TestFixture]
public sealed class SpatialEngineTests : RhinoTestFixture {

    private static readonly IGeometryContext Context = GeometryContext.Default;

    /// <summary>Test data generator using pattern matching for different spatial scenarios.</summary>
    private static IEnumerable<TestCaseData> SpatialRangeTestCases() {
        // Generate test point arrays with different distributions
        var uniformGrid = (
            from i in Enumerable.Range(0, 10)
            from j in Enumerable.Range(0, 10)
            select new Point3d(i * 10.0, j * 10.0, 0.0)
        ).ToArray();

        var clusteredPoints = new[] {
            new Point3d(0, 0, 0), new Point3d(1, 1, 0), new Point3d(2, 2, 0),
            new Point3d(100, 100, 0), new Point3d(101, 101, 0), new Point3d(102, 102, 0)
        };

        var singlePoint = new[] { new Point3d(5, 5, 5) };

        // Pattern match to generate test cases with descriptive names
        return new (string Name, Point3d[] Points, Sphere Query, int Expected)[] {
            ("Uniform grid with sphere query", uniformGrid, new Sphere(new Point3d(50, 50, 0), 25), 9),
            ("Clustered points with small sphere", clusteredPoints, new Sphere(new Point3d(1, 1, 0), 5), 3),
            ("Single point exact match", singlePoint, new Sphere(new Point3d(5, 5, 5), 1), 1),
            ("Empty result outside range", uniformGrid, new Sphere(new Point3d(1000, 1000, 0), 10), 0)
        }.Select(test => new TestCaseData(test.Points, test.Query, test.Expected)
            .SetName($"RangeQuery_{test.Name.Replace(" ", "_")}"));
    }

    /// <summary>
    /// Test spatial range queries using algebraic composition.
    /// Validates: Result monad, spatial indexing, polymorphic geometry handling.
    /// </summary>
    [Test, TestCaseSource(nameof(SpatialRangeTestCases))]
    public void SpatialRangeQuery_ReturnsExpectedIndices(Point3d[] points, Sphere queryShape, int expectedCount) {
        // Act - monadic composition with pattern matching
        var result = SpatialEngine.Index(
            input: points,
            method: SpatialMethod.PointsRange,
            context: Context,
            queryShape: queryShape
        );

        // Assert - pattern match on Result monad
        var assertion = result.Match(
            onSuccess: indices => (
                IsSuccess: true,
                Count: indices.Count,
                Message: $"Found {indices.Count} points"
            ),
            onFailure: errors => (
                IsSuccess: false,
                Count: 0,
                Message: string.Join(", ", errors.Select(e => e.Message))
            )
        );

        Assert.That(assertion.IsSuccess, Is.True, assertion.Message);
        Assert.That(assertion.Count, Is.EqualTo(expectedCount));
    }

    /// <summary>Test proximity queries with k-nearest neighbor search.</summary>
    private static IEnumerable<TestCaseData> ProximityTestCases() =>
        new (string Name, Point3d[] Points, Point3d Needle, int K, int Expected)[] {
            ("Find 3 nearest in grid",
                Enumerable.Range(0, 100).Select(i => new Point3d(i, i, 0)).ToArray(),
                new Point3d(50, 50, 0),
                3,
                3),
            ("K larger than point count",
                new[] { new Point3d(0, 0, 0), new Point3d(1, 1, 1) },
                new Point3d(0.5, 0.5, 0.5),
                10,
                2)
        }.Select(test => new TestCaseData(test.Points, test.Needle, test.K, test.Expected)
            .SetName($"Proximity_{test.Name.Replace(" ", "_")}"));

    [Test, TestCaseSource(nameof(ProximityTestCases))]
    public void SpatialProximity_FindsNearestNeighbors(Point3d[] points, Point3d needle, int k, int expectedCount) {
        // Act
        var result = SpatialEngine.Index(
            input: points,
            method: SpatialMethod.PointsProximity,
            context: Context,
            needles: [needle],
            k: k
        );

        // Assert - algebraic pattern matching
        result.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.EqualTo(expectedCount));
                Assert.That(indices.All(i => i >= 0 && i < points.Length), Is.True, "All indices should be valid");
            },
            onFailure: errors => Assert.Fail($"Expected success but got errors: {string.Join(", ", errors.Select(e => e.Message))}")
        );
    }

    /// <summary>Test error paths using pattern matching - no if/else chains.</summary>
    [Test]
    public void SpatialEngine_InvalidParameters_ReturnsError() {
        var points = new[] { new Point3d(0, 0, 0), new Point3d(1, 1, 1) };

        // Pattern match over different error scenarios
        var errorScenarios = new (string Name, Func<Result<IReadOnlyList<int>>> Operation)[] {
            ("Invalid K value (<=0)", () => SpatialEngine.Index(
                points, SpatialMethod.PointsProximity, Context, k: 0, needles: [Point3d.Origin])),
            ("Invalid distance value (<=0)", () => SpatialEngine.Index(
                points, SpatialMethod.PointsProximity, Context, k: 1, limitDistance: -5, needles: [Point3d.Origin]))
        };

        foreach (var scenario in errorScenarios) {
            var result = scenario.Operation();
            result.Match(
                onSuccess: _ => Assert.Fail($"{scenario.Name}: Expected error but got success"),
                onFailure: errors => Assert.That(errors, Is.Not.Empty, $"{scenario.Name} should return errors")
            );
        }
    }

    /// <summary>Test geometry-based spatial queries (polymorphic dispatch).</summary>
    [Test]
    public void SpatialEngine_GeometryInput_HandlesPolymorphically() {
        // Arrange - create test geometry
        var curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
        var points = Enumerable.Range(0, 20).Select(i => new Point3d(i * 5, i * 5, 0)).ToArray();

        // Act - polymorphic dispatch on GeometryBase
        var result = SpatialEngine.Index(
            input: curve,
            method: SpatialMethod.GeometryRange,
            context: Context,
            queryShape: new Sphere(new Point3d(50, 0, 0), 10)
        );

        // Assert - pattern match on result
        result.Match(
            onSuccess: indices => {
                Assert.That(indices, Is.Not.Empty);
                Assert.That(indices.All(i => i >= 0), Is.True, "All indices should be non-negative");
            },
            onFailure: errors => Assert.Fail($"Expected success but got: {string.Join(", ", errors.Select(e => e.Message))}")
        );
    }

    /// <summary>Test collection handling (recursive polymorphic dispatch).</summary>
    [Test]
    public void SpatialEngine_CollectionInput_TraversesMonadically() {
        // Arrange - collection of point arrays
        var collections = new[] {
            new[] { new Point3d(0, 0, 0), new Point3d(1, 1, 1) },
            new[] { new Point3d(10, 10, 10), new Point3d(11, 11, 11) }
        };

        var queryShape = new Sphere(new Point3d(0.5, 0.5, 0.5), 2);

        // Act - polymorphic collection handling with monadic traversal
        var result = SpatialEngine.Index(
            input: collections,
            method: SpatialMethod.PointsRange,
            context: Context,
            queryShape: queryShape
        );

        // Assert
        result.Match(
            onSuccess: indices => Assert.That(indices, Is.Not.Empty),
            onFailure: errors => Assert.Fail($"Collection traversal failed: {string.Join(", ", errors.Select(e => e.Message))}")
        );
    }
}
