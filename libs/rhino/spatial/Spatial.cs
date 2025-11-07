using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Errors;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial indexing with RhinoCommon RTree algorithms and monadic composition.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    /// <summary>RTree cache using weak references for automatic memory management and tree reuse across operations.</summary>
    private static readonly ConditionalWeakTable<object, RTree> _treeCache = [];

    /// <summary>RTree factory configuration mapping source types to construction strategies for optimal tree structure.</summary>
    private static readonly FrozenDictionary<Type, Func<object, RTree>> _treeFactories =
        new Dictionary<Type, Func<object, RTree>> {
            [typeof(Point3d[])] = s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(),
            [typeof(PointCloud)] = s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(),
            [typeof(Mesh)] = s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(),
            [typeof(Curve[])] = s => BuildGeometryArrayTree((Curve[])s),
            [typeof(Surface[])] = s => BuildGeometryArrayTree((Surface[])s),
            [typeof(Brep[])] = s => BuildGeometryArrayTree((Brep[])s),
        }.ToFrozenDictionary();

    /// <summary>Algorithm configuration mapping input/query type pairs to validation modes and buffer strategies.</summary>
    private static readonly FrozenDictionary<(Type Input, Type Query), (V Mode, int BufferSize)> _algorithmConfig =
        new Dictionary<(Type, Type), (V, int)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (V.None, 2048),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (V.None, 2048),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (V.None, 2048),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (V.None, 2048),
            [(typeof(PointCloud), typeof(Sphere))] = (V.Degeneracy, 2048),
            [(typeof(PointCloud), typeof(BoundingBox))] = (V.Degeneracy, 2048),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (V.Degeneracy, 2048),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (V.Degeneracy, 2048),
            [(typeof(Mesh), typeof(Sphere))] = (V.MeshSpecific, 2048),
            [(typeof(Mesh), typeof(BoundingBox))] = (V.MeshSpecific, 2048),
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = (V.MeshSpecific, 4096),
            [(typeof(Curve[]), typeof(Sphere))] = (V.Degeneracy, 2048),
            [(typeof(Curve[]), typeof(BoundingBox))] = (V.Degeneracy, 2048),
            [(typeof(Surface[]), typeof(Sphere))] = (V.BoundingBox, 2048),
            [(typeof(Surface[]), typeof(BoundingBox))] = (V.BoundingBox, 2048),
            [(typeof(Brep[]), typeof(Sphere))] = (V.Topology, 2048),
            [(typeof(Brep[]), typeof(BoundingBox))] = (V.Topology, 2048),
        }.ToFrozenDictionary();

    /// <summary>Performs spatial indexing operations using RhinoCommon RTree algorithms with type-based query dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        bool enableDiagnostics = false) where TInput : notnull where TQuery : notnull =>
        _algorithmConfig.TryGetValue((typeof(TInput), typeof(TQuery)), out (V mode, int bufferSize) config) switch {
            true => UnifiedOperation.Apply(
                input: input,
                operation: (Func<TInput, Result<IReadOnlyList<int>>>)(item => ExecuteAlgorithm(item, query, context, config.bufferSize)),
                config: new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.mode,
                    OperationName = $"Spatial.{typeof(TInput).Name}.{typeof(TQuery).Name}",
                    EnableDiagnostics = enableDiagnostics,
                }),
            false => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };

    /// <summary>Executes spatial algorithm based on input/query type patterns with RTree-backed operations.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteAlgorithm<TInput, TQuery>(
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
            (Point3d[] pts, object q) when q is ValueTuple<Point3d[], int> tuple1 => GetTree(pts).Bind(_ => tuple1.Item2 <= 0
                ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK)
                : ExecuteKNearestPoints(pts, tuple1.Item1, tuple1.Item2)),
            (Point3d[] pts, object q) when q is ValueTuple<Point3d[], double> tuple2 => GetTree(pts).Bind(_ => tuple2.Item2 <= 0
                ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance)
                : ExecuteDistanceLimitedPoints(pts, tuple2.Item1, tuple2.Item2)),
            // PointCloud range queries
            (PointCloud cloud, Sphere sphere) => GetTree(cloud).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (PointCloud cloud, BoundingBox box) => GetTree(cloud).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
            // PointCloud proximity queries
            (PointCloud cloud, object q) when q is ValueTuple<Point3d[], int> tuple3 => tuple3.Item2 <= 0
                ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK)
                : ExecuteKNearestCloud(cloud, tuple3.Item1, tuple3.Item2),
            (PointCloud cloud, object q) when q is ValueTuple<Point3d[], double> tuple4 => tuple4.Item2 <= 0
                ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance)
                : ExecuteDistanceLimitedCloud(cloud, tuple4.Item1, tuple4.Item2),
            // Mesh range queries
            (Mesh mesh, Sphere sphere) => GetTree(mesh).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, bufferSize)),
            (Mesh mesh, BoundingBox box) => GetTree(mesh).Bind(tree =>
                ExecuteRangeSearch(tree, box, bufferSize)),
            // Mesh overlap detection
            (ValueTuple<Mesh, Mesh> meshPair, double tolerance) => GetTree(meshPair.Item1).Bind(t1 =>
                GetTree(meshPair.Item2).Bind(t2 =>
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
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(
        RTree tree,
        object queryShape,
        int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            (queryShape switch {
                Sphere sphere => (Action)(() => tree.Search(sphere, (_, args) => {
                    if (count < buffer.Length) {
                        buffer[count++] = args.Id;
                    }
                })),
                BoundingBox box => () => tree.Search(box, (_, args) => {
                    if (count < buffer.Length) {
                        buffer[count++] = args.Id;
                    }
                }),
                _ => () => { }
                ,
            })();
            return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Executes k-nearest neighbor search for point arrays using RTree.Point3dKNeighbors.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteKNearestPoints(Point3d[] points, Point3d[] needles, int k) =>
        RTree.Point3dKNeighbors(points, needles, k) switch {
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

    /// <summary>Executes distance-limited proximity search for point arrays using RTree.Point3dClosestPoints.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteDistanceLimitedPoints(Point3d[] points, Point3d[] needles, double limit) =>
        RTree.Point3dClosestPoints(points, needles, limit) switch {
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

    /// <summary>Executes k-nearest neighbor search for point clouds using RTree.PointCloudKNeighbors.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteKNearestCloud(PointCloud cloud, Point3d[] needles, int k) =>
        RTree.PointCloudKNeighbors(cloud, needles, k) switch {
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

    /// <summary>Executes distance-limited proximity search for point clouds using RTree.PointCloudClosestPoints.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteDistanceLimitedCloud(PointCloud cloud, Point3d[] needles, double limit) =>
        RTree.PointCloudClosestPoints(cloud, needles, limit) switch {
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(indices => indices),]),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
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
        _treeFactories.TryGetValue(typeof(T), out Func<object, RTree>? factory) switch {
            true => ResultFactory.Create(value: _treeCache.GetValue(key: source, createValueCallback: _ => factory(source!))),
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
