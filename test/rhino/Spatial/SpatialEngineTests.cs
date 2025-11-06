using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Tests.Spatial;

/// <summary>
/// Foundational tests for SpatialEngine using Rhino.Testing fixtures.
/// Demonstrates algebraic patterns: pattern matching, monadic composition, polymorphic dispatch.
/// </summary>
[TestFixture]
#pragma warning disable CA1515 // Test fixture must be public for NUnit to discover it
public sealed class SpatialEngineTests : RhinoTestFixture {

    private static readonly IGeometryContext Context = GeometryContext.CreateWithDefaults(UnitSystem.Millimeters).Value;

    /// <summary>Test data generator using pattern matching for different spatial scenarios.</summary>
    private static IEnumerable<TestCaseData> SpatialRangeTestCases() {
        // Generate test point arrays with different distributions
        Point3d[] uniformGrid = [.. (
            from i in Enumerable.Range(0, 10)
            from j in Enumerable.Range(0, 10)
            select new Point3d(i * 10.0, j * 10.0, 0.0)
        ),
        ];

        Point3d[] clusteredPoints = [
            new Point3d(0, 0, 0),
            new Point3d(1, 1, 0),
            new Point3d(2, 2, 0),
            new Point3d(100, 100, 0),
            new Point3d(101, 101, 0),
            new Point3d(102, 102, 0),
        ];

        Point3d[] singlePoint = [new Point3d(5, 5, 5)];

        // Pattern match to generate test cases with descriptive names
        return new (string Name, Point3d[] Points, Sphere Query, int Expected)[] {
            ("Uniform grid with sphere query", uniformGrid, new Sphere(new Point3d(50, 50, 0), 25), 9),
            ("Clustered points with small sphere", clusteredPoints, new Sphere(new Point3d(1, 1, 0), 5), 3),
            ("Single point exact match", singlePoint, new Sphere(new Point3d(5, 5, 5), 1), 1),
            ("Empty result outside range", uniformGrid, new Sphere(new Point3d(1000, 1000, 0), 10), 0),
        }.Select(test => new TestCaseData(test.Points, test.Query, test.Expected)
            .SetName($"RangeQuery_{test.Name.Replace(' ', '_')}"));
    }

    /// <summary>
    /// Test spatial range queries using algebraic composition.
    /// Validates: Result monad, spatial indexing, polymorphic geometry handling.
    /// </summary>
    [Test, TestCaseSource(nameof(SpatialRangeTestCases))]
    public void SpatialRangeQueryReturnsExpectedIndices(Point3d[] points, Sphere queryShape, int expectedCount) {
        // Act - monadic composition with pattern matching
        Result<IReadOnlyList<int>> result = SpatialAnalyzer.Analyze(
            source: points,
            query: queryShape,
            context: Context
        );

        // Assert - pattern match on Result monad
        (bool IsSuccess, int Count, string Message) = result.Match(
            onSuccess: indices => (
                IsSuccess: true, indices.Count,
                Message: $"Found {indices.Count} points"
            ),
            onFailure: errors => (
                IsSuccess: false,
                Count: 0,
                Message: string.Join(", ", errors.Select(e => e.Message))
            )
        );

        Assert.That(IsSuccess, Is.True, Message);
        Assert.That(Count, Is.EqualTo(expectedCount));
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
                2),
        }.Select(test => new TestCaseData(test.Points, test.Needle, test.K, test.Expected)
            .SetName($"Proximity_{test.Name.Replace(' ', '_')}"));

    [Test, TestCaseSource(nameof(ProximityTestCases))]
    public void SpatialProximityFindsNearestNeighbors(Point3d[] points, Point3d needle, int k, int expectedCount) {
        // Act
        Result<IReadOnlyList<int>> result = SpatialAnalyzer.Analyze(
            source: points,
            query: (needle, k),
            context: Context
        );

        // Assert - algebraic pattern matching
        _ = result.Match(
            onSuccess: indices => {
                Assert.That(indices.Count, Is.EqualTo(expectedCount));
                Assert.That(indices.All(i => i >= 0 && i < points.Length), Is.True, "All indices should be valid");
                return 0;
            },
            onFailure: errors => {
                Assert.Fail($"Expected success but got errors: {string.Join(", ", errors.Select(e => e.Message))}");
                return 0;
            }
        );
    }

    /// <summary>Test error paths using pattern matching - no if/else chains.</summary>
    [Test]
    public void SpatialEngineInvalidParametersReturnsError() {
        Point3d[] points = [new Point3d(0, 0, 0), new Point3d(1, 1, 1)];

        // Pattern match over different error scenarios
        (string Name, Func<Result<IReadOnlyList<int>>> Operation)[] errorScenarios = [
            ("Invalid K value (<=0)", () => SpatialAnalyzer.Analyze(
                points, (Point3d.Origin, 0), Context)),
            ("Invalid distance value (<=0)", () => SpatialAnalyzer.Analyze(
                points, (Point3d.Origin, -5.0), Context)),
        ];

        foreach ((string Name, Func<Result<IReadOnlyList<int>>> Operation) in errorScenarios) {
            Result<IReadOnlyList<int>> result = Operation();
            _ = result.Match(
                onSuccess: _ => {
                    Assert.Fail($"{Name}: Expected error but got success");
                    return 0;
                },
                onFailure: errors => {
                    Assert.That(errors, Is.Not.Empty, $"{Name} should return errors");
                    return 0;
                }
            );
        }
    }

    /// <summary>Test geometry-based spatial queries (polymorphic dispatch).</summary>
    [Test]
    public void SpatialEngineGeometryInputHandlesPolymorphically() {
        // Arrange - create test curves array
        Curve[] curves = [.. Enumerable.Range(0, 20).Select(i => (Curve)new LineCurve(new Point3d(i * 5, i * 5, 0), new Point3d((i * 5) + 10, i * 5, 0)))];

        // Act - polymorphic dispatch on Curve[]
        Result<IReadOnlyList<int>> result = SpatialAnalyzer.Analyze(
            source: curves,
            query: new Sphere(new Point3d(50, 50, 0), 15),
            context: Context
        );

        // Assert - pattern match on result
        _ = result.Match(
            onSuccess: indices => {
                Assert.That(indices, Is.Not.Empty);
                Assert.That(indices.All(i => i >= 0), Is.True, "All indices should be non-negative");
                return 0;
            },
            onFailure: errors => {
                Assert.Fail($"Expected success but got: {string.Join(", ", errors.Select(e => e.Message))}");
                return 0;
            }
        );
    }

    /// <summary>Test collection handling (recursive polymorphic dispatch).</summary>
    [Test]
    public void SpatialEngineCollectionInputTraversesMonadically() {
        // Arrange - collection of point arrays
        Point3d[][] collections = [
            [new Point3d(0, 0, 0), new Point3d(1, 1, 1)],
            [new Point3d(10, 10, 10), new Point3d(11, 11, 11)],
        ];

        Sphere queryShape = new(new Point3d(0.5, 0.5, 0.5), 2);

        // Act - polymorphic collection handling with monadic traversal
        Result<IReadOnlyList<int>> result = SpatialAnalyzer.Analyze(
            source: collections,
            query: queryShape,
            context: Context
        );

        // Assert
        _ = result.Match(
            onSuccess: indices => {
                Assert.That(indices, Is.Not.Empty);
                return 0;
            },
            onFailure: errors => {
                Assert.Fail($"Collection traversal failed: {string.Join(", ", errors.Select(e => e.Message))}");
                return 0;
            }
        );
    }
}
