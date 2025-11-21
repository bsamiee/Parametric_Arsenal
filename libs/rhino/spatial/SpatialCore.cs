using System.Buffers;
using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Internal orchestration with UnifiedOperation for algebraic spatial operations.</summary>
[Pure]
internal static class SpatialCore {
    /// <summary>Algebraic Cluster dispatcher routing to specific algorithms.</summary>
    internal static Result<Spatial.ClusteringResult[]> Cluster<T>(T[] geometry, Spatial.ClusterRequest request, IGeometryContext context) where T : GeometryBase =>
        (geometry.Length, request) switch {
            (0, _) => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Geometry.InvalidCount.WithContext("Cluster requires at least one geometry")),
            (_, null) => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.ClusteringFailed.WithContext("Request cannot be null")),
            (_, Spatial.KMeansRequest { K: <= 0 }) => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.InvalidClusterK),
            (_, Spatial.DBSCANRequest { Epsilon: <= 0.0 }) => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.InvalidEpsilon),
            (_, Spatial.HierarchicalRequest { K: <= 0 }) => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.InvalidClusterK),
            (_, Spatial.KMeansRequest r) => SpatialCompute.ClusterKMeans(geometry: geometry, k: r.K, context: context).Map<Spatial.ClusteringResult[]>(results => [.. results.Select(static r => new Spatial.ClusteringResult(Centroid: r.Centroid, Radii: r.Radii)),]),
            (_, Spatial.DBSCANRequest r) => SpatialCompute.ClusterDBSCAN(geometry: geometry, epsilon: r.Epsilon, minPoints: r.MinPoints).Map<Spatial.ClusteringResult[]>(results => [.. results.Select(static r => new Spatial.ClusteringResult(Centroid: r.Centroid, Radii: r.Radii)),]),
            (_, Spatial.HierarchicalRequest r) => SpatialCompute.ClusterHierarchical(geometry: geometry, k: r.K).Map<Spatial.ClusteringResult[]>(results => [.. results.Select(static r => new Spatial.ClusteringResult(Centroid: r.Centroid, Radii: r.Radii)),]),
            _ => ResultFactory.Create<Spatial.ClusteringResult[]>(error: E.Spatial.ClusteringFailed.WithContext($"Unsupported cluster request: {request.GetType().Name}")),
        };

    /// <summary>Algebraic Analyze dispatcher routing to specific implementations via unified table lookup.</summary>
    internal static Result<IReadOnlyList<int>> Analyze(Spatial.AnalysisRequest request, IGeometryContext context) =>
        request switch {
            null => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext("Request cannot be null")),
            Spatial.RangeAnalysis<Point3d[]> r => RunRangeWithLookup(r, typeof(Point3d[]), static pts => RTree.CreateFromPointArray(pts) ?? new RTree(), context),
            Spatial.RangeAnalysis<PointCloud> r => RunRangeWithLookup(r, typeof(PointCloud), static cloud => RTree.CreatePointCloudTree(cloud) ?? new RTree(), context),
            Spatial.RangeAnalysis<Mesh> r => RunRangeWithLookup(r, typeof(Mesh), static mesh => RTree.CreateMeshFaceTree(mesh) ?? new RTree(), context),
            Spatial.RangeAnalysis<Curve[]> r => RunRangeWithLookup(r, typeof(Curve[]), BuildGeometryArrayTree, context),
            Spatial.RangeAnalysis<Surface[]> r => RunRangeWithLookup(r, typeof(Surface[]), BuildGeometryArrayTree, context),
            Spatial.RangeAnalysis<Brep[]> r => RunRangeWithLookup(r, typeof(Brep[]), BuildGeometryArrayTree, context),
            Spatial.ProximityAnalysis<Point3d[]> r => RunProximityWithLookup(r, typeof(Point3d[]), RTree.Point3dKNeighbors, RTree.Point3dClosestPoints, context),
            Spatial.ProximityAnalysis<PointCloud> r => RunProximityWithLookup(r, typeof(PointCloud), RTree.PointCloudKNeighbors, RTree.PointCloudClosestPoints, context),
            Spatial.MeshOverlapAnalysis r => RunMeshOverlapAnalysis(request: r, context: context),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext($"Unsupported analysis request: {request.GetType().Name}")),
        };

    /// <summary>Legacy Analyze dispatcher for backward compatibility with type-based API.</summary>
    internal static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(TInput input, TQuery query, IGeometryContext context, int? bufferSize) where TInput : notnull where TQuery : notnull =>
        (input, query) switch {
            (Point3d[] points, Sphere sphere) => Analyze(request: new Spatial.RangeAnalysis<Point3d[]>(Input: points, Shape: new Spatial.SphereRange(Sphere: sphere), BufferSize: bufferSize), context: context),
            (Point3d[] points, BoundingBox box) => Analyze(request: new Spatial.RangeAnalysis<Point3d[]>(Input: points, Shape: new Spatial.BoundingBoxRange(Box: box), BufferSize: bufferSize), context: context),
            (PointCloud cloud, Sphere sphere) => Analyze(request: new Spatial.RangeAnalysis<PointCloud>(Input: cloud, Shape: new Spatial.SphereRange(Sphere: sphere), BufferSize: bufferSize), context: context),
            (PointCloud cloud, BoundingBox box) => Analyze(request: new Spatial.RangeAnalysis<PointCloud>(Input: cloud, Shape: new Spatial.BoundingBoxRange(Box: box), BufferSize: bufferSize), context: context),
            (Mesh mesh, Sphere sphere) => Analyze(request: new Spatial.RangeAnalysis<Mesh>(Input: mesh, Shape: new Spatial.SphereRange(Sphere: sphere), BufferSize: bufferSize), context: context),
            (Mesh mesh, BoundingBox box) => Analyze(request: new Spatial.RangeAnalysis<Mesh>(Input: mesh, Shape: new Spatial.BoundingBoxRange(Box: box), BufferSize: bufferSize), context: context),
            (Curve[] curves, Sphere sphere) => Analyze(request: new Spatial.RangeAnalysis<Curve[]>(Input: curves, Shape: new Spatial.SphereRange(Sphere: sphere), BufferSize: bufferSize), context: context),
            (Curve[] curves, BoundingBox box) => Analyze(request: new Spatial.RangeAnalysis<Curve[]>(Input: curves, Shape: new Spatial.BoundingBoxRange(Box: box), BufferSize: bufferSize), context: context),
            (Surface[] surfaces, Sphere sphere) => Analyze(request: new Spatial.RangeAnalysis<Surface[]>(Input: surfaces, Shape: new Spatial.SphereRange(Sphere: sphere), BufferSize: bufferSize), context: context),
            (Surface[] surfaces, BoundingBox box) => Analyze(request: new Spatial.RangeAnalysis<Surface[]>(Input: surfaces, Shape: new Spatial.BoundingBoxRange(Box: box), BufferSize: bufferSize), context: context),
            (Brep[] breps, Sphere sphere) => Analyze(request: new Spatial.RangeAnalysis<Brep[]>(Input: breps, Shape: new Spatial.SphereRange(Sphere: sphere), BufferSize: bufferSize), context: context),
            (Brep[] breps, BoundingBox box) => Analyze(request: new Spatial.RangeAnalysis<Brep[]>(Input: breps, Shape: new Spatial.BoundingBoxRange(Box: box), BufferSize: bufferSize), context: context),
            _ => HandleTupleQueryTypes(input: input, query: query, bufferSize: bufferSize, context: context),
        };

    /// <summary>Algebraic ProximityField dispatcher.</summary>
    internal static Result<Spatial.ProximityFieldResult[]> ProximityField(GeometryBase[] geometry, Spatial.DirectionalProximityRequest request, IGeometryContext context) {
        if (geometry.Length is 0) {
            return ResultFactory.Create<Spatial.ProximityFieldResult[]>(error: E.Geometry.InvalidCount.WithContext("ProximityField requires at least one geometry"));
        }
        if (request is null) {
            return ResultFactory.Create<Spatial.ProximityFieldResult[]>(error: E.Spatial.InvalidDirection.WithContext("Request cannot be null"));
        }
        if (request.Direction.Length <= 0.0) {
            return ResultFactory.Create<Spatial.ProximityFieldResult[]>(error: E.Spatial.ZeroLengthDirection);
        }
        if (request.MaxDistance <= 0.0) {
            return ResultFactory.Create<Spatial.ProximityFieldResult[]>(error: E.Spatial.InvalidDistance.WithContext("MaxDistance must be positive"));
        }
        using RTree tree = new();
        BoundingBox bounds = BoundingBox.Empty;
        Point3d[] centers = new Point3d[geometry.Length];
        for (int i = 0; i < geometry.Length; i++) {
            BoundingBox bbox = geometry[i].GetBoundingBox(accurate: true);
            _ = tree.Insert(bbox, i);
            bounds.Union(bbox);
            centers[i] = bbox.Center;
        }
        Vector3d dir = request.Direction / request.Direction.Length;
        Point3d origin = bounds.Center;
        Sphere searchSphere = new(origin, request.MaxDistance);
        List<Spatial.ProximityFieldResult> results = [];
        void CollectResults(object? sender, RTreeEventArgs args) {
            Vector3d toGeom = centers[args.Id] - origin;
            double dist = toGeom.Length;
            double angle = dist > context.AbsoluteTolerance ? Vector3d.VectorAngle(dir, toGeom / dist) : 0.0;
            double weightedDist = dist * (1.0 + (request.AngleWeight * angle));
            if (weightedDist <= request.MaxDistance) {
                results.Add(new Spatial.ProximityFieldResult(args.Id, dist, angle, weightedDist));
            }
        }
        _ = tree.Search(searchSphere, CollectResults);
        return ResultFactory.Create<Spatial.ProximityFieldResult[]>(value: [.. results.OrderBy(static r => r.WeightedDistance),]);
    }

    private static Result<IReadOnlyList<int>> RunRangeAnalysis<TInput>(Spatial.RangeAnalysis<TInput> request, Func<TInput, RTree> factory, V validationMode, int defaultBuffer, string operationName, IGeometryContext context) where TInput : notnull =>
        UnifiedOperation.Apply(
            input: request.Input,
            operation: (Func<TInput, Result<IReadOnlyList<int>>>)(input => {
                using RTree tree = factory(input);
                object? queryShape = request.Shape switch {
                    Spatial.SphereRange s => s.Sphere,
                    Spatial.BoundingBoxRange b => b.Box,
                    _ => null,
                };
                int bufferSize = request.BufferSize ?? defaultBuffer;
                if (queryShape is null) {
                    return ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext($"Unsupported shape: {request.Shape?.GetType().Name ?? "null"}"));
                }
                int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
                int count = 0;
                try {
                    void Collect(object? sender, RTreeEventArgs args) =>
                        _ = count < buffer.Length ? (buffer[count++] = args.Id) : default;
                    _ = queryShape switch {
                        Sphere sphere => tree.Search(sphere, Collect),
                        BoundingBox box => tree.Search(box, Collect),
                        _ => false,
                    };
                    return ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : []);
                } finally {
                    ArrayPool<int>.Shared.Return(array: buffer, clearArray: true);
                }
            }),
            config: new OperationConfig<TInput, int> {
                Context = context,
                ValidationMode = validationMode,
                OperationName = operationName,
                EnableDiagnostics = false,
            });

    private static Result<IReadOnlyList<int>> RunProximityAnalysis<TInput>(Spatial.ProximityAnalysis<TInput> request, Func<TInput, Point3d[], int, IEnumerable<int[]>> kNearest, Func<TInput, Point3d[], double, IEnumerable<int[]>> distLimited, V validationMode, string operationName, IGeometryContext context) where TInput : notnull =>
        UnifiedOperation.Apply(
            input: request.Input,
            operation: (Func<TInput, Result<IReadOnlyList<int>>>)(input => request.Query switch {
                null => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed.WithContext("Query cannot be null")),
                Spatial.KNearestProximity { Count: <= 0 } => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidK),
                Spatial.DistanceLimitedProximity { Distance: <= 0.0 } => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.InvalidDistance),
                Spatial.KNearestProximity k => kNearest(input, k.Needles, k.Count).ToArray() is int[][] results
                    ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                    : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
                Spatial.DistanceLimitedProximity d => distLimited(input, d.Needles, d.Distance).ToArray() is int[][] results
                    ? ResultFactory.Create<IReadOnlyList<int>>(value: [.. results.SelectMany(static indices => indices),])
                    : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed),
                _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed.WithContext($"Unsupported query: {request.Query.GetType().Name}")),
            }),
            config: new OperationConfig<TInput, int> {
                Context = context,
                ValidationMode = validationMode,
                OperationName = operationName,
                EnableDiagnostics = false,
            });

    private static Result<IReadOnlyList<int>> RunMeshOverlapAnalysis(Spatial.MeshOverlapAnalysis request, IGeometryContext context) =>
        SpatialConfig.Operations.TryGetValue((typeof(Mesh), SpatialConfig.OperationTypeOverlap), out SpatialConfig.SpatialOperationMetadata? meta)
            ? UnifiedOperation.Apply(
                input: request.First,
                operation: (Func<Mesh, Result<IReadOnlyList<int>>>)(first => {
                    using RTree tree1 = RTree.CreateMeshFaceTree(first) ?? new RTree();
                    using RTree tree2 = RTree.CreateMeshFaceTree(request.Second) ?? new RTree();
                    double tolerance = context.AbsoluteTolerance + request.AdditionalTolerance;
                    int bufferSize = request.BufferSize ?? meta.BufferSize;
                    int[] buffer = ArrayPool<int>.Shared.Rent(bufferSize);
                    int count = 0;
                    try {
                        void CollectOverlaps(object? sender, RTreeEventArgs args) {
                            _ = count + 2 <= buffer.Length
                                ? (buffer[count++] = args.Id, buffer[count++] = args.IdB, true)
                                : default;
                        }
                        return RTree.SearchOverlaps(tree1, tree2, tolerance, CollectOverlaps)
                            ? ResultFactory.Create<IReadOnlyList<int>>(value: count > 0 ? [.. buffer[..count]] : [])
                            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.ProximityFailed);
                    } finally {
                        ArrayPool<int>.Shared.Return(array: buffer, clearArray: true);
                    }
                }),
                config: new OperationConfig<Mesh, int> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                })
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext("Mesh overlap operation not configured"));

    private static Result<IReadOnlyList<int>> RunRangeWithLookup<TInput>(Spatial.RangeAnalysis<TInput> request, Type inputType, Func<TInput, RTree> factory, IGeometryContext context) where TInput : notnull =>
        SpatialConfig.Operations.TryGetValue((inputType, SpatialConfig.OperationTypeRange), out SpatialConfig.SpatialOperationMetadata? meta)
            ? RunRangeAnalysis(request: request, factory: factory, validationMode: meta.ValidationMode, defaultBuffer: meta.BufferSize, operationName: meta.OperationName, context: context)
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext($"{inputType.Name} Range operation not configured"));

    private static Result<IReadOnlyList<int>> RunProximityWithLookup<TInput>(Spatial.ProximityAnalysis<TInput> request, Type inputType, Func<TInput, Point3d[], int, IEnumerable<int[]>> kNearest, Func<TInput, Point3d[], double, IEnumerable<int[]>> distLimited, IGeometryContext context) where TInput : notnull =>
        SpatialConfig.Operations.TryGetValue((inputType, SpatialConfig.OperationTypeProximity), out SpatialConfig.SpatialOperationMetadata? meta)
            ? RunProximityAnalysis(request: request, kNearest: kNearest, distLimited: distLimited, validationMode: meta.ValidationMode, operationName: meta.OperationName, context: context)
            : ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext($"{inputType.Name} Proximity operation not configured"));

    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }

    private static Result<IReadOnlyList<int>> HandleTupleQueryTypes<TInput, TQuery>(TInput input, TQuery query, int? bufferSize, IGeometryContext context) where TInput : notnull where TQuery : notnull =>
        query switch {
            ValueTuple<Point3d[], int> kNearest when input is Point3d[] points => Analyze(request: new Spatial.ProximityAnalysis<Point3d[]>(Input: points, Query: new Spatial.KNearestProximity(Needles: kNearest.Item1, Count: kNearest.Item2)), context: context),
            ValueTuple<Point3d[], double> distLimited when input is Point3d[] points => Analyze(request: new Spatial.ProximityAnalysis<Point3d[]>(Input: points, Query: new Spatial.DistanceLimitedProximity(Needles: distLimited.Item1, Distance: distLimited.Item2)), context: context),
            ValueTuple<Point3d[], int> kNearest when input is PointCloud cloud => Analyze(request: new Spatial.ProximityAnalysis<PointCloud>(Input: cloud, Query: new Spatial.KNearestProximity(Needles: kNearest.Item1, Count: kNearest.Item2)), context: context),
            ValueTuple<Point3d[], double> distLimited when input is PointCloud cloud => Analyze(request: new Spatial.ProximityAnalysis<PointCloud>(Input: cloud, Query: new Spatial.DistanceLimitedProximity(Needles: distLimited.Item1, Distance: distLimited.Item2)), context: context),
            double tolerance when input is ValueTuple<Mesh, Mesh> meshes => Analyze(request: new Spatial.MeshOverlapAnalysis(First: meshes.Item1, Second: meshes.Item2, AdditionalTolerance: tolerance, BufferSize: bufferSize), context: context),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: E.Spatial.UnsupportedTypeCombo.WithContext($"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };
}
