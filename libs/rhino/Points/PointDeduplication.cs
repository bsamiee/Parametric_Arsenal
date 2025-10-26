using System.Collections.Generic;
using Arsenal.Core;
using Arsenal.Rhino.Document;
using Rhino.Geometry;

namespace Arsenal.Rhino.Points;

/// <summary>Point deduplication operations using spatial indexing.</summary>
public static class PointDeduplication
{
    /// <summary>Removes duplicate points within tolerance. Preserves first occurrence order.</summary>
    public static Result<Point3d[]> Remove(IEnumerable<Point3d>? points, double tolerance)
    {
        if (points is null)
        {
            return Result<Point3d[]>.Fail($"{nameof(points)} cannot be null");
        }

        if (tolerance < 0)
        {
            return Result<Point3d[]>.Fail($"{nameof(tolerance)} must be non-negative, but was {tolerance}");
        }

        List<Point3d> uniquePoints = [];

        using (RTree rTree = new())
        {
            foreach (Point3d point in points)
            {
                Sphere searchSphere = new(point, tolerance);

                bool isDuplicate = false;
                rTree.Search(searchSphere, (_, args) =>
                {
                    Point3d existingPoint = uniquePoints[args.Id];
                    if (point.DistanceTo(existingPoint) <= tolerance)
                    {
                        isDuplicate = true;
                    }
                });

                if (!isDuplicate)
                {
                    int index = uniquePoints.Count;
                    uniquePoints.Add(point);
                    rTree.Insert(point, index);
                }
            }
        }

        return Result<Point3d[]>.Success([.. uniquePoints]);
    }

    /// <summary>Removes duplicate points using document tolerance.</summary>
    public static Result<Point3d[]> RemoveWithDocTolerance(IEnumerable<Point3d>? points)
    {
        return Remove(points, Tolerances.Abs());
    }

    /// <summary>Finds K nearest neighbors for each query point.</summary>
    public static Result<int[][]> FindKNearestNeighbors(IEnumerable<Point3d>? searchPoints,
        IEnumerable<Point3d>? queryPoints, int k)
    {
        if (searchPoints is null)
        {
            return Result<int[][]>.Fail($"{nameof(searchPoints)} cannot be null");
        }

        if (queryPoints is null)
        {
            return Result<int[][]>.Fail($"{nameof(queryPoints)} cannot be null");
        }

        if (k <= 0)
        {
            return Result<int[][]>.Fail($"{nameof(k)} must be positive, but was {k}");
        }

        List<Point3d> searchList = [.. searchPoints];
        List<Point3d> queryList = [.. queryPoints];

        if (searchList.Count == 0)
        {
            return Result<int[][]>.Fail("Search points collection is empty");
        }

        if (queryList.Count == 0)
        {
            return Result<int[][]>.Fail("Query points collection is empty");
        }

        if (k > searchList.Count)
        {
            return Result<int[][]>.Fail(
                $"K ({k}) cannot be larger than the number of search points ({searchList.Count})");
        }

        try
        {
            IEnumerable<int[]> results = RTree.Point3dKNeighbors(searchList, queryList, k);
            int[][] resultArray = [.. results];
            return Result<int[][]>.Success(resultArray);
        }
        catch (System.Exception ex)
        {
            return Result<int[][]>.Fail($"K-nearest neighbors search failed: {ex.Message}");
        }
    }
}
