# Implementation Plan A: Foundation & Extensibility Framework

**Document Purpose**: Defines foundational changes required in `libs/core/` and `libs/rhino/` to support advanced feature integration across all folders while maintaining strict architectural limits.

**Target**: libs/core/ enhancements + libs/rhino/ shared infrastructure

**Dependencies**: Must be completed BEFORE Plans B and C

---

## Executive Summary

The new features in `new_libs_functionality.md` require a **unified extensibility framework** to avoid code duplication and enable consistent advanced functionality across all 6 folders (spatial, analysis, extraction, intersection, orientation, topology) without violating the 4-file, 10-type limits.

**Key Insight**: Rather than adding 5+ new methods to each folder independently (which would explode file counts and duplicate logic), we need:
1. **Shared quality metrics system** in libs/core/
2. **Unified analysis framework** for geometric intelligence
3. **Pattern detection infrastructure** reusable across folders
4. **Enhanced error codes** for new failure modes
5. **Extended validation modes** for quality metrics

---

## A1: Core Infrastructure Enhancements (libs/core/)

### A1.1: New Error Domain & Codes (libs/core/errors/)

**Rationale**: New features introduce failure modes not covered by existing 1000-3999 ranges.

**Required Changes**:

1. **Add Topology error domain** (5000-5999 range)
2. **Extend Geometry errors** (2000-2999 with new codes)
3. **Add Quality metric errors** (consider 6000-6999 range)

**Implementation** in `libs/core/errors/E.cs`:

```csharp
// Add to _m dictionary (line 9-78)
// Topology errors (5000-5999)
[5000] = "Topology repair operation failed",
[5001] = "Edge healing strategy unsuccessful",
[5002] = "Invalid topology diagnosis",
[5003] = "Genus calculation failed",
[5004] = "Feature extraction from topology failed",
[5005] = "Manifold repair unsuccessful",
[5006] = "Topology validation depth exceeded",

// Quality/Analysis errors (6000-6999)
[6000] = "Quality metric computation failed",
[6001] = "Surface fairness analysis failed",
[6002] = "Curvature distribution analysis failed",
[6003] = "Manufacturing suitability check failed",
[6004] = "FEA mesh quality analysis failed",
[6005] = "Pattern detection failed",
[6006] = "Feature recognition failed",
[6007] = "Primitive decomposition failed",
[6008] = "Symmetry detection failed",
[6009] = "Clustering algorithm failed",
[6010] = "Medial axis computation failed",
[6011] = "Proximity field computation failed",
[6012] = "Intersection stability analysis failed",
[6013] = "Near-miss detection failed",
[6014] = "Intersection classification failed",
[6015] = "Orientation optimization failed",
[6016] = "Pattern alignment failed",
[6017] = "Relative orientation computation failed",

// Update GetDomain (line 87-93)
private static ErrorDomain GetDomain(int code) => code switch {
    >= 1000 and < 2000 => ErrorDomain.Results,
    >= 2000 and < 3000 => ErrorDomain.Geometry,
    >= 3000 and < 4000 => ErrorDomain.Validation,
    >= 4000 and < 5000 => ErrorDomain.Spatial,
    >= 5000 and < 6000 => ErrorDomain.Topology,    // NEW
    >= 6000 and < 7000 => ErrorDomain.Quality,     // NEW
    _ => ErrorDomain.Unknown,
};

// Add new nested classes
public static class Topology {
    public static readonly SystemError RepairFailed = Get(5000);
    public static readonly SystemError HealingFailed = Get(5001);
    public static readonly SystemError InvalidDiagnosis = Get(5002);
    public static readonly SystemError GenusCalculationFailed = Get(5003);
    public static readonly SystemError FeatureExtractionFailed = Get(5004);
    public static readonly SystemError ManifoldRepairFailed = Get(5005);
    public static readonly SystemError ValidationDepthExceeded = Get(5006);
}

public static class Quality {
    public static readonly SystemError MetricComputationFailed = Get(6000);
    public static readonly SystemError FairnessAnalysisFailed = Get(6001);
    public static readonly SystemError CurvatureDistributionFailed = Get(6002);
    public static readonly SystemError ManufacturingSuitabilityFailed = Get(6003);
    public static readonly SystemError FEAMeshQualityFailed = Get(6004);
    public static readonly SystemError PatternDetectionFailed = Get(6005);
    public static readonly SystemError FeatureRecognitionFailed = Get(6006);
    public static readonly SystemError PrimitiveDecompositionFailed = Get(6007);
    public static readonly SystemError SymmetryDetectionFailed = Get(6008);
    public static readonly SystemError ClusteringFailed = Get(6009);
    public static readonly SystemError MedialAxisFailed = Get(6010);
    public static readonly SystemError ProximityFieldFailed = Get(6011);
    public static readonly SystemError IntersectionStabilityFailed = Get(6012);
    public static readonly SystemError NearMissDetectionFailed = Get(6013);
    public static readonly SystemError IntersectionClassificationFailed = Get(6014);
    public static readonly SystemError OrientationOptimizationFailed = Get(6015);
    public static readonly SystemError PatternAlignmentFailed = Get(6016);
    public static readonly SystemError RelativeOrientationFailed = Get(6017);
}
```

**Required File Changes**:
- `libs/core/errors/E.cs` - Add error codes and nested classes
- `libs/core/errors/ErrorDomain.cs` - Add `Topology` and `Quality` enum values

**Impact**: +2 error domains, +24 error codes, maintains one-type-per-file rule

---

### A1.2: Extended Validation Modes (libs/core/validation/)

**Rationale**: Quality metrics require new validation modes beyond geometry correctness.

**Required Changes** in `libs/core/validation/V.cs`:

```csharp
// Add new validation mode flags (maintain bitwise flag pattern)
[Flags]
public enum V : ulong {
    None = 0,
    Standard = 1 << 0,              // Existing
    AreaCentroid = 1 << 1,          // Existing
    BoundingBox = 1 << 2,           // Existing
    MassProperties = 1 << 3,        // Existing
    Topology = 1 << 4,              // Existing
    Degeneracy = 1 << 5,            // Existing
    Tolerance = 1 << 6,             // Existing
    SelfIntersection = 1 << 7,      // Existing
    MeshSpecific = 1 << 8,          // Existing
    SurfaceContinuity = 1 << 9,     // Existing
    PolycurveStructure = 1 << 10,   // Existing
    NurbsGeometry = 1 << 11,        // Existing
    ExtrusionGeometry = 1 << 12,    // Existing
    UVDomain = 1 << 13,             // Existing
    
    // NEW: Quality & manufacturability validation modes
    CurvatureBounds = 1 << 14,      // Curvature within acceptable range
    FairnessMetrics = 1 << 15,      // Curve/surface fairness criteria
    ManufacturingConstraints = 1 << 16,  // Milling/printing constraints
    MeshQualityFEA = 1 << 17,       // FEA mesh element quality
    GeometricFeatures = 1 << 18,    // Recognizable features exist
    
    All = Standard | AreaCentroid | BoundingBox | MassProperties | Topology | 
          Degeneracy | Tolerance | SelfIntersection | MeshSpecific | 
          SurfaceContinuity | PolycurveStructure | NurbsGeometry | 
          ExtrusionGeometry | UVDomain | CurvatureBounds | FairnessMetrics | 
          ManufacturingConstraints | MeshQualityFEA | GeometricFeatures,
}
```

**Required Changes** in `libs/core/validation/ValidationRules.cs`:

```csharp
// Add to _validationRules FrozenDictionary (line 40-55)
[V.CurvatureBounds] = ([], ["GetCurvatureAt", "MaxCurvature", "MinCurvature"], 
    E.Quality.CurvatureDistributionFailed),
[V.FairnessMetrics] = ([], ["GetInflectionPoints", "GetTotalCurvature"], 
    E.Quality.FairnessAnalysisFailed),
[V.ManufacturingConstraints] = ([], ["GetMinRadius", "GetUndercuts"], 
    E.Quality.ManufacturingSuitabilityFailed),
[V.MeshQualityFEA] = (["HasFaces", "HasNormals"], ["GetAspectRatios", "GetSkewness"], 
    E.Quality.FEAMeshQualityFailed),
[V.GeometricFeatures] = ([], ["GetFillets", "GetHoles", "GetChamfers"], 
    E.Quality.FeatureRecognitionFailed),
```

**Impact**: +5 validation modes, maintains existing file structure

---

### A1.3: Quality Metrics System (NEW: libs/core/quality/)

**Rationale**: Shared quality metric computation infrastructure prevents duplication across folders.

**New Folder Structure**:
```
libs/core/quality/
├── QualityMetrics.cs      # Public API for metric computation
├── QualityConfig.cs       # Metric thresholds and configurations
└── QualityCore.cs         # Metric computation algorithms
```

**Purpose**: Provide reusable quality metric computations for:
- Curvature distribution analysis
- Fairness scoring
- Manufacturing constraint checking
- FEA mesh quality
- Geometric feature detection

**QualityMetrics.cs** (Public API):

```csharp
namespace Arsenal.Core.Quality;

public static class QualityMetrics {
    /// <summary>Curvature distribution for quality analysis.</summary>
    public readonly record struct CurvatureDistribution(
        double Mean,
        double StdDev,
        double Min,
        double Max,
        double[] Histogram,
        int HighVariationRegions);
    
    /// <summary>Fairness score with energy-based metrics.</summary>
    public readonly record struct FairnessScore(
        double CurvatureEnergy,
        double StrainEnergy,
        double BendingEnergy,
        int InflectionPoints,
        double Smoothness);
    
    /// <summary>Manufacturing constraint evaluation.</summary>
    public readonly record struct ManufacturabilityReport(
        bool MillingFeasible,
        bool PrintingFeasible,
        double MinToolRadius,
        (Point3d Location, double Angle)[] Undercuts,
        double OverhangAngle);
    
    /// <summary>FEA mesh quality metrics.</summary>
    public readonly record struct MeshQualityReport(
        double[] AspectRatios,
        double[] SkewAngles,
        double[] JacobianDeterminants,
        int ProblematicElements,
        double WorstQuality);
    
    /// <summary>Computes curvature distribution for surface/curve.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurvatureDistribution> ComputeCurvatureDistribution<T>(
        T geometry,
        IGeometryContext context,
        int sampleCount = 100) where T : GeometryBase;
    
    /// <summary>Computes fairness score for curve/surface.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FairnessScore> ComputeFairness<T>(
        T geometry,
        IGeometryContext context) where T : GeometryBase;
    
    /// <summary>Evaluates manufacturing constraints.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ManufacturabilityReport> EvaluateManufacturability(
        Brep brep,
        ManufacturingMode mode,
        IGeometryContext context);
    
    /// <summary>Analyzes mesh quality for FEA.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshQualityReport> AnalyzeMeshQuality(
        Mesh mesh,
        IGeometryContext context);
}
```

**QualityConfig.cs** (Constants):

```csharp
namespace Arsenal.Core.Quality;

internal static class QualityConfig {
    internal const int DefaultSampleCount = 100;
    internal const double MinAcceptableAspectRatio = 0.3;
    internal const double MaxAcceptableSkewAngle = 60.0;  // degrees
    internal const double MinJacobianDeterminant = 0.1;
    internal const double DefaultFairnessThreshold = 0.85;
    internal const double MinMillingToolRadius = 0.5;  // mm
    internal const double MaxOverhangAngle = 45.0;  // degrees for 3D printing
}
```

**QualityCore.cs** (Implementation):

```csharp
namespace Arsenal.Core.Quality;

internal static class QualityCore {
    // FrozenDictionary dispatch for type-specific quality metrics
    private static readonly FrozenDictionary<Type, Func<object, IGeometryContext, int, Result<QualityMetrics.CurvatureDistribution>>> _curvatureStrategies = /* ... */;
    
    // Implementation of metric computation algorithms
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<QualityMetrics.CurvatureDistribution> ComputeCurvatureDistribution(
        object geometry,
        IGeometryContext context,
        int sampleCount);
    
    // Additional internal methods for fairness, manufacturability, etc.
}
```

**Files**: 3 new files (within 4-file limit for folder)
**Types**: ~8 types (within 10-type limit for folder)

---

### A1.4: Pattern Detection System (NEW: libs/core/patterns/)

**Rationale**: Pattern detection (symmetries, sequences, clusters) is needed across multiple folders.

**New Folder Structure**:
```
libs/core/patterns/
├── PatternDetection.cs    # Public API for pattern detection
├── PatternConfig.cs       # Detection thresholds and algorithms
└── PatternCore.cs         # Detection algorithm implementations
```

**Purpose**: Provide reusable pattern detection for:
- Symmetry detection (reflection, rotation, translation)
- Sequence/progression detection
- Spatial clustering (K-means, hierarchical)
- Geometric pattern recognition

**PatternDetection.cs** (Public API):

```csharp
namespace Arsenal.Core.Patterns;

public static class PatternDetection {
    /// <summary>Symmetry types detected in geometry.</summary>
    public enum SymmetryType : byte {
        None = 0,
        Reflection = 1,
        Rotation = 2,
        Translation = 4,
        Glide = 8,
        All = Reflection | Rotation | Translation | Glide,
    }
    
    /// <summary>Detected symmetry with transformation.</summary>
    public readonly record struct Symmetry(
        SymmetryType Type,
        Transform Transform,
        Plane? ReflectionPlane,
        Line? RotationAxis,
        Vector3d? TranslationVector,
        double Confidence);
    
    /// <summary>Spatial cluster with statistics.</summary>
    public readonly record struct Cluster<T>(
        T[] Members,
        Point3d Centroid,
        BoundingBox Bounds,
        double Cohesion,
        int Index) where T : GeometryBase;
    
    /// <summary>Geometric progression/sequence.</summary>
    public readonly record struct Sequence<T>(
        T[] Elements,
        Transform StepTransform,
        double Regularity,
        SequenceType Type) where T : GeometryBase;
    
    /// <summary>Detects symmetries in geometry collection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Symmetry>> DetectSymmetries<T>(
        IReadOnlyList<T> geometries,
        SymmetryType searchTypes,
        IGeometryContext context,
        double confidenceThreshold = 0.85) where T : GeometryBase;
    
    /// <summary>Clusters geometry by spatial proximity.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Cluster<T>>> ClusterByProximity<T>(
        IReadOnlyList<T> geometries,
        ClusteringAlgorithm algorithm,
        IGeometryContext context,
        int? targetClusters = null) where T : GeometryBase;
    
    /// <summary>Detects geometric sequences/progressions.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Sequence<T>>> DetectSequences<T>(
        IReadOnlyList<T> geometries,
        IGeometryContext context) where T : GeometryBase;
}
```

**Files**: 3 new files (within 4-file limit)
**Types**: ~9 types (within 10-type limit)

---

## A2: Shared Rhino Infrastructure (libs/rhino/shared/)

### A2.1: Create Shared Utilities Folder

**Rationale**: Advanced features require shared RhinoCommon utility functions used across multiple folders. Prevents duplication.

**New Folder Structure**:
```
libs/rhino/shared/
├── RhinoUtilities.cs      # Shared RhinoCommon utilities
├── UtilitiesConfig.cs     # Constants and thresholds
└── UtilitiesCore.cs       # Implementation of shared algorithms
```

**Purpose**: Centralize common RhinoCommon operations:
- Medial axis computation
- Primitive fitting (planes, cylinders, spheres)
- Feature detection (fillets, chamfers, holes)
- Proximity field computation
- Intersection classification utilities

**RhinoUtilities.cs** (Public API - subset):

```csharp
namespace Arsenal.Rhino.Shared;

public static class RhinoUtilities {
    /// <summary>Medial axis with stability measure.</summary>
    public readonly record struct MedialAxis(
        Curve[] Skeleton,
        Point3d[] BranchPoints,
        double[] RadiusFunction,
        double LocalStability);
    
    /// <summary>Best-fit primitive with residual.</summary>
    public readonly record struct PrimitiveFit(
        GeometryBase Primitive,
        double Confidence,
        double RMSError,
        GeometryBase Residual);
    
    /// <summary>Detected geometric feature.</summary>
    public readonly record struct GeometricFeature(
        FeatureType Type,
        GeometryBase Geometry,
        double[] Parameters,
        Point3d Location);
    
    /// <summary>Computes medial axis for planar/volume geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MedialAxis> ComputeMedialAxis(
        Brep brep,
        MedialAxisMode mode,
        IGeometryContext context);
    
    /// <summary>Fits primitive geometry to point cloud or surface.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<PrimitiveFit>> FitPrimitives<T>(
        T geometry,
        PrimitiveType[] primitiveTypes,
        IGeometryContext context,
        double confidenceThreshold = 0.85) where T : GeometryBase;
    
    /// <summary>Extracts geometric features (fillets, chamfers, holes).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<GeometricFeature>> ExtractFeatures(
        Brep brep,
        FeatureType searchTypes,
        IGeometryContext context);
    
    /// <summary>Computes directional proximity field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<double[]> ComputeProximityField(
        GeometryBase[] geometries,
        Point3d[] samplePoints,
        Vector3d direction,
        IGeometryContext context);
}
```

**Files**: 3 new files
**Types**: ~8 types

---

## A3: Implementation Sequence & Dependencies

### Phase 1: Core Error Infrastructure (Week 1)
1. ✅ Add `Topology` and `Quality` to `ErrorDomain` enum
2. ✅ Extend `E.cs` with new error codes (5000-5999, 6000-6999)
3. ✅ Add nested classes `E.Topology` and `E.Quality`
4. ✅ **Verify**: `dotnet build libs/core/Core.csproj` succeeds

### Phase 2: Extended Validation (Week 1)
1. ✅ Add 5 new validation flags to `V` enum
2. ✅ Extend `ValidationRules._validationRules` FrozenDictionary
3. ✅ **Verify**: Existing validation tests still pass

### Phase 3: Quality Metrics System (Week 2)
1. ✅ Create `libs/core/quality/` folder
2. ✅ Implement `QualityMetrics.cs` (public API with result types)
3. ✅ Implement `QualityConfig.cs` (constants)
4. ✅ Implement `QualityCore.cs` (algorithms + FrozenDictionary dispatch)
5. ✅ Add reference to Core.csproj
6. ✅ **Verify**: Build succeeds, LOC < 300 per member

### Phase 4: Pattern Detection System (Week 2-3)
1. ✅ Create `libs/core/patterns/` folder
2. ✅ Implement `PatternDetection.cs` (public API)
3. ✅ Implement `PatternConfig.cs` (constants)
4. ✅ Implement `PatternCore.cs` (clustering, symmetry detection)
5. ✅ Add reference to Core.csproj
6. ✅ **Verify**: Build succeeds, file/type limits respected

### Phase 5: Shared Rhino Utilities (Week 3)
1. ✅ Create `libs/rhino/shared/` folder
2. ✅ Implement `RhinoUtilities.cs` (medial axis, primitives, features)
3. ✅ Implement `UtilitiesConfig.cs`
4. ✅ Implement `UtilitiesCore.cs`
5. ✅ Add reference to Rhino.csproj
6. ✅ **Verify**: Build succeeds, no RhinoCommon API misuse

### Phase 6: Integration Testing (Week 4)
1. ✅ Write unit tests for quality metrics
2. ✅ Write unit tests for pattern detection
3. ✅ Write integration tests for shared utilities
4. ✅ Property-based tests for metric invariants
5. ✅ **Verify**: All tests pass, coverage > 80%

---

## A4: Architectural Justification

### Why Central Quality Metrics?

**Problem**: Each folder (analysis, spatial, topology, etc.) needs quality assessment, but implementing independently would:
- Violate 4-file limits (each folder would need +2 files)
- Duplicate curvature/fairness logic 6 times
- Create inconsistent metric definitions
- Make maintenance nightmarish

**Solution**: Central `libs/core/quality/` provides:
- ✅ Single source of truth for metrics
- ✅ Reusable across all folders via simple API calls
- ✅ Consistent metric definitions
- ✅ Testable in isolation
- ✅ Extensible without touching existing folders

### Why Central Pattern Detection?

**Problem**: Pattern detection needed by:
- `spatial/` for clustering and proximity patterns
- `extraction/` for feature patterns
- `orientation/` for alignment patterns
- `topology/` for symmetry patterns

Implementing in each folder = 4× duplication.

**Solution**: Central `libs/core/patterns/` enables:
- ✅ K-means, hierarchical clustering shared
- ✅ Symmetry detection algorithms shared
- ✅ Consistent pattern definitions
- ✅ Single optimization point
- ✅ Folders remain thin orchestration layers

### Why Shared Rhino Utilities?

**Problem**: Advanced RhinoCommon operations (medial axis, primitive fitting, feature extraction) are:
- Non-trivial (100+ LOC each)
- Needed by multiple folders
- Complex enough to warrant dedicated implementation
- Would explode file counts if duplicated

**Solution**: `libs/rhino/shared/` provides:
- ✅ Shared complex RhinoCommon algorithms
- ✅ Prevents 4-file limit violations
- ✅ Single testing surface
- ✅ Consistent RhinoCommon API usage
- ✅ Folders delegate to shared utilities

---

## A5: File Count & Type Count Verification

### New Folders Summary

| Folder | Files | Types | Within Limits? |
|--------|-------|-------|----------------|
| `libs/core/quality/` | 3 | 8 | ✅ Yes (3/4, 8/10) |
| `libs/core/patterns/` | 3 | 9 | ✅ Yes (3/4, 9/10) |
| `libs/rhino/shared/` | 3 | 8 | ✅ Yes (3/4, 8/10) |

### Modified Files Summary

| File | Changes | Impact |
|------|---------|--------|
| `libs/core/errors/E.cs` | +24 error codes, +2 nested classes | Maintains 1 type/file |
| `libs/core/errors/ErrorDomain.cs` | +2 enum values | No type count impact |
| `libs/core/validation/V.cs` | +5 enum flags | No type count impact |
| `libs/core/validation/ValidationRules.cs` | +5 validation rule mappings | No type count impact |

**Total New Files**: 9 (3 folders × 3 files)
**Total New Types**: 25 (distributed across 3 folders)
**Violations**: ❌ NONE - all within limits

---

## A6: Risk Assessment & Mitigation

### Risk 1: RhinoCommon API Availability
**Risk**: Medial axis, primitive fitting may not have direct RhinoCommon APIs.
**Mitigation**: Use approximate algorithms (Voronoi for medial axis, RANSAC for primitives) + mark as "approximation" in docs.
**Fallback**: Stub implementations return "not implemented" error for Phase 1.

### Risk 2: Quality Metrics Performance
**Risk**: Curvature distribution computation may be slow for large surfaces.
**Mitigation**: Use adaptive sampling + caching via ConditionalWeakTable.
**Fallback**: Configurable sample counts + early termination.

### Risk 3: Pattern Detection Complexity
**Risk**: Symmetry detection is algorithmically complex (potentially > 300 LOC).
**Mitigation**: Use expression trees + FrozenDictionary dispatch to stay under limit.
**Fallback**: Implement only reflection/rotation symmetries in Phase 1, defer glide/translation.

### Risk 4: Integration Complexity
**Risk**: 3 new folders may have circular dependencies.
**Mitigation**: Strict dependency order: patterns → quality → shared → rhino folders.
**Fallback**: Merge quality + patterns if dependency issues arise.

---

## A7: Success Criteria

### Must Have (Phase 1)
- ✅ Error codes 5000-6999 defined and building
- ✅ Extended validation modes compiling
- ✅ Quality metrics folder structure exists
- ✅ Pattern detection folder structure exists
- ✅ Shared utilities folder structure exists
- ✅ All builds succeed with zero warnings
- ✅ No file/type limit violations

### Should Have (Phase 2)
- ✅ Quality metrics API implemented (stubs acceptable)
- ✅ Pattern detection API implemented (stubs acceptable)
- ✅ Shared utilities API implemented (stubs acceptable)
- ✅ Basic unit tests passing
- ✅ Documentation comments complete

### Nice to Have (Phase 3)
- ✅ Full implementations (no stubs)
- ✅ Integration tests passing
- ✅ Property-based tests for invariants
- ✅ Performance benchmarks established
- ✅ Example usage documented

---

## A8: Next Steps

After completing this foundation:

1. ✅ **Proceed to Plan B**: Feature integration strategy for existing folders
2. ✅ **Proceed to Plan C**: Feature-by-feature implementation across folders
3. ✅ **Documentation**: Update CLAUDE.md with new patterns
4. ✅ **Training**: Create exemplar code for new patterns

**Blockers**: NONE - This plan is self-contained and prerequisite for Plans B & C.

**Timeline**: 3-4 weeks for full implementation, 1 week for foundation skeleton.

---

**END PLAN A**
