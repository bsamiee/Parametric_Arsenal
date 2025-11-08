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
    /// <summary>RTree factory configuration mapping source types to construction strategies for optimal tree structure.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, RTree>> TreeFactories =
        new Dictionary<Type, Func<object, RTree>> {
            [typeof(Point3d[])] = s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(),
            [typeof(PointCloud)] = s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(),
            [typeof(Mesh)] = s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(),
            [typeof(Curve[])] = s => BuildGeometryArrayTree((Curve[])s),
            [typeof(Surface[])] = s => BuildGeometryArrayTree((Surface[])s),
            [typeof(Brep[])] = s => BuildGeometryArrayTree((Brep[])s),
        }.ToFrozenDictionary();

    /// <summary>Algorithm configuration mapping input/query type pairs to validation modes and buffer strategies.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (V Mode, int BufferSize)> AlgorithmConfig =
        new Dictionary<(Type, Type), (V, int)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (V.None, SpatialConfig.DefaultBufferSize),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (V.None, SpatialConfig.DefaultBufferSize),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (V.None, SpatialConfig.DefaultBufferSize),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (V.None, SpatialConfig.DefaultBufferSize),
            [(typeof(PointCloud), typeof(Sphere))] = (V.Degeneracy, SpatialConfig.DefaultBufferSize),
            [(typeof(PointCloud), typeof(BoundingBox))] = (V.Degeneracy, SpatialConfig.DefaultBufferSize),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (V.Degeneracy, SpatialConfig.DefaultBufferSize),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (V.Degeneracy, SpatialConfig.DefaultBufferSize),
            [(typeof(Mesh), typeof(Sphere))] = (V.MeshSpecific, SpatialConfig.DefaultBufferSize),
            [(typeof(Mesh), typeof(BoundingBox))] = (V.MeshSpecific, SpatialConfig.DefaultBufferSize),
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = (V.MeshSpecific, SpatialConfig.LargeBufferSize),
            [(typeof(Curve[]), typeof(Sphere))] = (V.Degeneracy, SpatialConfig.DefaultBufferSize),
            [(typeof(Curve[]), typeof(BoundingBox))] = (V.Degeneracy, SpatialConfig.DefaultBufferSize),
            [(typeof(Surface[]), typeof(Sphere))] = (V.BoundingBox, SpatialConfig.DefaultBufferSize),
            [(typeof(Surface[]), typeof(BoundingBox))] = (V.BoundingBox, SpatialConfig.DefaultBufferSize),
            [(typeof(Brep[]), typeof(Sphere))] = (V.Topology, SpatialConfig.DefaultBufferSize),
            [(typeof(Brep[]), typeof(BoundingBox))] = (V.Topology, SpatialConfig.DefaultBufferSize),
        }.ToFrozenDictionary();

    /// <summary>Executes spatial algorithm based on input/query type patterns with RTree-backed operations.</summary>
    [Pure]
    internal static Result<IReadOnlyList<int>> ExecuteAlgorithm<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        int bufferSize) where TInput : notnull where TQuery : notnull =>
        (input, query) switch {
            // Range queries: Sphere or BoundingBox on any spatial type
            (Point3d[] or PointCloud or Mesh or Curve[] or Surface[] or Brep[], Sphere or BoundingBox) =>
                GetTree(input).Bind(tree => ExecuteRangeSearch(tree, query, bufferSize)),
            // Proximity queries: k-nearest or distance-limited on Point3d[] or PointCloud
            (Point3d[] pts, ValueTuple<Point3d[], int>(var needles, var k)) => ExecuteProximity(pts, needles, k, RTree.Point3dKNeighbors),
            (Point3d[] pts, ValueTuple<Point3d[], double>(var needles, var d)) => ExecuteProximity(pts, needles, d, RTree.Point3dClosestPoints),
            (PointCloud cloud, ValueTuple<Point3d[], int>(var needles, var k)) => ExecuteProximity(cloud, needles, k, RTree.PointCloudKNeighbors),
            (PointCloud cloud, ValueTuple<Point3d[], double>(var needles, var d)) => ExecuteProximity(cloud, needles, d, RTree.PointCloudClosestPoints),
            // Mesh overlap detection
            (ValueTuple<Mesh, Mesh>(var mesh1, var mesh2), double tolerance) =>
                GetTree(mesh1).Bind(t1 => GetTree(mesh2).Bind(t2 =>
                    ExecuteOverlapSearch(t1, t2, context.AbsoluteTolerance + tolerance, bufferSize))),
            _ => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };

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

    /// <summary>Executes proximity search with k-nearest or distance-limited algorithm.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteProximity<T, TLimit>(T source, Point3d[] needles, TLimit limit, Delegate algorithm) where T : notnull =>
        (limit, algorithm) switch {
            (int k, Func<T, Point3d[], int, IEnumerable<int[]>> fn) when k > 0 => fn(source, needles, k).ToArray() switch {
                int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            },
            (double d, Func<T, Point3d[], double, IEnumerable<int[]>> fn) when d > 0 => fn(source, needles, d).ToArray() switch {
                int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            },
            (int, _) => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK),
            (double, _) => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance),
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
    private static Result<RTree> GetTree<T>(T source) where T : notnull =>
        TreeFactories.TryGetValue(typeof(T), out Func<object, RTree>? factory) switch {
            true => ResultFactory.Create(value: Spatial.TreeCache.GetValue(key: source, createValueCallback: _ => factory(source!))),
            false => ResultFactory.Create<RTree>(error: E.Spatial.UnsupportedTypeCombo.WithContext($"Type: {typeof(T).Name}")),
        };

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
