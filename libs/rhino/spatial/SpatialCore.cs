using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
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
    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new (Type Input, Type Query, Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)[] {
            (typeof(Point3d[]), typeof(Spatial.SphereQuery), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Point3d[], Spatial.SphereQuery>(_pointArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Point3d[]), typeof(Spatial.BoundingBoxQuery), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Point3d[], Spatial.BoundingBoxQuery>(_pointArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Point3d[]), typeof(Spatial.KNearestNeighborsQuery), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<Point3d[]>(RTree.Point3dKNeighbors, RTree.Point3dClosestPoints)),
            (typeof(Point3d[]), typeof(Spatial.DistanceLimitedQuery), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<Point3d[]>(RTree.Point3dKNeighbors, RTree.Point3dClosestPoints)),
            (typeof(PointCloud), typeof(Spatial.SphereQuery), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<PointCloud, Spatial.SphereQuery>(_pointCloudFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(PointCloud), typeof(Spatial.BoundingBoxQuery), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<PointCloud, Spatial.BoundingBoxQuery>(_pointCloudFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(PointCloud), typeof(Spatial.KNearestNeighborsQuery), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<PointCloud>(RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints)),
            (typeof(PointCloud), typeof(Spatial.DistanceLimitedQuery), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeProximityExecutor<PointCloud>(RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints)),
            (typeof(Mesh), typeof(Spatial.SphereQuery), _meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Mesh, Spatial.SphereQuery>(_meshFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Mesh), typeof(Spatial.BoundingBoxQuery), _meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Mesh, Spatial.BoundingBoxQuery>(_meshFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof((Mesh, Mesh)), typeof(Spatial.MeshOverlapQuery), null, V.MeshSpecific, SpatialConfig.LargeBufferSize, MakeMeshOverlapExecutor()),
            (typeof(Curve[]), typeof(Spatial.SphereQuery), _curveArrayFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Curve[], Spatial.SphereQuery>(_curveArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Curve[]), typeof(Spatial.BoundingBoxQuery), _curveArrayFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Curve[], Spatial.BoundingBoxQuery>(_curveArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Surface[]), typeof(Spatial.SphereQuery), _surfaceArrayFactory, V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Surface[], Spatial.SphereQuery>(_surfaceArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Surface[]), typeof(Spatial.BoundingBoxQuery), _surfaceArrayFactory, V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Surface[], Spatial.BoundingBoxQuery>(_surfaceArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Brep[]), typeof(Spatial.SphereQuery), _brepArrayFactory, V.Topology, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Brep[], Spatial.SphereQuery>(_brepArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
            (typeof(Brep[]), typeof(Spatial.BoundingBoxQuery), _brepArrayFactory, V.Topology, SpatialConfig.DefaultBufferSize, MakeRangeExecutor<Brep[], Spatial.BoundingBoxQuery>(_brepArrayFactory, static (tree, query, buffer) => ExecuteRangeSearch(tree, query, buffer))),
        }.ToFrozenDictionary(static entry => (entry.Input, entry.Query), static entry => (entry.Factory, entry.Mode, entry.BufferSize, entry.Execute));

    internal static Result<IReadOnlyList<int>> Analyze<TInput>(
        TInput input,
        Spatial.Query query,
        IGeometryContext context,
        int? bufferSize) where TInput : notnull =>
        OperationRegistry.TryGetValue((typeof(TInput), query.GetType()), out (Func<object, RTree>? _, V Mode, int BufferSize, Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute) config) switch {
            true => UnifiedOperation.Apply(
                input: input,
                operation: (Func<TInput, Result<IReadOnlyList<int>>>)(item => config.Execute(item, query, context, bufferSize ?? config.BufferSize)),
                config: new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.Mode,
                    OperationName = SpatialConfig.BuildOperationName(typeof(TInput), query.GetType()),
                    EnableDiagnostics = false,
                }),
            false => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {query.GetType().Name}")),
        };

    private static Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeRangeExecutor<TInput, TQuery>(
        Func<object, RTree> factory,
        Func<RTree, TQuery, int, Result<IReadOnlyList<int>>> executor) where TInput : notnull where TQuery : Spatial.Query =>
        (input, query, _, buffer) => query is TQuery typed
            ? ((Func<Result<IReadOnlyList<int>>>)(() => {
                using RTree tree = factory(input);
                return executor(tree, typed, buffer);
            }))()
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo);

    private static Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeProximityExecutor<TInput>(
        Func<TInput, Point3d[], int, IEnumerable<int[]>> kNearest,
        Func<TInput, Point3d[], double, IEnumerable<int[]>> distanceLimited) where TInput : notnull =>
        (input, query, _, _) => query switch {
            Spatial.KNearestNeighborsQuery nearest => ExecuteKNearestSearch(
                source: (TInput)input,
                needles: nearest.Points,
                count: nearest.Count,
                kNearest: kNearest),
            Spatial.DistanceLimitedQuery distanceQuery => ExecuteDistanceLimitedSearch(
                source: (TInput)input,
                needles: distanceQuery.Points,
                distance: distanceQuery.Distance,
                distanceLimited: distanceLimited),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
        };

    private static Func<object, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeMeshOverlapExecutor() =>
        (input, query, context, buffer) => input is (Mesh m1, Mesh m2) && query is Spatial.MeshOverlapQuery overlap
            ? ((Func<Result<IReadOnlyList<int>>>)(() => {
                using RTree tree1 = _meshFactory(m1);
                using RTree tree2 = _meshFactory(m2);
                double tolerance = context.AbsoluteTolerance + overlap.AdditionalTolerance;
                return ExecuteOverlapSearch(tree1: tree1, tree2: tree2, tolerance: tolerance, bufferSize: buffer);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, Spatial.SphereQuery query, int bufferSize) =>
        ExecuteRangeSearch(tree, handler => tree.Search(query.Sphere, handler), bufferSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, Spatial.BoundingBoxQuery query, int bufferSize) =>
        ExecuteRangeSearch(tree, handler => tree.Search(query.BoundingBox, handler), bufferSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, Func<System.EventHandler<RTreeEventArgs>, bool> search, int bufferSize) =>
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
                _ = search(Collect);
                return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
            } finally {
                ArrayPool<int>.Shared.Return(buffer, clearArray: true);
            }
        }))();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteKNearestSearch<T>(
        T source,
        Point3d[] needles,
        int count,
        Func<T, Point3d[], int, IEnumerable<int[]>> kNearest) where T : notnull =>
        count > 0
            ? kNearest(source, needles, count).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed)
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK.WithContext(count.ToString(CultureInfo.InvariantCulture)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteDistanceLimitedSearch<T>(
        T source,
        Point3d[] needles,
        double distance,
        Func<T, Point3d[], double, IEnumerable<int[]>> distanceLimited) where T : notnull =>
        distance > 0.0
            ? distanceLimited(source, needles, distance).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed)
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance.WithContext(distance.ToString(CultureInfo.InvariantCulture)));

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
