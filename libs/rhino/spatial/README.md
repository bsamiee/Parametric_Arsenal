# Spatial Indexing and Computational Geometry

RTree-based spatial indexing with polymorphic dispatch for proximity queries, range analysis, clustering, and computational geometry algorithms.

---

## Core Operations

### Range Queries

Search for geometry indices within spherical or bounding box regions using RTree spatial indexing.

```csharp
Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
    TInput input,
    TQuery query,
    IGeometryContext context,
    int? bufferSize = null)
```

**Supported Types**:
- `Point3d[]` with `Sphere` or `BoundingBox`
- `PointCloud` with `Sphere` or `BoundingBox`
- `Mesh` (face indices) with `Sphere` or `BoundingBox`
- `Curve[]` with `Sphere` or `BoundingBox`
- `Surface[]` with `Sphere` or `BoundingBox`
- `Brep[]` with `Sphere` or `BoundingBox`

**Usage**:
```csharp
Result<IReadOnlyList<int>> result = Spatial.Analyze(
    input: points,
    query: new Sphere(center, radius),
    context: context);

Result<IReadOnlyList<int>> meshFaces = Spatial.Analyze(
    input: mesh,
    query: new BoundingBox(min, max),
    context: context,
    bufferSize: 4096);
```

### Proximity Queries

Find k-nearest neighbors or distance-limited neighbors using RTree acceleration.

**Supported Types**:
- `Point3d[]` proximity queries
- `PointCloud` proximity queries

**Query Types**:
- `KNearestProximity(Point3d[] needles, int count)` - k-nearest neighbors per needle
- `DistanceLimitedProximity(Point3d[] needles, double distance)` - all neighbors within distance

**Usage**:
```csharp
// K-nearest neighbors
Result<IReadOnlyList<int>> knn = Spatial.Analyze(
    input: points,
    query: (new Point3d[] { needle1, needle2, }, 5),
    context: context);

// Distance-limited
Result<IReadOnlyList<int>> nearby = Spatial.Analyze(
    input: cloud,
    query: (new Point3d[] { searchPoint, }, 10.0),
    context: context);
```

### Mesh Overlap Detection

Find overlapping face pairs between two meshes using dual RTree search.

```csharp
// Returns flat array: [face1_mesh1, face1_mesh2, face2_mesh1, face2_mesh2, ...]
Result<IReadOnlyList<int>> overlaps = Spatial.Analyze(
    input: mesh1,
    query: (mesh2, 0.01),
    context: context);
```

---

## Clustering Algorithms

Cluster geometry by centroid proximity using K-means, DBSCAN, or hierarchical agglomerative clustering.

```csharp
Result<ClusteringResult[]> Cluster<T>(
    T[] geometry,
    ClusterRequest request,
    IGeometryContext context) where T : GeometryBase
```

### K-Means Clustering

Lloyd's algorithm with K-means++ initialization.

```csharp
Result<ClusteringResult[]> clusters = Spatial.Cluster(
    geometry: meshes,
    request: new KMeansRequest(K: 5),
    context: context);
```

### DBSCAN Clustering

Density-based spatial clustering with RTree acceleration for >100 points.

```csharp
Result<ClusteringResult[]> clusters = Spatial.Cluster(
    geometry: curves,
    request: new DBSCANRequest(Epsilon: 5.0, MinPoints: 4),
    context: context);
```

### Hierarchical Agglomerative Clustering

Bottom-up hierarchical clustering with single-linkage.

```csharp
Result<ClusteringResult[]> clusters = Spatial.Cluster(
    geometry: surfaces,
    request: new HierarchicalRequest(K: 3),
    context: context);
```

**Result Structure**:
```csharp
ClusteringResult {
    Point3d Centroid,    // Cluster centroid
    double[] Radii,      // Distance from each member to centroid
}
```

---

## Directional Proximity Fields

Compute anisotropic proximity with directional weighting for proximity field analysis.

```csharp
Result<ProximityFieldResult[]> ProximityField(
    GeometryBase[] geometry,
    DirectionalProximityRequest request,
    IGeometryContext context)
```

**Usage**:
```csharp
Result<ProximityFieldResult[]> field = Spatial.ProximityField(
    geometry: geometries,
    request: new DirectionalProximityRequest(
        Direction: new Vector3d(0, 0, 1),
        MaxDistance: 50.0,
        AngleWeight: 0.5),
    context: context);

// Each result contains: Index, Distance, Angle, WeightedDistance
// Sorted by WeightedDistance = Distance * (1 + AngleWeight * Angle)
```

---

## Computational Geometry

### Convex Hull (3D)

Incremental algorithm returning mesh face vertex indices as triples.

```csharp
Result<int[][]> ConvexHull3D(
    Point3d[] points,
    IGeometryContext context)
```

**Usage**:
```csharp
Result<int[][]> hull = Spatial.ConvexHull3D(
    points: points,
    context: context);

// Result: [[v0, v1, v2], [v1, v3, v2], ...] - triangular faces
```

**Requirements**: Minimum 4 non-coplanar points.

### Delaunay Triangulation (2D)

Bowyer-Watson algorithm for XY-coplanar points, returning triangle vertex indices.

```csharp
Result<int[][]> DelaunayTriangulation2D(
    Point3d[] points,
    IGeometryContext context)
```

**Usage**:
```csharp
Result<int[][]> triangles = Spatial.DelaunayTriangulation2D(
    points: planarPoints,
    context: context);

// Result: [[v0, v1, v2], [v1, v3, v2], ...] - triangle indices
```

**Requirements**: Minimum 3 non-collinear points with identical Z coordinates.

### Voronoi Diagram (2D)

Dual of Delaunay triangulation, returning cell vertices for each input point.

```csharp
Result<Point3d[][]> VoronoiDiagram2D(
    Point3d[] points,
    IGeometryContext context)
```

**Usage**:
```csharp
Result<Point3d[][]> cells = Spatial.VoronoiDiagram2D(
    points: planarPoints,
    context: context);

// Result: cells[i] contains ordered vertices of Voronoi cell for points[i]
```

**Requirements**: Same as Delaunay triangulation (XY-coplanar, ≥3 points).

### Medial Axis Skeleton

Computes medial axis skeleton for planar single-face Breps using Voronoi-based algorithm.

```csharp
Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(
    Brep brep,
    double tolerance,
    IGeometryContext context)
```

**Usage**:
```csharp
Result<(Curve[] Skeleton, double[] Stability)> medialAxis = Spatial.MedialAxis(
    brep: planarBrep,
    tolerance: 0.01,
    context: context);

// Skeleton: LineCurve segments forming the medial axis
// Stability: distance from each skeleton point to boundary (inradius)
```

**Requirements**: Single planar face with closed boundary.

---

## Algebraic Domain Types

### Analysis Request Hierarchy

Base type for all spatial queries enabling type-based dispatch.

```csharp
abstract record AnalysisRequest;

sealed record RangeAnalysis<TInput>(
    TInput Input,
    RangeShape Shape,
    int? BufferSize = null) : AnalysisRequest where TInput : notnull;

sealed record ProximityAnalysis<TInput>(
    TInput Input,
    ProximityQuery Query) : AnalysisRequest where TInput : notnull;

sealed record MeshOverlapAnalysis(
    Mesh First,
    Mesh Second,
    double AdditionalTolerance = 0.0,
    int? BufferSize = null) : AnalysisRequest;
```

### Range Shape Hierarchy

Discriminated union for geometric range queries.

```csharp
abstract record RangeShape;

sealed record SphereRange(Sphere Sphere) : RangeShape;

sealed record BoundingBoxRange(BoundingBox Box) : RangeShape;
```

### Proximity Query Hierarchy

Discriminated union for proximity search strategies.

```csharp
abstract record ProximityQuery(Point3d[] Needles);

sealed record KNearestProximity(
    Point3d[] Needles,
    int Count) : ProximityQuery(Needles);

sealed record DistanceLimitedProximity(
    Point3d[] Needles,
    double Distance) : ProximityQuery(Needles);
```

### Cluster Request Hierarchy

Discriminated union for clustering algorithms.

```csharp
abstract record ClusterRequest;

sealed record KMeansRequest(int K) : ClusterRequest;

sealed record DBSCANRequest(
    double Epsilon,
    int MinPoints = 4) : ClusterRequest;

sealed record HierarchicalRequest(int K) : ClusterRequest;
```

---

## Architecture Integration

### Result Monad

All operations return `Result<T>` for consistent error handling. See `libs/core/results/Result.cs`.

```csharp
Result<IReadOnlyList<int>> result = Spatial.Analyze(input, query, context);

result.Match(
    onSuccess: indices => Process(indices),
    onFailure: error => Handle(error));
```

### IGeometryContext

Provides tolerance settings and RhinoDoc integration. See `libs/core/context/IGeometryContext.cs`.

```csharp
IGeometryContext context = new GeometryContext(
    doc: RhinoDoc.ActiveDoc,
    absoluteTolerance: 0.001,
    angleTolerance: 0.01);
```

### Validation Modes

Operations automatically validate inputs using `ValidationRules` expression trees. See `libs/core/validation/V.cs`.

- `V.None` - Point arrays (no validation)
- `V.Standard` - PointCloud, GeometryBase arrays
- `V.MeshSpecific` - Mesh operations
- `V.Degeneracy` - Curve arrays
- `V.BoundingBox` - Surface arrays
- `V.Topology` - Brep arrays

### Error Codes

All errors use `E.*` constants. See `libs/core/errors/E.cs`.

- `E.Geometry.InvalidCount` - Insufficient geometry
- `E.Geometry.InvalidOrientationPlane` - Non-coplanar points
- `E.Spatial.ClusteringFailed` - Clustering algorithm failure
- `E.Spatial.InvalidClusterK` - Invalid K parameter
- `E.Spatial.InvalidEpsilon` - Invalid DBSCAN epsilon
- `E.Spatial.KExceedsPointCount` - K > point count
- `E.Spatial.UnsupportedTypeCombo` - Unsupported input/query combination
- `E.Spatial.ProximityFailed` - Proximity query failure
- `E.Spatial.MedialAxisFailed` - Medial axis computation failure
- `E.Validation.DegenerateGeometry` - Degenerate/collinear geometry

---

## Implementation Notes

### Performance

- **RTree indexing**: O(log n) spatial queries
- **K-means**: Lloyd's algorithm with K-means++ initialization, max 100 iterations
- **DBSCAN**: RTree acceleration when point count >100
- **Hot paths**: Array indexing in tight loops, ArrayPool<T> for buffers
- **Zero allocations**: FrozenDictionary dispatch tables, ConditionalWeakTable caching

### Dispatch Tables

Type-based dispatch via `SpatialConfig.Operations` frozen dictionary:
- Keys: `(Type InputType, string OperationType)`
- Values: `SpatialOperationMetadata(V ValidationMode, string OperationName, int BufferSize)`
- Operations: Range, Proximity, Overlap, Clustering, ProximityField

### Buffer Management

RTree queries use pooled buffers to minimize allocations:
- Default: 2048 indices (range/proximity), 4096 indices (overlap)
- Override via `bufferSize` parameter
- Automatic cleanup via `try/finally` and `ArrayPool<T>`

---

## Examples

### Complete Workflow: Spatial Analysis Pipeline

```csharp
IGeometryContext context = new GeometryContext(
    doc: RhinoDoc.ActiveDoc,
    absoluteTolerance: 0.001,
    angleTolerance: 0.01);

// Range query for nearby points
Result<IReadOnlyList<int>> nearbyPoints = Spatial.Analyze(
    input: pointCloud,
    query: new Sphere(searchCenter, searchRadius),
    context: context);

// Cluster points into groups
Result<ClusteringResult[]> clusters = nearbyPoints.Bind(indices =>
    Spatial.Cluster(
        geometry: indices.Select(i => pointCloud.PointAt(i)).ToArray(),
        request: new KMeansRequest(K: 3),
        context: context));

// Compute Voronoi diagram for planar points
Result<Point3d[][]> voronoi = Spatial.VoronoiDiagram2D(
    points: planarPoints,
    context: context);

// Chain operations with Result monad
Result<Mesh> finalMesh = voronoi
    .Map(cells => ConvertCellsToMesh(cells))
    .Bind(mesh => ValidateAndCleanMesh(mesh, context));
```

### Mesh Face Overlap Analysis

```csharp
Result<IReadOnlyList<int>> overlaps = Spatial.Analyze(
    input: mesh1,
    query: (mesh2, 0.01),
    context: context);

overlaps.Match(
    onSuccess: indices => {
        // Indices are flat: [face1_mesh1, face1_mesh2, face2_mesh1, ...]
        for (int i = 0; i < indices.Count; i += 2) {
            int face1 = indices[i];
            int face2 = indices[i + 1];
            ProcessOverlap(mesh1.Faces[face1], mesh2.Faces[face2]);
        }
    },
    onFailure: error => LogError(error));
```

### Directional Proximity Field for Wind Analysis

```csharp
Vector3d windDirection = new(1, 0, 0);

Result<ProximityFieldResult[]> windField = Spatial.ProximityField(
    geometry: buildings,
    request: new DirectionalProximityRequest(
        Direction: windDirection,
        MaxDistance: 100.0,
        AngleWeight: 0.7),
    context: context);

windField.Match(
    onSuccess: results => {
        // Results sorted by weighted distance
        ProximityFieldResult closest = results[0];
        double exposureFactor = 1.0 - (closest.WeightedDistance / 100.0);
    },
    onFailure: error => Handle(error));
```

---

## File Organization

```
spatial/
├── Spatial.cs          # Public API with algebraic domain types
├── SpatialCore.cs      # Internal orchestration via UnifiedOperation
├── SpatialCompute.cs   # Dense algorithm implementations
└── SpatialConfig.cs    # FrozenDictionary dispatch tables and constants
```

**Files**: 4 (✓ within limit)  
**Types**: 21 (algebraic domain types + internal implementations)  
**LOC**: ~950 total (Spatial: 116, SpatialCore: 201, SpatialCompute: 533, SpatialConfig: 54)

---

## Dependencies

- `libs/core/results` - Result monad, ResultFactory
- `libs/core/context` - IGeometryContext, GeometryContext
- `libs/core/validation` - V flags, ValidationRules
- `libs/core/errors` - E error registry, SystemError
- `libs/core/operations` - UnifiedOperation, OperationConfig
- `RhinoCommon` - Geometry types, RTree, mass properties

---

## Testing

See `test/rhino/spatial/` for NUnit + Rhino.Testing integration tests.

```bash
dotnet test --filter "FullyQualifiedName~Arsenal.Rhino.Spatial"
```

---

## See Also

- `libs/core/operations/UnifiedOperation.cs` - Polymorphic dispatch pattern
- `libs/core/validation/ValidationRules.cs` - Expression tree validation
- `libs/rhino/extraction/` - Point extraction from geometry
- `libs/rhino/intersection/` - Geometric intersection operations
- `CLAUDE.md` - Complete coding standards and architectural patterns
