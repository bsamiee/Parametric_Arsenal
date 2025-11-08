using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Internal spatial computation algorithms with RTree-backed operations and type-based dispatch.</summary>
internal static class SpatialCore {
    /// <summary>Unified configuration mapping input/query type pairs to tree factory, validation mode, buffer size, and execution strategy.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new Dictionary<(Type, Type), (Func<object, RTree>?, V, int, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>>)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(), V.None, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Point3d[])i, s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree()).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(), V.None, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Point3d[])i, s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree()).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(), V.None, SpatialConfig.DefaultBufferSize,
                (i, q, _, _) => q is ValueTuple<Point3d[], int> t ? ExecuteProximitySearch((Point3d[])i, t.Item1, t.Item2, RTree.Point3dKNeighbors, RTree.Point3dClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(), V.None, SpatialConfig.DefaultBufferSize,
                (i, q, _, _) => q is ValueTuple<Point3d[], double> t ? ExecuteProximitySearch((Point3d[])i, t.Item1, t.Item2, RTree.Point3dKNeighbors, RTree.Point3dClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(PointCloud), typeof(Sphere))] = (s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(), V.Degeneracy, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((PointCloud)i, s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree()).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(PointCloud), typeof(BoundingBox))] = (s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(), V.Degeneracy, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((PointCloud)i, s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree()).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(), V.Degeneracy, SpatialConfig.DefaultBufferSize,
                (i, q, _, _) => q is ValueTuple<Point3d[], int> t ? ExecuteProximitySearch((PointCloud)i, t.Item1, t.Item2, RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(), V.Degeneracy, SpatialConfig.DefaultBufferSize,
                (i, q, _, _) => q is ValueTuple<Point3d[], double> t ? ExecuteProximitySearch((PointCloud)i, t.Item1, t.Item2, RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(Mesh), typeof(Sphere))] = (s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(), V.MeshSpecific, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Mesh)i, s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree()).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Mesh), typeof(BoundingBox))] = (s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(), V.MeshSpecific, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Mesh)i, s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree()).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = (null, V.MeshSpecific, SpatialConfig.LargeBufferSize,
                (i, q, c, b) => i is ValueTuple<Mesh, Mesh> m && q is double tolerance ? GetTree(m.Item1, s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree()).Bind(t1 => GetTree(m.Item2, s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree()).Bind(t2 => ExecuteOverlapSearch(t1, t2, c.AbsoluteTolerance + tolerance, b))) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(Curve[]), typeof(Sphere))] = (s => BuildGeometryArrayTree((Curve[])s), V.Degeneracy, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Curve[])i, s => BuildGeometryArrayTree((Curve[])s)).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Curve[]), typeof(BoundingBox))] = (s => BuildGeometryArrayTree((Curve[])s), V.Degeneracy, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Curve[])i, s => BuildGeometryArrayTree((Curve[])s)).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Surface[]), typeof(Sphere))] = (s => BuildGeometryArrayTree((Surface[])s), V.BoundingBox, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Surface[])i, s => BuildGeometryArrayTree((Surface[])s)).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Surface[]), typeof(BoundingBox))] = (s => BuildGeometryArrayTree((Surface[])s), V.BoundingBox, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Surface[])i, s => BuildGeometryArrayTree((Surface[])s)).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Brep[]), typeof(Sphere))] = (s => BuildGeometryArrayTree((Brep[])s), V.Topology, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Brep[])i, s => BuildGeometryArrayTree((Brep[])s)).Bind(tree => ExecuteRangeSearch(tree, q, b))),
            [(typeof(Brep[]), typeof(BoundingBox))] = (s => BuildGeometryArrayTree((Brep[])s), V.Topology, SpatialConfig.DefaultBufferSize,
                (i, q, _, b) => GetTree((Brep[])i, s => BuildGeometryArrayTree((Brep[])s)).Bind(tree => ExecuteRangeSearch(tree, q, b))),
        }.ToFrozenDictionary();

    /// <summary>Executes RTree range search with sphere or bounding box query using ArrayPool for zero-allocation results.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, object queryShape, int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            Action search = queryShape switch {
                Sphere sphere => () => tree.Search(sphere, (_, args) => { if (count < buffer.Length) { buffer[count++] = args.Id; } }),
                BoundingBox box => () => tree.Search(box, (_, args) => { if (count < buffer.Length) { buffer[count++] = args.Id; } }),
                _ => () => { }
                ,
            };
            search();
            return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Executes k-nearest or distance-limited proximity search using RTree algorithms.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteProximitySearch<T>(T source, Point3d[] needles, object limit, Func<T, Point3d[], int, IEnumerable<int[]>> kNearest, Func<T, Point3d[], double, IEnumerable<int[]>> distLimited) where T : notnull =>
        limit switch {
            int k when k > 0 => kNearest(source, needles, k).ToArray() switch {
                int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            },
            double d when d > 0 => distLimited(source, needles, d).ToArray() switch {
                int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            },
            int => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK),
            double => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

    /// <summary>Executes mesh overlap detection using RTree.SearchOverlaps with tolerance-aware double-tree algorithm.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteOverlapSearch(
        RTree tree1,
        RTree tree2,
        double tolerance,
        int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            bool success = RTree.SearchOverlaps(tree1, tree2, tolerance, (_, args) => {
                if (count + 1 < buffer.Length) {
                    buffer[count++] = args.Id;
                    buffer[count++] = args.IdB;
                }
            });
            return success switch {
                true => ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []),
                false => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            };
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Retrieves or constructs RTree for geometry with automatic caching using ConditionalWeakTable.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<RTree> GetTree<T>(T source, Func<object, RTree> factory) where T : notnull =>
        ResultFactory.Create(value: Spatial.TreeCache.GetValue(key: source, createValueCallback: _ => factory(source!)));

    /// <summary>Constructs RTree from geometry array by inserting bounding boxes with index tracking.</summary>
    [Pure]
    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }
}
