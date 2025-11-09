# Rhino Libraries — Value‑Add Plan

## Recommendations

1. **Add Value, Don’t Just Wrap**  
   ```csharp
   // Instead of just wrapping:
   Result<Output> Intersect(a, b)

   // Add actual value:
   Result<IntersectionAnalysis> IntersectWithAnalysis(a, b) {
       // Include tangency info, approach angles,
       // intersection quality metrics, etc.
   }
   ```

2. **Provide Escape Hatches**  
   Let users access raw RhinoCommon when needed.
   ```csharp
   public static class DirectAccess {
       public static CurveIntersections RawIntersect(...)
   }
   ```

3. **Consider Progressive Disclosure**
   - Simple API for common cases  
     ```csharp
     Point3d[] QuickIntersect(Curve a, Curve b)
     ```
   - Full API for complex cases  
     ```csharp
     Result<IntersectionOutput> Intersect(...)
     ```

4. **Add Real‑World Utilities**
   - Intersection clustering / deduplication
   - Tolerance‑aware intersection merging
   - Intersection event ordering along curves
   - Self‑intersection detection with classifications


---

## libs/rhino/topology/ — Graph Analysis & Repair

```csharp
public static class TopologyAnalysis {

    // Find and diagnose topology problems with detailed diagnostics
    Result<TopologyDiagnosis> DiagnoseTopology(Brep brep, [GeometryContext ctx]) =>
        // Edge pairs that are "almost" joined (within 10× tolerance)
        // Report probable cause (tolerance, trimming, etc.)
        // Suggest repair strategies ranked by likelihood of success

    // Intelligent topology repair with rollback on failure
    Result<Brep> HealTopology(Brep brep, HealingStrategy strategy) =>
        // Progressive healing: try least invasive first
        // Return detailed report of what was fixed
        // Automatic rollback if topology becomes invalid

    // Extract topological features
    Result<TopologicalFeatures> ExtractFeatures(Brep brep) =>
        // Holes, handles, genus calculation
        // Sheet/solid classification with confidence score
        // Identify probable design intent (revolved, extruded, lofted)
}
```

---

## libs/rhino/spatial/ — Spatial Relationships & Proximity

```csharp
public static class SpatialRelationships {

    // Spatial relationships that RhinoCommon doesn't provide directly
    Result<SpatialCluster[]> ClusterByProximity<T>(
        IEnumerable<T> geometry,
        ClusteringStrategy strategy) where T : GeometryBase =>
        // Strategies: K‑means, hierarchical clustering
        // Cluster sizes with statistical confidence
        // Detect outliers and boundary cases

    // Compute medial axis/skeleton with error bounds
    Result<TopologyAxes> ComputeMedialAxis(Brep brep, MedialAxisOptions opts) =>
        // Exact skeleton for planar shapes
        // Approximated skeletons for volumes
        // Local stability measure (sensitivity to small perturbations)

    // Directional proximity queries
    Result<ProximityField> ComputeProximityField(
        GeometryBase[] geom,
        Vector3d direction,
        ProximityOptions opts) =>
        // “What’s nearby in this direction?”
        // Weighted by angle deviation
        // Useful for sight lines, solar access, etc.
}
```

---

## libs/rhino/analysis/ — Geometric Quality Metrics

```csharp
public static class GeometricQuality {

    // Deep geometric analysis beyond basic properties
    Result<SurfaceQuality> AnalyzeSurfaceQuality(Surface surface) =>
        // Gaussian/mean curvature distribution statistics
        // Identify regions of high curvature variation
        // Detect near‑singularities and degenerate regions
        // Rate surface for manufacturing processes (milling, 3D printing, etc.)

    // Curve fairness analysis
    Result<CurveFairnessAnalysis> AnalyzeCurveFairness(Curve curve) =>
        // Curvature‑comb smoothness score
        // Identify inflection points and their sharpness
        // Suggest control‑point adjustments for smoother curves
        // Energy‑based fairness metrics

    // Mesh quality for simulation
    Result<MeshQuality> AnalyzeForFEA(Mesh mesh) =>
        // Element aspect ratios, skewness, Jacobian
        // Identify problematic elements for simulation
        // Suggest remeshing strategies
        // Support for different simulation types
}
```

---

## libs/rhino/intersection/ — Intersection Analysis

```csharp
public static class IntersectionAnalysis {

    // Advanced intersection analysis beyond just finding intersections
    Result<IntersectionClassification> ClassifyIntersection(
        GeometryBase a,
        GeometryBase b,
        IntersectionOutput raw) =>
        // Tangency vs. transverse
        // Approach and departure angles
        // Grazing (near miss) vs. crossing
        // Suitability for smooth blending

    // Near‑miss quality analysis
    Result<NearMiss> FindNearMisses(
        GeometryBase a,
        GeometryBase b,
        double searchRadius) =>
        // Points that almost intersect (within tolerance band)
        // Closest‑approach points with distance
        // Useful for clash detection with clearance

    // Intersection stability analysis
    Result<Stability> AnalyzeStability(
        GeometryBase a,
        GeometryBase b,
        IntersectionOutput intersection) =>
        // How much does intersection change with small perturbations?
        // Condition number of intersection
        // Identify unstable/grazing intersections
}
```

### Intersection API — Additional Recommendations

- Provide **progressive disclosure**: `QuickIntersect(Curve a, Curve b)` for common cases and `Intersect(...)` for full control.  
- Expose **raw RhinoCommon access** via a `DirectAccess.RawIntersect(...)` escape hatch.  
- Add **utilities**: clustering/deduping of events, tolerance‑aware merging, ordering along curves, and self‑intersection detection with classifications.  
- Prefer **value‑add** methods such as `Result<IntersectionAnalysis> IntersectWithAnalysis(a, b)` that include tangency info, approach angles, and quality metrics.


---

## libs/rhino/orientation/ — Advanced Spatial Transformations

```csharp
public static class OrientationAnalysis {

    // Intelligent orientation and alignment operations
    Result<OptimalOrientation> OptimizeOrientation(
        Brep brep,
        OrientationCriteria criteria) =>
        // Minimize material waste (bounding box)
        // Maximize stability (center of mass vs base)
        // Optimize for 3‑axis milling
        // Best fit to world axes

    // Compute orientation relationships
    Result<RelativeOrientation> ComputeRelativeOrientation(
        GeometryBase a,
        GeometryBase b) =>
        // Relative twist, tilt, and rotation
        // Best‑fit transformation between geometries
        // Symmetry relationships
        // Parallel / perpendicular / skew classification

    // Pattern detection and alignment
    Result<PatternAlignment> DetectAndAlign(GeometryBase[] geometry) =>
        // Detect linear, circular, grid patterns
        // Quantify anomalies/deviations
        // Suggest corrections to perfect the pattern
}
```

---

## libs/rhino/extraction/ — Feature & Pattern Extraction

```csharp
public static class FeatureExtraction {

    // Extract high‑level features from geometry
    Result<DesignFeatures> ExtractFeatures(Brep brep) =>
        // Fillets, chamfers, holes, bosses
        // Feature parameters (radius, depth, etc.)
        // Feature relationships and dependencies

    // Extract mathematical primitives
    Result<PrimitiveDecomposition> DecomposeToPrimitives(GeometryBase geometry) =>
        // Best‑fit planes, cylinders, spheres, cones
        // Confidence scores for each fit
        // Residual geometry that doesn't fit primitives

    // Pattern recognition
    Result<GeometricPattern> ExtractPatterns(GeometryBase[] geometry) =>
        // Symmetries (reflection, rotation, translation)
        // Sequences and progressions
        // Fractals and self‑similarity
}
```

---

## Key Principles for Value‑Add

1. **Solve problems RhinoCommon doesn’t.** Don’t just wrap existing methods. Add analysis, intelligence, and insight.  
2. **Leverage your error handling.** These operations can fail in complex ways.  
3. **Use your validation system.** Complex operations need robust pre‑validation. Your `ValidationRules` pattern is a good fit.  
4. **Unify operations for batching.** Batch operations benefit from a `UnifiedOperation` pattern. Include progress reporting and cancellation.  
5. **Cache expensive computations.** Save intermediate results, indexes, and quality metrics.  
6. **Exploit conditional/walkable patterns.** Your conditional/walkable pipeline composes well here.

**Audience fit:**  
- Engineers needing quality metrics and validation.  
- Designers seeking geometric intelligence.  
- Researchers prototyping geometric algorithms.
