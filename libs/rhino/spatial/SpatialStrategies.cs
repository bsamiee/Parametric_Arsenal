using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Internal spatial algorithms with geometry-specific strategy dispatch and RhinoCommon RTree SDK integration.</summary>
internal static class SpatialStrategies {
    /// <summary>RTree cache with weak references for automatic memory management.</summary>
    private static readonly ConditionalWeakTable<object, RTree> _treeCache = new();

    /// <summary>Unified spatial configuration with advanced tuple-based validation modes, tree factories, and buffer allocation strategies.</summary>
    private static readonly FrozenDictionary<(SpatialMethod, Type), (ValidationMode Mode, Func<object, RTree?>? TreeFactory, int BufferSize, bool RequiresDoubleBuffer)> _spatialConfig =
        new Dictionary<(SpatialMethod, Type), (ValidationMode, Func<object, RTree?>?, int, bool)> {
            [(SpatialMethod.PointsRange, typeof(Point3d[]))] = (ValidationMode.Standard, s => RTree.CreateFromPointArray((Point3d[])s), 2048, false),
            [(SpatialMethod.PointsProximity, typeof(Point3d[]))] = (ValidationMode.Standard, null, 2048, true),
            [(SpatialMethod.PointCloudRange, typeof(PointCloud))] = (ValidationMode.Standard | ValidationMode.Degeneracy, s => RTree.CreatePointCloudTree((PointCloud)s), 2048, false),
            [(SpatialMethod.PointCloudProximity, typeof(PointCloud))] = (ValidationMode.Standard | ValidationMode.Degeneracy, null, 2048, true),
            [(SpatialMethod.MeshRange, typeof(Mesh))] = (ValidationMode.MeshSpecific, s => RTree.CreateMeshFaceTree((Mesh)s), 2048, false),
            [(SpatialMethod.MeshOverlap, typeof(Mesh))] = (ValidationMode.MeshSpecific, null, 4096, true),
            [(SpatialMethod.MeshOverlap, typeof(ValueTuple<Mesh, Mesh>))] = (ValidationMode.MeshSpecific, null, 4096, true),
            [(SpatialMethod.CurveRange, typeof(Curve[]))] = (ValidationMode.Standard | ValidationMode.Degeneracy, s => _treeCache.GetValue(s, _ => { var t = new RTree(); _ = ((Curve[])s).Select((c, i) => (t.Insert(c.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }), 2048, false),
            [(SpatialMethod.SurfaceRange, typeof(Surface[]))] = (ValidationMode.Standard | ValidationMode.BoundingBox, s => _treeCache.GetValue(s, _ => { var t = new RTree(); _ = ((Surface[])s).Select((surf, i) => (t.Insert(surf.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }), 2048, false),
            [(SpatialMethod.BrepRange, typeof(Brep[]))] = (ValidationMode.Standard | ValidationMode.Topology, s => _treeCache.GetValue(s, _ => { var t = new RTree(); _ = ((Brep[])s).Select((b, i) => (t.Insert(b.GetBoundingBox(accurate: true), i), 0).Item2).ToArray(); return t; }), 2048, false),
        }.ToFrozenDictionary();

    /// <summary>Dispatches spatial methods with direct validation and automatic null-to-error mapping.</summary>
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
            (var m, _, _, { } kVal, _) when (m & (SpatialMethod.PointsProximity | SpatialMethod.PointCloudProximity)) != SpatialMethod.None && kVal <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidCount),
            (var m, _, _, _, { } dist) when (m & (SpatialMethod.PointsProximity | SpatialMethod.PointCloudProximity)) != SpatialMethod.None && dist <= 0 =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidDistance),
            (var m, _, null, _, _) when (m & (SpatialMethod.PointsProximity | SpatialMethod.PointCloudProximity)) != SpatialMethod.None =>
                ResultFactory.Create<IReadOnlyList<int>>(error: SpatialErrors.Parameters.InvalidNeedles),
            _ => _spatialConfig.TryGetValue((method, source switch { GeometryBase g => g.GetType(), Point3d[] => typeof(Point3d[]), _ => source.GetType() }), out var config) switch {
                true => ResultFactory.Create(value: source)
                    .Validate(args: [context, config.Mode])
                    .Map(_ => IndexCore(source, method, context, queryShape, needles, k, limitDistance, toleranceBuffer) ?? [])
                    .Map(result => (IReadOnlyList<int>)result.AsReadOnly()),
                false => ResultFactory.Create(value: (IReadOnlyList<int>)(IndexCore(source, method, context, queryShape, needles, k, limitDistance, toleranceBuffer) ?? []).AsReadOnly())
            }
        };

    /// <summary>Core spatial logic with algebraic method dispatch and unified operation patterns.</summary>
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
        var (config, queryTransform) = (_spatialConfig.GetValueOrDefault((method, source.GetType())),
            (queryShape, toleranceBuffer, context.AbsoluteTolerance) switch {
                (Sphere { Center: var c, Radius: var r }, { } buf, _) => (object)new Sphere(c, r + buf),
                (BoundingBox { Min: var min, Max: var max }, { } buf, _) => new BoundingBox(min - new Vector3d(buf, buf, buf), max + new Vector3d(buf, buf, buf)),
                (Point3d point, _, var tol) => new Sphere(point, tol + (toleranceBuffer ?? 0)),
                (GeometryBase geom, { } buf, _) => geom.GetBoundingBox(accurate: true) switch {
                    var bbox => new BoundingBox(bbox.Min - new Vector3d(buf, buf, buf), bbox.Max + new Vector3d(buf, buf, buf))
                },
                _ => queryShape ?? new Sphere(Point3d.Origin, context.AbsoluteTolerance)
            });

        return (method, source, needles, k, limitDistance) switch {
            (SpatialMethod.PointsProximity, Point3d[] pts, var n, { } kVal, _) when n is not null =>
                RTree.Point3dKNeighbors(pts, n, kVal)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            (SpatialMethod.PointsProximity, Point3d[] pts, var n, _, { } lim) when n is not null =>
                RTree.Point3dClosestPoints(pts, n, lim)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            (SpatialMethod.PointCloudProximity, PointCloud cloud, var n, { } kVal, _) when n is not null =>
                RTree.PointCloudKNeighbors(cloud, n, kVal)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            (SpatialMethod.PointCloudProximity, PointCloud cloud, var n, _, { } lim) when n is not null =>
                RTree.PointCloudClosestPoints(cloud, n, lim)?.SelectMany<int[], int>(g => [.. g, -1]).ToArray(),
            (SpatialMethod.MeshOverlap, ValueTuple<Mesh, Mesh> meshes, _, _, _) => ArrayPool<int>.Shared.Rent(config.BufferSize) switch {
                var buffer => ((Func<int[]?>)(() => {
                    var (t1, t2, count) = (_treeCache.GetValue(meshes.Item1, static m => RTree.CreateMeshFaceTree((Mesh)m)),
                                            _treeCache.GetValue(meshes.Item2, static m => RTree.CreateMeshFaceTree((Mesh)m)), 0);
                    try {
                        RTree.SearchOverlaps(t1, t2, context.AbsoluteTolerance + (toleranceBuffer ?? 0),
                            (_, args) => count = count + 1 < buffer.Length ? (buffer[count] = args.Id, buffer[count + 1] = args.IdB, count + 2).Item3 : count);
                        return count > 0 ? buffer[..count].ToArray() : [];
                    } finally { ArrayPool<int>.Shared.Return(buffer, clearArray: true); }
                }))()
            },
            _ when config.TreeFactory is not null => ArrayPool<int>.Shared.Rent(config.BufferSize) switch {
                var buffer => ((Func<int[]?>)(() => {
                    var (tree, count) = (_treeCache.GetValue(source, _ => config.TreeFactory!(source)) ?? RTree.CreateFromPointArray([]), 0);
                    try {
                        (queryTransform switch {
                            Sphere sphere => (Action)(() => tree.Search(sphere, (_, args) => count = count < buffer.Length ? (buffer[count] = args.Id, count + 1).Item2 : count)),
                            BoundingBox box => () => tree.Search(box, (_, args) => count = count < buffer.Length ? (buffer[count] = args.Id, count + 1).Item2 : count),
                            _ => () => { }
                        })();
                        return count > 0 ? buffer[..count].ToArray() : [];
                    } finally { ArrayPool<int>.Shared.Return(buffer, clearArray: true); }
                }))()
            },
            _ => null
        };
    }
}
