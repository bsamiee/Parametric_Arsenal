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
    private static readonly FrozenDictionary<Type, Func<object, RTree>> _factories = new Dictionary<Type, Func<object, RTree>> {
        [typeof(Point3d[])] = static s => (RTree)SpatialConfig.TypeExtractors[("RTreeFactory", typeof(Point3d[]))](s),
        [typeof(PointCloud)] = static s => (RTree)SpatialConfig.TypeExtractors[("RTreeFactory", typeof(PointCloud))](s),
        [typeof(Mesh)] = static s => (RTree)SpatialConfig.TypeExtractors[("RTreeFactory", typeof(Mesh))](s),
        [typeof(Curve[])] = static s => BuildGeometryArrayTree((Curve[])s),
        [typeof(Surface[])] = static s => BuildGeometryArrayTree((Surface[])s),
        [typeof(Brep[])] = static s => BuildGeometryArrayTree((Brep[])s),
    }.ToFrozenDictionary();

    /// <summary>(Input, Query) type pairs to (Factory, Mode, BufferSize, Execute) mapping.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)> OperationRegistry =
        new (Type Input, Type Query, Func<object, RTree>? Factory, V Mode, int BufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)[] {
            (typeof(Point3d[]), typeof(Sphere), _factories[typeof(Point3d[])], V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_factories[typeof(Point3d[])])),
            (typeof(Point3d[]), typeof(BoundingBox), _factories[typeof(Point3d[])], V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_factories[typeof(Point3d[])])),
            (typeof(Point3d[]), typeof((Point3d[], int)), _factories[typeof(Point3d[])], V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_factories[typeof(Point3d[])], (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            (typeof(Point3d[]), typeof((Point3d[], double)), _factories[typeof(Point3d[])], V.None, SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_factories[typeof(Point3d[])], (RTree.Point3dKNeighbors, RTree.Point3dClosestPoints))),
            (typeof(PointCloud), typeof(Sphere), _factories[typeof(PointCloud)], V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_factories[typeof(PointCloud)])),
            (typeof(PointCloud), typeof(BoundingBox), _factories[typeof(PointCloud)], V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_factories[typeof(PointCloud)])),
            (typeof(PointCloud), typeof((Point3d[], int)), _factories[typeof(PointCloud)], V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_factories[typeof(PointCloud)], (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            (typeof(PointCloud), typeof((Point3d[], double)), _factories[typeof(PointCloud)], V.Standard, SpatialConfig.DefaultBufferSize, MakeExecutor<PointCloud>(_factories[typeof(PointCloud)], (RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints))),
            (typeof(Mesh), typeof(Sphere), _factories[typeof(Mesh)], V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_factories[typeof(Mesh)])),
            (typeof(Mesh), typeof(BoundingBox), _factories[typeof(Mesh)], V.MeshSpecific, SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_factories[typeof(Mesh)])),
            (typeof((Mesh, Mesh)), typeof(double), null, V.MeshSpecific, SpatialConfig.LargeBufferSize, MakeMeshOverlapExecutor()),
            (typeof(Curve[]), typeof(Sphere), _factories[typeof(Curve[])], V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<Curve[]>(_factories[typeof(Curve[])])),
            (typeof(Curve[]), typeof(BoundingBox), _factories[typeof(Curve[])], V.Degeneracy, SpatialConfig.DefaultBufferSize, MakeExecutor<Curve[]>(_factories[typeof(Curve[])])),
            (typeof(Surface[]), typeof(Sphere), _factories[typeof(Surface[])], V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeExecutor<Surface[]>(_factories[typeof(Surface[])])),
            (typeof(Surface[]), typeof(BoundingBox), _factories[typeof(Surface[])], V.BoundingBox, SpatialConfig.DefaultBufferSize, MakeExecutor<Surface[]>(_factories[typeof(Surface[])])),
            (typeof(Brep[]), typeof(Sphere), _factories[typeof(Brep[])], V.Topology, SpatialConfig.DefaultBufferSize, MakeExecutor<Brep[]>(_factories[typeof(Brep[])])),
            (typeof(Brep[]), typeof(BoundingBox), _factories[typeof(Brep[])], V.Topology, SpatialConfig.DefaultBufferSize, MakeExecutor<Brep[]>(_factories[typeof(Brep[])])),
        }.ToFrozenDictionary(static entry => (entry.Input, entry.Query), static entry => (entry.Factory, entry.Mode, entry.BufferSize, entry.Execute));

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
                using RTree tree1 = _factories[typeof(Mesh)](m1);
                using RTree tree2 = _factories[typeof(Mesh)](m2);
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
