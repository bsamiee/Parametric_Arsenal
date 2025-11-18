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
    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new (Type Input, Type Query, Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)[] {
            (typeof(Point3d[]), typeof(Sphere), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
            (typeof(Point3d[]), typeof(BoundingBox), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
            (typeof(Point3d[]), typeof((Point3d[], int)), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            (typeof(Point3d[]), typeof((Point3d[], double)), _pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            (typeof(PointCloud), typeof(Sphere), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory)),
            (typeof(PointCloud), typeof(BoundingBox), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory)),
            (typeof(PointCloud), typeof((Point3d[], int)), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            (typeof(PointCloud), typeof((Point3d[], double)), _pointCloudFactory, V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            (typeof(Mesh), typeof(Sphere), _meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
            (typeof(Mesh), typeof(BoundingBox), _meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
            (typeof((Mesh, Mesh)), typeof(double), null, V.MeshSpecific, SpatialConfig.LargeBufferSize, MakeMeshOverlapExecutor()),
            (typeof(Curve[]), typeof(Sphere), _curveArrayFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<Curve[]>(_curveArrayFactory)),
            (typeof(Curve[]), typeof(BoundingBox), _curveArrayFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<Curve[]>(_curveArrayFactory)),
            (typeof(Surface[]), typeof(Sphere), _surfaceArrayFactory, V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeExecutor<Surface[]>(_surfaceArrayFactory)),
            (typeof(Surface[]), typeof(BoundingBox), _surfaceArrayFactory, V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeExecutor<Surface[]>(_surfaceArrayFactory)),
            (typeof(Brep[]), typeof(Sphere), _brepArrayFactory, V.Topology, SpatialConfig.DefaultBufferSize, MakeExecutor<Brep[]>(_brepArrayFactory)),
            (typeof(Brep[]), typeof(BoundingBox), _brepArrayFactory, V.Topology, SpatialConfig.DefaultBufferSize, MakeExecutor<Brep[]>(_brepArrayFactory)),
        }.ToFrozenDictionary(static entry => (entry.Input, entry.Query), static entry => (entry.Factory, entry.Mode, entry.BufferSize, entry.Execute));

    /// <summary>Orchestrate clustering operation via algebraic request dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Spatial.ClusteringResult[]> Cluster<T>(T[] geometry, Spatial.ClusteringRequest request, IGeometryContext context) where T : GeometryBase =>
        request switch {
            Spatial.KMeansClusteringRequest { K: <= 0 } => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.InvalidClusterK),
            Spatial.HierarchicalClusteringRequest { K: <= 0 } => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.InvalidClusterK),
            Spatial.DBSCANClusteringRequest { Epsilon: <= 0 } => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.InvalidEpsilon),
            Spatial.KMeansClusteringRequest kmeans => SpatialCompute.ClusterKMeans(geometry: geometry, k: kmeans.K, context: context)
                .Map(static tuples => tuples.Select(static t => new Spatial.ClusteringResult(t.Centroid, t.Radii)).ToArray()),
            Spatial.DBSCANClusteringRequest dbscan => SpatialCompute.ClusterDBSCAN(geometry: geometry, epsilon: dbscan.Epsilon, context: context)
                .Map(static tuples => tuples.Select(static t => new Spatial.ClusteringResult(t.Centroid, t.Radii)).ToArray()),
            Spatial.HierarchicalClusteringRequest hierarchical => SpatialCompute.ClusterHierarchical(geometry: geometry, k: hierarchical.K, context: context)
                .Map(static tuples => tuples.Select(static t => new Spatial.ClusteringResult(t.Centroid, t.Radii)).ToArray()),
            _ => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.ClusteringFailed.WithContext($"Unknown clustering request type: {request.GetType().Name}")),
        };

    /// <summary>Orchestrate proximity field operation via algebraic request dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Spatial.ProximityFieldResult[]> ProximityField(GeometryBase[] geometry, Spatial.DirectionalProximityRequest request, IGeometryContext context) =>
        SpatialCompute.ProximityField(geometry: geometry, direction: request.Direction, maxDist: request.MaxDistance, angleWeight: request.AngleWeight, context: context)
            .Map(static tuples => tuples.Select(static t => new Spatial.ProximityFieldResult(t.Index, t.Distance, t.Angle)).ToArray());

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeExecutor<TInput>(
        Func<object, RTree> factory,
        (Func<TInput, Point3d[], int, IEnumerable<int[]>>? kNearest, Func<TInput, Point3d[], double, IEnumerable<int[]>>? distLimited)? proximityFuncs = null
    ) where TInput : notnull =>
        proximityFuncs is (Func<TInput, Point3d[], int, IEnumerable<int[]>> nearest, Func<TInput, Point3d[], double, IEnumerable<int[]>> limited)
            ? (i, q, _, _) => q switch {
                (Point3d[] needles, int countLimit) => ExecuteProximitySearch(source: (TInput)i, needles: needles, limit: countLimit, kNearest: nearest, distLimited: limited),
                (Point3d[] needles, double distanceLimit) => ExecuteProximitySearch(source: (TInput)i, needles: needles, limit: distanceLimit, kNearest: nearest, distLimited: limited),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
            }
            : (i, q, _, b) => {
                using RTree tree = factory(i);
                return ExecuteRangeSearch(tree: tree, queryShape: q, bufferSize: b);
            };

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeMeshOverlapExecutor() =>
        (i, q, c, b) => i is (Mesh m1, Mesh m2) && q is double tolerance
            ? ((Func<Result<IReadOnlyList<int>>>)(() => {
                using RTree tree1 = _meshFactory(m1);
                using RTree tree2 = _meshFactory(m2);
                return ExecuteOverlapSearch(tree1: tree1, tree2: tree2, tolerance: c.AbsoluteTolerance + tolerance, bufferSize: b);
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
