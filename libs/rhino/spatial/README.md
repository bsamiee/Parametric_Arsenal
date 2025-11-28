# Spatial Indexing and Computational Geometry

RTree-based spatial indexing for proximity queries, range analysis, clustering, and computational geometry algorithms.

---

## API

```csharp
Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(TInput input, TQuery query, IGeometryContext context, int? bufferSize = null)
Result<ClusteringResult[]> Cluster<T>(T[] geometry, ClusterRequest request, IGeometryContext context) where T : GeometryBase
Result<ProximityFieldResult[]> ProximityField(GeometryBase[] geometry, DirectionalProximityRequest request, IGeometryContext context)
Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(Brep brep, double tolerance, IGeometryContext context)
Result<ComputationalGeometryResult> Compute(Point3d[] points, ComputationalGeometryOperation operation, IGeometryContext context)
```

---

## Operations/Types

**Range Queries**: `RangeAnalysis<TInput>(TInput, RangeShape, int?)` with `SphereRange` or `BoundingBoxRange`

**Proximity Queries**: `ProximityAnalysis<TInput>(TInput, ProximityQuery)` with `KNearestProximity(Point3d[], int)` or `DistanceLimitedProximity(Point3d[], double)`

**Clustering**: `KMeansRequest(int)`, `DBSCANRequest(double, int)`, `HierarchicalRequest(int)`

**Directional Proximity**: `DirectionalProximityRequest(Vector3d, double, double)`

**Mesh Overlap**: `MeshOverlapAnalysis(Mesh, Mesh, double, int?)`

**Computational Geometry**: `ConvexHull3D`, `Delaunay2D`, `Voronoi2D`

**Results**: `ClusteringResult(Point3d, double[])`, `ProximityFieldResult(int, double, double, double)`, `ComputationalGeometryResult.ConvexHull(int[][])`, `ComputationalGeometryResult.Delaunay(int[][])`, `ComputationalGeometryResult.Voronoi(Point3d[][])`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Range query
Result<IReadOnlyList<int>> nearby = Spatial.Analyze(
    input: points,
    query: new Sphere(center, radius: 10.0),
    context: context);

// K-means clustering
Result<Spatial.ClusteringResult[]> clusters = Spatial.Cluster(
    geometry: meshes,
    request: new Spatial.KMeansRequest(K: 5),
    context: context);

// Delaunay triangulation via unified Compute
Result<Spatial.ComputationalGeometryResult> result = Spatial.Compute(
    points: planarPoints,
    operation: new Spatial.Delaunay2D(),
    context: context);
// Pattern match result: result.Map(r => r is ComputationalGeometryResult.Delaunay d ? d.TriangleIndices : null)
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<T>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.None` (Point3d[]), `V.Standard` (PointCloud, GeometryBase[]), `V.MeshSpecific` (meshes), `V.Degeneracy` (curves), `V.Topology` (Breps)
- **Errors**: `E.Geometry.InvalidCount`, `E.Spatial.ClusteringFailed`, `E.Spatial.InvalidClusterK`, `E.Spatial.InvalidEpsilon`, `E.Spatial.UnsupportedTypeCombo`, `E.Spatial.MedialAxisFailed`

---

## Internals

**Files**: `Spatial.cs` (API, 102 LOC), `SpatialCore.cs` (dispatch, 228 LOC), `SpatialCompute.cs` (algorithms, 489 LOC), `SpatialConfig.cs` (config, 53 LOC)

**Dispatch**: `FrozenDictionary<(Type, string), SpatialOperationMetadata>` with operation types: Range, Proximity, Overlap, Clustering, ProximityField

**Buffer sizes**: Default 2048 (range/proximity), 4096 (overlap); `ArrayPool<T>` for buffer management

**Clustering**: K-means max 100 iterations with K-means++ init (seed 42); DBSCAN uses RTree for >100 points

**Computational geometry**: Delaunay via Bowyer-Watson with 2× super-triangle; medial axis via Voronoi with 50-500 boundary samples

**Performance**: RTree O(log n) queries; K-means O(k×n×iterations); DBSCAN O(n log n) with RTree
