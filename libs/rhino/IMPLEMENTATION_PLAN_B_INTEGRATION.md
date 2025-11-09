# Implementation Plan B: Consistent Integration Pattern

**Document Purpose**: Defines the architectural pattern for integrating advanced features into existing `libs/rhino/` folders without violating file/type limits.

**Target**: All 6 existing folders (spatial, analysis, extraction, intersection, orientation, topology)

**Dependencies**: Requires Plan A (Foundation) to be completed first

---

## Executive Summary

The challenge: Add 5-10 new methods per folder from `new_libs_functionality.md` without:
- ❌ Exceeding 4-file limit per folder
- ❌ Exceeding 10-type limit per folder
- ❌ Duplicating logic across folders
- ❌ Violating 300 LOC per member limit

**Solution**: **Vertical Feature Slicing** pattern where each existing 3-file folder adds advanced features by:
1. Extending main file with new public API methods
2. Extending config file with new FrozenDictionary mappings
3. Extending core file with new dispatch handlers
4. Delegating complex logic to `libs/core/quality/`, `libs/core/patterns/`, `libs/rhino/shared/`

**Key Insight**: Current folders are already at 3 files. We can add ~10-15 methods per folder WITHOUT adding files by:
- Using extension pattern: new methods delegate to foundation infrastructure
- Using nested types within existing classes for configuration
- Using FrozenDictionary expansion for new type mappings
- Keeping LOC under 300 by pushing algorithms to `libs/core/` or `libs/rhino/shared/`

---

## B1: Integration Architecture Pattern

### B1.1: Vertical Feature Slicing Model

```
┌─────────────────────────────────────────────────────────────┐
│ libs/rhino/[folder]/[Folder].cs (Main File)                 │
│                                                              │
│ NEW METHODS:                                                 │
│ • Advanced[Operation](...)  →  delegates to Core             │
│ • [Operation]WithQuality(...) → delegates to quality/       │
│ • Classify[Operation](...) → delegates to shared/            │
│ • Detect[Pattern](...) → delegates to patterns/              │
│                                                              │
│ CHARACTERISTICS:                                             │
│ • Thin orchestration (5-15 LOC per method)                   │
│ • Named parameters, Result<T> returns                        │
│ • Type-specific overloads for ergonomics                     │
│ • XML documentation                                          │
└─────────────────────────────────────────────────────────────┘
         ↓ delegates to
┌─────────────────────────────────────────────────────────────┐
│ libs/rhino/[folder]/[Folder]Core.cs (Core File)             │
│                                                              │
│ EXTENDED DISPATCH REGISTRY:                                 │
│ • _advancedOperations FrozenDictionary                       │
│ • _qualityStrategies FrozenDictionary                        │
│ • _classificationHandlers FrozenDictionary                   │
│                                                              │
│ NEW INTERNAL METHODS:                                        │
│ • ExecuteAdvanced(...) → calls shared utilities              │
│ • ComputeQuality(...) → calls quality metrics                │
│ • ClassifyResult(...) → calls pattern detection              │
│                                                              │
│ CHARACTERISTICS:                                             │
│ • Internal only, not exposed                                 │
│ • 50-150 LOC per method (under 300 limit)                    │
│ • Heavy delegation to libs/core/ and libs/rhino/shared/      │
└─────────────────────────────────────────────────────────────┘
         ↓ delegates to
┌─────────────────────────────────────────────────────────────┐
│ libs/core/quality/, libs/core/patterns/,                     │
│ libs/rhino/shared/                                           │
│                                                              │
│ ACTUAL IMPLEMENTATIONS:                                      │
│ • Quality metric computation (100-250 LOC)                   │
│ • Pattern detection algorithms (100-250 LOC)                 │
│ • Medial axis, primitive fitting (100-250 LOC)               │
│                                                              │
│ CHARACTERISTICS:                                             │
│ • Reusable across all folders                                │
│ • Dense, algorithmic code                                    │
│ • Thoroughly tested in isolation                             │
└─────────────────────────────────────────────────────────────┘
```

### B1.2: Method Naming Convention

To distinguish new advanced features from existing operations, use consistent naming:

```csharp
// Pattern 1: Quality-enhanced operations (adds metrics)
public static Result<(TResult Result, QualityMetrics.FairnessScore Quality)> AnalyzeWithQuality<T>(...)
public static Result<(TResult Result, QualityMetrics.CurvatureDistribution Curvature)> ExtractWithCurvature<T>(...)

// Pattern 2: Classification/diagnosis operations
public static Result<IntersectionClassification> ClassifyIntersection(...)
public static Result<TopologyDiagnosis> DiagnoseTopology(...)
public static Result<NearMiss> FindNearMisses(...)

// Pattern 3: Advanced/optimized operations
public static Result<TResult> OptimizeOrientation(...)
public static Result<TResult> HealTopology(...)
public static Result<IReadOnlyList<Cluster<T>>> ClusterByProximity<T>(...)

// Pattern 4: Pattern detection operations
public static Result<IReadOnlyList<Symmetry>> DetectSymmetries<T>(...)
public static Result<IReadOnlyList<GeometricFeature>> ExtractFeatures(...)
public static Result<IReadOnlyList<PrimitiveFit>> DecomposeToPrimitives<T>(...)
```

**Rationale**: 
- Clear semantic intent without ambiguity
- Namespace doesn't collide with existing methods
- Easy to discover via IntelliSense
- Follows existing naming patterns

---

## B2: Folder-by-Folder Integration Strategy

### B2.1: libs/rhino/spatial/ Extensions

**Current State**: 3 files, ~6 types, basic RTree spatial queries

**New Features to Add** (from new_libs_functionality.md):
1. Spatial clustering (K-means, hierarchical)
2. Medial axis computation
3. Directional proximity fields
4. Near-miss detection

**Integration Approach**:

**spatial/Spatial.cs** (Main File - ADD methods):
```csharp
/// <summary>Clusters geometry by spatial proximity using specified algorithm.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<PatternDetection.Cluster<T>>> ClusterByProximity<T>(
    IReadOnlyList<T> geometries,
    ClusteringAlgorithm algorithm,
    IGeometryContext context,
    int? targetClusters = null,
    bool enableDiagnostics = false) where T : GeometryBase =>
    UnifiedOperation.Apply(
        input: geometries,
        operation: (Func<IReadOnlyList<T>, Result<IReadOnlyList<PatternDetection.Cluster<T>>>>)(
            items => PatternDetection.ClusterByProximity(items, algorithm, context, targetClusters)),
        config: new OperationConfig<IReadOnlyList<T>, PatternDetection.Cluster<T>> {
            Context = context,
            ValidationMode = V.Standard,
            OperationName = "Spatial.ClusterByProximity",
            EnableDiagnostics = enableDiagnostics,
        });

/// <summary>Computes medial axis for planar/volume geometry.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<RhinoUtilities.MedialAxis> ComputeMedialAxis(
    Brep brep,
    MedialAxisMode mode,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    ResultFactory.Create(value: brep)
        .Validate(args: [context, V.Standard | V.Topology])
        .Bind(b => RhinoUtilities.ComputeMedialAxis(b, mode, context));

/// <summary>Computes directional proximity field for sight lines/solar access.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<double[]> ComputeProximityField(
    GeometryBase[] geometries,
    Point3d[] samplePoints,
    Vector3d direction,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    (geometries, samplePoints, direction) switch {
        ({ Length: 0 }, _, _) => ResultFactory.Create<double[]>(
            error: E.Spatial.InvalidGeometryCollection),
        (_, { Length: 0 }, _) => ResultFactory.Create<double[]>(
            error: E.Spatial.InvalidSamplePoints),
        (_, _, Vector3d d) when d.Length < context.AbsoluteTolerance => 
            ResultFactory.Create<double[]>(error: E.Geometry.InvalidDirection),
        _ => RhinoUtilities.ComputeProximityField(geometries, samplePoints, direction, context),
    };

/// <summary>Finds near-miss intersections within search radius.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<NearMiss>> FindNearMisses<TA, TB>(
    TA geometryA,
    TB geometryB,
    double searchRadius,
    IGeometryContext context,
    bool enableDiagnostics = false) where TA : GeometryBase where TB : GeometryBase =>
    searchRadius <= context.AbsoluteTolerance
        ? ResultFactory.Create<IReadOnlyList<NearMiss>>(
            error: E.Spatial.InvalidSearchRadius)
        : SpatialCore.FindNearMisses(geometryA, geometryB, searchRadius, context);
```

**spatial/SpatialConfig.cs** (Config File - ADD constants):
```csharp
// ADD to existing constants
internal const int DefaultClusterCount = 5;
internal const double DefaultClusterTolerance = 1.0;
internal const int MedialAxisSampleCount = 50;
internal const double ProximityFieldAngleCutoff = 45.0;  // degrees
internal const double NearMissSearchMultiplier = 10.0;  // × tolerance
```

**spatial/SpatialCore.cs** (Core File - ADD handlers):
```csharp
// ADD new internal method
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<IReadOnlyList<NearMiss>> FindNearMisses<TA, TB>(
    TA geometryA,
    TB geometryB,
    double searchRadius,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase {
    // Build RTrees for both geometries
    Result<RTree> treeA = GetTree(geometryA, /* factory */);
    Result<RTree> treeB = GetTree(geometryB, /* factory */);
    
    return treeA.Bind(ta => treeB.Bind(tb => {
        // Query tree A for candidates near tree B
        List<NearMiss> nearMisses = [];
        Sphere searchSphere = new(/* ... */, searchRadius);
        
        ta.Search(searchSphere, (sender, args) => {
            // For each candidate, compute actual closest point
            Point3d closestPt = /* RhinoCommon API */;
            double distance = /* compute distance */;
            
            if (distance <= searchRadius && distance > context.AbsoluteTolerance) {
                nearMisses.Add(new NearMiss(/* ... */));
            }
        });
        
        return ResultFactory.Create(value: (IReadOnlyList<NearMiss>)nearMisses);
    }));
}
```

**Impact Analysis**:
- Files: 3 (no change, within limit)
- Types: 7 (adds `NearMiss` type, within 10 limit)
- LOC per method: All < 50 LOC (delegation pattern)
- Dependencies: `libs/core/patterns/`, `libs/rhino/shared/`

---

### B2.2: libs/rhino/analysis/ Extensions

**Current State**: 3 files, ~8 types, differential geometry analysis

**New Features to Add**:
1. Quality metrics (surface fairness, curvature distribution)
2. Manufacturing suitability analysis
3. FEA mesh quality analysis
4. Geometric feature extraction

**Integration Approach**:

**analysis/Analysis.cs** (Main File - ADD methods):
```csharp
/// <summary>Analyzes surface quality with curvature distribution and fairness.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<(SurfaceData Analysis, QualityMetrics.SurfaceQuality Quality)> AnalyzeWithQuality(
    Surface surface,
    IGeometryContext context,
    (double u, double v)? uvParameter = null,
    bool enableDiagnostics = false) =>
    Analyze(surface, context, uvParameter, derivativeOrder: 2, enableDiagnostics)
        .Bind(surfaceData => QualityMetrics.AnalyzeSurfaceQuality(surface, context)
            .Map(quality => (surfaceData, quality)));

/// <summary>Analyzes curve fairness with energy-based metrics.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<(CurveData Analysis, QualityMetrics.FairnessScore Fairness)> AnalyzeCurveFairness(
    Curve curve,
    IGeometryContext context,
    double? parameter = null,
    bool enableDiagnostics = false) =>
    Analyze(curve, context, parameter, derivativeOrder: 3, enableDiagnostics)
        .Bind(curveData => QualityMetrics.ComputeFairness(curve, context)
            .Map(fairness => (curveData, fairness)));

/// <summary>Evaluates manufacturability (milling, 3D printing).</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<QualityMetrics.ManufacturabilityReport> EvaluateManufacturability(
    Brep brep,
    ManufacturingMode mode,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    ResultFactory.Create(value: brep)
        .Validate(args: [context, V.Standard | V.Topology])
        .Bind(b => QualityMetrics.EvaluateManufacturability(b, mode, context));

/// <summary>Analyzes mesh quality for FEA simulation.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<QualityMetrics.MeshQualityReport> AnalyzeMeshQuality(
    Mesh mesh,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    ResultFactory.Create(value: mesh)
        .Validate(args: [context, V.MeshSpecific | V.MeshQualityFEA])
        .Bind(m => QualityMetrics.AnalyzeMeshQuality(m, context));

/// <summary>Extracts geometric features (fillets, chamfers, holes).</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<RhinoUtilities.GeometricFeature>> ExtractFeatures(
    Brep brep,
    FeatureType searchTypes,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    ResultFactory.Create(value: brep)
        .Validate(args: [context, V.Standard | V.Topology])
        .Bind(b => RhinoUtilities.ExtractFeatures(b, searchTypes, context));
```

**analysis/AnalysisConfig.cs** (Config File - ADD constants):
```csharp
// ADD to existing constants
internal const int QualitySampleCount = 100;
internal const double FairnessThreshold = 0.85;
internal const double ManufacturingToolRadius = 0.5;  // mm
internal const double PrintOverhangAngle = 45.0;  // degrees
internal const double FEAAspectRatioThreshold = 0.3;
```

**analysis/AnalysisCore.cs** (Core File - minimal changes):
```csharp
// Most logic delegated to libs/core/quality/ and libs/rhino/shared/
// Only add validation wrappers if needed
```

**Impact Analysis**:
- Files: 3 (no change)
- Types: 8 (no new types, reuses quality/ types)
- LOC per method: All < 30 LOC (pure delegation)
- Dependencies: `libs/core/quality/`, `libs/rhino/shared/`

---

### B2.3: libs/rhino/extraction/ Extensions

**Current State**: 3 files, ~6 types, point extraction

**New Features to Add**:
1. Feature extraction (fillets, chamfers, holes)
2. Primitive decomposition
3. Pattern recognition

**Integration Approach**:

**extraction/Extraction.cs** (Main File - ADD methods):
```csharp
/// <summary>Extracts geometric features from Brep.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<RhinoUtilities.GeometricFeature>> ExtractFeatures(
    Brep brep,
    FeatureType searchTypes,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    RhinoUtilities.ExtractFeatures(brep, searchTypes, context);

/// <summary>Decomposes geometry into best-fit primitives.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<RhinoUtilities.PrimitiveFit>> DecomposeToPrimitives<T>(
    T geometry,
    PrimitiveType[] primitiveTypes,
    IGeometryContext context,
    double confidenceThreshold = 0.85,
    bool enableDiagnostics = false) where T : GeometryBase =>
    RhinoUtilities.FitPrimitives(geometry, primitiveTypes, context, confidenceThreshold);

/// <summary>Recognizes geometric patterns in collection.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<PatternDetection.Sequence<T>>> RecognizePatterns<T>(
    IReadOnlyList<T> geometries,
    IGeometryContext context,
    bool enableDiagnostics = false) where T : GeometryBase =>
    PatternDetection.DetectSequences(geometries, context);
```

**extraction/ExtractionConfig.cs** (Config File - ADD):
```csharp
// ADD new constants
internal const double PrimitiveConfidenceThreshold = 0.85;
internal const double FeatureMinRadius = 0.1;  // mm
internal const int MaxFeatureCount = 100;
```

**Impact Analysis**:
- Files: 3 (no change)
- Types: 6 (no new types, reuses shared/ and patterns/ types)
- LOC per method: All < 20 LOC (pure delegation)

---

### B2.4: libs/rhino/intersection/ Extensions

**Current State**: 3 files, ~7 types, intersection computation

**New Features to Add**:
1. Intersection classification (tangency, transverse, grazing)
2. Intersection stability analysis
3. Near-miss detection
4. Approach/departure angle computation

**Integration Approach**:

**intersection/Intersection.cs** (Main File - ADD methods):
```csharp
/// <summary>Classifies intersection with tangency and angle analysis.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IntersectionClassification> ClassifyIntersection<TA, TB>(
    TA geometryA,
    TB geometryB,
    IntersectionResult intersection,
    IGeometryContext context,
    bool enableDiagnostics = false) where TA : GeometryBase where TB : GeometryBase =>
    IntersectionCore.ClassifyIntersection(geometryA, geometryB, intersection, context);

/// <summary>Analyzes intersection stability under perturbations.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IntersectionStability> AnalyzeStability<TA, TB>(
    TA geometryA,
    TB geometryB,
    IntersectionResult intersection,
    IGeometryContext context,
    double perturbationScale = 0.01,
    bool enableDiagnostics = false) where TA : GeometryBase where TB : GeometryBase =>
    IntersectionCore.AnalyzeStability(geometryA, geometryB, intersection, context, perturbationScale);

/// <summary>Finds near-miss intersections within tolerance band.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<NearMiss>> FindNearMisses<TA, TB>(
    TA geometryA,
    TB geometryB,
    double searchRadius,
    IGeometryContext context,
    bool enableDiagnostics = false) where TA : GeometryBase where TB : GeometryBase =>
    searchRadius <= context.AbsoluteTolerance
        ? ResultFactory.Create<IReadOnlyList<NearMiss>>(error: E.Geometry.InvalidSearchRadius)
        : SpatialCore.FindNearMisses(geometryA, geometryB, searchRadius, context);
```

**intersection/IntersectionConfig.cs** (Config File - ADD):
```csharp
// ADD new constants
internal const double TangencyAngleThreshold = 5.0;  // degrees
internal const double GrazingAngleThreshold = 10.0;  // degrees
internal const double StabilityPerturbationScale = 0.01;  // 1% of bounding box
internal const int StabilitySampleCount = 10;
```

**intersection/IntersectionCore.cs** (Core File - ADD handlers):
```csharp
// ADD classification logic
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<IntersectionClassification> ClassifyIntersection<TA, TB>(
    TA geometryA,
    TB geometryB,
    IntersectionResult intersection,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase {
    
    // Compute tangent vectors at intersection points
    // Compute approach/departure angles
    // Classify as tangent/transverse/grazing
    // Return classification with confidence
    
    return ResultFactory.Create(value: new IntersectionClassification(/* ... */));
}

// ADD stability analysis
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<IntersectionStability> AnalyzeStability<TA, TB>(
    TA geometryA,
    TB geometryB,
    IntersectionResult baseIntersection,
    IGeometryContext context,
    double perturbationScale) where TA : GeometryBase where TB : GeometryBase {
    
    // Perturb geometries in multiple directions
    // Recompute intersections
    // Measure variation in intersection results
    // Compute condition number
    
    return ResultFactory.Create(value: new IntersectionStability(/* ... */));
}
```

**Impact Analysis**:
- Files: 3 (no change)
- Types: 9 (adds `IntersectionClassification`, `IntersectionStability`, within 10 limit)
- LOC per method: 50-150 LOC (algorithmic, under 300 limit)

---

### B2.5: libs/rhino/orientation/ Extensions

**Current State**: 3 files, ~6 types, geometric alignment

**New Features to Add**:
1. Optimization-based orientation
2. Relative orientation computation
3. Pattern alignment detection
4. Symmetry-aware alignment

**Integration Approach**:

**orientation/Orientation.cs** (Main File - ADD methods):
```csharp
/// <summary>Optimizes orientation based on criteria (waste, stability, milling).</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<OptimalOrientation> OptimizeOrientation(
    Brep brep,
    OrientationCriteria criteria,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    OrientationCore.OptimizeOrientation(brep, criteria, context);

/// <summary>Computes relative orientation between geometries.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<RelativeOrientation> ComputeRelativeOrientation<TA, TB>(
    TA geometryA,
    TB geometryB,
    IGeometryContext context,
    bool enableDiagnostics = false) where TA : GeometryBase where TB : GeometryBase =>
    OrientationCore.ComputeRelativeOrientation(geometryA, geometryB, context);

/// <summary>Detects and aligns geometric patterns.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<PatternAlignment> DetectAndAlignPattern<T>(
    IReadOnlyList<T> geometries,
    IGeometryContext context,
    bool enableDiagnostics = false) where T : GeometryBase =>
    PatternDetection.DetectSequences(geometries, context)
        .Bind(sequences => OrientationCore.AlignToPattern(sequences, context));
```

**orientation/OrientationCore.cs** (Core File - ADD):
```csharp
// ADD optimization logic
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<OptimalOrientation> OptimizeOrientation(
    Brep brep,
    OrientationCriteria criteria,
    IGeometryContext context) {
    
    // Compute bounding box for material waste
    // Compute center of mass for stability
    // Compute undercuts for milling
    // Optimize transform based on criteria weights
    
    return ResultFactory.Create(value: new OptimalOrientation(/* ... */));
}

// ADD relative orientation computation
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<RelativeOrientation> ComputeRelativeOrientation<TA, TB>(
    TA geometryA,
    TB geometryB,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase {
    
    // Compute principal axes for both geometries
    // Compute relative transform
    // Classify relationship (parallel, perpendicular, skew)
    // Compute symmetry relationships
    
    return ResultFactory.Create(value: new RelativeOrientation(/* ... */));
}
```

**Impact Analysis**:
- Files: 3 (no change)
- Types: 9 (adds `OptimalOrientation`, `RelativeOrientation`, `PatternAlignment`, within 10 limit)
- LOC per method: 100-200 LOC (optimization algorithms, under 300 limit)

---

### B2.6: libs/rhino/topology/ Extensions

**Current State**: 3 files, ~8 types, topology analysis and repair

**New Features to Add**:
1. Intelligent topology diagnosis
2. Progressive healing strategies
3. Topological feature extraction
4. Design intent detection

**Integration Approach**:

**topology/Topology.cs** (Main File - ADD methods):
```csharp
/// <summary>Diagnoses topology problems with repair suggestions.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<TopologyDiagnosis> DiagnoseTopology(
    Brep brep,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    TopologyCore.DiagnoseTopology(brep, context);

/// <summary>Heals topology using progressive repair strategies.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<Brep> HealTopology(
    Brep brep,
    HealingStrategy strategy,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    TopologyCore.HealTopology(brep, strategy, context);

/// <summary>Extracts topological features (holes, handles, genus).</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<TopologicalFeatures> ExtractTopologicalFeatures(
    Brep brep,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    TopologyCore.ExtractFeatures(brep, context);

/// <summary>Detects probable design intent from topology.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<DesignIntent> DetectDesignIntent(
    Brep brep,
    IGeometryContext context,
    bool enableDiagnostics = false) =>
    RhinoUtilities.ExtractFeatures(brep, FeatureType.All, context)
        .Bind(features => TopologyCore.InferDesignIntent(features, context));
```

**topology/TopologyCore.cs** (Core File - ADD):
```csharp
// ADD diagnosis logic
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<TopologyDiagnosis> DiagnoseTopology(
    Brep brep,
    IGeometryContext context) {
    
    // Find edge pairs that are "almost" joined (within 10× tolerance)
    // Classify problems (tolerance, trimming, naked edges)
    // Rank repair strategies by likelihood of success
    // Return detailed diagnosis
    
    return ResultFactory.Create(value: new TopologyDiagnosis(/* ... */));
}

// ADD healing logic with rollback
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<Brep> HealTopology(
    Brep brep,
    HealingStrategy strategy,
    IGeometryContext context) {
    
    // Clone brep for rollback safety
    // Apply healing strategies progressively (least invasive first)
    // Validate topology after each step
    // Rollback if topology becomes invalid
    // Return healed brep or error
    
    return ResultFactory.Create(value: healedBrep);
}
```

**Impact Analysis**:
- Files: 3 (no change)
- Types: 10 (adds `TopologyDiagnosis`, `DesignIntent`, at 10 limit)
- LOC per method: 150-250 LOC (complex algorithms, under 300 limit)

---

## B3: Unified API Design Principles

### B3.1: Consistent Method Signatures

All new methods across folders follow this pattern:

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<TOutput> MethodName<TInput>(
    TInput input,
    TConfig config,  // if needed
    IGeometryContext context,
    bool enableDiagnostics = false) where TInput : notnull =>
    /* thin orchestration delegating to core/quality/patterns/shared */;
```

**Key Elements**:
1. `[Pure]` attribute for referential transparency
2. `[MethodImpl(AggressiveInlining)]` for performance
3. `Result<T>` return type for error handling
4. `IGeometryContext` for tolerance management
5. Optional `enableDiagnostics` for debugging
6. Named parameters for clarity
7. Generic constraints where appropriate

### B3.2: Error Handling Strategy

All new methods use Result<T> monad:

```csharp
// Pattern 1: Validation → Delegation
public static Result<T> Method(...) =>
    ResultFactory.Create(value: input)
        .Validate(args: [context, V.Standard | V.Specific])
        .Bind(validated => SomeCore.Execute(validated, ...));

// Pattern 2: Direct delegation with error context
public static Result<T> Method(...) =>
    SomeCore.Execute(input, ...) switch {
        { IsSuccess: true } result => result,
        { IsSuccess: false } result => ResultFactory.Create<T>(
            error: result.Errors[0].WithContext($"Method: {nameof(Method)}")),
    };

// Pattern 3: Guard clauses with early return
public static Result<T> Method(...) =>
    input is null ? ResultFactory.Create<T>(error: E.Validation.NullInput)
    : input.Length == 0 ? ResultFactory.Create<T>(error: E.Validation.Empty)
    : SomeCore.Execute(input, ...);
```

### B3.3: Configuration Pattern

New methods use readonly structs for options:

```csharp
// In main file's nested types
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct QualityOptions(
    int SampleCount = 100,
    double FairnessThreshold = 0.85,
    bool IncludeMetrics = true);

// Used in method signature
public static Result<(TData Data, QualityMetrics Quality)> AnalyzeWithQuality<T>(
    T geometry,
    QualityOptions options,
    IGeometryContext context,
    bool enableDiagnostics = false) where T : GeometryBase;
```

---

## B4: LOC Budget Verification

### B4.1: Per-Method LOC Targets

| Method Type | Target LOC | Reasoning |
|-------------|------------|-----------|
| Pure delegation | 5-15 LOC | Just calls foundation + Result wrap |
| Validation + delegation | 15-30 LOC | Adds validation chain |
| Guard clauses + delegation | 20-40 LOC | Pattern matching for validation |
| Thin orchestration | 30-60 LOC | Combines multiple foundation calls |
| Algorithmic (in Core) | 100-250 LOC | Complex logic, under 300 limit |

### B4.2: Example LOC Analysis

**spatial/Spatial.cs ClusterByProximity** (~18 LOC):
```csharp
public static Result<IReadOnlyList<PatternDetection.Cluster<T>>> ClusterByProximity<T>(
    IReadOnlyList<T> geometries,
    ClusteringAlgorithm algorithm,
    IGeometryContext context,
    int? targetClusters = null,
    bool enableDiagnostics = false) where T : GeometryBase =>
    UnifiedOperation.Apply(
        input: geometries,
        operation: (Func<IReadOnlyList<T>, Result<IReadOnlyList<PatternDetection.Cluster<T>>>>)(
            items => PatternDetection.ClusterByProximity(items, algorithm, context, targetClusters)),
        config: new OperationConfig<IReadOnlyList<T>, PatternDetection.Cluster<T>> {
            Context = context,
            ValidationMode = V.Standard,
            OperationName = "Spatial.ClusterByProximity",
            EnableDiagnostics = enableDiagnostics,
        });
```
**LOC Count**: 15 lines (within 5-15 target)

**intersection/IntersectionCore.ClassifyIntersection** (~180 LOC estimate):
```csharp
internal static Result<IntersectionClassification> ClassifyIntersection<TA, TB>(
    TA geometryA,
    TB geometryB,
    IntersectionResult intersection,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase {
    
    // Compute tangent vectors at each intersection point (30 LOC)
    Vector3d[] tangentsA = [.. intersection.Points.Select(pt => 
        ComputeTangentAt(geometryA, pt, context))];
    Vector3d[] tangentsB = [.. intersection.Points.Select(pt => 
        ComputeTangentAt(geometryB, pt, context))];
    
    // Compute angles between tangents (20 LOC)
    double[] angles = [.. tangentsA.Zip(tangentsB)
        .Select((pair, i) => Vector3d.VectorAngle(pair.First, pair.Second))];
    
    // Classify each point (40 LOC)
    IntersectionPointType[] types = [.. angles.Select(angle => angle switch {
        < IntersectionConfig.TangencyAngleThreshold => IntersectionPointType.Tangent,
        < IntersectionConfig.GrazingAngleThreshold => IntersectionPointType.Grazing,
        _ => IntersectionPointType.Transverse,
    })];
    
    // Compute approach/departure angles (50 LOC)
    (double approach, double departure)[] angleData = [.. intersection.Points
        .Select((pt, i) => ComputeApproachDeparture(
            geometryA, geometryB, pt, tangentsA[i], tangentsB[i], context))];
    
    // Compute suitability for blending (30 LOC)
    double[] blendSuitability = [.. angles.Select(angle => 
        ComputeBlendSuitability(angle, context))];
    
    // Assemble classification (10 LOC)
    return ResultFactory.Create(value: new IntersectionClassification(
        Points: intersection.Points,
        Types: types,
        ApproachAngles: [.. angleData.Select(static d => d.approach)],
        DepartureAngles: [.. angleData.Select(static d => d.departure)],
        BlendSuitability: blendSuitability,
        OverallConfidence: ComputeOverallConfidence(types)));
}
```
**LOC Count**: ~180 lines (within 100-250 target, under 300 limit)

---

## B5: File/Type Count Verification Per Folder

| Folder | Current Files | New Files | Total | Current Types | New Types | Total | Within Limits? |
|--------|---------------|-----------|-------|---------------|-----------|-------|----------------|
| spatial/ | 3 | 0 | 3 | 6 | +1 (NearMiss) | 7 | ✅ Yes (3/4, 7/10) |
| analysis/ | 3 | 0 | 3 | 8 | 0 (reuses) | 8 | ✅ Yes (3/4, 8/10) |
| extraction/ | 3 | 0 | 3 | 6 | 0 (reuses) | 6 | ✅ Yes (3/4, 6/10) |
| intersection/ | 3 | 0 | 3 | 7 | +2 (Classification, Stability) | 9 | ✅ Yes (3/4, 9/10) |
| orientation/ | 3 | 0 | 3 | 6 | +3 (Optimal, Relative, Pattern) | 9 | ✅ Yes (3/4, 9/10) |
| topology/ | 3 | 0 | 3 | 8 | +2 (Diagnosis, Intent) | 10 | ✅ Yes (3/4, 10/10) |

**Summary**: 
- ✅ All folders remain at 3 files
- ✅ All folders stay within 10-type limit
- ✅ No file/type violations
- ✅ Pattern successfully scales to all 6 folders

---

## B6: Dependencies & Build Order

### Dependency Graph

```
libs/core/errors/ (E.cs + ErrorDomain.cs)
         ↓
libs/core/validation/ (V.cs + ValidationRules.cs)
         ↓
libs/core/quality/    libs/core/patterns/
         ↓                    ↓
      libs/rhino/shared/
         ↓
libs/rhino/[6 folders]
```

### Build Order

1. ✅ Build `libs/core/errors/` with new domains
2. ✅ Build `libs/core/validation/` with new modes
3. ✅ Build `libs/core/quality/` (depends on errors, validation)
4. ✅ Build `libs/core/patterns/` (depends on errors, validation)
5. ✅ Build `libs/rhino/shared/` (depends on quality, patterns)
6. ✅ Build all 6 `libs/rhino/` folders (depend on shared)

**Critical Path**: errors → validation → quality/patterns → shared → folders

---

## B7: Testing Strategy

### B7.1: Unit Tests Per Folder

Each folder gets ~5-10 new unit tests:

```csharp
// Example: spatial/Spatial.ClusterByProximity tests
[Fact]
public void ClusterByProximity_ValidGeometries_ReturnsExpectedClusters() { }

[Fact]
public void ClusterByProximity_EmptyInput_ReturnsError() { }

[Theory]
[InlineData(ClusteringAlgorithm.KMeans, 3)]
[InlineData(ClusteringAlgorithm.Hierarchical, 5)]
public void ClusterByProximity_DifferentAlgorithms_ProducesCorrectCount(
    ClusteringAlgorithm algorithm, int targetClusters) { }
```

### B7.2: Integration Tests

Test cross-folder workflows:

```csharp
[Fact]
public void Workflow_ExtractFeatures_ThenAnalyzeQuality_ThenOptimizeOrientation() {
    // Extract features from Brep
    Result<IReadOnlyList<GeometricFeature>> features = 
        Extraction.ExtractFeatures(brep, FeatureType.All, context);
    
    // Analyze quality of Brep
    Result<QualityReport> quality = 
        Analysis.EvaluateManufacturability(brep, ManufacturingMode.Milling, context);
    
    // Optimize orientation based on quality
    Result<OptimalOrientation> orientation = 
        Orientation.OptimizeOrientation(brep, OrientationCriteria.Milling, context);
    
    Assert.True(features.IsSuccess && quality.IsSuccess && orientation.IsSuccess);
}
```

---

## B8: Risk Assessment

### Risk 1: Type Count Ceiling
**Issue**: topology/ hits 10-type limit exactly.
**Mitigation**: If more types needed, use nested types or consolidate result types.
**Monitoring**: Track type count in each folder during implementation.

### Risk 2: LOC Creep in Core Files
**Issue**: Core file methods may exceed 300 LOC.
**Mitigation**: Delegate to shared/ or quality/ if methods grow too large.
**Monitoring**: Pre-commit LOC checks, code review.

### Risk 3: Circular Dependencies
**Issue**: Folder dependencies could create cycles.
**Mitigation**: Strict build order, dependencies only flow downward (core → shared → folders).
**Monitoring**: Build graph analysis, no `<ProjectReference>` between folders.

---

## B9: Success Criteria

### Phase 1: API Skeleton (Week 1)
- ✅ All 6 folders have new method signatures (stubs acceptable)
- ✅ All builds succeed
- ✅ No file/type limit violations
- ✅ XML documentation complete

### Phase 2: Implementation (Weeks 2-3)
- ✅ All methods implemented (may delegate to stubs in foundation)
- ✅ Unit tests passing
- ✅ LOC within limits
- ✅ Integration tests written

### Phase 3: Full Integration (Week 4)
- ✅ Foundation fully implemented (from Plan A)
- ✅ All methods functional (no stubs)
- ✅ Integration tests passing
- ✅ Documentation complete

---

## B10: Next Steps

After completing integration:

1. ✅ **Proceed to Plan C**: Feature-by-feature implementation details
2. ✅ **Update exemplars**: Add new methods to LIBRARY_GUIDELINES.md
3. ✅ **Documentation**: Update API reference with new methods
4. ✅ **User guide**: Create examples of advanced workflows

**Dependencies**: Requires Plan A (Foundation) completed first.

---

**END PLAN B**
