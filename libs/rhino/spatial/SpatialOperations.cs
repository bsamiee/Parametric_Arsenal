using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Core spatial algorithms with zero-null guarantees and Result monad integration.</summary>
internal static class SpatialOperations {
    private static readonly ConditionalWeakTable<object, RTree> _treeCache = [];

    /// <summary>Executes range query with RTree search and query shape transformation.</summary>
    [Pure]
    internal static Result<IReadOnlyList<int>> RangeQuery<TSource, TQuery>(
        TSource source,
        TQuery query,
        IGeometryContext context,
        double? toleranceBuffer,
        Func<TSource, RTree>? treeFactory) where TSource : notnull where TQuery : notnull =>
        BuildTree(source, treeFactory)
            .Map(tree => (tree, TransformQuery(query, context.AbsoluteTolerance, toleranceBuffer)))
            .Bind(pair => pair.Item1 switch {
                RTree t when pair.Item2 is Sphere sphere => ResultFactory.Create(value: ExecuteSearch(t, sphere)),
                RTree t when pair.Item2 is BoundingBox box => ResultFactory.Create(value: ExecuteSearch(t, box)),
                _ => ResultFactory.Create(value: (IReadOnlyList<int>)[]),
            });

    /// <summary>Executes proximity query with parameter validation and RhinoCommon SDK integration.</summary>
    [Pure]
    internal static Result<IReadOnlyList<int>> ProximityQuery<TSource>(
        TSource source,
        (Point3d Needle, int K) query,
        IGeometryContext _) where TSource : notnull =>
        query.K switch {
            <= 0 => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidCount),
            _ => source switch {
                Point3d[] pts => ResultFactory.Create(value: (IReadOnlyList<int>)(RTree.Point3dKNeighbors(pts, [query.Needle], query.K)
                    ?.SelectMany<int[], int>(g => [.. g, -1]).ToArray() ?? [])),
                PointCloud cloud => ResultFactory.Create(value: (IReadOnlyList<int>)(RTree.PointCloudKNeighbors(cloud, [query.Needle], query.K)
                    ?.SelectMany<int[], int>(g => [.. g, -1]).ToArray() ?? [])),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.UnsupportedOperation),
            },
        };

    /// <summary>Executes proximity query with distance limit validation and RhinoCommon SDK integration.</summary>
    [Pure]
    internal static Result<IReadOnlyList<int>> ProximityQuery<TSource>(
        TSource source,
        (Point3d Needle, double Distance) query,
        IGeometryContext _) where TSource : notnull =>
        query.Distance switch {
            <= 0 => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidDistance),
            _ => source switch {
                Point3d[] pts => ResultFactory.Create(value: (IReadOnlyList<int>)(RTree.Point3dClosestPoints(pts, [query.Needle], query.Distance)
                    ?.SelectMany<int[], int>(g => [.. g, -1]).ToArray() ?? [])),
                PointCloud cloud => ResultFactory.Create(value: (IReadOnlyList<int>)(RTree.PointCloudClosestPoints(cloud, [query.Needle], query.Distance)
                    ?.SelectMany<int[], int>(g => [.. g, -1]).ToArray() ?? [])),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.UnsupportedOperation),
            },
        };

    /// <summary>Executes mesh overlap detection with ArrayPool buffer management and tolerance handling.</summary>
    [Pure]
    internal static Result<IReadOnlyList<int>> OverlapQuery(
        (Mesh Mesh1, Mesh Mesh2) meshes,
        IGeometryContext context,
        double? toleranceBuffer) {
        int[] buffer = ArrayPool<int>.Shared.Rent(4096);
        try {
            (RTree tree1, RTree tree2, int count) = (
                _treeCache.GetValue(meshes.Mesh1, static m => RTree.CreateMeshFaceTree((Mesh)m)!),
                _treeCache.GetValue(meshes.Mesh2, static m => RTree.CreateMeshFaceTree((Mesh)m)!),
                0);
            _ = RTree.SearchOverlaps(tree1, tree2, context.AbsoluteTolerance + (toleranceBuffer ?? 0),
                (_, args) => count = count + 1 < buffer.Length ? ((buffer[count], buffer[count + 1]) = (args.Id, args.IdB), count += 2).Item2 : count);
            return ResultFactory.Create(value: (IReadOnlyList<int>)(count > 0 ? [.. buffer[..count]] : []));
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Builds or retrieves cached RTree with inline construction for geometry arrays.</summary>
    [Pure]
    private static Result<RTree> BuildTree<TSource>(TSource source, Func<TSource, RTree>? factory) where TSource : notnull =>
        factory is not null ? ResultFactory.Create(value: _treeCache.GetValue(source, _ => factory(source))) :
        source switch {
            Curve[] curves => ResultFactory.Create(value: _treeCache.GetValue(source, _ => {
                RTree t = new();
                _ = curves.Select((c, i) => (t.Insert(c.GetBoundingBox(accurate: true), i), 0).Item2).ToArray();
                return t;
            })),
            Surface[] surfaces => ResultFactory.Create(value: _treeCache.GetValue(source, _ => {
                RTree t = new();
                _ = surfaces.Select((s, i) => (t.Insert(s.GetBoundingBox(accurate: true), i), 0).Item2).ToArray();
                return t;
            })),
            Brep[] breps => ResultFactory.Create(value: _treeCache.GetValue(source, _ => {
                RTree t = new();
                _ = breps.Select((b, i) => (t.Insert(b.GetBoundingBox(accurate: true), i), 0).Item2).ToArray();
                return t;
            })),
            _ => ResultFactory.Create<RTree>(error: SpatialErrors.Parameters.UnsupportedOperation),
        };

    /// <summary>Transforms query shape with tolerance buffer expansion for spatial accuracy.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object TransformQuery<TQuery>(TQuery query, double tolerance, double? buffer) where TQuery : notnull =>
        buffer switch {
            double buf => query switch {
                Sphere { Center: Point3d c, Radius: double r } => new Sphere(c, r + buf),
                BoundingBox { Min: Point3d min, Max: Point3d max } => new BoundingBox(min - new Vector3d(buf, buf, buf), max + new Vector3d(buf, buf, buf)),
                Point3d point => new Sphere(point, tolerance + buf),
                GeometryBase geom => new BoundingBox(
                    geom.GetBoundingBox(accurate: true).Min - new Vector3d(buf, buf, buf),
                    geom.GetBoundingBox(accurate: true).Max + new Vector3d(buf, buf, buf)),
                _ => new Sphere(Point3d.Origin, tolerance),
            },
            null => query switch {
                Sphere sphere => sphere,
                BoundingBox box => box,
                Point3d point => new Sphere(point, tolerance),
                _ => new Sphere(Point3d.Origin, tolerance),
            },
        };

    /// <summary>Executes RTree search with ArrayPool buffer management and zero allocations.</summary>
    [Pure]
    private static IReadOnlyList<int> ExecuteSearch(RTree tree, Sphere sphere) {
        int[] buffer = ArrayPool<int>.Shared.Rent(2048);
        try {
            int count = 0;
            _ = tree.Search(sphere, (_, args) => count = count < buffer.Length ? (buffer[count] = args.Id, count + 1).Item2 : count);
            return count > 0 ? [.. buffer[..count]] : [];
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Executes RTree search with ArrayPool buffer management and zero allocations.</summary>
    [Pure]
    private static IReadOnlyList<int> ExecuteSearch(RTree tree, BoundingBox box) {
        int[] buffer = ArrayPool<int>.Shared.Rent(2048);
        try {
            int count = 0;
            _ = tree.Search(box, (_, args) => count = count < buffer.Length ? (buffer[count] = args.Id, count + 1).Item2 : count);
            return count > 0 ? [.. buffer[..count]] : [];
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }
}
