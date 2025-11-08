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
    private static readonly Func<object, RTree> _pointArrayFactory = s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree();
    private static readonly Func<object, RTree> _pointCloudFactory = s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree();
    private static readonly Func<object, RTree> _meshFactory = s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree();
    private static readonly Func<object, RTree> _curveArrayFactory = s => BuildGeometryArrayTree((Curve[])s);
    private static readonly Func<object, RTree> _surfaceArrayFactory = s => BuildGeometryArrayTree((Surface[])s);
    private static readonly Func<object, RTree> _brepArrayFactory = s => BuildGeometryArrayTree((Brep[])s);

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeExecutor<TInput>(
        Func<object, RTree> factory,
        (Func<TInput, Point3d[], int, IEnumerable<int[]>>? kNearest, Func<TInput, Point3d[], double, IEnumerable<int[]>>? distLimited)? proximityFuncs = null
    ) where TInput : notnull =>
        proximityFuncs.HasValue
            ? (i, q, _, _) => q switch {
                ValueTuple<Point3d[], int>(Point3d[] needles, int k) => ExecuteProximitySearch((TInput)i, needles, k, proximityFuncs.Value.kNearest!, proximityFuncs.Value.distLimited!),
                ValueTuple<Point3d[], double>(Point3d[] needles, double distance) => ExecuteProximitySearch((TInput)i, needles, distance, proximityFuncs.Value.kNearest!, proximityFuncs.Value.distLimited!),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
            }
            : (i, q, _, b) => GetTree((TInput)i, factory).Bind(tree => ExecuteRangeSearch(tree, q, b));

    /// <summary>Unified configuration mapping input/query type pairs to tree factory, validation mode, buffer size, and execution strategy.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), OperationDescriptor> OperationRegistry =
        new Dictionary<(Type, Type), OperationDescriptor> {
            [(typeof(Point3d[]), typeof(Sphere))] = new(_pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
            [(typeof(Point3d[]), typeof(BoundingBox))] = new(_pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = new(_pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = new(_pointArrayFactory, V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            [(typeof(PointCloud), typeof(Sphere))] = new(_pointCloudFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory)),
            [(typeof(PointCloud), typeof(BoundingBox))] = new(_pointCloudFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory)),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = new(_pointCloudFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = new(_pointCloudFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_pointCloudFactory, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            [(typeof(Mesh), typeof(Sphere))] = new(_meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
            [(typeof(Mesh), typeof(BoundingBox))] = new(_meshFactory, V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = new(null, V.MeshSpecific, SpatialConfig.LargeBufferSize,
                (i, q, c, b) => (i, q) switch {
                    (ValueTuple<Mesh, Mesh>(Mesh m1, Mesh m2), double tolerance) => GetTree(m1, _meshFactory).Bind(t1 => GetTree(m2, _meshFactory).Bind(t2 => ExecuteOverlapSearch(t1, t2, c.AbsoluteTolerance + tolerance, b))),
                    _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
                }),
            [(typeof(Curve[]), typeof(Sphere))] = new(_curveArrayFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<Curve[]>(_curveArrayFactory)),
            [(typeof(Curve[]), typeof(BoundingBox))] = new(_curveArrayFactory, V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<Curve[]>(_curveArrayFactory)),
            [(typeof(Surface[]), typeof(Sphere))] = new(_surfaceArrayFactory, V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeExecutor<Surface[]>(_surfaceArrayFactory)),
            [(typeof(Surface[]), typeof(BoundingBox))] = new(_surfaceArrayFactory, V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeExecutor<Surface[]>(_surfaceArrayFactory)),
            [(typeof(Brep[]), typeof(Sphere))] = new(_brepArrayFactory, V.Topology, SpatialConfig.DefaultBufferSize, MakeExecutor<Brep[]>(_brepArrayFactory)),
            [(typeof(Brep[]), typeof(BoundingBox))] = new(_brepArrayFactory, V.Topology, SpatialConfig.DefaultBufferSize, MakeExecutor<Brep[]>(_brepArrayFactory)),
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
