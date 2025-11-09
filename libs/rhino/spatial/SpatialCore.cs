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

/// <summary>RTree construction and spatial query execution with zero-allocation pooled buffers.</summary>
internal static class SpatialCore {
    private const int DefaultBufferSize = 2048;
    private const int LargeBufferSize = 4096;

    private static readonly Func<object, RTree> _pointArrayFactory = s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree();
    private static readonly Func<object, RTree> _pointCloudFactory = s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree();
    private static readonly Func<object, RTree> _meshFactory = s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree();
    private static readonly Func<object, RTree> _curveArrayFactory = s => BuildGeometryArrayTree((Curve[])s);
    private static readonly Func<object, RTree> _surfaceArrayFactory = s => BuildGeometryArrayTree((Surface[])s);
    private static readonly Func<object, RTree> _brepArrayFactory = s => BuildGeometryArrayTree((Brep[])s);

    /// <summary>Unified configuration mapping input/query type pairs to validation mode, buffer size, and execution strategy.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new Dictionary<(Type, Type), (V, int, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>>)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (V.None, DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (V.None, DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (V.None, DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (V.None, DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            [(typeof(PointCloud), typeof(Sphere))] = (V.Standard, DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory)),
            [(typeof(PointCloud), typeof(BoundingBox))] = (V.Standard, DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory)),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (V.Standard, DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (V.Standard, DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            [(typeof(Mesh), typeof(Sphere))] = (V.MeshSpecific, DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
            [(typeof(Mesh), typeof(BoundingBox))] = (V.MeshSpecific, DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
            [(typeof((Mesh, Mesh)), typeof(double))] = (V.MeshSpecific, LargeBufferSize, MakeMeshOverlapExecutor()),
            [(typeof(Curve[]), typeof(Sphere))] = (V.Degeneracy, DefaultBufferSize, MakeExecutor<Curve[]>(_curveArrayFactory)),
            [(typeof(Curve[]), typeof(BoundingBox))] = (V.Degeneracy, DefaultBufferSize, MakeExecutor<Curve[]>(_curveArrayFactory)),
            [(typeof(Surface[]), typeof(Sphere))] = (V.BoundingBox, DefaultBufferSize, MakeExecutor<Surface[]>(_surfaceArrayFactory)),
            [(typeof(Surface[]), typeof(BoundingBox))] = (V.BoundingBox, DefaultBufferSize, MakeExecutor<Surface[]>(_surfaceArrayFactory)),
            [(typeof(Brep[]), typeof(Sphere))] = (V.Topology, DefaultBufferSize, MakeExecutor<Brep[]>(_brepArrayFactory)),
            [(typeof(Brep[]), typeof(BoundingBox))] = (V.Topology, DefaultBufferSize, MakeExecutor<Brep[]>(_brepArrayFactory)),
        }.ToFrozenDictionary();

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeExecutor<TInput>(
        Func<object, RTree> factory,
        (Func<TInput, Point3d[], int, IEnumerable<int[]>>? kNearest, Func<TInput, Point3d[], double, IEnumerable<int[]>>? distLimited)? proximityFuncs = null
    ) where TInput : notnull =>
        proximityFuncs.HasValue
            ? (i, q, _, _) => q switch {
                (Point3d[] needles, int k) => ExecuteProximitySearch(source: (TInput)i, needles: needles, limit: k, kNearest: proximityFuncs.Value.kNearest!, distLimited: proximityFuncs.Value.distLimited!),
                (Point3d[] needles, double distance) => ExecuteProximitySearch(source: (TInput)i, needles: needles, limit: distance, kNearest: proximityFuncs.Value.kNearest!, distLimited: proximityFuncs.Value.distLimited!),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
            }
            : (i, q, _, b) => GetTree(source: (TInput)i, factory: factory).Bind(tree => ExecuteRangeSearch(tree: tree, queryShape: q, bufferSize: b));

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeMeshOverlapExecutor() =>
        (i, q, c, b) => i is (Mesh m1, Mesh m2) && q is double tolerance
            ? GetTree(source: m1, factory: _meshFactory).Bind(t1 => GetTree(source: m2, factory: _meshFactory).Bind(t2 => ExecuteOverlapSearch(tree1: t1, tree2: t2, tolerance: c.AbsoluteTolerance + tolerance, bufferSize: b)))
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo);

    /// <summary>Retrieves or constructs RTree for geometry with automatic caching using ConditionalWeakTable.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<RTree> GetTree<T>(T source, Func<object, RTree> factory) where T : notnull =>
        ResultFactory.Create(value: Spatial.TreeCache.GetValue(key: source, createValueCallback: _ => factory(source)));

    /// <summary>Constructs RTree from geometry array by inserting bounding boxes with index tracking.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }

    /// <summary>Executes RTree range search with sphere or bounding box query using ArrayPool for zero-allocation results.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, object queryShape, int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            _ = queryShape switch {
                Sphere sphere => tree.Search(sphere, (_, args) => { if (count < buffer.Length) { buffer[count++] = args.Id; } }),
                BoundingBox box => tree.Search(box, (_, args) => { if (count < buffer.Length) { buffer[count++] = args.Id; } }),
                _ => false,
            };
            return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Executes k-nearest or distance-limited proximity search using RTree algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteProximitySearch<T>(T source, Point3d[] needles, object limit, Func<T, Point3d[], int, IEnumerable<int[]>> kNearest, Func<T, Point3d[], double, IEnumerable<int[]>> distLimited) where T : notnull =>
        limit switch {
            int k when k > 0 => ResultFactory.Create<IReadOnlyList<int>>(value: [.. kNearest(source, needles, k).SelectMany(static indices => indices),]),
            double d when d > 0 => ResultFactory.Create<IReadOnlyList<int>>(value: [.. distLimited(source, needles, d).SelectMany(static indices => indices),]),
            int => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK),
            double => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

    /// <summary>Executes mesh overlap detection using RTree.SearchOverlaps with tolerance-aware double-tree algorithm.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteOverlapSearch(RTree tree1, RTree tree2, double tolerance, int bufferSize) {
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
    }
}
