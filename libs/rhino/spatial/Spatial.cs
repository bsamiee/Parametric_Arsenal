using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial indexing with RhinoCommon RTree algorithms and monadic composition.</summary>
public static class Spatial {
    /// <summary>Algorithm configuration mapping input/query type pairs to validation modes and buffer strategies.</summary>
    private static readonly FrozenDictionary<(Type Input, Type Query), (ValidationMode Mode, int BufferSize)> _algorithmConfig =
        new Dictionary<(Type, Type), (ValidationMode, int)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (ValidationMode.None, 2048),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (ValidationMode.None, 2048),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (ValidationMode.None, 2048),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (ValidationMode.None, 2048),
            [(typeof(PointCloud), typeof(Sphere))] = (ValidationMode.Degeneracy, 2048),
            [(typeof(PointCloud), typeof(BoundingBox))] = (ValidationMode.Degeneracy, 2048),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (ValidationMode.Degeneracy, 2048),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (ValidationMode.Degeneracy, 2048),
            [(typeof(Mesh), typeof(Sphere))] = (ValidationMode.MeshSpecific, 2048),
            [(typeof(Mesh), typeof(BoundingBox))] = (ValidationMode.MeshSpecific, 2048),
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = (ValidationMode.MeshSpecific, 4096),
            [(typeof(Curve[]), typeof(Sphere))] = (ValidationMode.Degeneracy, 2048),
            [(typeof(Curve[]), typeof(BoundingBox))] = (ValidationMode.Degeneracy, 2048),
            [(typeof(Surface[]), typeof(Sphere))] = (ValidationMode.BoundingBox, 2048),
            [(typeof(Surface[]), typeof(BoundingBox))] = (ValidationMode.BoundingBox, 2048),
            [(typeof(Brep[]), typeof(Sphere))] = (ValidationMode.Topology, 2048),
            [(typeof(Brep[]), typeof(BoundingBox))] = (ValidationMode.Topology, 2048),
        }.ToFrozenDictionary();

    /// <summary>Performs spatial indexing operations using RhinoCommon RTree algorithms with type-based query dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context) where TInput : notnull where TQuery : notnull =>
        _algorithmConfig.TryGetValue((typeof(TInput), typeof(TQuery)), out (ValidationMode mode, int bufferSize) config) switch {
            true => UnifiedOperation.Apply(
                input,
                (Func<TInput, Result<IReadOnlyList<int>>>)(item => ExecuteAlgorithm(item, query, context, config.bufferSize)),
                new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.mode,
                }),
            false => ResultFactory.Create<IReadOnlyList<int>>(
                error: SpatialErrors.Query.UnsupportedTypeCombo.WithContext(
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
            (Point3d[] pts, Sphere sphere) => SpatialCache.GetTree(pts).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, context.AbsoluteTolerance, bufferSize)),
            (Point3d[] pts, BoundingBox box) => SpatialCache.GetTree(pts).Bind(tree =>
                ExecuteRangeSearch(tree, box, context.AbsoluteTolerance, bufferSize)),
            // Point array proximity queries
            (Point3d[] pts, (Point3d[] needles, int k)) => SpatialCache.GetTree(pts).Bind(tree =>
                k <= 0 ?
                    ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.InvalidK) :
                    ExecuteKNearestPoints(pts, needles, k)),
            (Point3d[] pts, (Point3d[] needles, double limit)) => SpatialCache.GetTree(pts).Bind(tree =>
                limit <= 0 ?
                    ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.InvalidDistance) :
                    ExecuteDistanceLimitedPoints(pts, needles, limit)),
            // PointCloud range queries
            (PointCloud cloud, Sphere sphere) => SpatialCache.GetTree(cloud).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, context.AbsoluteTolerance, bufferSize)),
            (PointCloud cloud, BoundingBox box) => SpatialCache.GetTree(cloud).Bind(tree =>
                ExecuteRangeSearch(tree, box, context.AbsoluteTolerance, bufferSize)),
            // PointCloud proximity queries
            (PointCloud cloud, (Point3d[] needles, int k)) =>
                k <= 0 ?
                    ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.InvalidK) :
                    ExecuteKNearestCloud(cloud, needles, k),
            (PointCloud cloud, (Point3d[] needles, double limit)) =>
                limit <= 0 ?
                    ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.InvalidDistance) :
                    ExecuteDistanceLimitedCloud(cloud, needles, limit),
            // Mesh range queries
            (Mesh mesh, Sphere sphere) => SpatialCache.GetTree(mesh).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, context.AbsoluteTolerance, bufferSize)),
            (Mesh mesh, BoundingBox box) => SpatialCache.GetTree(mesh).Bind(tree =>
                ExecuteRangeSearch(tree, box, context.AbsoluteTolerance, bufferSize)),
            // Mesh overlap detection
            (ValueTuple<Mesh, Mesh> meshPair, double tolerance) => SpatialCache.GetTree(meshPair.Item1).Bind(t1 =>
                SpatialCache.GetTree(meshPair.Item2).Bind(t2 =>
                    ExecuteOverlapSearch(t1, t2, context.AbsoluteTolerance + tolerance, bufferSize))),
            // Curve array range queries
            (Curve[] curves, Sphere sphere) => SpatialCache.GetTree(curves).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, context.AbsoluteTolerance, bufferSize)),
            (Curve[] curves, BoundingBox box) => SpatialCache.GetTree(curves).Bind(tree =>
                ExecuteRangeSearch(tree, box, context.AbsoluteTolerance, bufferSize)),
            // Surface array range queries
            (Surface[] surfaces, Sphere sphere) => SpatialCache.GetTree(surfaces).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, context.AbsoluteTolerance, bufferSize)),
            (Surface[] surfaces, BoundingBox box) => SpatialCache.GetTree(surfaces).Bind(tree =>
                ExecuteRangeSearch(tree, box, context.AbsoluteTolerance, bufferSize)),
            // Brep array range queries
            (Brep[] breps, Sphere sphere) => SpatialCache.GetTree(breps).Bind(tree =>
                ExecuteRangeSearch(tree, sphere, context.AbsoluteTolerance, bufferSize)),
            (Brep[] breps, BoundingBox box) => SpatialCache.GetTree(breps).Bind(tree =>
                ExecuteRangeSearch(tree, box, context.AbsoluteTolerance, bufferSize)),
            _ => ResultFactory.Create<IReadOnlyList<int>>(
                error: SpatialErrors.Query.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };

    /// <summary>Executes RTree range search with sphere or bounding box query using ArrayPool for zero-allocation results.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(
        RTree tree,
        object queryShape,
        double tolerance,
        int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            (queryShape switch {
                Sphere sphere => (Action)(() => tree.Search(sphere, (sender, args) => {
                    if (count < buffer.Length) {
                        buffer[count++] = args.Id;
                    }
                })),
                BoundingBox box => () => tree.Search(box, (sender, args) => {
                    if (count < buffer.Length) {
                        buffer[count++] = args.Id;
                    }
                }),
                _ => () => { },
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
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: (IReadOnlyList<int>)results.SelectMany(indices => indices).ToArray()),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.ProximityFailed),
        };

    /// <summary>Executes distance-limited proximity search for point arrays using RTree.Point3dClosestPoints.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteDistanceLimitedPoints(Point3d[] points, Point3d[] needles, double limit) =>
        RTree.Point3dClosestPoints(points, needles, limit) switch {
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: (IReadOnlyList<int>)results.SelectMany(indices => indices).ToArray()),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.ProximityFailed),
        };

    /// <summary>Executes k-nearest neighbor search for point clouds using RTree.PointCloudKNeighbors.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteKNearestCloud(PointCloud cloud, Point3d[] needles, int k) =>
        RTree.PointCloudKNeighbors(cloud, needles, k) switch {
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: (IReadOnlyList<int>)results.SelectMany(indices => indices).ToArray()),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.ProximityFailed),
        };

    /// <summary>Executes distance-limited proximity search for point clouds using RTree.PointCloudClosestPoints.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteDistanceLimitedCloud(PointCloud cloud, Point3d[] needles, double limit) =>
        RTree.PointCloudClosestPoints(cloud, needles, limit) switch {
            int[][] results => ResultFactory.Create<IReadOnlyList<int>>(value: (IReadOnlyList<int>)results.SelectMany(indices => indices).ToArray()),
            null => ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Query.ProximityFailed),
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
            _ = RTree.SearchOverlaps(tree1, tree2, tolerance, (sender, args) => {
                if (count + 1 < buffer.Length) {
                    buffer[count++] = args.Id;
                    buffer[count++] = args.IdB;
                }
            });
            return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }
}
