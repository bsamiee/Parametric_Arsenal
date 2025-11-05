using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial algorithm implementations with RhinoCommon RTree SDK integration and tolerance-aware geometry processing.</summary>
internal static class SpatialStrategies {
    /// <summary>RTree cache using weak references for automatic memory management and tree reuse optimization.</summary>
    private static readonly ConditionalWeakTable<object, RTree> _treeCache = [];

    /// <summary>Spatial algorithm configuration mapping methods to validation modes, tree factories, and buffer allocation strategies.</summary>
    private static readonly FrozenDictionary<(SpatialMethod, Type), (ValidationMode Mode, Func<object, RTree?>? TreeFactory, int BufferSize, bool RequiresDoubleBuffer)> _spatialConfig =
        new Dictionary<(SpatialMethod, Type), (ValidationMode, Func<object, RTree?>?, int, bool)> {
            [(SpatialMethod.PointsRange, typeof(Point3d[]))] = (ValidationMode.Standard, s => RTree.CreateFromPointArray((Point3d[])s), 2048, false),
            [(SpatialMethod.PointsProximity, typeof(Point3d[]))] = (ValidationMode.Standard, null, 2048, true),
            [(SpatialMethod.PointCloudRange, typeof(PointCloud))] = (ValidationMode.Standard | ValidationMode.Degeneracy, s => RTree.CreatePointCloudTree((PointCloud)s), 2048, false),
            [(SpatialMethod.PointCloudProximity, typeof(PointCloud))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null, 2048, true),
            [(SpatialMethod.MeshRange, typeof(Mesh))] = (ValidationMode.MeshSpecific, s => RTree.CreateMeshFaceTree((Mesh)s), 2048, false),
            [(SpatialMethod.MeshOverlap, typeof(Mesh))] = (ValidationMode.MeshSpecific, null, 4096, true),
            [(SpatialMethod.MeshOverlap, typeof(ValueTuple<Mesh, Mesh>))] = (ValidationMode.MeshSpecific, null, 4096, true),
            [(SpatialMethod.CurveRange, typeof(Curve[]))] = (ValidationMode.Standard | ValidationMode.Degeneracy, s => _treeCache.GetValue(s, _ => { RTree t = new(); _ = ((Curve[])s).Select((c, i) => (t.Insert(c.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }), 2048, false),
            [(SpatialMethod.SurfaceRange, typeof(Surface[]))] = (ValidationMode.Standard | ValidationMode.BoundingBox, s => _treeCache.GetValue(s, _ => { RTree t = new(); _ = ((Surface[])s).Select((surf, i) => (t.Insert(surf.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }), 2048, false),
            [(SpatialMethod.BrepRange, typeof(Brep[]))] = (ValidationMode.Standard | ValidationMode.Topology, s => _treeCache.GetValue(s, _ => { RTree t = new(); _ = ((Brep[])s).Select((b, i) => (t.Insert(b.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }), 2048, false),
        }.ToFrozenDictionary();

    /// <summary>Dispatches spatial indexing operations with parameter validation and tolerance-aware error handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<int>> Index(
        object source,
        SpatialMethod method,
        IGeometryContext context,
        object? queryShape,
        IEnumerable<Point3d>? needles,
        int? k,
        double? limitDistance,
        double? toleranceBuffer = null) =>
        (method, source, needles, k, limitDistance) switch {
            (SpatialMethod m, _, _, int kVal, _) when (m & (SpatialMethod.PointsProximity | SpatialMethod.PointCloudProximity)) != SpatialMethod.None && kVal <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidCount),
            (SpatialMethod m, _, _, _, double dist) when (m & (SpatialMethod.PointsProximity | SpatialMethod.PointCloudProximity)) != SpatialMethod.None && dist <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidDistance),
            (SpatialMethod m, _, null, _, _) when (m & (SpatialMethod.PointsProximity | SpatialMethod.PointCloudProximity)) != SpatialMethod.None =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidNeedles),
            _ => _spatialConfig.TryGetValue((method, source switch { GeometryBase g => g.GetType(), Point3d[] => typeof(Point3d[]), _ => source.GetType() }), out (ValidationMode Mode, Func<object, RTree?>? TreeFactory, int BufferSize, bool RequiresDoubleBuffer) config) switch {
                true => ResultFactory.Create(value: source)
                    .Validate(args: [context, config.Mode])
                    .Map(_ => IndexCore(source, method, context, queryShape, needles, k, limitDistance, toleranceBuffer) ?? [])
                    .Map(result => (IReadOnlyList<int>)result.AsReadOnly()),
                false => ResultFactory.Create(value: (IReadOnlyList<int>)(IndexCore(source, method, context, queryShape, needles, k, limitDistance, toleranceBuffer) ?? []).AsReadOnly()),
            },
        };

    /// <summary>Core spatial indexing algorithms with RhinoCommon RTree operations and tolerance buffer handling.</summary>
    [Pure]
    private static int[]? IndexCore(
        object source,
        SpatialMethod method,
        IGeometryContext context,
        object? queryShape,
        IEnumerable<Point3d>? needles,
        int? k,
        double? limitDistance,
        double? toleranceBuffer = null) {
        // Tolerance-aware query shape transformation with buffer expansion for spatial accuracy
        ((ValidationMode Mode, Func<object, RTree?>? TreeFactory, int BufferSize, bool RequiresDoubleBuffer) config, object queryTransform) = (_spatialConfig.GetValueOrDefault((method, source.GetType())),
            (queryShape, toleranceBuffer, context.AbsoluteTolerance) switch {
                (Sphere { Center: Point3d c, Radius: double r }, double buf, _) => new Sphere(c, r + buf),
                (BoundingBox { Min: Point3d min, Max: Point3d max }, double buf, _) => new BoundingBox(min - new Vector3d(buf, buf, buf), max + new Vector3d(buf, buf, buf)),
                (Point3d point, _, double tol) => new Sphere(point, tol + (toleranceBuffer ?? 0)),
                (GeometryBase geom, double buf, _) => geom.GetBoundingBox(accurate: true) switch {
                    BoundingBox bbox => new BoundingBox(bbox.Min - new Vector3d(buf, buf, buf), bbox.Max + new Vector3d(buf, buf, buf)),
                },
                _ => queryShape ?? new Sphere(Point3d.Origin, context.AbsoluteTolerance),
            });

        return (method, source, needles, k, limitDistance) switch {
            (SpatialMethod.PointsProximity, Point3d[] pts, IEnumerable<Point3d> n, int kVal, _) =>
                RTree.Point3dKNeighbors(pts, n, kVal)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            (SpatialMethod.PointsProximity, Point3d[] pts, IEnumerable<Point3d> n, _, double lim) =>
                RTree.Point3dClosestPoints(pts, n, lim)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            (SpatialMethod.PointCloudProximity, PointCloud cloud, IEnumerable<Point3d> n, int kVal, _) =>
                RTree.PointCloudKNeighbors(cloud, n, kVal)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            (SpatialMethod.PointCloudProximity, PointCloud cloud, IEnumerable<Point3d> n, _, double lim) =>
                RTree.PointCloudClosestPoints(cloud, n, lim)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            // Mesh overlap detection using cached RTree face trees with tolerance-aware SearchOverlaps algorithm
            (SpatialMethod.MeshOverlap, ValueTuple<Mesh, Mesh> meshes, _, _, _) => ArrayPool<int>.Shared.Rent(config.BufferSize) switch {
                int[] buffer => ((Func<int[]>)(() => {
                    (RTree t1, RTree t2, int count) = (_treeCache.GetValue(meshes.Item1, static m => RTree.CreateMeshFaceTree((Mesh)m)!),
                                            _treeCache.GetValue(meshes.Item2, static m => RTree.CreateMeshFaceTree((Mesh)m)!), 0);
                    try {
                        _ = RTree.SearchOverlaps(t1, t2, context.AbsoluteTolerance + (toleranceBuffer ?? 0),
                            (_, args) => count = count + 1 < buffer.Length ? ((buffer[count], buffer[count + 1]) = (args.Id, args.IdB), count += 2).Item2 : count);
                        return count > 0 ? [.. buffer[..count]] : [];
                    } finally { ArrayPool<int>.Shared.Return(buffer, clearArray: true); }
                }))(),
            },
            // General RTree search using cached trees with sphere/bounding box query shape dispatch
            _ when config.TreeFactory is not null => ArrayPool<int>.Shared.Rent(config.BufferSize) switch {
                int[] buffer => ((Func<int[]>)(() => {
                    (RTree tree, int count) = (_treeCache.GetValue(source, _ => config.TreeFactory!(source)!) ?? RTree.CreateFromPointArray([]), 0);
                    try {
                        (queryTransform switch {
                            Sphere sphere => (Action)(() => tree.Search(sphere, (_, args) => count = count < buffer.Length ? (buffer[count] = args.Id, count + 1).Item2 : count)),
                            BoundingBox box => () => tree.Search(box, (_, args) => count = count < buffer.Length ? (buffer[count] = args.Id, count + 1).Item2 : count),
                            _ => () => { }
                            ,
                        })();
                        return count > 0 ? [.. buffer[..count]] : [];
                    } finally { ArrayPool<int>.Shared.Return(buffer, clearArray: true); }
                }))(),
            },
            _ => [],
        };
    }
}
