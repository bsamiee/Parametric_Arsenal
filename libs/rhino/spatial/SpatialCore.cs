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

    /// <summary>Unified configuration mapping input/query type pairs to validation mode, buffer size, and execution strategy.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new Dictionary<(Type, Type), (Func<object, RTree>?, V, int, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>>)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (null, V.None, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Point3d[])i, static s => RTree.CreateFromPointArray((IEnumerable<Point3d>)s) ?? new())).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (null, V.None, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Point3d[])i, static s => RTree.CreateFromPointArray((IEnumerable<Point3d>)s) ?? new())).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (null, V.None, DefaultBufferSize, (i, q, _, _) => q is (Point3d[] needles, int k) ? ExecuteProximitySearch((Point3d[])i, needles, k, RTree.Point3dKNeighbors, RTree.Point3dClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (null, V.None, DefaultBufferSize, (i, q, _, _) => q is (Point3d[] needles, double d) ? ExecuteProximitySearch((Point3d[])i, needles, d, RTree.Point3dKNeighbors, RTree.Point3dClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(PointCloud), typeof(Sphere))] = (null, V.Standard, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((PointCloud)i, static s => RTree.CreatePointCloudTree((PointCloud)s) ?? new())).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(PointCloud), typeof(BoundingBox))] = (null, V.Standard, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((PointCloud)i, static s => RTree.CreatePointCloudTree((PointCloud)s) ?? new())).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (null, V.Standard, DefaultBufferSize, (i, q, _, _) => q is (Point3d[] needles, int k) ? ExecuteProximitySearch((PointCloud)i, needles, k, RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (null, V.Standard, DefaultBufferSize, (i, q, _, _) => q is (Point3d[] needles, double d) ? ExecuteProximitySearch((PointCloud)i, needles, d, RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(Mesh), typeof(Sphere))] = (null, V.MeshSpecific, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Mesh)i, static s => RTree.CreateMeshFaceTree((Mesh)s) ?? new())).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Mesh), typeof(BoundingBox))] = (null, V.MeshSpecific, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Mesh)i, static s => RTree.CreateMeshFaceTree((Mesh)s) ?? new())).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof((Mesh, Mesh)), typeof(double))] = (null, V.MeshSpecific, LargeBufferSize, (i, q, c, b) => i is (Mesh m1, Mesh m2) && q is double tol ? ResultFactory.Create(value: Spatial.TreeCache.GetValue(m1, static s => RTree.CreateMeshFaceTree((Mesh)s) ?? new())).Bind(t1 => ResultFactory.Create(value: Spatial.TreeCache.GetValue(m2, static s => RTree.CreateMeshFaceTree((Mesh)s) ?? new())).Bind(t2 => ExecuteOverlapSearch(t1, t2, c.AbsoluteTolerance + tol, b))) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)),
            [(typeof(Curve[]), typeof(Sphere))] = (null, V.Degeneracy, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Curve[])i, static s => BuildGeometryArrayTree<Curve>((Curve[])s))).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Curve[]), typeof(BoundingBox))] = (null, V.Degeneracy, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Curve[])i, static s => BuildGeometryArrayTree<Curve>((Curve[])s))).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Surface[]), typeof(Sphere))] = (null, V.BoundingBox, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Surface[])i, static s => BuildGeometryArrayTree<Surface>((Surface[])s))).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Surface[]), typeof(BoundingBox))] = (null, V.BoundingBox, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Surface[])i, static s => BuildGeometryArrayTree<Surface>((Surface[])s))).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Brep[]), typeof(Sphere))] = (null, V.Topology, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Brep[])i, static s => BuildGeometryArrayTree<Brep>((Brep[])s))).Bind(t => ExecuteRangeSearch(t, q, b))),
            [(typeof(Brep[]), typeof(BoundingBox))] = (null, V.Topology, DefaultBufferSize, (i, q, _, b) => ResultFactory.Create(value: Spatial.TreeCache.GetValue((Brep[])i, static s => BuildGeometryArrayTree<Brep>((Brep[])s))).Bind(t => ExecuteRangeSearch(t, q, b))),
        }.ToFrozenDictionary();

    /// <summary>Constructs RTree from geometry array by inserting bounding boxes with index tracking.</summary>
    [Pure]
    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }

    /// <summary>Executes RTree range search with sphere or bounding box query using ArrayPool for zero-allocation results.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteRangeSearch(RTree tree, object queryShape, int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            return queryShape switch {
                Sphere s => tree.Search(s, (_, args) => { if (count < buffer.Length) { buffer[count++] = args.Id; } }),
                BoundingBox b => tree.Search(b, (_, args) => { if (count < buffer.Length) { buffer[count++] = args.Id; } }),
                _ => false,
            }
                ? ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : [])
                : ResultFactory.Create<IReadOnlyList<int>>(value: []);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Executes k-nearest or distance-limited proximity search using RTree algorithms.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteProximitySearch<T>(T source, Point3d[] needles, object limit, Func<T, Point3d[], int, IEnumerable<int[]>> kNearest, Func<T, Point3d[], double, IEnumerable<int[]>> distLimited) where T : notnull =>
        limit switch {
            int k when k > 0 => ResultFactory.Create<IReadOnlyList<int>>(value: [.. kNearest(source, needles, k).SelectMany(static indices => indices),]),
            double d when d > 0 => ResultFactory.Create<IReadOnlyList<int>>(value: [.. distLimited(source, needles, d).SelectMany(static indices => indices),]),
            int => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK),
            double => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
        };

    /// <summary>Executes mesh overlap detection using RTree.SearchOverlaps with tolerance-aware double-tree algorithm.</summary>
    [Pure]
    private static Result<IReadOnlyList<int>> ExecuteOverlapSearch(RTree tree1, RTree tree2, double tolerance, int bufferSize) {
        int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
        int count = 0;
        try {
            return RTree.SearchOverlaps(tree1, tree2, tolerance, (_, args) => {
                if (count + 1 < buffer.Length) {
                    (buffer[count++], buffer[count++]) = (args.Id, args.IdB);
                }
            }) ? ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []) : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed);
        } finally {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }
}
