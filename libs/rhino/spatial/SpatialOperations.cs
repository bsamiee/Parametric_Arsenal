using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Core spatial algorithm implementations with RhinoCommon RTree SDK and tolerance-aware geometry processing.</summary>
internal static class SpatialOperations {
    private static readonly ConditionalWeakTable<object, RTree> _treeCache = [];

    /// <summary>Executes range queries using RTree search with sphere or bounding box dispatch.</summary>
    [Pure]
    internal static int[]? Range<TSource, TQuery>(
        TSource source,
        TQuery query,
        IGeometryContext context,
        Func<object, RTree?>? treeFactory,
        double? toleranceBuffer) where TSource : notnull where TQuery : notnull {
        RTree? tree = treeFactory is not null ? _treeCache.GetValue(source, _ => treeFactory(source)!) :
            source switch {
                Curve[] curves => _treeCache.GetValue(source, _ => { RTree t = new(); _ = curves.Select((c, i) => (t.Insert(c.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }),
                Surface[] surfaces => _treeCache.GetValue(source, _ => { RTree t = new(); _ = surfaces.Select((s, i) => (t.Insert(s.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }),
                Brep[] breps => _treeCache.GetValue(source, _ => { RTree t = new(); _ = breps.Select((b, i) => (t.Insert(b.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }),
                _ => null,
            };

        return (tree, TransformQuery(query, context.AbsoluteTolerance, toleranceBuffer)) switch {
            (null, _) => [],
            (RTree t, Sphere sphere) => ExecuteSearch(t, sphere),
            (RTree t, BoundingBox box) => ExecuteSearch(t, box),
            _ => [],
        };
    }

    /// <summary>Executes k-nearest neighbor proximity queries using RhinoCommon algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int[]? ProximityK(Point3d[] points, Point3d needle, int k) =>
        RTree.Point3dKNeighbors(points, [needle], k)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray();

    /// <summary>Executes k-nearest neighbor proximity queries for PointCloud using RhinoCommon algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int[]? ProximityK(PointCloud cloud, Point3d needle, int k) =>
        RTree.PointCloudKNeighbors(cloud, [needle], k)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray();

    /// <summary>Executes distance-limited proximity queries using RhinoCommon algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int[]? ProximityDistance(Point3d[] points, Point3d needle, double distance) =>
        RTree.Point3dClosestPoints(points, [needle], distance)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray();

    /// <summary>Executes distance-limited proximity queries for PointCloud using RhinoCommon algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int[]? ProximityDistance(PointCloud cloud, Point3d needle, double distance) =>
        RTree.PointCloudClosestPoints(cloud, [needle], distance)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray();

    /// <summary>Executes mesh overlap detection using RTree.SearchOverlaps with tolerance-aware face tree construction.</summary>
    [Pure]
    internal static int[]? Overlap(Mesh mesh1, Mesh mesh2, double tolerance) {
        int[] buffer = ArrayPool<int>.Shared.Rent(4096);
        try {
            (RTree tree1, RTree tree2, int count) = (
                _treeCache.GetValue(mesh1, static m => RTree.CreateMeshFaceTree((Mesh)m)!),
                _treeCache.GetValue(mesh2, static m => RTree.CreateMeshFaceTree((Mesh)m)!),
                0);
            _ = RTree.SearchOverlaps(tree1, tree2, tolerance,
                (_, args) => count = count + 1 < buffer.Length ? ((buffer[count], buffer[count + 1]) = (args.Id, args.IdB), count += 2).Item2 : count);
            return count > 0 ? [.. buffer[..count]] : [];
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

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

    /// <summary>Executes RTree search with sphere query and ArrayPool buffer management.</summary>
    [Pure]
    private static int[] ExecuteSearch(RTree tree, Sphere sphere) {
        int[] buffer = ArrayPool<int>.Shared.Rent(2048);
        try {
            int count = 0;
            _ = tree.Search(sphere, (_, args) => count = count < buffer.Length ? (buffer[count] = args.Id, count + 1).Item2 : count);
            return count > 0 ? [.. buffer[..count]] : [];
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Executes RTree search with bounding box query and ArrayPool buffer management.</summary>
    [Pure]
    private static int[] ExecuteSearch(RTree tree, BoundingBox box) {
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
