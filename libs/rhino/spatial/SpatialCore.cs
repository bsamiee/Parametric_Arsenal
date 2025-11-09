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
    private static readonly Func<object, RTree> _pointArray = static s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree();
    private static readonly Func<object, RTree> _pointCloud = static s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree();
    private static readonly Func<object, RTree> _mesh = static s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree();
    private static readonly Func<object, RTree> _curve = static s => BuildGeometryArrayTree((Curve[])s);
    private static readonly Func<object, RTree> _surface = static s => BuildGeometryArrayTree((Surface[])s);
    private static readonly Func<object, RTree> _brep = static s => BuildGeometryArrayTree((Brep[])s);

    /// <summary>Unified configuration mapping input/query type pairs to tree factory, validation mode, buffer size, and execution strategy.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new Dictionary<(Type, Type), (Func<object, RTree>?, V, int, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>>)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (_pointArray, V.None, 2048, MakeExecutor<Point3d[]>(_pointArray)),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (_pointArray, V.None, 2048, MakeExecutor<Point3d[]>(_pointArray)),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (_pointArray, V.None, 2048, MakeExecutor<Point3d[]>(_pointArray, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (_pointArray, V.None, 2048, MakeExecutor<Point3d[]>(_pointArray, (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            [(typeof(PointCloud), typeof(Sphere))] = (_pointCloud, V.Standard, 2048, MakeExecutor<PointCloud>(_pointCloud)),
            [(typeof(PointCloud), typeof(BoundingBox))] = (_pointCloud, V.Standard, 2048, MakeExecutor<PointCloud>(_pointCloud)),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (_pointCloud, V.Standard, 2048, MakeExecutor<PointCloud>(_pointCloud, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (_pointCloud, V.Standard, 2048, MakeExecutor<PointCloud>(_pointCloud, (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            [(typeof(Mesh), typeof(Sphere))] = (_mesh, V.MeshSpecific, 2048, MakeExecutor<Mesh>(_mesh)),
            [(typeof(Mesh), typeof(BoundingBox))] = (_mesh, V.MeshSpecific, 2048, MakeExecutor<Mesh>(_mesh)),
            [(typeof((Mesh, Mesh)), typeof(double))] = (null, V.MeshSpecific, 4096, MakeMeshOverlapExecutor(_mesh)),
            [(typeof(Curve[]), typeof(Sphere))] = (_curve, V.Degeneracy, 2048, MakeExecutor<Curve[]>(_curve)),
            [(typeof(Curve[]), typeof(BoundingBox))] = (_curve, V.Degeneracy, 2048, MakeExecutor<Curve[]>(_curve)),
            [(typeof(Surface[]), typeof(Sphere))] = (_surface, V.BoundingBox, 2048, MakeExecutor<Surface[]>(_surface)),
            [(typeof(Surface[]), typeof(BoundingBox))] = (_surface, V.BoundingBox, 2048, MakeExecutor<Surface[]>(_surface)),
            [(typeof(Brep[]), typeof(Sphere))] = (_brep, V.Topology, 2048, MakeExecutor<Brep[]>(_brep)),
            [(typeof(Brep[]), typeof(BoundingBox))] = (_brep, V.Topology, 2048, MakeExecutor<Brep[]>(_brep)),
        }.ToFrozenDictionary();

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeExecutor<TInput>(
        Func<object, RTree> factory,
        (Func<TInput, Point3d[], int, IEnumerable<int[]>>? kNearest, Func<TInput, Point3d[], double, IEnumerable<int[]>>? distLimited)? proximityFuncs = null
    ) where TInput : notnull =>
        proximityFuncs.HasValue
            ? (i, q, _, _) => (q, proximityFuncs.Value) switch {
                ((Point3d[] needles, int k), var (kn, _)) when k > 0 && kn!((TInput)i, needles, k).ToArray() is int[][] r1 =>
                    ResultFactory.Create<IReadOnlyList<int>>(value: [.. r1.SelectMany(static indices => indices),]),
                ((Point3d[] needles, double d), var (_, dl)) when d > 0 && dl!((TInput)i, needles, d).ToArray() is int[][] r2 =>
                    ResultFactory.Create<IReadOnlyList<int>>(value: [.. r2.SelectMany(static indices => indices),]),
                ((Point3d[], int), _) => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK),
                ((Point3d[], double), _) => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            }
            : (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue(key: (TInput)i, createValueCallback: _ => factory(i))).Bind(tree => ExecuteRangeSearch(tree: tree, queryShape: q, bufferSize: b));

    private static Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> MakeMeshOverlapExecutor(Func<object, RTree> meshFactory) =>
        (i, q, c, b) => i is (Mesh m1, Mesh m2) && q is double tolerance
            ? ResultFactory.Create(value: Spatial.TreeCache.GetValue(key: m1, createValueCallback: _ => meshFactory(m1)))
                .Bind(t1 => ResultFactory.Create(value: Spatial.TreeCache.GetValue(key: m2, createValueCallback: _ => meshFactory(m2)))
                    .Bind(t2 => ExecuteOverlapSearch(tree1: t1, tree2: t2, tolerance: c.AbsoluteTolerance + tolerance, bufferSize: b)))
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo);

    /// <summary>Constructs RTree from geometry array by inserting bounding boxes with index tracking.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) { _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i); }
        return tree;
    }

    /// <summary>Executes RTree range search with sphere or bounding box query using ArrayPool for zero-allocation results.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, object queryShape, int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            void Callback(object? _, RTreeEventArgs args) { if (count < buffer.Length) { buffer[count++] = args.Id; } }
            _ = queryShape switch {
                Sphere s => tree.Search(s, Callback),
                BoundingBox b => tree.Search(b, Callback),
                _ => false,
            };
            return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Executes mesh overlap detection using RTree.SearchOverlaps with tolerance-aware double-tree algorithm.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<int>> ExecuteOverlapSearch(RTree tree1, RTree tree2, double tolerance, int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            return RTree.SearchOverlaps(tree1, tree2, tolerance, (_, args) => { if (count + 1 < buffer.Length) { buffer[count++] = args.Id; buffer[count++] = args.IdB; } })
                ? ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : [])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }
}
