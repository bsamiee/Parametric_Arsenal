using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
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
    private static readonly Func<Spatial.PointArraySource, RTree> _pointArrayTreeFactory = static source =>
        RTree.CreateFromPointArray(source.Points) ?? new RTree();

    private static readonly Func<Spatial.PointCloudSource, RTree> _pointCloudTreeFactory = static source =>
        RTree.CreatePointCloudTree(source.PointCloud) ?? new RTree();

    private static readonly Func<Spatial.MeshSource, RTree> _meshTreeFactory = static source =>
        RTree.CreateMeshFaceTree(source.Mesh) ?? new RTree();

    private static readonly Func<Spatial.CurveArraySource, RTree> _curveTreeFactory = static source =>
        BuildGeometryArrayTree(source.Curves);

    private static readonly Func<Spatial.SurfaceArraySource, RTree> _surfaceTreeFactory = static source =>
        BuildGeometryArrayTree(source.Surfaces);

    private static readonly Func<Spatial.BrepArraySource, RTree> _brepTreeFactory = static source =>
        BuildGeometryArrayTree(source.Breps);

    private static readonly FrozenDictionary<(Type Source, Type Query), QueryOperationEntry> OperationRegistry =
        new (Type Source, Type Query, QueryOperationEntry Entry)[] {
            (typeof(Spatial.PointArraySource), typeof(Spatial.RTreeSphereQuery),
                CreateRangeEntry(
                    factory: _pointArrayTreeFactory,
                    shapeSelector: static query => query.Sphere,
                    executor: static (tree, sphere, buffer) => ExecuteRangeSearch(tree: tree, queryShape: sphere, bufferSize: buffer),
                    mode: V.None,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.PointArraySource), typeof(Spatial.RTreeBoundingBoxQuery),
                CreateRangeEntry(
                    factory: _pointArrayTreeFactory,
                    shapeSelector: static query => query.BoundingBox,
                    executor: static (tree, box, buffer) => ExecuteRangeSearch(tree: tree, queryShape: box, bufferSize: buffer),
                    mode: V.None,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.PointArraySource), typeof(Spatial.KNearestQuery),
                CreateProximityEntry(
                    kNearest: static (source, needles, count) => RTree.Point3dKNeighbors(source.Points, needles, count),
                    distanceLimited: static (source, needles, distance) => RTree.Point3dClosestPoints(source.Points, needles, distance),
                    mode: V.None)),
            (typeof(Spatial.PointArraySource), typeof(Spatial.DistanceThresholdQuery),
                CreateProximityEntry(
                    kNearest: static (source, needles, count) => RTree.Point3dKNeighbors(source.Points, needles, count),
                    distanceLimited: static (source, needles, distance) => RTree.Point3dClosestPoints(source.Points, needles, distance),
                    mode: V.None)),
            (typeof(Spatial.PointCloudSource), typeof(Spatial.RTreeSphereQuery),
                CreateRangeEntry(
                    factory: _pointCloudTreeFactory,
                    shapeSelector: static query => query.Sphere,
                    executor: static (tree, sphere, buffer) => ExecuteRangeSearch(tree: tree, queryShape: sphere, bufferSize: buffer),
                    mode: V.Standard,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.PointCloudSource), typeof(Spatial.RTreeBoundingBoxQuery),
                CreateRangeEntry(
                    factory: _pointCloudTreeFactory,
                    shapeSelector: static query => query.BoundingBox,
                    executor: static (tree, box, buffer) => ExecuteRangeSearch(tree: tree, queryShape: box, bufferSize: buffer),
                    mode: V.Standard,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.PointCloudSource), typeof(Spatial.KNearestQuery),
                CreateProximityEntry(
                    kNearest: static (source, needles, count) => RTree.PointCloudKNeighbors(source.PointCloud, needles, count),
                    distanceLimited: static (source, needles, distance) => RTree.PointCloudClosestPoints(source.PointCloud, needles, distance),
                    mode: V.Standard)),
            (typeof(Spatial.PointCloudSource), typeof(Spatial.DistanceThresholdQuery),
                CreateProximityEntry(
                    kNearest: static (source, needles, count) => RTree.PointCloudKNeighbors(source.PointCloud, needles, count),
                    distanceLimited: static (source, needles, distance) => RTree.PointCloudClosestPoints(source.PointCloud, needles, distance),
                    mode: V.Standard)),
            (typeof(Spatial.MeshSource), typeof(Spatial.RTreeSphereQuery),
                CreateRangeEntry(
                    factory: _meshTreeFactory,
                    shapeSelector: static query => query.Sphere,
                    executor: static (tree, sphere, buffer) => ExecuteRangeSearch(tree: tree, queryShape: sphere, bufferSize: buffer),
                    mode: V.MeshSpecific,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.MeshSource), typeof(Spatial.RTreeBoundingBoxQuery),
                CreateRangeEntry(
                    factory: _meshTreeFactory,
                    shapeSelector: static query => query.BoundingBox,
                    executor: static (tree, box, buffer) => ExecuteRangeSearch(tree: tree, queryShape: box, bufferSize: buffer),
                    mode: V.MeshSpecific,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.MeshPairSource), typeof(Spatial.MeshOverlapQuery),
                CreateMeshOverlapEntry(mode: V.MeshSpecific, bufferSize: SpatialConfig.LargeBufferSize)),
            (typeof(Spatial.CurveArraySource), typeof(Spatial.RTreeSphereQuery),
                CreateRangeEntry(
                    factory: _curveTreeFactory,
                    shapeSelector: static query => query.Sphere,
                    executor: static (tree, sphere, buffer) => ExecuteRangeSearch(tree: tree, queryShape: sphere, bufferSize: buffer),
                    mode: V.Degeneracy,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.CurveArraySource), typeof(Spatial.RTreeBoundingBoxQuery),
                CreateRangeEntry(
                    factory: _curveTreeFactory,
                    shapeSelector: static query => query.BoundingBox,
                    executor: static (tree, box, buffer) => ExecuteRangeSearch(tree: tree, queryShape: box, bufferSize: buffer),
                    mode: V.Degeneracy,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.SurfaceArraySource), typeof(Spatial.RTreeSphereQuery),
                CreateRangeEntry(
                    factory: _surfaceTreeFactory,
                    shapeSelector: static query => query.Sphere,
                    executor: static (tree, sphere, buffer) => ExecuteRangeSearch(tree: tree, queryShape: sphere, bufferSize: buffer),
                    mode: V.BoundingBox,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.SurfaceArraySource), typeof(Spatial.RTreeBoundingBoxQuery),
                CreateRangeEntry(
                    factory: _surfaceTreeFactory,
                    shapeSelector: static query => query.BoundingBox,
                    executor: static (tree, box, buffer) => ExecuteRangeSearch(tree: tree, queryShape: box, bufferSize: buffer),
                    mode: V.BoundingBox,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.BrepArraySource), typeof(Spatial.RTreeSphereQuery),
                CreateRangeEntry(
                    factory: _brepTreeFactory,
                    shapeSelector: static query => query.Sphere,
                    executor: static (tree, sphere, buffer) => ExecuteRangeSearch(tree: tree, queryShape: sphere, bufferSize: buffer),
                    mode: V.Topology,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
            (typeof(Spatial.BrepArraySource), typeof(Spatial.RTreeBoundingBoxQuery),
                CreateRangeEntry(
                    factory: _brepTreeFactory,
                    shapeSelector: static query => query.BoundingBox,
                    executor: static (tree, box, buffer) => ExecuteRangeSearch(tree: tree, queryShape: box, bufferSize: buffer),
                    mode: V.Topology,
                    bufferSize: SpatialConfig.DefaultBufferSize)),
        }.ToFrozenDictionary(static entry => (entry.Source, entry.Query), static entry => entry.Entry);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<int>> Query(Spatial.RTreeQueryRequest request, IGeometryContext context) =>
        OperationRegistry.TryGetValue((request.Source.GetType(), request.Query.GetType()), out QueryOperationEntry entry)
            ? UnifiedOperation.Apply(
                input: request,
                operation: (Func<Spatial.RTreeQueryRequest, Result<IReadOnlyList<int>>>)(req =>
                    entry.Execute(req.Source, req.Query, context, req.BufferSize ?? entry.BufferSize)),
                config: new OperationConfig<Spatial.RTreeQueryRequest, int> {
                    Context = context,
                    ValidationMode = entry.Mode,
                    OperationName = $"Spatial.{request.Source.GetType().Name}.{request.Query.GetType().Name}",
                    EnableDiagnostics = false,
                })
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext(
                $"Source: {request.Source.GetType().Name}, Query: {request.Query.GetType().Name}"));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Spatial.ClusterResult[]> Cluster(Spatial.ClusteringRequest request, IGeometryContext context) {
        Result<IReadOnlyList<Spatial.ClusterResult>> result = UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.ClusteringRequest, Result<IReadOnlyList<Spatial.ClusterResult>>>)(req => req switch {
                Spatial.KMeansClusteringRequest kmeans => SpatialCompute.Cluster(
                        geometry: kmeans.Geometry,
                        algorithm: 0,
                        k: kmeans.ClusterCount,
                        epsilon: context.AbsoluteTolerance,
                        minPoints: SpatialConfig.DBSCANMinPoints,
                        context: context)
                    .Map(clusters => (IReadOnlyList<Spatial.ClusterResult>)[.. clusters.Select(static cluster =>
                        new Spatial.ClusterResult(cluster.Centroid, cluster.Radii)),]),
                Spatial.DBSCANClusteringRequest dbscan => SpatialCompute.Cluster(
                        geometry: dbscan.Geometry,
                        algorithm: 1,
                        k: dbscan.MinPoints,
                        epsilon: dbscan.Epsilon,
                        minPoints: dbscan.MinPoints,
                        context: context)
                    .Map(clusters => (IReadOnlyList<Spatial.ClusterResult>)[.. clusters.Select(static cluster =>
                        new Spatial.ClusterResult(cluster.Centroid, cluster.Radii)),]),
                Spatial.HierarchicalClusteringRequest hierarchical => SpatialCompute.Cluster(
                        geometry: hierarchical.Geometry,
                        algorithm: 2,
                        k: hierarchical.ClusterCount,
                        epsilon: context.AbsoluteTolerance,
                        minPoints: SpatialConfig.DBSCANMinPoints,
                        context: context)
                    .Map(clusters => (IReadOnlyList<Spatial.ClusterResult>)[.. clusters.Select(static cluster =>
                        new Spatial.ClusterResult(cluster.Centroid, cluster.Radii)),]),
                _ => ResultFactory.Create<IReadOnlyList<Spatial.ClusterResult>>(error: E.Spatial.ClusteringFailed),
            }),
            config: new OperationConfig<Spatial.ClusteringRequest, Spatial.ClusterResult> {
                Context = context,
                ValidationMode = V.Standard,
                OperationName = $"Spatial.Cluster.{request.GetType().Name}",
                EnableDiagnostics = false,
            });
        return result.Map(static list => list.ToArray());
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(Spatial.MedialAxisRequest request, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.MedialAxisRequest, Result<(Curve[], double[])>>)(req =>
                SpatialCompute.MedialAxis(brep: req.Brep, tolerance: req.Tolerance, context: context)),
            config: new OperationConfig<Spatial.MedialAxisRequest, (Curve[], double[])> {
                Context = context,
                ValidationMode = V.Topology,
                OperationName = "Spatial.MedialAxis",
                EnableDiagnostics = false,
            }).Map(static list => list[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Spatial.ProximitySample[]> ProximityField(Spatial.ProximityFieldRequest request, IGeometryContext context) {
        Result<IReadOnlyList<Spatial.ProximitySample>> result = UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.ProximityFieldRequest, Result<IReadOnlyList<Spatial.ProximitySample>>>)(req =>
                SpatialCompute.ProximityField(
                        geometry: req.Geometry,
                        direction: req.Direction,
                        maxDist: req.MaxDistance,
                        angleWeight: req.AngleWeight,
                        context: context)
                    .Map(samples => (IReadOnlyList<Spatial.ProximitySample>)[.. samples.Select(static sample =>
                        new Spatial.ProximitySample(sample.Index, sample.Distance, sample.Angle)),])),
            config: new OperationConfig<Spatial.ProximityFieldRequest, Spatial.ProximitySample> {
                Context = context,
                ValidationMode = V.None,
                OperationName = "Spatial.ProximityField",
                EnableDiagnostics = false,
            });
        return result.Map(static list => list.ToArray());
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<int[][]> ConvexHull3D(Spatial.ConvexHull3DRequest request, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.ConvexHull3DRequest, Result<int[][]>>)(req =>
                SpatialCompute.ConvexHull3D(points: req.Points, context: context)),
            config: new OperationConfig<Spatial.ConvexHull3DRequest, int[][]> {
                Context = context,
                ValidationMode = V.BoundingBox,
                OperationName = "Spatial.ConvexHull3D",
                EnableDiagnostics = false,
            }).Map(static list => list[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<int[][]> DelaunayTriangulation2D(Spatial.DelaunayTriangulationRequest request, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.DelaunayTriangulationRequest, Result<int[][]>>)(req =>
                SpatialCompute.DelaunayTriangulation2D(points: req.Points, context: context)),
            config: new OperationConfig<Spatial.DelaunayTriangulationRequest, int[][]> {
                Context = context,
                ValidationMode = V.None,
                OperationName = "Spatial.DelaunayTriangulation2D",
                EnableDiagnostics = false,
            }).Map(static list => list[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d[][]> VoronoiDiagram2D(Spatial.VoronoiDiagramRequest request, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.VoronoiDiagramRequest, Result<Point3d[][]>>)(req =>
                SpatialCompute.VoronoiDiagram2D(points: req.Points, context: context)),
            config: new OperationConfig<Spatial.VoronoiDiagramRequest, Point3d[][]> {
                Context = context,
                ValidationMode = V.None,
                OperationName = "Spatial.VoronoiDiagram2D",
                EnableDiagnostics = false,
            }).Map(static list => list[0]);

    private static QueryOperationEntry CreateRangeEntry<TSource, TQuery, TShape>(
        Func<TSource, RTree> factory,
        Func<TQuery, TShape> shapeSelector,
        Func<RTree, TShape, int, Result<IReadOnlyList<int>>> executor,
        V mode,
        int bufferSize) where TSource : Spatial.QuerySource where TQuery : Spatial.Query =>
        new QueryOperationEntry(
            Mode: mode,
            BufferSize: bufferSize,
            Execute: (source, query, context, buffer) => source is TSource typedSource && query is TQuery typedQuery
                ? ((Func<Result<IReadOnlyList<int>>>)(() => {
                    using RTree tree = factory(typedSource);
                    return executor(tree, shapeSelector(typedQuery), buffer);
                }))()
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo));

    private static QueryOperationEntry CreateProximityEntry<TSource>(
        Func<TSource, Point3d[], int, IEnumerable<int[]>> kNearest,
        Func<TSource, Point3d[], double, IEnumerable<int[]>> distanceLimited,
        V mode) where TSource : Spatial.QuerySource =>
        new QueryOperationEntry(
            Mode: mode,
            BufferSize: SpatialConfig.DefaultBufferSize,
            Execute: (source, query, _, _) => source is TSource typedSource
                ? query switch {
                    Spatial.KNearestQuery nearest => ExecuteProximitySearch(
                        source: typedSource,
                        needles: nearest.Needles,
                        limit: nearest.Count,
                        kNearest: kNearest,
                        distLimited: distanceLimited),
                    Spatial.DistanceThresholdQuery distance => ExecuteProximitySearch(
                        source: typedSource,
                        needles: distance.Needles,
                        limit: distance.Distance,
                        kNearest: kNearest,
                        distLimited: distanceLimited),
                    _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
                }
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo));

    private static QueryOperationEntry CreateMeshOverlapEntry(V mode, int bufferSize) =>
        new QueryOperationEntry(
            Mode: mode,
            BufferSize: bufferSize,
            Execute: (source, query, context, buffer) => source is Spatial.MeshPairSource meshes && query is Spatial.MeshOverlapQuery overlap
                ? ((Func<Result<IReadOnlyList<int>>>)(() => {
                    using RTree treeA = _meshTreeFactory(new Spatial.MeshSource(meshes.First));
                    using RTree treeB = _meshTreeFactory(new Spatial.MeshSource(meshes.Second));
                    return ExecuteOverlapSearch(tree1: treeA, tree2: treeB, tolerance: context.AbsoluteTolerance + overlap.ExtraTolerance, bufferSize: buffer);
                }))()
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteProximitySearch<TSource>(
        TSource source,
        Point3d[] needles,
        object limit,
        Func<TSource, Point3d[], int, IEnumerable<int[]>> kNearest,
        Func<TSource, Point3d[], double, IEnumerable<int[]>> distLimited) where TSource : Spatial.QuerySource =>
        limit switch {
            int k when k > 0 => kNearest(source, needles, k).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            double distance when distance > 0 => distLimited(source, needles, distance).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            int invalidK => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK.WithContext(invalidK.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            double invalidDistance => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance.WithContext(invalidDistance.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

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

    private readonly record struct QueryOperationEntry(
        V Mode,
        int BufferSize,
        Func<Spatial.QuerySource, Spatial.Query, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute);
}
