# Rhino Libraries Implementation Blueprint

**Purpose**: Actionable plan to add value-add geometry analysis features to `libs/rhino/`

**Last Updated**: 2025-11-09

---

## Overview

This blueprint defines new features to add across existing `libs/rhino/` folders. Each folder already has the standard 3-file structure (`FolderName.cs`, `FolderNameConfig.cs`, `FolderNameCore.cs`). New features will be added to these existing files while respecting organizational limits.

**Design Principles**:
- Add value, don't just wrap RhinoCommon
- Provide diagnostics, quality metrics, and analysis beyond basic operations
- Leverage existing `libs/core/` infrastructure (Result monad, UnifiedOperation, ValidationRules)
- Follow patterns from `LIBRARY_GUIDELINES.md`
- Comply with all standards in `CLAUDE.md`

---

## Current State

### Folder Structure (libs/rhino/)

| Folder | Files | Public Types | Status | Room to Add |
|--------|-------|--------------|--------|-------------|
| `topology/` | 3 | 11 | **AT LIMIT** | 0 types (already at 10 max) |
| `analysis/` | 3 | 6 | Good | 2-4 types |
| `spatial/` | 3 | 1 | Excellent | 7-9 types |
| `extraction/` | 3 | 2 | Excellent | 6-8 types |
| `intersection/` | 3 | 3 | Excellent | 5-7 types |
| `orientation/` | 3 | 3 | Excellent | 5-7 types |

**CRITICAL**: `topology/` folder is already at type limit. Must consolidate existing types before adding new features.

### Error Code Ranges (libs/core/errors/E.cs)

| Domain | Range | Usage |
|--------|-------|-------|
| Results | 1000-1999 | Core result monad errors |
| Geometry | 2000-2999 | General geometry operations |
| Validation | 3000-3999 | Geometry validation failures |
| Spatial | 4000-4999 | Spatial indexing/proximity |
| **AVAILABLE** | **5000-5999** | **New topology domain** |

---

## Implementation Plan by Folder

### 1. libs/rhino/topology/ — Graph Analysis & Repair

**Status**: REQUIRES CONSOLIDATION FIRST (11/10 types — limit exceeded by 1)

**Consolidation Strategy**:
- Review existing 11 types in `Topology.cs`
- Consolidate related record types that can share fields
- Reduce to 8-9 types before adding new features
- Consider making some types internal if not exposed in public API

**New Features** (add after consolidation):

#### 1.1 DiagnoseTopology
```
Operation: Topology.DiagnoseTopology(Brep, IGeometryContext) → Result<TopologyDiagnosis>
Purpose: Find topology problems with detailed diagnostics
RhinoCommon APIs:
  - Brep.Edges (BrepEdgeList)
  - BrepEdge.EdgeCurveIndex
  - BrepEdge.TrimIndices
  - Curve.ClosestPoints for edge proximity
  - Brep.IsValidTopology
New Types: TopologyDiagnosis record (fields: edge gaps, near-misses, repair suggestions)
Error Codes: 5001 (topology diagnosis failed), 5002 (topology too complex)
Validation: V.Standard | V.Topology
```

#### 1.2 HealTopology
```
Operation: Topology.HealTopology(Brep, HealingStrategy, IGeometryContext) → Result<Brep>
Purpose: Intelligent topology repair with rollback on failure
RhinoCommon APIs:
  - Brep.JoinEdges
  - Brep.JoinNakedEdges
  - Brep.Repair (with tolerance scaling)
  - Brep.RemoveHoles
  - Brep.IsValidTopology for verification
New Types: HealingStrategy enum (Conservative, Moderate, Aggressive), HealingReport record
Error Codes: 5010 (healing failed), 5011 (healing made topology worse)
Validation: V.Standard | V.Topology
Pattern: Progressive healing (try least invasive first), automatic rollback if invalid
```

#### 1.3 ExtractTopologicalFeatures
```
Operation: Topology.ExtractFeatures(Brep, IGeometryContext) → Result<TopologicalFeatures>
Purpose: Extract holes, handles, genus, design intent
RhinoCommon APIs:
  - Brep.Loops (BrepLoopList) for hole detection
  - Euler characteristic (V - E + F) for genus
  - Brep.IsSolid, Brep.Faces.Count
  - VolumeMassProperties for solid vs shell classification
New Types: TopologicalFeatures record (fields: genus, holes[], handles[], sheet/solid classification)
Error Codes: 5020 (feature extraction failed)
Validation: V.Standard | V.Topology
```

**Implementation Location**: Add to existing `Topology.cs`, `TopologyCore.cs`, `TopologyConfig.cs`

**New Validation Modes**: None needed (use existing V.Topology)

---

### 2. libs/rhino/spatial/ — Advanced Spatial Operations

**Status**: EXCELLENT (1/10 types — plenty of room)

**New Features**:

#### 2.1 ClusterByProximity
```
Operation: Spatial.ClusterByProximity<T>(IReadOnlyList<T>, ClusteringStrategy, IGeometryContext) → Result<SpatialCluster[]>
Purpose: K-means or DBSCAN clustering of geometry by proximity
RhinoCommon APIs:
  - RTree for spatial indexing (already used in Spatial.cs)
  - Point3d distance calculations
  - BoundingBox centroid for cluster centers
New Types: ClusteringStrategy enum (KMeans, DBSCAN, Hierarchical), SpatialCluster record (members[], centroid, radius)
Error Codes: 4100 (clustering failed), 4101 (invalid k), 4102 (invalid epsilon)
Validation: V.Standard for geometry items
Pattern: Use existing RTree cache, iterative refinement
```

#### 2.2 ComputeMedialAxis
```
Operation: Spatial.ComputeMedialAxis(Brep, MedialAxisOptions, IGeometryContext) → Result<MedialAxisResult>
Purpose: Skeleton computation with error bounds
RhinoCommon APIs:
  - Brep.Edges for boundary extraction
  - Curve offsetting for planar shapes
  - Distance fields via RTree proximity
  - Mesh from Brep for volumetric skeleton approximation
New Types: MedialAxisOptions struct (tolerance, planarOnly), MedialAxisResult record (skeleton curves[], stability[])
Error Codes: 4200 (medial axis failed), 4201 (non-planar not supported)
Validation: V.Standard | V.Topology
```

#### 2.3 ComputeProximityField
```
Operation: Spatial.ComputeProximityField(GeometryBase[], Vector3d, ProximityOptions, IGeometryContext) → Result<ProximityField>
Purpose: Directional proximity queries ("what's nearby in this direction?")
RhinoCommon APIs:
  - RTree for spatial indexing
  - Ray-geometry intersection for directional queries
  - Vector3d.VectorAngle for angle weighting
New Types: ProximityOptions struct (maxDistance, angleWeight), ProximityField record (query results by direction)
Error Codes: 4300 (proximity field failed), 4301 (invalid direction)
Validation: V.Standard
Pattern: Extend existing spatial indexing dispatch
```

**Implementation Location**: Add to existing `Spatial.cs`, `SpatialCore.cs`, `SpatialConfig.cs`

**New Validation Modes**: None needed (use existing V.Standard, V.Topology)

---

### 3. libs/rhino/analysis/ — Geometric Quality Metrics

**Status**: GOOD (6/10 types — room for 2-4 more)

**New Features**:

#### 3.1 AnalyzeSurfaceQuality
```
Operation: Analysis.AnalyzeSurfaceQuality(Surface, IGeometryContext) → Result<SurfaceQualityData>
Purpose: Deep geometric analysis (curvature distribution, singularities, manufacturing ratings)
RhinoCommon APIs:
  - Surface.CurvatureAt (Gaussian, mean, principal)
  - Surface evaluation across UV grid
  - AreaMassProperties for distribution statistics
  - Surface.IsSingular for degeneracy detection
New Types: SurfaceQualityData record (extends IResult interface)
Fields: curvature distribution stats, high-variation regions[], singularities[], manufacturing rating
Error Codes: Use existing 2311 (surface analysis failed)
Validation: V.Standard | V.UVDomain
Pattern: Follow existing Analysis.CurveData / SurfaceData pattern
```

#### 3.2 AnalyzeCurveFairness
```
Operation: Analysis.AnalyzeCurveFairness(Curve, IGeometryContext) → Result<CurveFairnessData>
Purpose: Curvature-comb analysis, inflection points, fairness score
RhinoCommon APIs:
  - Curve.CurvatureAt sampled along length
  - Curve.InflectionPoints
  - Curve.DerivativeAt for curvature rate of change
  - Energy-based metrics via arc-length integration
New Types: CurveFairnessData record (extends IResult)
Fields: fairness score, inflection points[], curvature-comb samples[], smoothness rating
Error Codes: Use existing 2310 (curve analysis failed)
Validation: V.Standard | V.Degeneracy
Pattern: Follow existing Analysis.CurveData pattern with additional metrics
```

#### 3.3 AnalyzeForFEA
```
Operation: Analysis.AnalyzeForFEA(Mesh, IGeometryContext) → Result<MeshQualityData>
Purpose: Mesh quality metrics for simulation (aspect ratio, skewness, Jacobian)
RhinoCommon APIs:
  - Mesh.Faces, Mesh.Vertices
  - MeshFace geometric properties
  - Edge length calculations
  - Face normal consistency
New Types: MeshQualityData record (extends IResult)
Fields: aspect ratios[], skewness[], problematic elements[], suggested remeshing regions[]
Error Codes: Use existing 2313 (mesh analysis failed)
Validation: V.Standard | V.MeshSpecific
Pattern: Follow existing Analysis pattern with mesh-specific metrics
```

**Implementation Location**: Add to existing `Analysis.cs`, `AnalysisCore.cs`, `AnalysisConfig.cs`

**New Validation Modes**: None needed (use existing modes)

**Config Updates**: Add constants to `AnalysisConfig.cs` for sampling parameters (UV grid density, curvature sample count, etc.)

---

### 4. libs/rhino/intersection/ — Intersection Analysis

**Status**: EXCELLENT (3/10 types — room for 5-7 more)

**New Features**:

#### 4.1 ClassifyIntersection
```
Operation: Intersection.ClassifyIntersection(GeometryBase, GeometryBase, IntersectionOutput, IGeometryContext) → Result<IntersectionClassification>
Purpose: Classify tangency vs transverse, approach angles, grazing vs crossing
RhinoCommon APIs:
  - CurveIntersections (from existing intersection operations)
  - Curve.TangentAt / Surface.NormalAt at intersection points
  - Vector3d.VectorAngle for approach angles
  - Second derivatives for curvature at intersection
New Types: IntersectionClassification record (type: tangent/transverse, approach angles[], grazing flag, blend suitability score)
Error Codes: 2210 (classification failed), 2211 (insufficient intersection data)
Validation: V.Standard
Pattern: Post-process existing intersection results
```

#### 4.2 FindNearMisses
```
Operation: Intersection.FindNearMisses(GeometryBase, GeometryBase, double searchRadius, IGeometryContext) → Result<NearMissData>
Purpose: Almost-intersecting detection within tolerance band
RhinoCommon APIs:
  - Curve.ClosestPoint / Surface.ClosestPoint
  - RTree for broad-phase culling
  - Distance calculations with tolerance scaling
New Types: NearMissData record (near-miss locations[], distances[], closest approach points[])
Error Codes: 2220 (near-miss search failed), 2221 (invalid search radius)
Validation: V.Standard
Pattern: Similar to spatial proximity but geometry-to-geometry
```

#### 4.3 AnalyzeStability
```
Operation: Intersection.AnalyzeStability(GeometryBase, GeometryBase, IntersectionOutput, IGeometryContext) → Result<StabilityData>
Purpose: How much does intersection change with small perturbations?
RhinoCommon APIs:
  - Perform intersection with perturbed geometry
  - Transform.Translation for perturbation
  - Compare intersection results
  - Condition number estimation via derivative analysis
New Types: StabilityData record (stability score, perturbation sensitivity, unstable intersection flags[])
Error Codes: 2230 (stability analysis failed)
Validation: V.Standard
Pattern: Iterative perturbation testing
```

**Implementation Location**: Add to existing `Intersect.cs`, `IntersectionCore.cs`, `IntersectionConfig.cs`

**New Validation Modes**: None needed (use existing V.Standard)

---

### 5. libs/rhino/orientation/ — Advanced Spatial Transformations

**Status**: EXCELLENT (3/10 types — room for 5-7 more)

**New Features**:

#### 5.1 OptimizeOrientation
```
Operation: Orient.OptimizeOrientation(Brep, OrientationCriteria, IGeometryContext) → Result<OptimalOrientation>
Purpose: Find best orientation for manufacturing, stability, or minimal bounding box
RhinoCommon APIs:
  - Brep.GetBoundingBox with world planes
  - VolumeMassProperties.Compute for center of mass
  - Principal component analysis via eigen decomposition of inertia tensor
  - Transform.Rotation for testing orientations
New Types: OrientationCriteria flags enum (MinimizeBBox, MaximizeStability, Align3Axis, OptimizeMilling),
           OptimalOrientation record (transform, score, criteria met[])
Error Codes: Use existing 2500-2509 range
Validation: V.Standard | V.MassProperties
Pattern: Iterative optimization with scoring function
```

#### 5.2 ComputeRelativeOrientation
```
Operation: Orient.ComputeRelativeOrientation(GeometryBase, GeometryBase, IGeometryContext) → Result<RelativeOrientation>
Purpose: Relative twist, tilt, rotation; best-fit transformation; symmetry relationships
RhinoCommon APIs:
  - Transform.PlaneToPlane for alignment
  - VolumeMassProperties centroids
  - Principal axis computation
  - Symmetry detection via reflection testing
New Types: RelativeOrientation record (relative transform, twist/tilt angles, symmetry type, parallel/perpendicular/skew classification)
Error Codes: 2520 (relative orientation failed), 2521 (geometries too dissimilar)
Validation: V.Standard
```

#### 5.3 DetectAndAlign
```
Operation: Orient.DetectAndAlign(GeometryBase[], IGeometryContext) → Result<PatternAlignment>
Purpose: Detect linear, circular, grid patterns; quantify anomalies; suggest corrections
RhinoCommon APIs:
  - Centroid extraction for pattern detection
  - Line/Circle fitting to point arrays
  - Distance from ideal pattern for anomaly detection
  - Transform for alignment suggestion
New Types: PatternAlignment record (pattern type, ideal transform[], anomalies[], deviation statistics)
Error Codes: 2530 (pattern detection failed), 2531 (no pattern found)
Validation: V.Standard
```

**Implementation Location**: Add to existing `Orient.cs`, `OrientCore.cs`, `OrientConfig.cs`

**New Validation Modes**: None needed (use existing modes)

---

### 6. libs/rhino/extraction/ — Feature & Pattern Extraction

**Status**: EXCELLENT (2/10 types — room for 6-8 more)

**New Features**:

#### 6.1 ExtractDesignFeatures
```
Operation: Extract.ExtractFeatures(Brep, IGeometryContext) → Result<DesignFeatures>
Purpose: Extract fillets, chamfers, holes, bosses with parameters
RhinoCommon APIs:
  - Brep.Edges for fillet detection (G2 continuity testing)
  - BrepEdge.EdgeCurve for chamfer angle detection
  - Brep.Loops for hole detection
  - Extrusion detection via BrepFace.IsSurface(SurfaceType.Extrusion)
New Types: DesignFeatures record (fillets[], chamfers[], holes[], bosses[], feature relationships[])
           FilletFeature, ChamferFeature, HoleFeature, BossFeature records
Error Codes: 2600 (feature extraction failed), 2601 (feature classification failed)
Validation: V.Standard | V.Topology
Pattern: Heuristic detection with confidence scores
```

#### 6.2 DecomposeToPrimitives
```
Operation: Extract.DecomposeToPrimitives(GeometryBase, IGeometryContext) → Result<PrimitiveDecomposition>
Purpose: Best-fit planes, cylinders, spheres, cones with confidence scores
RhinoCommon APIs:
  - Plane.FitPlaneToPoints for planar detection
  - Cylinder.FitCylinderToPoints (if available, else custom)
  - Sphere.FitSphereToPoints (custom implementation)
  - Surface type detection for NURBS primitives
New Types: PrimitiveDecomposition record (primitives[], confidence[], residual geometry[])
           PrimitiveShape discriminated union (Plane/Cylinder/Sphere/Cone)
Error Codes: 2610 (decomposition failed), 2611 (no primitives detected)
Validation: V.Standard
```

#### 6.3 ExtractPatterns
```
Operation: Extract.ExtractPatterns(GeometryBase[], IGeometryContext) → Result<GeometricPattern>
Purpose: Symmetries (reflection, rotation, translation), sequences, fractals
RhinoCommon APIs:
  - Transform testing for symmetry detection
  - Centroid comparison for translation patterns
  - Rotation testing around computed axes
  - Self-similarity testing at multiple scales
New Types: GeometricPattern record (pattern type, symmetry axis/plane, sequence parameters, self-similarity scale)
Error Codes: 2620 (pattern extraction failed), 2621 (no pattern detected)
Validation: V.Standard
```

**Implementation Location**: Add to existing `Extract.cs`, `ExtractionCore.cs`, `ExtractionConfig.cs`

**New Validation Modes**: None needed (use existing modes)

---

## Error Code Registry Updates

**File**: `libs/core/errors/E.cs`

### New Domain: Topology (5000-5999)

Add to `GetDomain` switch expression:
```csharp
>= 5000 and < 6000 => ErrorDomain.Topology,
```

Add new static class:
```csharp
public static class Topology {
    public static readonly SystemError DiagnosisFailed = Get(5001);
    public static readonly SystemError TopologyTooComplex = Get(5002);
    public static readonly SystemError HealingFailed = Get(5010);
    public static readonly SystemError HealingMadeWorse = Get(5011);
    public static readonly SystemError FeatureExtractionFailed = Get(5020);
}
```

### Extend Spatial Domain (4000-4999)

```csharp
public static class Spatial {
    // ... existing errors ...
    public static readonly SystemError ClusteringFailed = Get(4100);
    public static readonly SystemError InvalidK = Get(4101);
    public static readonly SystemError InvalidEpsilon = Get(4102);
    public static readonly SystemError MedialAxisFailed = Get(4200);
    public static readonly SystemError NonPlanarNotSupported = Get(4201);
    public static readonly SystemError ProximityFieldFailed = Get(4300);
    public static readonly SystemError InvalidDirection = Get(4301);
}
```

### Extend Geometry Domain (2000-2999)

```csharp
public static class Geometry {
    // ... existing errors ...
    public static readonly SystemError ClassificationFailed = Get(2210);
    public static readonly SystemError InsufficientIntersectionData = Get(2211);
    public static readonly SystemError NearMissSearchFailed = Get(2220);
    public static readonly SystemError InvalidSearchRadius = Get(2221);
    public static readonly SystemError StabilityAnalysisFailed = Get(2230);
    public static readonly SystemError RelativeOrientationFailed = Get(2520);
    public static readonly SystemError GeometriesTooDissimilar = Get(2521);
    public static readonly SystemError PatternDetectionFailed = Get(2530);
    public static readonly SystemError NoPatternFound = Get(2531);
    public static readonly SystemError FeatureExtractionFailed = Get(2600);
    public static readonly SystemError FeatureClassificationFailed = Get(2601);
    public static readonly SystemError DecompositionFailed = Get(2610);
    public static readonly SystemError NoPrimitivesDetected = Get(2611);
    public static readonly SystemError PatternExtractionFailed = Get(2620);
    public static readonly SystemError NoPatternDetected = Get(2621);
}
```

### Update Message Dictionary

Add to `_m` dictionary in `E.cs`:
```csharp
[5001] = "Topology diagnosis failed",
[5002] = "Topology is too complex for diagnosis",
[5010] = "Topology healing failed",
[5011] = "Topology healing made geometry worse",
[5020] = "Topological feature extraction failed",
[4100] = "Spatial clustering operation failed",
[4101] = "K-means k parameter must be positive",
[4102] = "DBSCAN epsilon parameter must be positive",
[4200] = "Medial axis computation failed",
[4201] = "Non-planar medial axis not supported",
[4300] = "Proximity field computation failed",
[4301] = "Proximity field direction vector is invalid",
[2210] = "Intersection classification failed",
[2211] = "Insufficient intersection data for classification",
[2220] = "Near-miss search failed",
[2221] = "Invalid search radius for near-miss detection",
[2230] = "Intersection stability analysis failed",
[2520] = "Relative orientation computation failed",
[2521] = "Geometries too dissimilar for orientation comparison",
[2530] = "Pattern detection failed",
[2531] = "No geometric pattern detected",
[2600] = "Design feature extraction failed",
[2601] = "Feature classification failed",
[2610] = "Primitive decomposition failed",
[2611] = "No primitives detected in geometry",
[2620] = "Geometric pattern extraction failed",
[2621] = "No pattern detected in geometry array",
```

---

## Validation Mode Updates

**File**: `libs/core/validation/V.cs`

**Status**: All required validation modes already exist. No changes needed.

**Usage**:
- Topology operations: `V.Standard | V.Topology`
- Spatial operations: `V.Standard`
- Analysis operations: `V.Standard | V.Degeneracy | V.UVDomain | V.MeshSpecific` (as appropriate)
- Intersection operations: `V.Standard`
- Orientation operations: `V.Standard | V.MassProperties`
- Extraction operations: `V.Standard | V.Topology`

---

## Implementation Order with Dependencies

### Phase 1: Foundation (No Dependencies)

**Order**: 2.1, 3.1, 3.2, 4.1, 4.2, 5.1, 6.2

1. **Spatial.ClusterByProximity** — Standalone spatial algorithm
2. **Analysis.AnalyzeSurfaceQuality** — Standalone analysis
3. **Analysis.AnalyzeCurveFairness** — Standalone analysis
4. **Intersection.ClassifyIntersection** — Post-processes existing intersection output
5. **Intersection.FindNearMisses** — Standalone proximity detection
6. **Orient.OptimizeOrientation** — Standalone optimization
7. **Extract.DecomposeToPrimitives** — Standalone primitive fitting

### Phase 2: Dependent Features

**Order**: 2.2, 2.3, 4.3, 5.2, 6.3

8. **Spatial.ComputeMedialAxis** — Depends on spatial indexing (Phase 1 complete)
9. **Spatial.ComputeProximityField** — Depends on clustering (2.1)
10. **Intersection.AnalyzeStability** — Depends on classification (4.1)
11. **Orient.ComputeRelativeOrientation** — Depends on optimization (5.1)
12. **Extract.ExtractPatterns** — Depends on decomposition (6.2)

### Phase 3: Advanced Features

**Order**: 5.3, 6.1, 1.1, 1.2, 1.3

13. **Orient.DetectAndAlign** — Depends on relative orientation (5.2) and patterns (6.3)
14. **Extract.ExtractFeatures** — Depends on decomposition (6.2)
15. **Topology.DiagnoseTopology** — REQUIRES consolidation first
16. **Topology.HealTopology** — Depends on diagnosis (1.1)
17. **Topology.ExtractFeatures** — Depends on diagnosis (1.1)

---

## Shared Utilities

### Should Anything Go to libs/core/?

**Answer**: NO. All features are geometry-specific and belong in `libs/rhino/`.

**Rationale**:
- `libs/core/` is for domain-agnostic primitives (Result, UnifiedOperation, Validation)
- All new features require RhinoCommon types and geometric knowledge
- Pattern detection, clustering, and analysis are inherently geometric

### Should Anything Go to libs/rhino/shared/?

**Answer**: NO. No shared folder needed.

**Rationale**:
- Each folder has its own Config file for shared constants
- FrozenDictionary dispatch eliminates need for shared dispatch logic
- No cross-folder dependencies identified
- Shared utilities would violate single-responsibility principle

---

## Implementation Patterns Reference

**DO NOT INCLUDE CODE SNIPPETS**. Instead, reference existing implementations:

### For Polymorphic Dispatch
- See `libs/rhino/spatial/Spatial.cs` — FrozenDictionary dispatch with type pairs
- See `libs/rhino/analysis/AnalysisCore.cs` — Strategy pattern with validation

### For Result Types with IResult Marker
- See `libs/rhino/analysis/Analysis.cs` — IResult interface, CurveData/SurfaceData records
- See `libs/rhino/topology/Topology.cs` — Multiple record types implementing IResult

### For Configuration Enums/Structs
- See `libs/rhino/topology/TopologyConfig.cs` — EdgeContinuityType enum pattern
- See `libs/rhino/spatial/SpatialConfig.cs` — Constants for buffer sizes

### For ArrayPool Buffer Management
- See `libs/rhino/spatial/SpatialCore.cs` — ExecuteRangeSearch method
- Pattern: Rent, try-finally, Return with clearArray: true

### For ConditionalWeakTable Caching
- See `libs/rhino/spatial/Spatial.cs` — TreeCache pattern
- Pattern: Define cache, use GetValue with factory callback

### For UnifiedOperation Integration
- See `libs/rhino/analysis/Analysis.cs` — Public API methods
- Pattern: Thin orchestration calling UnifiedOperation with OperationConfig

### For FrozenDictionary Dispatch
- See `libs/rhino/spatial/SpatialCore.cs` — OperationRegistry with complex tuples
- See `libs/rhino/analysis/AnalysisCore.cs` — Strategy pattern dispatch

---

## Organizational Compliance Checklist

Before implementing any feature:

- [ ] **Topology folder**: Consolidate from 11 to 8-9 types first
- [ ] **File count**: Each folder stays at 3 files (do not add 4th file)
- [ ] **Type count**: Each folder stays under 10 types total
- [ ] **Member LOC**: Each method stays under 300 LOC (improve algorithm if needed)
- [ ] **Error codes**: Add to `libs/core/errors/E.cs` with appropriate domain
- [ ] **Validation modes**: Use existing modes from `V.cs` (no new modes needed)
- [ ] **No var**: Explicit types everywhere
- [ ] **No if/else**: Use ternary, switch expressions, pattern matching
- [ ] **Named parameters**: For all non-obvious arguments
- [ ] **Trailing commas**: All multi-line collections
- [ ] **Target-typed new**: Use `new()` not `new Type()`
- [ ] **Collection expressions**: Use `[]` not `new List<T>()`

---

## Testing Strategy

### Unit Tests (test/rhino/)

Each new feature requires:
- **Positive cases**: Valid inputs produce expected results
- **Negative cases**: Invalid inputs produce appropriate errors
- **Edge cases**: Empty collections, degenerate geometry, extreme parameters
- **Validation integration**: Ensure ValidationRules are triggered correctly

### Integration Tests

- **RhinoCommon compatibility**: Ensure APIs work with real Rhino geometry
- **Performance**: Benchmark against RhinoCommon baseline
- **Memory**: Verify ArrayPool returns, cache behavior, no leaks

### Property-Based Tests (CsCheck)

Where applicable:
- **Invariants**: Clustering should preserve all input items
- **Reversibility**: Orientation transforms should be invertible
- **Stability**: Small input changes should produce small output changes

---

## Documentation Requirements

### XML Documentation

All public API methods require:
- `<summary>` describing operation and value-add over RhinoCommon
- `<param>` for each parameter with valid ranges
- `<returns>` describing Result success/failure semantics
- `<remarks>` for performance characteristics or limitations

### DebuggerDisplay Attributes

All new record types require:
```csharp
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record MyData(...) {
    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"...");
}
```

---

## Success Criteria

Implementation complete when:

1. ✅ All 18 features implemented across 6 folders
2. ✅ Topology folder consolidated to ≤10 types
3. ✅ All folders remain at 3 files each
4. ✅ All error codes added to `E.cs`
5. ✅ All features use existing validation modes
6. ✅ `dotnet build` succeeds with zero warnings
7. ✅ `dotnet test` passes all tests
8. ✅ No `var`, no `if`/`else`, all standards complied
9. ✅ All code reviewed against `LIBRARY_GUIDELINES.md` patterns
10. ✅ Documentation complete for all public APIs

---

## References

- **Feature Requirements**: `libs/rhino/new_libs_functionality.md`
- **Coding Standards**: `/CLAUDE.md`
- **Architecture Patterns**: `libs/rhino/LIBRARY_GUIDELINES.md`
- **Error Registry**: `libs/core/errors/E.cs`
- **Validation Modes**: `libs/core/validation/V.cs`
- **Exemplar Code**: `libs/rhino/spatial/Spatial.cs`, `libs/rhino/analysis/Analysis.cs`

---

**Next Steps**: 
1. Consolidate topology folder types (11 → 8-9)
2. Implement Phase 1 features (foundation, no dependencies)
3. Implement Phase 2 features (dependent on Phase 1)
4. Implement Phase 3 features (advanced, topology last)
5. Verify all organizational limits and standards
6. Complete testing and documentation
