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
            // Point array range queries
            (Point3d[] pts, Sphere sphere) => GetTree(pts).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (Point3d[] pts, BoundingBox box) => GetTree(pts).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
            // Point array proximity queries
            (Point3d[] pts, object q) when q is ValueTuple<Point3d[], int> tuple1 => GetTree(pts).Bind(_ =>
                ExecuteProximitySearch(pts, tuple1.Item1, tuple1.Item2, RTree.Point3dKNeighbors, RTree.Point3dClosestPoints)),
            (Point3d[] pts, object q) when q is ValueTuple<Point3d[], double> tuple2 => GetTree(pts).Bind(_ =>
                ExecuteProximitySearch(pts, tuple2.Item1, tuple2.Item2, RTree.Point3dKNeighbors, RTree.Point3dClosestPoints)),
            // PointCloud range queries
            (PointCloud cloud, Sphere sphere) => GetTree(cloud).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (PointCloud cloud, BoundingBox box) => GetTree(cloud).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
            // PointCloud proximity queries
            (PointCloud cloud, ValueTuple<Point3d[], int>(var needles, var k)) =>
                ExecuteProximitySearch(cloud, needles, k, RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints),
            (PointCloud cloud, ValueTuple<Point3d[], double>(var needles, var distance)) =>
                ExecuteProximitySearch(cloud, needles, distance, RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints),
            // Mesh range queries
            (Mesh mesh, Sphere sphere) => GetTree(mesh).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (Mesh mesh, BoundingBox box) => GetTree(mesh).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
            // Mesh overlap detection
            (ValueTuple<Mesh, Mesh>(var mesh1, var mesh2), double tolerance) => GetTree(mesh1).Bind(t1 =>
                GetTree(mesh2).Bind(t2 =>
                    ExecuteOverlapSearch(t1, t2, context.AbsoluteTolerance + tolerance, bufferSize))),
            // Curve array range queries
            (Curve[] curves, Sphere sphere) => GetTree(curves).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (Curve[] curves, BoundingBox box) => GetTree(curves).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
            // Surface array range queries
            (Surface[] surfaces, Sphere sphere) => GetTree(surfaces).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (Surface[] surfaces, BoundingBox box) => GetTree(surfaces).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
            // Brep array range queries
            (Brep[] breps, Sphere sphere) => GetTree(breps).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (Brep[] breps, BoundingBox box) => GetTree(breps).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
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
