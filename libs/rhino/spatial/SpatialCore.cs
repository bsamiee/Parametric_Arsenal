using System.Buffers;
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
    private static readonly Func<Point3d[], RTree> _pointArrayFactory = static source => RTree.CreateFromPointArray(source) ?? new RTree();
    private static readonly Func<PointCloud, RTree> _pointCloudFactory = static source => RTree.CreatePointCloudTree(source) ?? new RTree();
    private static readonly Func<Mesh, RTree> _meshFactory = static source => RTree.CreateMeshFaceTree(source) ?? new RTree();
    private static readonly Func<Curve[], RTree> _curveArrayFactory = static source => BuildGeometryArrayTree(source);
    private static readonly Func<Surface[], RTree> _surfaceArrayFactory = static source => BuildGeometryArrayTree(source);
    private static readonly Func<Brep[], RTree> _brepArrayFactory = static source => BuildGeometryArrayTree(source);

    internal static Result<IReadOnlyList<int>> Analyze(Spatial.AnalysisRequest request, IGeometryContext context) =>
        request switch {
            Spatial.RangeAnalysis<Point3d[]> range => RunRange(range, context, _pointArrayFactory, SpatialConfig.OperationNames.PointArrayRange, V.None, SpatialConfig.DefaultBufferSize),
            Spatial.RangeAnalysis<PointCloud> range => RunRange(range, context, _pointCloudFactory, SpatialConfig.OperationNames.PointCloudRange, V.Standard, SpatialConfig.DefaultBufferSize),
            Spatial.RangeAnalysis<Mesh> range => RunRange(range, context, _meshFactory, SpatialConfig.OperationNames.MeshRange, V.MeshSpecific, SpatialConfig.DefaultBufferSize),
            Spatial.RangeAnalysis<Curve[]> range => RunRange(range, context, _curveArrayFactory, SpatialConfig.OperationNames.CurveArrayRange, V.Degeneracy, SpatialConfig.DefaultBufferSize),
            Spatial.RangeAnalysis<Surface[]> range => RunRange(range, context, _surfaceArrayFactory, SpatialConfig.OperationNames.SurfaceArrayRange, V.BoundingBox, SpatialConfig.DefaultBufferSize),
            Spatial.RangeAnalysis<Brep[]> range => RunRange(range, context, _brepArrayFactory, SpatialConfig.OperationNames.BrepArrayRange, V.Topology, SpatialConfig.DefaultBufferSize),
            Spatial.ProximityAnalysis<Point3d[]> proximity => RunProximity(proximity, context, RTree.Point3dKNeighbors, RTree.Point3dClosestPoints, SpatialConfig.OperationNames.PointArrayProximity, V.None),
            Spatial.ProximityAnalysis<PointCloud> proximity => RunProximity(proximity, context, RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints, SpatialConfig.OperationNames.PointCloudProximity, V.Standard),
            Spatial.MeshOverlapAnalysis overlap => RunMeshOverlap(overlap, context, SpatialConfig.OperationNames.MeshOverlap, V.MeshSpecific, SpatialConfig.LargeBufferSize),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo),
        };

    private static Result<IReadOnlyList<int>> RunRange<TInput>(
        Spatial.RangeAnalysis<TInput> request,
        IGeometryContext context,
        Func<TInput, RTree> factory,
        string operationName,
        V mode,
        int defaultBuffer) where TInput : notnull =>
        UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.RangeAnalysis<TInput>, Result<IReadOnlyList<int>>>)(analysis =>
                ((Func<Result<IReadOnlyList<int>>>)(() => {
                    using RTree tree = factory(analysis.Input);
                    object? queryShape = ResolveQueryShape(analysis.Shape);
                    int bufferSize = analysis.BufferSize ?? defaultBuffer;
                    return queryShape is null
                        ? ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo)
                        : ExecuteRangeSearch(tree: tree, queryShape: queryShape, bufferSize: bufferSize);
                }))()),
            config: new OperationConfig<Spatial.RangeAnalysis<TInput>, int> {
                Context = context,
                ValidationMode = mode,
                OperationName = operationName,
                EnableDiagnostics = false,
            });

    private static Result<IReadOnlyList<int>> RunProximity<TInput>(
        Spatial.ProximityAnalysis<TInput> request,
        IGeometryContext context,
        Func<TInput, Point3d[], int, IEnumerable<int[]>> kNearest,
        Func<TInput, Point3d[], double, IEnumerable<int[]>> distLimited,
        string operationName,
        V mode) where TInput : notnull =>
        UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.ProximityAnalysis<TInput>, Result<IReadOnlyList<int>>>)(analysis =>
                ExecuteProximitySearch(
                    source: analysis.Input,
                    query: analysis.Query,
                    kNearest: kNearest,
                    distLimited: distLimited)),
            config: new OperationConfig<Spatial.ProximityAnalysis<TInput>, int> {
                Context = context,
                ValidationMode = mode,
                OperationName = operationName,
                EnableDiagnostics = false,
            });

    private static Result<IReadOnlyList<int>> RunMeshOverlap(
        Spatial.MeshOverlapAnalysis request,
        IGeometryContext context,
        string operationName,
        V mode,
        int defaultBuffer) =>
        UnifiedOperation.Apply(
            input: request,
            operation: (Func<Spatial.MeshOverlapAnalysis, Result<IReadOnlyList<int>>>)(analysis =>
                ((Func<Result<IReadOnlyList<int>>>)(() => {
                    using RTree tree1 = _meshFactory(analysis.First);
                    using RTree tree2 = _meshFactory(analysis.Second);
                    int bufferSize = analysis.BufferSize ?? defaultBuffer;
                    double tolerance = context.AbsoluteTolerance + analysis.AdditionalTolerance;
                    return ExecuteOverlapSearch(tree1: tree1, tree2: tree2, tolerance: tolerance, bufferSize: bufferSize);
                }))()),
            config: new OperationConfig<Spatial.MeshOverlapAnalysis, int> {
                Context = context,
                ValidationMode = mode,
                OperationName = operationName,
                EnableDiagnostics = false,
            });

    private static object? ResolveQueryShape(Spatial.RangeShape shape) =>
        shape switch {
            Spatial.SphereRange sphere => sphere.Sphere,
            Spatial.BoundingBoxRange box => box.Box,
            _ => null,
        };

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
    private static Result<IReadOnlyList<int>> ExecuteProximitySearch<T>(
        T source,
        Spatial.ProximityQuery query,
        Func<T, Point3d[], int, IEnumerable<int[]>> kNearest,
        Func<T, Point3d[], double, IEnumerable<int[]>> distLimited) where T : notnull =>
        query switch {
            Spatial.KNearestProximity nearest when nearest.Count > 0 => kNearest(source, nearest.Needles, nearest.Count).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            Spatial.KNearestProximity nearest => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.InvalidK.WithContext(nearest.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            Spatial.DistanceLimitedProximity distance when distance.Distance > 0.0 => distLimited(source, distance.Needles, distance.Distance).ToArray() is int[][] results
                ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
            Spatial.DistanceLimitedProximity distance => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.InvalidDistance.WithContext(distance.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture))),
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
}
