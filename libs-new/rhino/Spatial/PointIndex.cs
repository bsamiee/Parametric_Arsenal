using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>
/// Spatial indexing helpers built on RhinoCommon <see cref="RTree"/> for point deduplication and nearest searches.
/// </summary>
public static class PointIndex
{
    /// <summary>Removes duplicate points within the specified tolerance using spatial indexing.</summary>
    /// <param name="points">The points to deduplicate.</param>
    /// <param name="tolerance">The tolerance for considering points as duplicates.</param>
    /// <returns>A result containing the deduplicated points or a failure.</returns>
    public static Result<IReadOnlyList<Point3d>> Deduplicate(IEnumerable<Point3d>? points, double tolerance)
    {
        Result<IReadOnlyCollection<Point3d>> collectionResult = Guard.AgainstEmpty(points, nameof(points));
        if (!collectionResult.IsSuccess)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(collectionResult.Failure!);
        }

        if (!double.IsFinite(tolerance) || tolerance < 0)
        {
            return Result<IReadOnlyList<Point3d>>.Fail(new Failure("tolerance.invalid", $"Tolerance must be non-negative finite, received {tolerance}."));
        }

        IReadOnlyCollection<Point3d> collection = collectionResult.Value!;
        if (collection.Count == 0)
        {
            return Result<IReadOnlyList<Point3d>>.Success(Array.Empty<Point3d>());
        }

        List<Point3d> unique = [];
        using RTree index = new();

        int insertIndex = 0;
        foreach (Point3d point in collection)
        {
            if (!point.IsValid)
            {
                continue;
            }

            bool duplicateFound = false;
            Sphere sphere = new(point, tolerance);

            index.Search(sphere, (_, args) =>
            {
                Point3d existing = unique[args.Id];
                if (existing.DistanceTo(point) <= tolerance)
                {
                    duplicateFound = true;
                }
            });

            if (!duplicateFound)
            {
                unique.Add(point);
                index.Insert(point, insertIndex);
                insertIndex++;
            }
        }

        return Result<IReadOnlyList<Point3d>>.Success(unique);
    }

    /// <summary>Finds the k nearest neighbors for each query point using spatial indexing.</summary>
    /// <param name="searchPoints">The points to search within.</param>
    /// <param name="queryPoints">The points to find neighbors for.</param>
    /// <param name="k">The number of nearest neighbors to find.</param>
    /// <returns>A result containing arrays of neighbor indices for each query point, or a failure.</returns>
    public static Result<int[][]> NearestNeighbors(IEnumerable<Point3d>? searchPoints, IEnumerable<Point3d>? queryPoints, int k)
    {
        Result<IReadOnlyCollection<Point3d>> search = Guard.AgainstEmpty(searchPoints, nameof(searchPoints));
        if (!search.IsSuccess)
        {
            return Result<int[][]>.Fail(search.Failure!);
        }

        Result<IReadOnlyCollection<Point3d>> query = Guard.AgainstEmpty(queryPoints, nameof(queryPoints));
        if (!query.IsSuccess)
        {
            return Result<int[][]>.Fail(query.Failure!);
        }

        if (k <= 0)
        {
            return Result<int[][]>.Fail(new Failure("spatial.knn.k", "K must be positive."));
        }

        if (k > search.Value.Count)
        {
            return Result<int[][]>.Fail(new Failure("spatial.knn.k", "K cannot exceed the number of search points."));
        }

        try
        {
            IEnumerable<int[]> indices = RTree.Point3dKNeighbors(search.Value!, query.Value!, k);
            return Result<int[][]>.Success(indices.Select(array => array.ToArray()).ToArray());
        }
        catch (Exception ex)
        {
            return Result<int[][]>.Fail(Failure.From(ex));
        }
    }
}
