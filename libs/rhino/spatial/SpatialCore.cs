using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>RTree spatial indexing with ArrayPool buffers for zero-allocation queries.</summary>
[Pure]
internal static class SpatialCore {
    private static readonly Func<object, RTree> _pointArrayFactory = static s => (RTree)SpatialConfig.TypeExtractors[("RTreeFactory", typeof(Point3d[]))](s);
    private static readonly Func<object, RTree> _pointCloudFactory = static s => (RTree)SpatialConfig.TypeExtractors[("RTreeFactory", typeof(PointCloud))](s);
    private static readonly Func<object, RTree> _meshFactory = static s => (RTree)SpatialConfig.TypeExtractors[("RTreeFactory", typeof(Mesh))](s);
    private static readonly Func<object, RTree> _curveArrayFactory = static s => BuildGeometryArrayTree((Curve[])s);
    private static readonly Func<object, RTree> _surfaceArrayFactory = static s => BuildGeometryArrayTree((Surface[])s);
    private static readonly Func<object, RTree> _brepArrayFactory = static s => BuildGeometryArrayTree((Brep[])s);

    /// <summary>(Input, Query) type pairs to (Factory, Mode, BufferSize, Execute) mapping.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (V Mode, int BufferSize, Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new (Type Input, Type Query, V Mode, int BufferSize, Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)[] {
            (typeof(Point3d[]), typeof(Spatial.SphereQuery), V.None, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_pointArrayFactory, static query => query is Spatial.SphereQuery sphere ? sphere.Sphere : null)),
            (typeof(Point3d[]), typeof(Spatial.BoundingBoxQuery), V.None, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_pointArrayFactory, static query => query is Spatial.BoundingBoxQuery box ? box.BoundingBox : null)),
            (typeof(Point3d[]), typeof(Spatial.KNearestProximityQuery), V.None, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<Point3d[]>(
                specExtractor: static query => query is Spatial.KNearestProximityQuery nearest ? (nearest.Needles, (object)nearest.Count) : null,
                kNearest: RTree.Point3dKNeighbors,
                distLimited: RTree.Point3dClosestPoints)),
            (typeof(Point3d[]), typeof(Spatial.DistanceProximityQuery), V.None, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<Point3d[]>(
                specExtractor: static query => query is Spatial.DistanceProximityQuery distance ? (distance.Needles, (object)distance.Distance) : null,
                kNearest: RTree.Point3dKNeighbors,
                distLimited: RTree.Point3dClosestPoints)),
            (typeof(PointCloud), typeof(Spatial.SphereQuery), V.Standard, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_pointCloudFactory, static query => query is Spatial.SphereQuery sphere ? sphere.Sphere : null)),
            (typeof(PointCloud), typeof(Spatial.BoundingBoxQuery), V.Standard, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_pointCloudFactory, static query => query is Spatial.BoundingBoxQuery box ? box.BoundingBox : null)),
            (typeof(PointCloud), typeof(Spatial.KNearestProximityQuery), V.Standard, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<PointCloud>(
                specExtractor: static query => query is Spatial.KNearestProximityQuery nearest ? (nearest.Needles, (object)nearest.Count) : null,
                kNearest: RTree.PointCloudKNeighbors,
                distLimited: RTree.PointCloudClosestPoints)),
            (typeof(PointCloud), typeof(Spatial.DistanceProximityQuery), V.Standard, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<PointCloud>(
                specExtractor: static query => query is Spatial.DistanceProximityQuery distance ? (distance.Needles, (object)distance.Distance) : null,
                kNearest: RTree.PointCloudKNeighbors,
                distLimited: RTree.PointCloudClosestPoints)),
            (typeof(Mesh), typeof(Spatial.SphereQuery), V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_meshFactory, static query => query is Spatial.SphereQuery sphere ? sphere.Sphere : null)),
            (typeof(Mesh), typeof(Spatial.BoundingBoxQuery), V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_meshFactory, static query => query is Spatial.BoundingBoxQuery box ? box.BoundingBox : null)),
            (typeof((Mesh, Mesh)), typeof(Spatial.MeshOverlapQuery), V.MeshSpecific, SpatialConfig.LargeBufferSize, MakeMeshOverlapExecutor()),
            (typeof(Curve[]), typeof(Spatial.SphereQuery), V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_curveArrayFactory, static query => query is Spatial.SphereQuery sphere ? sphere.Sphere : null)),
            (typeof(Curve[]), typeof(Spatial.BoundingBoxQuery), V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_curveArrayFactory, static query => query is Spatial.BoundingBoxQuery box ? box.BoundingBox : null)),
            (typeof(Surface[]), typeof(Spatial.SphereQuery), V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_surfaceArrayFactory, static query => query is Spatial.SphereQuery sphere ? sphere.Sphere : null)),
            (typeof(Surface[]), typeof(Spatial.BoundingBoxQuery), V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_surfaceArrayFactory, static query => query is Spatial.BoundingBoxQuery box ? box.BoundingBox : null)),
            (typeof(Brep[]), typeof(Spatial.SphereQuery), V.Topology, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_brepArrayFactory, static query => query is Spatial.SphereQuery sphere ? sphere.Sphere : null)),
            (typeof(Brep[]), typeof(Spatial.BoundingBoxQuery), V.Topology, SpatialConfig.DefaultBufferSize, MakeRangeExecutor(_brepArrayFactory, static query => query is Spatial.BoundingBoxQuery box ? box.BoundingBox : null)),
        }.ToFrozenDictionary(static entry => (entry.Input, entry.Query), static entry => (entry.Mode, entry.BufferSize, entry.Execute));

    internal static Result<IReadOnlyList<int>> Analyze<TInput>(TInput input, Spatial.Query query, IGeometryContext context, int? bufferSizeOverride) where TInput : notnull =>
        OperationRegistry.TryGetValue((typeof(TInput), query.GetType()), out (V Mode, int BufferSize, Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute) config)
            ? UnifiedOperation.Apply(
                input: input,
                operation: (Func<TInput, Result<IReadOnlyList<int>>>)(item => config.Execute(item!, query, context, bufferSizeOverride ?? config.BufferSize)),
                config: new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.Mode,
                    OperationName = $"Spatial.{typeof(TInput).Name}.{query.GetType().Name}",
                    EnableDiagnostics = false,
                })
            : ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext($"Input: {typeof(TInput).Name}, Query: {query.GetType().Name}"));

    private static Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeRangeExecutor(
        Func<object, RTree> factory,
        Func<Spatial.Query, object?> querySelector) =>
        (input, query, _, buffer) => querySelector(query) is { } shape
            ? ((Func<Result<IReadOnlyList<int>>>)(() => {
                using RTree tree = factory(input);
                return ExecuteRangeSearch(tree: tree, queryShape: shape, bufferSize: buffer);
            }))()
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo);

    private static Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeProximityExecutor<TInput>(
        Func<Spatial.Query, (Point3d[] Needles, object Limit)?> specExtractor,
        Func<TInput, Point3d[], int, IEnumerable<int[]>> kNearest,
        Func<TInput, Point3d[], double, IEnumerable<int[]>> distLimited) where TInput : notnull =>
        (input, query, _, _) => specExtractor(query) is (Point3d[] Needles, object Limit) spec
            ? ExecuteProximitySearch(source: (TInput)input, needles: spec.Needles, limit: spec.Limit, kNearest: kNearest, distLimited: distLimited)
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo);

    private static Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeMeshOverlapExecutor() =>
        (input, query, context, bufferSize) => input is (Mesh m1, Mesh m2) && query is Spatial.MeshOverlapQuery overlap
            ? ((Func<Result<IReadOnlyList<int>>>)(() => {
                using RTree tree1 = _meshFactory(m1);
                using RTree tree2 = _meshFactory(m2);
                return ExecuteOverlapSearch(tree1: tree1, tree2: tree2, tolerance: context.AbsoluteTolerance + overlap.AdditionalTolerance, bufferSize: bufferSize);
            }))()
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo);

    /// <summary>Build RTree from geometry array via bounding box insertion.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }

    /// <summary>Execute RTree range search with ArrayPool buffer for zero allocation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, object queryShape, int bufferSize) =>
        ((Func<Result<IReadOnlyList<int>>>)(() => {
            int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
            int count = 0;
            try {
                void Collect(object? sender, RTreeEventArgs args) {
                    if (count >= buffer.Length) {
                        return;
                    }
                    buffer[count++] = args.Id;
                }
                _ = queryShape switch {
                    Sphere sphere => tree.Search(sphere, Collect),
                    BoundingBox box => tree.Search(box, Collect),
                    _ => false,
                };
                return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
            } finally {
                ArrayPool<int>.Shared.Return(buffer, clearArray: true);
            }
        }))();

    /// <summary>Execute k-nearest or distance-limited proximity search via RTree.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteProximitySearch<T>(T source, Point3d[] needles, object limit, Func<T, Point3d[], int, IEnumerable<int[]>> kNearest, Func<T, Point3d[], double, IEnumerable<int[]>> distLimited) where T : notnull =>
        limit switch {
            int k when k > 0 => kNearest(source, needles, k).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            double d when d > 0 => distLimited(source, needles, d).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            int k => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK.WithContext(k.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            double d => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance.WithContext(d.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

    /// <summary>Execute mesh face overlap detection between two RTrees with tolerance.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteOverlapSearch(RTree tree1, RTree tree2, double tolerance, int bufferSize) =>
        ((Func<Result<IReadOnlyList<int>>>)(() => {
            int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
            int count = 0;
            try {
                return RTree.SearchOverlaps(tree1, tree2, tolerance, (_, args) => {
                    if (count + 1 < buffer.Length) {
                        buffer[count++] = args.Id;
                        buffer[count++] = args.IdB;
                    }
                })
                    ? ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : [])
                    : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed);
            } finally {
                ArrayPool<int>.Shared.Return(buffer, clearArray: true);
            }
        }))();
}
