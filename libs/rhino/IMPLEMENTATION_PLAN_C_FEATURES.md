# Implementation Plan C: Feature-by-Feature Implementation Guide

**Document Purpose**: Provides detailed implementation specifications for each feature from `new_libs_functionality.md`, organized by feature domain with RhinoCommon API research and algorithmic approaches.

**Target**: Detailed implementation blueprints for developers

**Dependencies**: Requires Plans A (Foundation) and B (Integration) for context

---

## Executive Summary

This document breaks down the 20+ new features from `new_libs_functionality.md` into implementation-ready specifications:

1. **Feature specification** with inputs/outputs
2. **RhinoCommon API research** with specific methods to use
3. **Algorithm sketch** with pseudo-code
4. **Integration points** with foundation/shared
5. **LOC estimates** with complexity assessment
6. **Testing approach** with test cases
7. **Success criteria** with validation

**Organization**: Features grouped by domain (Quality, Topology, Spatial, Intersection, Orientation, Extraction)

---

## Feature Domain 1: Quality & Manufacturability Analysis

### Feature 1.1: Surface Quality Analysis

**Specification**:
```csharp
Result<SurfaceQuality> AnalyzeSurfaceQuality(
    Surface surface,
    IGeometryContext context,
    int sampleCount = 100)
```

**Inputs**:
- `Surface surface`: Surface geometry to analyze
- `IGeometryContext context`: Tolerance settings
- `int sampleCount`: Number of sample points for curvature

**Outputs**:
- `CurvatureDistribution`: Mean, StdDev, Min, Max curvature
- `HighVariationRegions`: Count of regions with high curvature change
- `SingularityPoints`: Near-singularities detected
- `ManufacturabilityScore`: 0-1 rating for machining

**RhinoCommon APIs**:
```csharp
// Primary APIs
Surface.CurvatureAt(double u, double v) → SurfaceCurvature
Surface.Domain(int direction) → Interval
Surface.PointAt(double u, double v) → Point3d

// Curvature analysis
SurfaceCurvature.Gaussian → double
SurfaceCurvature.Mean → double
SurfaceCurvature.Kappa(int index) → double

// Singularity detection
Surface.IsSingular(int side) → bool
Surface.IsAtSingularity(double u, double v, bool exact) → bool
```

**Algorithm**:
```
1. Sample surface using stratified random sampling in UV domain
   - Divide UV domain into grid (√sampleCount × √sampleCount)
   - Sample at grid centers + small random offset
   
2. For each sample point:
   - Compute Gaussian curvature K = κ1 * κ2
   - Compute mean curvature H = (κ1 + κ2) / 2
   - Store in arrays
   
3. Compute statistics:
   - Mean(K), StdDev(K), Min(K), Max(K)
   - Mean(H), StdDev(H), Min(H), Max(H)
   
4. Detect high variation regions:
   - Compute local curvature gradients
   - Flag regions where |∇K| > threshold
   - Count connected regions
   
5. Detect near-singularities:
   - Check Surface.IsSingular() for edges
   - Sample near edges, check IsAtSingularity()
   - Store singularity locations
   
6. Compute manufacturability:
   - Check min principal curvature radius vs tool radius
   - Penalize high curvature variation
   - Return 0-1 score
```

**Integration Points**:
- Located in: `libs/core/quality/QualityCore.cs`
- Called by: `analysis/Analysis.cs`, `spatial/Spatial.cs`
- Dependencies: `libs/core/validation/` for surface validation

**LOC Estimate**: 180-220 LOC
- Sampling: 40 LOC
- Statistics: 30 LOC
- High variation detection: 50 LOC
- Singularity detection: 40 LOC
- Manufacturability scoring: 30 LOC

**Testing**:
```csharp
[Fact] void AnalyzeSurfaceQuality_PlanarSurface_ReturnsZeroCurvature()
[Fact] void AnalyzeSurfaceQuality_SphericalSurface_ReturnsConstantGaussian()
[Theory] void AnalyzeSurfaceQuality_VariousSampleCounts_ConsistentResults()
[Fact] void AnalyzeSurfaceQuality_NearSingularity_DetectsCorrectly()
```

---

### Feature 1.2: Curve Fairness Analysis

**Specification**:
```csharp
Result<FairnessScore> ComputeFairness(
    Curve curve,
    IGeometryContext context)
```

**Inputs**:
- `Curve curve`: Curve to analyze fairness
- `IGeometryContext context`: Tolerance settings

**Outputs**:
- `CurvatureEnergy`: ∫κ² ds
- `StrainEnergy`: ∫κ'² ds
- `BendingEnergy`: ∫κ²κ' ds
- `InflectionPoints`: Count and locations
- `Smoothness`: 0-1 fairness score

**RhinoCommon APIs**:
```csharp
Curve.CurvatureAt(double t) → Vector3d
Curve.TangentAt(double t) → Vector3d
Curve.Domain → Interval
Curve.GetLength() → double
Curve.DivideByLength(double segmentLength, bool includeStart, out double[] parameters) → Point3d[]
```

**Algorithm**:
```
1. Divide curve into N segments by arc length
   - N = max(50, curve.GetLength() / tolerance)
   - Use Curve.DivideByLength() for uniform spacing
   
2. At each parameter t:
   - κ(t) = |Curve.CurvatureAt(t)|
   - If have previous κ, compute κ'(t) ≈ (κ(t) - κ(t-1)) / Δs
   
3. Numerical integration using trapezoidal rule:
   - CurvatureEnergy = Σ κ(i)² * Δs
   - StrainEnergy = Σ κ'(i)² * Δs
   - BendingEnergy = Σ κ(i)² * κ'(i) * Δs
   
4. Detect inflection points:
   - Inflection where κ(t) changes sign or κ(t) ≈ 0
   - Record parameters where this occurs
   
5. Compute smoothness score:
   - Normalize energies by curve length
   - Penalize high strain energy (indicates jerky curvature)
   - Penalize many inflection points
   - Return score ∈ [0, 1]
```

**Integration Points**:
- Located in: `libs/core/quality/QualityCore.cs`
- Called by: `analysis/Analysis.cs`
- Dependencies: None (pure computation)

**LOC Estimate**: 140-180 LOC
- Curve division: 20 LOC
- Curvature sampling: 30 LOC
- Energy integration: 40 LOC
- Inflection detection: 30 LOC
- Smoothness scoring: 20 LOC

**Testing**:
```csharp
[Fact] void ComputeFairness_StraightLine_ReturnsZeroEnergy()
[Fact] void ComputeFairness_Circle_ReturnsConstantCurvature()
[Fact] void ComputeFairness_SCurve_DetectsInflectionPoint()
[Theory] void ComputeFairness_VaryingSmooth_CorrectRanking()
```

---

### Feature 1.3: FEA Mesh Quality Analysis

**Specification**:
```csharp
Result<MeshQualityReport> AnalyzeMeshQuality(
    Mesh mesh,
    IGeometryContext context)
```

**Inputs**:
- `Mesh mesh`: Mesh for FEA analysis
- `IGeometryContext context`: Tolerance settings

**Outputs**:
- `AspectRatios[]`: Per-element aspect ratios
- `SkewAngles[]`: Per-element skew angles
- `JacobianDeterminants[]`: Per-element Jacobians
- `ProblematicElements`: Count of poor-quality elements
- `WorstQuality`: Minimum quality metric

**RhinoCommon APIs**:
```csharp
Mesh.Faces → MeshFaceList
Mesh.Vertices → MeshVertexList
MeshFace.IsTriangle → bool
MeshFace.IsQuad → bool
Mesh.GetOutline(Plane plane) → Polyline[]
```

**Algorithm**:
```
1. For each mesh face:
   a) Extract vertices (3 for triangle, 4 for quad)
   
   b) Compute aspect ratio:
      - Triangle: ratio of longest edge to shortest altitude
      - Quad: ratio of max diagonal to min diagonal
   
   c) Compute skew angle:
      - Triangle: deviation of angles from 60°
      - Quad: deviation of angles from 90°
   
   d) Compute Jacobian determinant:
      - Map element to reference element (iso-parametric)
      - Det(J) = |∂x/∂ξ * ∂y/∂η - ∂x/∂η * ∂y/∂ξ|
      - Negative det indicates inverted element
   
2. Quality thresholds:
   - Aspect ratio: acceptable if < 3.0, poor if > 5.0
   - Skew angle: acceptable if < 45°, poor if > 60°
   - Jacobian: problematic if < 0.1 or negative
   
3. Count problematic elements (any metric fails threshold)

4. Compute worst quality = min(all normalized metrics)
```

**Integration Points**:
- Located in: `libs/core/quality/QualityCore.cs`
- Called by: `analysis/Analysis.cs`
- Dependencies: None (pure mesh computation)

**LOC Estimate**: 200-240 LOC
- Aspect ratio computation: 50 LOC
- Skew angle computation: 50 LOC
- Jacobian computation: 70 LOC
- Quality assessment: 30 LOC

**Testing**:
```csharp
[Fact] void AnalyzeMeshQuality_UniformTriMesh_HighQuality()
[Fact] void AnalyzeMeshQuality_SkewedQuads_LowQuality()
[Fact] void AnalyzeMeshQuality_InvertedElements_Detected()
[Theory] void AnalyzeMeshQuality_VaryingQuality_CorrectClassification()
```

---

## Feature Domain 2: Topology Analysis & Repair

### Feature 2.1: Intelligent Topology Diagnosis

**Specification**:
```csharp
Result<TopologyDiagnosis> DiagnoseTopology(
    Brep brep,
    IGeometryContext context)
```

**Inputs**:
- `Brep brep`: Brep to diagnose
- `IGeometryContext context`: Tolerance for "almost joined"

**Outputs**:
- `NakedEdges[]`: Naked edge locations
- `AlmostJoinedPairs[]`: Edges within 10× tolerance
- `ProbableCauses[]`: Diagnosis of each problem
- `RepairStrategies[]`: Ranked repair suggestions
- `Confidence`: Overall diagnosis confidence

**RhinoCommon APIs**:
```csharp
Brep.GetEdgeIndices() → IEnumerable<int>
BrepEdge.EdgeCurve → Curve
BrepEdge.IsValid → bool
Brep.IsValid → bool
Brep.JoinNakedEdges(double tolerance) → bool
Curve.ClosestPoints(Curve other, out Point3d pointA, out Point3d pointB) → bool
```

**Algorithm**:
```
1. Identify naked edges:
   - Iterate all edges, check if AdjacentFaces.Count == 1
   - Store naked edge indices
   
2. Find "almost joined" pairs:
   - For each naked edge A:
     - Build RTree of other naked edges
     - Query within 10× tolerance of A's bounding box
     - For each candidate B:
       - Compute closest points between A and B
       - If distance < 10× tolerance, record pair
   
3. Classify problems:
   - Gap < 2× tolerance: "Tolerance issue - tight join should work"
   - Gap < 10× tolerance, curves parallel: "Trimming issue - edges should align"
   - Gap < 10× tolerance, curves not parallel: "Modeling issue - geometry mismatch"
   - Singularity nearby: "Singularity affecting join"
   
4. Rank repair strategies:
   - For tolerance issues: "Increase join tolerance to [value]"
   - For trimming: "Extend/trim curves, then re-trim surfaces"
   - For modeling: "Rebuild adjacent surfaces"
   - For singularity: "Move away from singularity"
   
5. Compute confidence based on number of problems and clarity of diagnosis
```

**Integration Points**:
- Located in: `topology/TopologyCore.cs`
- Called by: `topology/Topology.cs`
- Dependencies: `spatial/SpatialCore.cs` for RTree queries

**LOC Estimate**: 220-260 LOC
- Naked edge detection: 30 LOC
- Almost-joined detection: 60 LOC
- Problem classification: 70 LOC
- Strategy ranking: 50 LOC

**Testing**:
```csharp
[Fact] void DiagnoseTopology_ManifoldBrep_NoIssues()
[Fact] void DiagnoseTopology_SmallGap_IdentifiesToleranceIssue()
[Fact] void DiagnoseTopology_MisalignedEdges_IdentifiesTrimmingIssue()
[Theory] void DiagnoseTopology_VariousProblems_CorrectDiagnosis()
```

---

### Feature 2.2: Progressive Topology Healing

**Specification**:
```csharp
Result<Brep> HealTopology(
    Brep brep,
    HealingStrategy strategy,
    IGeometryContext context)
```

**Inputs**:
- `Brep brep`: Brep to heal
- `HealingStrategy strategy`: Conservative | Moderate | Aggressive
- `IGeometryContext context`: Tolerance settings

**Outputs**:
- `Brep`: Healed brep (or original if healing fails)
- Detailed report of what was fixed

**RhinoCommon APIs**:
```csharp
Brep.Duplicate() → Brep
Brep.JoinNakedEdges(double tolerance) → int (returns count joined)
Brep.Repair(double tolerance) → bool
Brep.IsValid → bool
Brep.Flip() → void
Brep.ShrinkSurfaces() → bool
```

**Algorithm**:
```
CONSERVATIVE strategy:
1. Clone brep for rollback safety
2. Try Brep.JoinNakedEdges(tolerance)
3. If IsValid and fewer naked edges: success
4. Else: rollback, return original

MODERATE strategy (progressive):
1. Clone brep
2. Try JoinNakedEdges(tolerance)
3. If still naked edges, try JoinNakedEdges(tolerance × 2)
4. Try Brep.Repair(tolerance)
5. Try ShrinkSurfaces()
6. Validate after each step, rollback if invalid

AGGRESSIVE strategy:
1. Clone brep
2. Try all Moderate steps with up to 10× tolerance
3. If still issues, try rebuilding surfaces near naked edges
4. Consider flipping normals if inside-out detected
5. Validate after each step, rollback if invalid

Validation criteria:
- Brep.IsValid must be true
- Naked edge count must decrease
- Topology connectivity must improve
- No inverted faces introduced
```

**Integration Points**:
- Located in: `topology/TopologyCore.cs`
- Called by: `topology/Topology.cs`
- Dependencies: Diagnosis result for informed healing

**LOC Estimate**: 180-220 LOC
- Conservative healing: 40 LOC
- Moderate healing: 60 LOC
- Aggressive healing: 70 LOC
- Validation logic: 30 LOC

**Testing**:
```csharp
[Fact] void HealTopology_SmallGap_ConservativeSucceeds()
[Fact] void HealTopology_ComplexIssue_ModerateSucceeds()
[Fact] void HealTopology_Invalid_RollsBack()
[Theory] void HealTopology_VariousStrategies_AppropriateResults()
```

---

### Feature 2.3: Topological Feature Extraction

**Specification**:
```csharp
Result<TopologicalFeatures> ExtractTopologicalFeatures(
    Brep brep,
    IGeometryContext context)
```

**Inputs**:
- `Brep brep`: Brep to analyze
- `IGeometryContext context`: Settings

**Outputs**:
- `Genus`: Topological genus (number of "holes")
- `Handles`: Number of handles
- `Voids`: Number of internal voids
- `Classification`: Sheet | Shell | Solid
- `ConfidenceScore`: Reliability of classification

**RhinoCommon APIs**:
```csharp
Brep.IsSolid → bool
Brep.SolidOrientation → BrepSolidOrientation
Brep.Faces → BrepFaceList
Brep.Loops → BrepLoopList
Brep.Vertices → BrepVertexList
Brep.Edges → BrepEdgeList
```

**Algorithm** (Euler characteristic):
```
1. Extract topological elements:
   V = brep.Vertices.Count
   E = brep.Edges.Count
   F = brep.Faces.Count
   
2. Compute Euler characteristic:
   χ = V - E + F
   
3. Compute genus using Euler-Poincaré formula:
   For closed surface: χ = 2 - 2g (where g = genus)
   Solve: g = (2 - χ) / 2
   
4. Count handles:
   - Handles = genus (for orientable surfaces)
   - Each handle contributes -1 to χ
   
5. Detect voids (internal cavities):
   - For solid breps, check for internal loops
   - Each void contributes +2 to χ
   
6. Classify:
   - Sheet: Single face, not closed
   - Shell: Multiple faces, closed but not solid
   - Solid: IsSolid == true
   
7. Compute confidence:
   - High if Brep.IsValid and topology consistent
   - Lower if near-degenerate or questionable geometry
```

**Integration Points**:
- Located in: `topology/TopologyCore.cs`
- Called by: `topology/Topology.cs`
- Dependencies: Validation for geometry quality

**LOC Estimate**: 140-180 LOC
- Element counting: 20 LOC
- Genus computation: 40 LOC
- Void detection: 50 LOC
- Classification: 30 LOC

**Testing**:
```csharp
[Fact] void ExtractFeatures_Sphere_GenusZero()
[Fact] void ExtractFeatures_Torus_GenusOne()
[Fact] void ExtractFeatures_Sheet_ClassifiedCorrectly()
[Theory] void ExtractFeatures_VariousTopologies_CorrectGenus()
```

---

## Feature Domain 3: Spatial Relationships & Clustering

### Feature 3.1: K-Means Clustering

**Specification**:
```csharp
Result<IReadOnlyList<Cluster<T>>> ClusterByProximity<T>(
    IReadOnlyList<T> geometries,
    ClusteringAlgorithm.KMeans,
    IGeometryContext context,
    int targetClusters) where T : GeometryBase
```

**Inputs**:
- `geometries[]`: Collection to cluster
- `targetClusters`: Number of clusters (k)
- `context`: Tolerance settings

**Outputs**:
- `Clusters[]`: Each cluster with members, centroid, bounds
- `Cohesion`: Within-cluster variance
- `Separation`: Between-cluster variance

**RhinoCommon APIs**:
```csharp
GeometryBase.GetBoundingBox(bool accurate) → BoundingBox
BoundingBox.Center → Point3d
Point3d.DistanceTo(Point3d other) → double
```

**Algorithm** (Lloyd's algorithm):
```
1. Initialize k cluster centroids:
   - Use k-means++ initialization:
     - First centroid = random geometry center
     - Each subsequent centroid: choose geometry furthest from existing centroids
     - Reduces poor initializations
   
2. Iterate until convergence (max 100 iterations):
   a) Assignment step:
      - For each geometry:
        - Compute distance to each centroid
        - Assign to nearest centroid
   
   b) Update step:
      - For each cluster:
        - Compute new centroid = mean of member positions
   
   c) Check convergence:
      - If centroids moved < tolerance: converged
      - If iteration >= max: force stop
   
3. Compute cluster statistics:
   - Within-cluster sum of squares (cohesion)
   - Between-cluster sum of squares (separation)
   - Silhouette coefficient for quality
   
4. Build result objects with members, centroids, bounds
```

**Integration Points**:
- Located in: `libs/core/patterns/PatternCore.cs`
- Called by: `spatial/Spatial.cs`, `extraction/Extraction.cs`
- Dependencies: None (pure algorithm)

**LOC Estimate**: 160-200 LOC
- Initialization: 40 LOC
- Assignment step: 30 LOC
- Update step: 30 LOC
- Convergence check: 20 LOC
- Statistics: 40 LOC

**Testing**:
```csharp
[Fact] void KMeansClustering_WellSeparated_FindsCorrectClusters()
[Fact] void KMeansClustering_K1_AllInOneCluster()
[Theory] void KMeansClustering_VaryingK_AppropriateResults()
[Fact] void KMeansClustering_Converges_WithinIterations()
```

---

### Feature 3.2: Medial Axis Computation

**Specification**:
```csharp
Result<MedialAxis> ComputeMedialAxis(
    Brep brep,
    MedialAxisMode mode,
    IGeometryContext context)
```

**Inputs**:
- `Brep brep`: Geometry to compute medial axis
- `MedialAxisMode mode`: Planar | Volume
- `context`: Tolerance

**Outputs**:
- `Skeleton[]`: Medial axis curves
- `BranchPoints[]`: Branch/junction points
- `RadiusFunction[]`: Radius at each skeleton point
- `LocalStability`: Sensitivity to perturbations

**RhinoCommon APIs**:
```csharp
Brep.GetBoundingBox(bool accurate) → BoundingBox
Brep.ClosestPoint(Point3d testPoint) → Point3d
Brep.Faces[i].ClosestPoint(Point3d testPoint, out double u, out double v) → bool
Curve.PointAt(double t) → Point3d
```

**Algorithm** (Voronoi-based approximation):
```
PLANAR mode (for 2D profiles):
1. Extract boundary curves from Brep faces
2. Sample boundary uniformly (adaptive by curvature)
3. Compute 2D Voronoi diagram of sample points
4. Extract Voronoi edges fully interior to shape
5. Prune short branches (< threshold)
6. Build skeleton curves from Voronoi edges

VOLUME mode (for 3D solids):
1. Voxelize brep interior:
   - Create 3D grid inside bounding box
   - Test each voxel center with Brep.IsPointInside()
   
2. Distance transform:
   - For each interior voxel:
     - Compute distance to nearest boundary (ClosestPoint)
     - Store distance in voxel grid
   
3. Local maxima detection:
   - Find voxels with distance > all 26 neighbors
   - These are medial axis points
   
4. Connect medial axis points into curves:
   - Build graph from neighboring maxima
   - Extract curves using graph traversal
   
5. Compute radius function:
   - Radius at each point = distance to boundary
   
6. Detect branch points:
   - Points with > 2 neighboring medial points
   
7. Stability analysis:
   - Perturb boundary slightly
   - Recompute medial axis
   - Measure change (small change = stable)
```

**Integration Points**:
- Located in: `libs/rhino/shared/UtilitiesCore.cs`
- Called by: `spatial/Spatial.cs`
- Dependencies: Voxelization (custom implementation)

**LOC Estimate**: 240-280 LOC
- Planar mode: 100 LOC
- Volume mode: 140 LOC
- Stability: 40 LOC

**Testing**:
```csharp
[Fact] void ComputeMedialAxis_Circle_ReturnsCenterPoint()
[Fact] void ComputeMedialAxis_Rectangle_ReturnsCorrectSkeleton()
[Fact] void ComputeMedialAxis_Cylinder_ReturnsAxisLine()
[Theory] void ComputeMedialAxis_Stable_ConsistentResults()
```

---

## Feature Domain 4: Intersection Analysis

### Feature 4.1: Intersection Classification

**Specification**:
```csharp
Result<IntersectionClassification> ClassifyIntersection(
    GeometryBase geometryA,
    GeometryBase geometryB,
    IntersectionResult intersection,
    IGeometryContext context)
```

**Inputs**:
- `geometryA`, `geometryB`: Intersecting geometries
- `intersection`: Raw intersection result
- `context`: Tolerance

**Outputs**:
- `Types[]`: Per-point classification (Tangent | Transverse | Grazing)
- `ApproachAngles[]`: Approach angles at each point
- `DepartureAngles[]`: Departure angles
- `BlendSuitability[]`: Suitability for smooth blending (0-1)
- `OverallConfidence`: Classification confidence

**RhinoCommon APIs**:
```csharp
Curve.TangentAt(double t) → Vector3d
Surface.NormalAt(double u, double v) → Vector3d
Curve.ClosestPoint(Point3d testPoint, out double t) → bool
Vector3d.VectorAngle(Vector3d a, Vector3d b) → double (radians)
```

**Algorithm**:
```
1. For each intersection point P:
   
   a) Compute tangent/normal vectors:
      - If geometryA is Curve: tangentA = TangentAt(P)
      - If geometryA is Surface: normalA = NormalAt(P)
      - Similarly for geometryB
   
   b) Compute intersection angle:
      - angle = VectorAngle(vectorA, vectorB)
      - Convert to degrees
   
   c) Classify intersection type:
      - Tangent: angle < 5° (nearly parallel)
      - Grazing: 5° ≤ angle < 10° (shallow)
      - Transverse: angle ≥ 10° (crossing)
   
   d) Compute approach/departure angles:
      - Sample geometry slightly before/after intersection
      - Measure angle of approach to intersection
      - Measure angle of departure from intersection
   
   e) Blend suitability:
      - High suitability: Tangent intersections (easy to blend)
      - Medium: Transverse with angle ≈ 90°
      - Low: Grazing intersections (difficult to blend)
      - Score = f(angle, curvature_continuity)
   
2. Overall confidence:
   - High if all intersections clearly classified
   - Lower if near-boundary cases or noisy geometry
```

**Integration Points**:
- Located in: `intersection/IntersectionCore.cs`
- Called by: `intersection/Intersection.cs`
- Dependencies: None (pure computation)

**LOC Estimate**: 180-220 LOC
- Tangent/normal computation: 50 LOC
- Angle computation: 30 LOC
- Classification: 40 LOC
- Approach/departure: 50 LOC
- Blend suitability: 30 LOC

**Testing**:
```csharp
[Fact] void ClassifyIntersection_Perpendicular_Transverse()
[Fact] void ClassifyIntersection_Parallel_Tangent()
[Fact] void ClassifyIntersection_ShallowAngle_Grazing()
[Theory] void ClassifyIntersection_VaryingAngles_CorrectClassification()
```

---

## Feature Domain 5: Orientation & Alignment

### Feature 5.1: Orientation Optimization

**Specification**:
```csharp
Result<OptimalOrientation> OptimizeOrientation(
    Brep brep,
    OrientationCriteria criteria,
    IGeometryContext context)
```

**Inputs**:
- `Brep brep`: Geometry to orient
- `OrientationCriteria criteria`: MinimizeWaste | MaximizeStability | OptimizeMilling
- `context`: Settings

**Outputs**:
- `OptimalTransform`: Transform to apply
- `Score`: Optimization score
- `Metrics`: Detailed metrics (bounding box volume, center of mass, etc.)

**RhinoCommon APIs**:
```csharp
Brep.GetBoundingBox(Plane plane) → BoundingBox
VolumeMassProperties.Compute(Brep brep) → VolumeMassProperties
VolumeMassProperties.Centroid → Point3d
Transform.Rotation(double angleRadians, Vector3d axis, Point3d center) → Transform
```

**Algorithm**:
```
MINIMIZE_WASTE criteria:
Goal: Find orientation minimizing bounding box volume

1. Sample rotations:
   - Rotate around X, Y, Z axes at 15° increments
   - For each rotation:
     - Compute bounding box
     - Record box volume
   
2. Refine best candidates:
   - Take top 5 rotations
   - Sample more finely around each (±7.5° at 1° increments)
   
3. Select optimal: rotation with minimum box volume

MAXIMIZE_STABILITY criteria:
Goal: Maximize stability (low center of gravity, base area)

1. Compute volume mass properties
2. For each candidate rotation:
   - Compute center of mass height above base
   - Compute base area (projection onto XY plane)
   - Stability score = base_area / COM_height
   
3. Select rotation maximizing stability score

OPTIMIZE_MILLING criteria:
Goal: Minimize undercuts and maximize machining access

1. For each candidate rotation:
   - Ray-cast from top along +Z direction
   - Count surfaces visible from top
   - Detect undercuts (normal pointing downward)
   
2. Score = (visible_area - undercut_area) / total_area

3. Select rotation maximizing milling score
```

**Integration Points**:
- Located in: `orientation/OrientationCore.cs`
- Called by: `orientation/Orientation.cs`
- Dependencies: Volume mass properties

**LOC Estimate**: 200-240 LOC
- Rotation sampling: 50 LOC
- Waste minimization: 40 LOC
- Stability maximization: 60 LOC
- Milling optimization: 50 LOC

**Testing**:
```csharp
[Fact] void OptimizeOrientation_Cube_AllRotationsEquivalent()
[Fact] void OptimizeOrientation_Cylinder_HorizontalMinimizesWaste()
[Theory] void OptimizeOrientation_VaryingCriteria_DifferentResults()
```

---

## Feature Domain 6: Pattern Recognition

### Feature 6.1: Symmetry Detection

**Specification**:
```csharp
Result<IReadOnlyList<Symmetry>> DetectSymmetries(
    IReadOnlyList<GeometryBase> geometries,
    SymmetryType searchTypes,
    IGeometryContext context,
    double confidenceThreshold = 0.85)
```

**Inputs**:
- `geometries[]`: Collection to analyze
- `searchTypes`: Reflection | Rotation | Translation | Glide
- `context`: Tolerance
- `confidenceThreshold`: Minimum confidence to report

**Outputs**:
- `Symmetries[]`: Detected symmetries with transforms
- `Confidence`: Per-symmetry confidence score

**RhinoCommon APIs**:
```csharp
GeometryBase.GetBoundingBox(bool accurate) → BoundingBox
GeometryBase.Transform(Transform xform) → bool
GeometryBase.GetUserString(string key) → string
Transform.Rotation(...) → Transform
Transform.Mirror(Plane plane) → Transform
```

**Algorithm**:
```
REFLECTION symmetry:
1. Compute overall bounding box of collection
2. Test candidate reflection planes:
   - XY, YZ, XZ planes through centroid
   - Custom planes through principal axes
   
3. For each plane:
   - Mirror each geometry across plane
   - Find nearest original geometry to mirrored
   - If distance < tolerance, count as match
   - Confidence = match_count / total_count
   
4. Report symmetries above threshold

ROTATION symmetry:
1. Compute centroid of collection
2. Test rotation angles: 60°, 90°, 120°, 180°
3. For each angle around Z axis:
   - Rotate each geometry
   - Match to originals (as above)
   - Compute confidence
   
4. Report symmetries above threshold

TRANSLATION symmetry:
1. Compute pairwise distances between geometries
2. Cluster distances (frequent distance = translation vector)
3. Test candidate translation vectors:
   - Translate each geometry by vector
   - Match to originals
   - Compute confidence
   
4. Report symmetries above threshold
```

**Integration Points**:
- Located in: `libs/core/patterns/PatternCore.cs`
- Called by: `extraction/Extraction.cs`, `orientation/Orientation.cs`
- Dependencies: None (pure computation)

**LOC Estimate**: 220-260 LOC
- Reflection detection: 60 LOC
- Rotation detection: 60 LOC
- Translation detection: 80 LOC
- Confidence scoring: 20 LOC

**Testing**:
```csharp
[Fact] void DetectSymmetries_MirrorPair_FindsReflection()
[Fact] void DetectSymmetries_RotationalPattern_FindsRotation()
[Fact] void DetectSymmetries_LinearArray_FindsTranslation()
[Theory] void DetectSymmetries_VaryingPatterns_CorrectDetection()
```

---

## Feature Implementation Priority & Sequencing

### Phase 1: Foundation (Weeks 1-2)
**Focus**: Infrastructure and simple features

1. ✅ Error codes (5000-6999) in `E.cs`
2. ✅ Validation modes (V.CurvatureBounds, etc.) in `V.cs`
3. ✅ Quality metrics structs in `QualityMetrics.cs`
4. ✅ Pattern detection structs in `PatternDetection.cs`
5. ⚠️ Surface quality analysis (implement stub)
6. ⚠️ K-means clustering (implement stub)

**Deliverable**: Foundation compiles, stubs callable

### Phase 2: Quality Metrics (Weeks 3-4)
**Focus**: Complete quality analysis features

1. ✅ Surface quality analysis (full implementation)
2. ✅ Curve fairness analysis
3. ✅ FEA mesh quality analysis
4. ✅ Manufacturability evaluation

**Deliverable**: Quality metrics fully functional, tested

### Phase 3: Topology (Weeks 5-6)
**Focus**: Topology diagnosis and repair

1. ✅ Topology diagnosis
2. ✅ Progressive healing
3. ✅ Topological feature extraction
4. ✅ Design intent detection

**Deliverable**: Topology tools fully functional, tested

### Phase 4: Spatial & Clustering (Weeks 7-8)
**Focus**: Spatial relationships and patterns

1. ✅ K-means clustering (full implementation)
2. ✅ Hierarchical clustering
3. ✅ Medial axis computation
4. ✅ Proximity fields

**Deliverable**: Spatial analysis complete, tested

### Phase 5: Intersection Analysis (Weeks 9-10)
**Focus**: Advanced intersection tools

1. ✅ Intersection classification
2. ✅ Stability analysis
3. ✅ Near-miss detection

**Deliverable**: Intersection tools complete, tested

### Phase 6: Orientation & Patterns (Weeks 11-12)
**Focus**: Orientation optimization and patterns

1. ✅ Orientation optimization
2. ✅ Relative orientation
3. ✅ Symmetry detection
4. ✅ Pattern alignment

**Deliverable**: All features complete, integration tested

---

## RhinoCommon API Research Summary

### Curvature Analysis APIs
```csharp
// Surface curvature
Surface.CurvatureAt(double u, double v) → SurfaceCurvature
SurfaceCurvature.Gaussian → double  // K = κ1 * κ2
SurfaceCurvature.Mean → double      // H = (κ1 + κ2) / 2
SurfaceCurvature.Kappa(int index) → double  // κ1, κ2

// Curve curvature
Curve.CurvatureAt(double t) → Vector3d
Vector3d.Length → double  // Magnitude of curvature

// Singularities
Surface.IsSingular(int side) → bool
Surface.IsAtSingularity(double u, double v, bool exact) → bool
```

### Topology APIs
```csharp
// Topology elements
Brep.Vertices → BrepVertexList
Brep.Edges → BrepEdgeList
Brep.Faces → BrepFaceList
BrepEdge.AdjacentFaces() → BrepFace[]

// Manifold checking
Brep.IsManifold → bool
Brep.IsSolid → bool
Brep.SolidOrientation → BrepSolidOrientation

// Repair
Brep.JoinNakedEdges(double tolerance) → int
Brep.Repair(double tolerance) → bool
Brep.ShrinkSurfaces() → bool
```

### Mass Properties APIs
```csharp
// Area properties (for curves, surfaces)
AreaMassProperties.Compute(Curve/Surface geometry) → AreaMassProperties
AreaMassProperties.Centroid → Point3d
AreaMassProperties.Area → double

// Volume properties (for Breps)
VolumeMassProperties.Compute(Brep brep) → VolumeMassProperties
VolumeMassProperties.Centroid → Point3d
VolumeMassProperties.Volume → double
```

### Mesh Quality APIs
```csharp
// Mesh structure
Mesh.Faces → MeshFaceList
Mesh.Vertices → MeshVertexList
MeshFace.IsTriangle → bool
MeshFace.IsQuad → bool

// Mesh analysis
Mesh.GetNakedEdges() → Polyline[]
Mesh.IsManifold → bool
Mesh.GetOutline(Plane plane) → Polyline[]
```

### Proximity APIs
```csharp
// Closest point queries
Brep.ClosestPoint(Point3d testPoint) → Point3d
Curve.ClosestPoint(Point3d testPoint, out double t) → bool
Surface.ClosestPoint(Point3d testPoint, out double u, out double v) → bool

// Distance queries
Point3d.DistanceTo(Point3d other) → double
Curve.DistanceTo(Curve other) → double
```

---

## LOC Summary by Feature

| Feature | Estimated LOC | Complexity | Location |
|---------|---------------|------------|----------|
| Surface Quality | 180-220 | High | libs/core/quality/ |
| Curve Fairness | 140-180 | Medium | libs/core/quality/ |
| FEA Mesh Quality | 200-240 | High | libs/core/quality/ |
| Topology Diagnosis | 220-260 | High | topology/TopologyCore.cs |
| Topology Healing | 180-220 | High | topology/TopologyCore.cs |
| Topological Features | 140-180 | Medium | topology/TopologyCore.cs |
| K-Means Clustering | 160-200 | Medium | libs/core/patterns/ |
| Medial Axis | 240-280 | Very High | libs/rhino/shared/ |
| Intersection Classification | 180-220 | High | intersection/IntersectionCore.cs |
| Orientation Optimization | 200-240 | High | orientation/OrientationCore.cs |
| Symmetry Detection | 220-260 | High | libs/core/patterns/ |

**Total Estimated LOC**: ~2,200-2,600 lines across all features

**Average per Feature**: ~200 LOC

**All Within Limits**: ✅ Every method < 300 LOC

---

## Testing Strategy Summary

### Test Coverage Targets
- **Unit tests**: 90%+ coverage for all new code
- **Integration tests**: 80%+ coverage for cross-module workflows
- **Property tests**: Where applicable (clustering, symmetry)

### Test Categories

**1. Correctness Tests**:
```csharp
[Fact] void Feature_CorrectInput_ExpectedOutput()
[Theory] void Feature_EdgeCases_HandledCorrectly()
```

**2. Error Handling Tests**:
```csharp
[Fact] void Feature_InvalidInput_ReturnsError()
[Fact] void Feature_NullInput_ReturnsError()
```

**3. Performance Tests**:
```csharp
[Fact] void Feature_LargeInput_CompletesInTime()
```

**4. Property-Based Tests** (CsCheck):
```csharp
[Fact] void Clustering_CommutativeProperty()
[Fact] void Symmetry_ReflectionInvolution()
```

---

## Success Metrics

### Per-Feature Success Criteria
1. ✅ Builds with zero warnings
2. ✅ All unit tests passing
3. ✅ LOC < 300 per method
4. ✅ RhinoCommon APIs used correctly
5. ✅ Result<T> error handling complete
6. ✅ XML documentation complete
7. ✅ Integration tests passing

### Overall Success Criteria
1. ✅ All 20+ features implemented
2. ✅ No file/type limit violations
3. ✅ Test coverage > 85%
4. ✅ Documentation complete
5. ✅ No performance regressions
6. ✅ Example workflows documented

---

**END PLAN C**
