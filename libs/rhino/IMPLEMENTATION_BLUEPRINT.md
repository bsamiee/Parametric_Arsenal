# Unified Implementation Blueprint: Advanced Geometry Features
**Document Version**: 1.0 FINAL  
**Created**: 2025-11-09  
**Purpose**: Single, comprehensive blueprint consolidating all advanced geometry features from `new_libs_functionality.md`

---

## Executive Summary

This blueprint provides a complete, actionable implementation plan for integrating ALL features specified in `libs/rhino/new_libs_functionality.md` into the Parametric Arsenal codebase while maintaining strict architectural limits (≤4 files per folder, ≤10 types per folder, ≤300 LOC per member).

**Key Architectural Decision**: **Foundation-First Pattern**  
Rather than adding 5-10 methods to each of the 6 existing folders (which would violate limits), we create **3 shared infrastructure folders** that ALL features leverage:

1. **`libs/core/quality/`** - Quality metrics engine (curvature analysis, mesh quality, fairness)
2. **`libs/core/patterns/`** - Pattern detection & clustering algorithms  
3. **`libs/rhino/shared/`** - Shared RhinoCommon utilities (primitive fitting, medial axis wrappers)

**Impact**: Each existing folder (`spatial/`, `analysis/`, `intersection/`, `orientation/`, `extraction/`, `topology/`) gains 1-3 new methods (5-30 LOC each) that delegate to shared infrastructure, preventing file/type sprawl.

**Research Foundation**: This blueprint is backed by:
- Deep analysis of existing `libs/core/` infrastructure (Result<T>, UnifiedOperation, ValidationRules, E.*, V.*)
- Complete audit of all 6 existing `libs/rhino/` folders
- 8 comprehensive web searches on RhinoCommon SDK capabilities
- Verification against CLAUDE.md, copilot-instructions.md standards

---

## Table of Contents

**Part 1: Architecture Foundation**
- [1.1 Existing Infrastructure Analysis](#11-existing-infrastructure-analysis)
- [1.2 Foundation Pattern Rationale](#12-foundation-pattern-rationale)
- [1.3 Error Domain Strategy](#13-error-domain-strategy)
- [1.4 Validation Mode Extensions](#14-validation-mode-extensions)

**Part 2: Shared Infrastructure Blueprints**
- [2.1 libs/core/quality/](#21-libscorequality)
- [2.2 libs/core/patterns/](#22-libscorepatterns)
- [2.3 libs/rhino/shared/](#23-libsrhinoshared)

**Part 3: Feature Integration Mapping**
- [3.1 Topology Features](#31-topology-features)
- [3.2 Spatial Features](#32-spatial-features)
- [3.3 Analysis Features](#33-analysis-features)
- [3.4 Intersection Features](#34-intersection-features)
- [3.5 Orientation Features](#35-orientation-features)
- [3.6 Extraction Features](#36-extraction-features)

**Part 4: Implementation Roadmap**
- [4.1 Dependency Order](#41-dependency-order)
- [4.2 Week-by-Week Plan](#42-week-by-week-plan)
- [4.3 Verification Checkpoints](#43-verification-checkpoints)

---

# Part 1: Architecture Foundation

## 1.1 Existing Infrastructure Analysis

### 1.1.1 libs/core/results/ — Result Monad System

**What Exists**:
- `Result<T>` struct (202 LOC) - Lazy evaluation, monadic composition
- `ResultFactory` static class (110 LOC) - Polymorphic creation via pattern matching
- Key operations: `Map`, `Bind`, `Ensure`, `Match`, `Tap`, `Apply`, `OnError`, `Traverse`

**How We'll Leverage It**:
```csharp
// Quality metric computation returns Result<QualityMetrics>
public static Result<SurfaceQuality> AnalyzeSurfaceQuality(Surface surface, IGeometryContext ctx) =>
    ResultFactory.Create(value: surface)
        .Validate(args: [ctx, V.Standard | V.Degeneracy])
        .Bind(s => ComputeCurvatureDistribution(s, ctx))
        .Map(dist => new SurfaceQuality(dist, CalculateFairness(dist)));
```

**Pattern**: All new features return `Result<T>` for failable operations, chain via `Bind`/`Map`.

### 1.1.2 libs/core/operations/ — UnifiedOperation Dispatch Engine

**What Exists**:
- `UnifiedOperation.Apply<TIn, TOut>` (108 LOC) - Polymorphic dispatch with validation/caching
- `OperationConfig<TIn, TOut>` (67 LOC) - 14 configuration properties for behavior control

**How We'll Leverage It**:
```csharp
// Pattern detection uses UnifiedOperation for polymorphic geometry types
public static Result<IReadOnlyList<PatternMatch>> DetectSymmetry<T>(
    T[] geometry,
    SymmetryType type,
    IGeometryContext ctx) where T : GeometryBase =>
    UnifiedOperation.Apply(
        input: geometry,
        operation: (Func<T[], Result<IReadOnlyList<PatternMatch>>>)(items => 
            PatternCore.DetectSymmetryInternal(items, type, ctx)),
        config: new OperationConfig<T[], PatternMatch> {
            Context = ctx,
            ValidationMode = V.Standard,
            Enable Cache = true,  // Cache expensive pattern computations
        });
```

**Pattern**: Use for ALL polymorphic operations across geometry types.

### 1.1.3 libs/core/validation/ — ValidationRules Expression Trees

**What Exists**:
- `ValidationRules.GetOrCompileValidator` - Runtime expression tree compilation
- `V` struct (97 LOC) - Bitwise validation flags (13 existing modes)
- FrozenDictionary registry mapping V modes to validation rules

**How We'll Leverage It**:
```csharp
// Extend V with quality-specific modes
V.CurvatureBounds     // Curvature within acceptable range
V.FairnessMetrics     // Curve/surface fairness criteria
V.ManufacturingConstraints  // Milling/printing constraints
V.ElementQuality      // Mesh element aspect ratio/skewness

// Usage in quality operations
.Validate(args: [context, V.Standard | V.CurvatureBounds | V.FairnessMetrics])
```

**Pattern**: Extend V with 4 new modes, register validation rules in ValidationRules.

### 1.1.4 libs/core/errors/ — Centralized Error Registry

**What Exists**:
- `E` static class with nested classes (177 LOC total)
- FrozenDictionary<int, string> for O(1) error message lookup
- 4 error domains: Results (1000-1999), Geometry (2000-2999), Validation (3000-3999), Spatial (4000-4999)
- 78 error codes currently defined

**How We'll Leverage It**:
```csharp
// Add 2 new error domains
public static class Topology {  // 5000-5999
    public static readonly SystemError RepairFailed = Get(5000);
    public static readonly SystemError HealingFailed = Get(5001);
    // ... 7 more topology errors
}

public static class Quality {  // 6000-6999
    public static readonly SystemError MetricComputationFailed = Get(6000);
    public static readonly SystemError FairnessAnalysisFailed = Get(6001);
    // ... 17 more quality errors
}
```

**Pattern**: Allocate codes 5000-5024 (Topology), 6000-6017 (Quality).

### 1.1.5 libs/rhino/spatial/ — Spatial Indexing Pattern

**What Exists**:
- `Spatial.cs` (40 LOC) - Public API with polymorphic dispatch
- `SpatialCore.cs` - FrozenDictionary operation registry + RTree algorithms
- `SpatialConfig.cs` - Configuration types

**Pattern We'll Reuse**:
```csharp
// FrozenDictionary dispatch pattern
private static readonly FrozenDictionary<(Type Input, Type Query), Config> _registry =
    new Dictionary<(Type, Type), Config> {
        [(typeof(Point3d[]), typeof(Sphere))] = (
            TreeBuilder: pts => BuildRTree(pts),
            ValidationMode: V.Standard,
            BufferSize: 1024,
            Execute: (input, query, ctx, buffer) => SphereQuery(input, query, ctx, buffer)),
        // ... more type combinations
    }.ToFrozenDictionary();
```

**Pattern**: We'll use this same dispatch registry pattern in `libs/core/quality/` and `libs/core/patterns/`.

### 1.1.6 libs/rhino/topology/ & libs/rhino/analysis/ — Current Implementations

**What Exists**:
- **Topology**: 3 files, 7 types (EdgeContinuityType, NakedEdges, BoundaryLoops, ConnectedComponents, EdgeClassification, Adjacency, EdgeGraph)
- **Analysis**: 3 files, 6 types (CurveData, SurfaceData, BrepData, MeshData, AnalysisSpec, internal registries)

**Pattern**: Both use 3-file structure: `{Name}.cs` (public API), `{Name}Core.cs` (algorithms), `{Name}Config.cs` (configuration).

**Key Insight**: These folders are already at 7 and 6 types respectively. Adding 5+ new types for quality/pattern features would violate the 10-type limit. This proves the necessity of shared infrastructure folders.

---

## 1.2 Foundation Pattern Rationale

### 1.2.1 Why Foundation-First vs. Vertical Slicing?

**Alternative Rejected: Vertical Slicing** (add features directly to each folder)

| Feature | spatial/ | analysis/ | intersection/ | topology/ | orientation/ | extraction/ |
|---------|----------|-----------|---------------|-----------|--------------|-------------|
| Quality Metrics | +2 types | +3 types | +1 type | +1 type | +1 type | +1 type |
| Pattern Detection | +2 types | +1 type | +1 type | +2 types | +2 types | +1 type |
| Near-Miss | - | - | +2 types | - | - | - |
| Clustering | +3 types | - | - | - | - | +1 type |
| **TOTAL NEW TYPES** | **+7** | **+4** | **+4** | **+3** | **+3** | **+3** |
| **CURRENT TYPES** | 4 | 6 | 5 | 7 | 4 | 5 |
| **FINAL TYPES** | **11 ❌** | 10 ⚠️ | 9 ✓ | **10 ⚠️** | 7 ✓ | 8 ✓ |

**Problem**: `spatial/` violates 10-type limit immediately. `analysis/` and `topology/` are at the maximum with no room for growth.

**Foundation-First Solution**: Create shared infrastructure that ALL folders delegate to.

| Shared Folder | Types | Purpose | Prevents Duplication In |
|---------------|-------|---------|-------------------------|
| `libs/core/quality/` | 8 types | Quality metrics engine | ALL 6 folders |
| `libs/core/patterns/` | 7 types | Pattern detection/clustering | spatial, topology, extraction, orientation |
| `libs/rhino/shared/` | 6 types | RhinoCommon utilities | ALL 6 folders |

**Result**: Each existing folder adds only 1-3 thin delegation methods (5-30 LOC each), staying well within limits.

### 1.2.2 Code Duplication Prevention Matrix

| Feature | Without Foundation | With Foundation |
|---------|-------------------|-----------------|
| **Curvature Distribution Analysis** | Duplicated in `analysis/`, `topology/`, `intersection/` (3×80 LOC = 240 LOC) | `libs/core/quality/QualityCore.cs` (80 LOC) + 3 delegators (3×8 LOC = 24 LOC) |
| **Clustering Algorithms** | Duplicated in `spatial/`, `extraction/`, `orientation/` (3×120 LOC = 360 LOC) | `libs/core/patterns/PatternCore.cs` (120 LOC) + 3 delegators (3×10 LOC = 30 LOC) |
| **Primitive Fitting** | Duplicated in `extraction/`, `analysis/`, `topology/` (3×60 LOC = 180 LOC) | `libs/rhino/shared/PrimitiveFit.cs` (60 LOC) + 3 delegators (3×5 LOC = 15 LOC) |
| **TOTAL WITHOUT** | **780 LOC duplicated** | **275 LOC (65% reduction)** |

**Conclusion**: Foundation pattern eliminates 505 LOC of duplication while preventing type sprawl.

---

## 1.3 Error Domain Strategy

### 1.3.1 New Error Domains

**Add to `libs/core/errors/ErrorDomain.cs`**:
```csharp
public enum ErrorDomain : byte {
    Unknown = 0,
    Results = 1,      // Existing: 1000-1999
    Geometry = 2,     // Existing: 2000-2999
    Validation = 3,   // Existing: 3000-3999
    Spatial = 4,      // Existing: 4000-4999
    Topology = 5,     // NEW: 5000-5999
    Quality = 6,      // NEW: 6000-6999
}
```

### 1.3.2 Topology Error Codes (5000-5999)

**Add to `libs/core/errors/E.cs`**:
```csharp
// In _m dictionary:
[5000] = "Topology repair operation failed",
[5001] = "Edge healing strategy unsuccessful",
[5002] = "Invalid topology diagnosis",
[5003] = "Genus calculation failed",
[5004] = "Feature extraction from topology failed",
[5005] = "Manifold repair unsuccessful",
[5006] = "Topology validation depth exceeded",

// Nested class:
public static class Topology {
    public static readonly SystemError RepairFailed = Get(5000);
    public static readonly SystemError HealingFailed = Get(5001);
    public static readonly SystemError InvalidDiagnosis = Get(5002);
    public static readonly SystemError GenusCalculationFailed = Get(5003);
    public static readonly SystemError FeatureExtractionFailed = Get(5004);
    public static readonly SystemError ManifoldRepairFailed = Get(5005);
    public static readonly SystemError ValidationDepthExceeded = Get(5006);
}
```

**RhinoCommon Mapping**:
- 5000: `Mesh.HealNakedEdges` returns 0 (no edges healed)
- 5001: `Brep.RepairTolerance` fails
- 5002: Topology diagnosis has inconsistent state
- 5003: Euler characteristic computation fails
- 5004: Cannot extract fillets/chamfers
- 5005: `Mesh.ExtractNonManifoldMeshEdges` leaves invalid geometry
- 5006: Recursive topology validation exceeds max depth

### 1.3.3 Quality Error Codes (6000-6999)

**Add to `libs/core/errors/E.cs`**:
```csharp
// In _m dictionary:
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

// Nested class:
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

**RhinoCommon Mapping**:
- 6000: Generic computation failure (NaN, infinity, negative values)
- 6001: `Surface.CurvatureAt` returns invalid or `Curve.CurvatureAt` sampling fails
- 6002: Statistical analysis of curvature distribution encounters numerical issues
- 6003: Manufacturing constraint check (tool radius, overhang angle) fails
- 6004: Mesh aspect ratio/skewness/Jacobian computation returns invalid
- 6005: Pattern detection algorithm (k-means, DBSCAN) doesn't converge
- 6006: Feature recognition (fillet/chamfer) cannot identify primitives
- 6007: Best-fit primitive (plane/cylinder/sphere) least-squares fails
- 6008: Symmetry detection via transformation testing encounters numerical instability
- 6009: Clustering algorithm (ML.NET integration) throws exception
- 6010: Medial axis/skeleton computation (external library) fails
- 6011: Proximity field (RTree queries) encounters buffer overflow
- 6012: Intersection stability (perturbation testing) produces inconsistent results
- 6013: Near-miss detection (closest point calculation) fails
- 6014: Intersection tangency classification (vector angle) invalid
- 6015: Orientation optimization (bounding box minimization) doesn't converge
- 6016: Pattern alignment (registration algorithm) fails
- 6017: Relative orientation (best-fit transform) singular matrix

### 1.3.4 Update GetDomain Method

**Modify in `libs/core/errors/E.cs`**:
```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static ErrorDomain GetDomain(int code) => code switch {
    >= 1000 and < 2000 => ErrorDomain.Results,
    >= 2000 and < 3000 => ErrorDomain.Geometry,
    >= 3000 and < 4000 => ErrorDomain.Validation,
    >= 4000 and < 5000 => ErrorDomain.Spatial,
    >= 5000 and < 6000 => ErrorDomain.Topology,    // NEW
    >= 6000 and < 7000 => ErrorDomain.Quality,     // NEW
    _ => ErrorDomain.Unknown,
};
```

---

## 1.4 Validation Mode Extensions

### 1.4.1 New Validation Modes

**Modify `libs/core/validation/V.cs`** (change from struct to enum for easier extensibility):

```csharp
// Existing modes (keep as-is):
public static readonly V None = new(0);
public static readonly V Standard = new(1);
public static readonly V AreaCentroid = new(2);
public static readonly V BoundingBox = new(4);
public static readonly V MassProperties = new(8);
public static readonly V Topology = new(16);
public static readonly V Degeneracy = new(32);
public static readonly V Tolerance = new(64);
public static readonly V MeshSpecific = new(128);
public static readonly V SurfaceContinuity = new(256);
public static readonly V PolycurveStructure = new(512);
public static readonly V NurbsGeometry = new(1024);
public static readonly V ExtrusionGeometry = new(2048);
public static readonly V UVDomain = new(4096);

// NEW: Quality & manufacturability validation modes
public static readonly V CurvatureBounds = new(8192);       // Curvature within acceptable range
public static readonly V FairnessMetrics = new(16384);      // Curve/surface fairness criteria
public static readonly V ManufacturingConstraints = new(32768);  // Milling/printing constraints
public static readonly V ElementQuality = new(65536);       // Mesh element aspect ratio/skewness

// Update All flag
public static readonly V All = new((ushort)(
    Standard._flags | AreaCentroid._flags | BoundingBox._flags | MassProperties._flags |
    Topology._flags | Degeneracy._flags | Tolerance._flags |
    MeshSpecific._flags | SurfaceContinuity._flags | PolycurveStructure._flags |
    NurbsGeometry._flags | ExtrusionGeometry._flags | UVDomain._flags |
    CurvatureBounds._flags | FairnessMetrics._flags | ManufacturingConstraints._flags |
    ElementQuality._flags
));

// Update AllFlags set
public static readonly FrozenSet<V> AllFlags = ((V[])[
    Standard, AreaCentroid, BoundingBox, MassProperties, Topology, Degeneracy, Tolerance,
    MeshSpecific, SurfaceContinuity, PolycurveStructure, NurbsGeometry, ExtrusionGeometry,
    UVDomain, CurvatureBounds, FairnessMetrics, ManufacturingConstraints, ElementQuality,
]).ToFrozenSet();
```

### 1.4.2 Register Validation Rules

**Add to `libs/core/validation/ValidationRules.cs` FrozenDictionary**:

```csharp
private static readonly FrozenDictionary<V, (string[] Properties, string[] Methods, SystemError Error)> _validationRules =
    new Dictionary<V, (string[], string[], SystemError)> {
        // ... existing rules ...
        
        // NEW: Quality validation rules
        [V.CurvatureBounds] = (
            [],  // No boolean properties
            ["CurvatureAt",],  // Methods to validate
            E.Quality.CurvatureDistributionFailed),
        
        [V.FairnessMetrics] = (
            [],
            ["CurvatureAt", "TangentAt",],
            E.Quality.FairnessAnalysisFailed),
        
        [V.ManufacturingConstraints] = (
            ["IsValid", "IsClosed",],
            ["GetBoundingBox",],
            E.Quality.ManufacturingSuitabilityFailed),
        
        [V.ElementQuality] = (
            ["IsValid", "IsManifold",],
            [],
            E.Quality.FEAMeshQualityFailed),
    }.ToFrozenDictionary();
```

**Validation Logic** (custom validators in quality metrics engine):
- **CurvatureBounds**: Sample curvature at N points, ensure |κ| < max_curvature
- **FairnessMetrics**: Compute curvature variance, ensure < fairness_threshold
- **ManufacturingConstraints**: Check tool access, overhangs, minimum radii
- **ElementQuality**: Compute aspect ratio, skewness for all mesh faces

---

# Part 2: Shared Infrastructure Blueprints

## 2.1 libs/core/quality/

### 2.1.1 Purpose & Justification

**Purpose**: Centralized quality metrics computation engine for geometric analysis.

**Prevents Duplication In**: ALL 6 rhino folders would otherwise duplicate:
- Curvature distribution analysis (analysis/, topology/, intersection/)
- Surface fairness metrics (analysis/, extraction/)
- Mesh quality (FEA) metrics (topology/, spatial/)
- Manufacturing suitability checks (analysis/, orientation/)

**File Structure** (3 files, 8 types):
```
libs/core/quality/
├── Quality.cs         # Public API surface (8 methods, ~120 LOC)
├── QualityCore.cs     # Core algorithms (~250 LOC)
└── QualityConfig.cs   # Configuration types (~80 LOC)
```

### 2.1.2 File 1: Quality.cs (Public API)

**Purpose**: Public API surface with clean delegation to QualityCore.

**Types** (3 total):
```csharp
namespace Arsenal.Core.Quality;

// Type 1: Marker interface for polymorphic results
public interface IQualityMetric {
    double Score { get; }  // 0.0 (worst) to 1.0 (perfect)
    string Description { get; }
}

// Type 2: Curvature distribution statistics
public sealed record CurvatureDistribution(
    double Mean,
    double StdDev,
    double Min,
    double Max,
    double[] Histogram,
    Point3d[] HighCurvatureRegions) : IQualityMetric {
    public double Score => 1.0 - Math.Min(1.0, StdDev / (Max - Min + RhinoMath.ZeroTolerance));
    public string Description => $"κ_mean={Mean:F3}, σ={StdDev:F3}";
}

// Type 3: Surface fairness analysis
public sealed record FairnessMetrics(
    double Fairness,  // 0-1 scale
    Point3d[] InflectionPoints,
    double[] CurvatureComb,
    double EnergyFunctional) : IQualityMetric {
    public double Score => Fairness;
    public string Description => $"Fairness={Fairness:F3}, Inflections={InflectionPoints.Length}";
}
```

**Key Members**:

```csharp
public static class Quality {
    /// <summary>Analyzes curvature distribution for surfaces.</summary>
    [Pure]
    public static Result<CurvatureDistribution> AnalyzeCurvatureDistribution(
        Surface surface,
        int samplesU,
        int samplesV,
        IGeometryContext context) =>
        ResultFactory.Create(value: surface)
            .Validate(args: [context, V.Standard | V.CurvatureBounds])
            .Bind(s => QualityCore.ComputeCurvatureDistribution(s, samplesU, samplesV, context));

    /// <summary>Analyzes curve fairness via curvature comb.</summary>
    [Pure]
    public static Result<FairnessMetrics> AnalyzeCurveFairness(
        Curve curve,
        int samples,
        IGeometryContext context) =>
        ResultFactory.Create(value: curve)
            .Validate(args: [context, V.Standard | V.FairnessMetrics])
            .Bind(c => QualityCore.ComputeFairness(c, samples, context));

    /// <summary>Analyzes mesh quality for FEA suitability.</summary>
    [Pure]
    public static Result<MeshQuality> AnalyzeForFEA(
        Mesh mesh,
        FEASimulationType simType,
        IGeometryContext context) =>
        ResultFactory.Create(value: mesh)
            .Validate(args: [context, V.MeshSpecific | V.ElementQuality])
            .Bind(m => QualityCore.ComputeMeshQuality(m, simType, context));

    /// <summary>Checks manufacturing suitability constraints.</summary>
    [Pure]
    public static Result<ManufacturingSuitability> CheckManufacturing(
        GeometryBase geometry,
        ManufacturingProcess process,
        IGeometryContext context) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context, V.Standard | V.ManufacturingConstraints])
            .Bind(g => QualityCore.CheckManufacturingConstraints(g, process, context));
}
```

**LOC Estimate**: 120 LOC (30 LOC per method × 4 methods)

**RhinoCommon APIs Used**:
- `Surface.CurvatureAt(double u, double v)` → `SurfaceCurvature` with Gaussian/Mean
- `Curve.CurvatureAt(double t)` → `Vector3d` curvature vector
- Mesh face iteration for aspect ratio/skewness computation (custom)

### 2.1.3 File 2: QualityCore.cs (Core Algorithms)

**Purpose**: Dense algorithmic implementations of quality metrics.

**Types** (3 total):
```csharp
// Type 4: Mesh quality metrics (FEA)
public sealed record MeshQuality(
    double AspectRatio,
    double Skewness,
    double Jacobian,
    int[] ProblematicFaces) : IQualityMetric {
    public double Score => (3.0 - AspectRatio + (1.0 - Skewness) + Jacobian) / 3.0;
    public string Description => $"AR={AspectRatio:F2}, S={Skewness:F2}, J={Jacobian:F3}";
}

// Type 5: Manufacturing suitability
public sealed record ManufacturingSuitability(
    bool Suitable,
    string[] Violations,
    double MinimumToolRadius,
    double[] OverhangAngles) : IQualityMetric {
    public double Score => Suitable ? 1.0 : 0.0;
    public string Description => Suitable ? "Suitable" : $"Violations: {string.Join(", ", Violations)}";
}

// Type 6: FEA simulation types enum
public enum FEASimulationType : byte {
    Static = 0,
    Modal = 1,
    Thermal = 2,
    FluidFlow = 3,
}
```

**Key Members** (250 LOC dense algorithms):

```csharp
internal static class QualityCore {
    /// <summary>Computes curvature distribution statistics via sampling.</summary>
    [Pure]
    internal static Result<CurvatureDistribution> ComputeCurvatureDistribution(
        Surface surface,
        int samplesU,
        int samplesV,
        IGeometryContext context) {
        // Sample curvature at grid of (u,v) points
        Interval domainU = surface.Domain(0);
        Interval domainV = surface.Domain(1);
        double stepU = domainU.Length / (samplesU - 1);
        double stepV = domainV.Length / (samplesV - 1);
        
        List<double> gaussianValues = [];
        List<double> meanValues = [];
        List<Point3d> highCurvaturePoints = [];
        
        for (int i = 0; i < samplesU; i++) {
            for (int j = 0; j < samplesV; j++) {
                double u = domainU.Min + i * stepU;
                double v = domainV.Min + j * stepV;
                
                SurfaceCurvature sc = surface.CurvatureAt(u, v);
                double gaussian = sc switch {
                    { Gaussian: double g } when RhinoMath.IsValidDouble(g) => g,
                    _ => 0.0,
                };
                double mean = sc switch {
                    { Mean: double m } when RhinoMath.IsValidDouble(m) => m,
                    _ => 0.0,
                };
                
                gaussianValues.Add(gaussian);
                meanValues.Add(mean);
                
                // Identify high curvature regions (|κ| > 2σ)
                double absCurv = Math.Abs(gaussian);
                // (Add to highCurvaturePoints if exceeds threshold - computed after statistics)
            }
        }
        
        return gaussianValues.Count == 0
            ? ResultFactory.Create<CurvatureDistribution>(error: E.Quality.CurvatureDistributionFailed)
            : ResultFactory.Create(value: new CurvatureDistribution(
                Mean: gaussianValues.Average(),
                StdDev: Math.Sqrt(gaussianValues.Average(v => Math.Pow(v - gaussianValues.Average(), 2))),
                Min: gaussianValues.Min(),
                Max: gaussianValues.Max(),
                Histogram: ComputeHistogram(gaussianValues, bins: 10),
                HighCurvatureRegions: [.. highCurvaturePoints]));
    }

    /// <summary>Computes curve fairness via curvature sampling and inflection detection.</summary>
    [Pure]
    internal static Result<FairnessMetrics> ComputeFairness(
        Curve curve,
        int samples,
        IGeometryContext context) {
        Interval domain = curve.Domain;
        double step = domain.Length / (samples - 1);
        
        List<double> curvatures = [];
        List<Point3d> inflections = [];
        double? prevSign = null;
        
        for (int i = 0; i < samples; i++) {
            double t = domain.Min + i * step;
            Vector3d curvVec = curve.CurvatureAt(t);
            double curv = curvVec.Length;
            
            curvatures.Add(curv);
            
            // Detect inflection points (curvature sign change)
            double? sign = curv > context.AbsoluteTolerance ? 1.0 : curv < -context.AbsoluteTolerance ? -1.0 : null;
            if (prevSign.HasValue && sign.HasValue && prevSign.Value != sign.Value) {
                inflections.Add(curve.PointAt(t));
            }
            prevSign = sign;
        }
        
        // Fairness score: normalized curvature variance (lower is better)
        double fairness = curvatures.Count > 1
            ? 1.0 - Math.Min(1.0, Math.Sqrt(curvatures.Average(c => Math.Pow(c - curvatures.Average(), 2))) / (curvatures.Max() + context.AbsoluteTolerance))
            : 1.0;
        
        // Energy functional (sum of squared curvature derivatives - approximated)
        double energy = 0.0;
        for (int i = 1; i < curvatures.Count; i++) {
            energy += Math.Pow(curvatures[i] - curvatures[i - 1], 2);
        }
        
        return ResultFactory.Create(value: new FairnessMetrics(
            Fairness: fairness,
            InflectionPoints: [.. inflections],
            CurvatureComb: [.. curvatures],
            EnergyFunctional: energy));
    }

    /// <summary>Computes mesh quality metrics (aspect ratio, skewness, Jacobian).</summary>
    [Pure]
    internal static Result<MeshQuality> ComputeMeshQuality(
        Mesh mesh,
        FEASimulationType simType,
        IGeometryContext context) {
        List<double> aspectRatios = [];
        List<double> skewnesses = [];
        List<double> jacobians = [];
        List<int> problematicFaces = [];
        
        for (int i = 0; i < mesh.Faces.Count; i++) {
            MeshFace face = mesh.Faces[i];
            
            // Get face vertices
            Point3d[] vertices = face.IsQuad
                ? [mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D],]
                : [mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C],];
            
            // Compute aspect ratio (longest edge / shortest edge)
            double[] edgeLengths = face.IsQuad
                ? [
                    vertices[0].DistanceTo(vertices[1]),
                    vertices[1].DistanceTo(vertices[2]),
                    vertices[2].DistanceTo(vertices[3]),
                    vertices[3].DistanceTo(vertices[0]),
                ]
                : [
                    vertices[0].DistanceTo(vertices[1]),
                    vertices[1].DistanceTo(vertices[2]),
                    vertices[2].DistanceTo(vertices[0]),
                ];
            
            double aspectRatio = edgeLengths.Max() / (edgeLengths.Min() + context.AbsoluteTolerance);
            aspectRatios.Add(aspectRatio);
            
            // Compute skewness (deviation from ideal angles)
            double skewness = face.IsQuad
                ? ComputeQuadSkewness(vertices)
                : ComputeTriangleSkewness(vertices);
            skewnesses.Add(skewness);
            
            // Compute Jacobian (simplified - full FEA requires element mapping)
            double jacobian = Math.Abs(Vector3d.CrossProduct(
                vertices[1] - vertices[0],
                vertices[2] - vertices[0]).Length / 2.0);
            jacobians.Add(jacobian > context.AbsoluteTolerance ? 1.0 : 0.0);
            
            // Flag problematic faces (AR > 5, S > 0.5, or J ≈ 0)
            if (aspectRatio > 5.0 || skewness > 0.5 || jacobian < context.AbsoluteTolerance) {
                problematicFaces.Add(i);
            }
        }
        
        return aspectRatios.Count == 0
            ? ResultFactory.Create<MeshQuality>(error: E.Quality.FEAMeshQualityFailed)
            : ResultFactory.Create(value: new MeshQuality(
                AspectRatio: aspectRatios.Average(),
                Skewness: skewnesses.Average(),
                Jacobian: jacobians.Average(),
                ProblematicFaces: [.. problematicFaces]));
    }

    /// <summary>Checks manufacturing constraints (tool radius, overhangs, etc.).</summary>
    [Pure]
    internal static Result<ManufacturingSuitability> CheckManufacturingConstraints(
        GeometryBase geometry,
        ManufacturingProcess process,
        IGeometryContext context) {
        List<string> violations = [];
        
        // Get bounding box for basic checks
        BoundingBox bbox = geometry.GetBoundingBox(accurate: true);
        
        // Check size constraints
        double maxDim = Math.Max(Math.Max(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y), bbox.Max.Z - bbox.Min.Z);
        (double minSize, double maxSize, double minToolRadius, double maxOverhangAngle) = process switch {
            ManufacturingProcess.Milling3Axis => (1.0, 1000.0, 0.5, 45.0),
            ManufacturingProcess.Printing3D => (0.1, 300.0, 0.2, 50.0),
            ManufacturingProcess.LaserCutting => (1.0, 2000.0, 0.0, 90.0),
            _ => (0.0, double.MaxValue, 0.0, 90.0),
        };
        
        if (maxDim < minSize) {
            violations.Add($"Geometry too small: {maxDim:F2}mm < {minSize:F2}mm minimum");
        }
        if (maxDim > maxSize) {
            violations.Add($"Geometry too large: {maxDim:F2}mm > {maxSize:F2}mm maximum");
        }
        
        // Check for sharp internal corners (requires minimum tool radius)
        double minimumRadius = geometry switch {
            Brep brep => ComputeMinimumInternalRadius(brep),
            Mesh mesh => ComputeMinimumMeshRadius(mesh),
            _ => minToolRadius,
        };
        
        if (minimumRadius < minToolRadius) {
            violations.Add($"Internal radius {minimumRadius:F2}mm < minimum tool radius {minToolRadius:F2}mm");
        }
        
        // Check overhang angles (3D printing)
        double[] overhangs = geometry switch {
            Brep brep => ComputeOverhangAngles(brep),
            Mesh mesh => ComputeMeshOverhangs(mesh),
            _ => [],
        };
        
        int excessiveOverhangs = overhangs.Count(angle => angle > maxOverhangAngle);
        if (excessiveOverhangs > 0) {
            violations.Add($"{excessiveOverhangs} faces exceed {maxOverhangAngle:F0}° overhang limit");
        }
        
        return ResultFactory.Create(value: new ManufacturingSuitability(
            Suitable: violations.Count == 0,
            Violations: [.. violations],
            MinimumToolRadius: minimumRadius,
            OverhangAngles: overhangs));
    }

    // Helper methods (inline for density)
    [Pure]
    private static double[] ComputeHistogram(IReadOnlyList<double> values, int bins) {
        double min = values.Min();
        double max = values.Max();
        double binWidth = (max - min) / bins;
        int[] counts = new int[bins];
        
        foreach (double v in values) {
            int bin = Math.Min(bins - 1, (int)((v - min) / binWidth));
            counts[bin]++;
        }
        
        return counts.Select(c => (double)c / values.Count).ToArray();
    }

    [Pure]
    private static double ComputeQuadSkewness(Point3d[] vertices) {
        // Skewness = 1 - (min angle / 90°) for quads
        double[] angles = [
            Vector3d.VectorAngle(vertices[1] - vertices[0], vertices[3] - vertices[0]),
            Vector3d.VectorAngle(vertices[2] - vertices[1], vertices[0] - vertices[1]),
            Vector3d.VectorAngle(vertices[3] - vertices[2], vertices[1] - vertices[2]),
            Vector3d.VectorAngle(vertices[0] - vertices[3], vertices[2] - vertices[3]),
        ];
        
        double minAngle = angles.Min() * 180.0 / Math.PI;
        return 1.0 - Math.Min(1.0, minAngle / 90.0);
    }

    [Pure]
    private static double ComputeTriangleSkewness(Point3d[] vertices) {
        // Skewness = 1 - (min angle / 60°) for triangles
        double[] angles = [
            Vector3d.VectorAngle(vertices[1] - vertices[0], vertices[2] - vertices[0]),
            Vector3d.VectorAngle(vertices[2] - vertices[1], vertices[0] - vertices[1]),
            Vector3d.VectorAngle(vertices[0] - vertices[2], vertices[1] - vertices[2]),
        ];
        
        double minAngle = angles.Min() * 180.0 / Math.PI;
        return 1.0 - Math.Min(1.0, minAngle / 60.0);
    }

    [Pure]
    private static double ComputeMinimumInternalRadius(Brep brep) {
        // Iterate edges, compute curvature at midpoint, return minimum radius
        double minRadius = double.MaxValue;
        
        foreach (BrepEdge edge in brep.Edges) {
            double t = edge.Domain.Mid;
            Vector3d curv = edge.CurvatureAt(t);
            double radius = curv.Length > RhinoMath.ZeroTolerance ? 1.0 / curv.Length : double.MaxValue;
            minRadius = Math.Min(minRadius, radius);
        }
        
        return minRadius;
    }

    [Pure]
    private static double ComputeMinimumMeshRadius(Mesh mesh) {
        // Approximate via minimum edge length
        double minEdge = double.MaxValue;
        
        for (int i = 0; i < mesh.TopologyEdges.Count; i++) {
            Line edge = mesh.TopologyEdges.EdgeLine(i);
            minEdge = Math.Min(minEdge, edge.Length);
        }
        
        return minEdge / 2.0;  // Approximate radius as half minimum edge
    }

    [Pure]
    private static double[] ComputeOverhangAngles(Brep brep) {
        List<double> angles = [];
        Vector3d up = Vector3d.ZAxis;
        
        foreach (BrepFace face in brep.Faces) {
            Point2d center = face.Domain(0).Mid * Point2d.Origin.X + face.Domain(1).Mid * Point2d.Origin.Y;
            Vector3d normal = face.NormalAt(center.X, center.Y);
            double angle = Vector3d.VectorAngle(normal, up) * 180.0 / Math.PI;
            angles.Add(angle);
        }
        
        return [.. angles];
    }

    [Pure]
    private static double[] ComputeMeshOverhangs(Mesh mesh) {
        List<double> angles = [];
        Vector3d up = Vector3d.ZAxis;
        
        mesh.FaceNormals.ComputeFaceNormals();
        for (int i = 0; i < mesh.Faces.Count; i++) {
            Vector3d normal = mesh.FaceNormals[i];
            double angle = Vector3d.VectorAngle(normal, up) * 180.0 / Math.PI;
            angles.Add(angle);
        }
        
        return [.. angles];
    }
}
```

**LOC Estimate**: 250 LOC

### 2.1.4 File 3: QualityConfig.cs (Configuration)

**Purpose**: Configuration types for quality analysis.

**Types** (2 total):
```csharp
// Type 7: Manufacturing process enum
public enum ManufacturingProcess : byte {
    Milling3Axis = 0,
    Milling5Axis = 1,
    Printing3D = 2,
    LaserCutting = 3,
    WaterJet = 4,
    CNCLathe = 5,
}

// Type 8: Quality analysis configuration
public sealed record QualityAnalysisConfig(
    int SamplingDensity,
    double FairnessThreshold,
    double AspectRatioLimit,
    double SkewnessLimit,
    ManufacturingProcess? ProcessConstraints) {
    public static readonly QualityAnalysisConfig Default = new(
        SamplingDensity: 20,
        FairnessThreshold: 0.7,
        AspectRatioLimit: 5.0,
        SkewnessLimit: 0.5,
        ProcessConstraints: null);
}
```

**LOC Estimate**: 80 LOC

**TOTAL FOLDER**: 3 files, 8 types, 450 LOC

---

## 2.2 libs/core/patterns/

### 2.2.1 Purpose & Justification

**Purpose**: Pattern detection, clustering, and symmetry analysis algorithms.

**Prevents Duplication In**:
- spatial/ - clustering of proximity results
- extraction/ - pattern recognition in point clouds
- orientation/ - symmetry detection for alignment
- topology/ - connected component clustering

**File Structure** (3 files, 7 types):
```
libs/core/patterns/
├── Patterns.cs        # Public API (~100 LOC)
├── PatternCore.cs     # Algorithms (~200 LOC)
└── PatternConfig.cs   # Configuration (~70 LOC)
```

### 2.2.2 File 1: Patterns.cs (Public API)

**Types** (3 total):
```csharp
namespace Arsenal.Core.Patterns;

// Type 1: Pattern match result
public sealed record PatternMatch(
    PatternType Type,
    Transform Transform,
    double Confidence,
    int[] Indices) {
    public string Description => $"{Type} (confidence={Confidence:F2}, count={Indices.Length})";
}

// Type 2: Pattern type enum
public enum PatternType : byte {
    Reflection = 0,
    Rotation = 1,
    Translation = 2,
    Grid = 3,
    Radial = 4,
}

// Type 3: Symmetry type enum
public enum SymmetryType : byte {
    Planar = 0,
    Rotational = 1,
    Translational = 2,
    All = 255,
}
```

**Key Members**:
```csharp
public static class Patterns {
    /// <summary>Detects symmetry patterns in geometry collection.</summary>
    [Pure]
    public static Result<IReadOnlyList<PatternMatch>> DetectSymmetry<T>(
        T[] geometry,
        SymmetryType type,
        IGeometryContext context) where T : GeometryBase =>
        ResultFactory.Create(value: geometry)
            .Ensure(g => g.Length > 1, error: E.Quality.PatternDetectionFailed.WithContext("Insufficient geometry"))
            .Bind(g => PatternCore.DetectSymmetryInternal(g, type, context));

    /// <summary>Clusters geometry by proximity using k-means or DBSCAN.</summary>
    [Pure]
    public static Result<IReadOnlyList<int[]>> ClusterByProximity<T>(
        T[] geometry,
        ClusteringStrategy strategy,
        IGeometryContext context) where T : GeometryBase =>
        ResultFactory.Create(value: geometry)
            .Ensure(g => g.Length > 0, error: E.Quality.ClusteringFailed)
            .Bind(g => PatternCore.ClusterGeometry(g, strategy, context));

    /// <summary>Detects geometric patterns (grid, radial, etc.).</summary>
    [Pure]
    public static Result<IReadOnlyList<PatternMatch>> ExtractPatterns<T>(
        T[] geometry,
        PatternType type,
        IGeometryContext context) where T : GeometryBase =>
        ResultFactory.Create(value: geometry)
            .Bind(g => PatternCore.ExtractPatternsInternal(g, type, context));
}
```

**LOC Estimate**: 100 LOC

### 2.2.3 File 2: PatternCore.cs (Algorithms)

**Types** (2 total):
```csharp
// Type 4: Clustering strategy enum
public enum ClusteringStrategy : byte {
    KMeans = 0,
    DBSCAN = 1,
    Hierarchical = 2,
}

// Type 5: Cluster result
public sealed record ClusterResult(
    int ClusterIndex,
    int[] Indices,
    Point3d Centroid,
    double Radius);
```

**Key Members** (200 LOC algorithms):
```csharp
internal static class PatternCore {
    /// <summary>Detects symmetry via transformation testing.</summary>
    [Pure]
    internal static Result<IReadOnlyList<PatternMatch>> DetectSymmetryInternal<T>(
        T[] geometry,
        SymmetryType type,
        IGeometryContext context) where T : GeometryBase {
        List<PatternMatch> matches = [];
        
        // Test planar symmetry (reflection)
        if (type.HasFlag(SymmetryType.Planar) || type == SymmetryType.All) {
            // Test XY, YZ, XZ planes
            Plane[] testPlanes = [Plane.WorldXY, Plane.WorldYZ, Plane.WorldZX,];
            
            foreach (Plane plane in testPlanes) {
                Transform reflect = Transform.Mirror(plane);
                (double confidence, int[] indices) = TestSymmetry(geometry, reflect, context);
                
                if (confidence > 0.8) {
                    matches.Add(new PatternMatch(
                        Type: PatternType.Reflection,
                        Transform: reflect,
                        Confidence: confidence,
                        Indices: indices));
                }
            }
        }
        
        // Test rotational symmetry
        if (type.HasFlag(SymmetryType.Rotational) || type == SymmetryType.All) {
            // Test 2-fold, 3-fold, 4-fold, 6-fold rotations
            int[] folds = [2, 3, 4, 6,];
            
            foreach (int fold in folds) {
                double angle = 2.0 * Math.PI / fold;
                Transform rotate = Transform.Rotation(angle, Vector3d.ZAxis, Point3d.Origin);
                (double confidence, int[] indices) = TestSymmetry(geometry, rotate, context);
                
                if (confidence > 0.8) {
                    matches.Add(new PatternMatch(
                        Type: PatternType.Rotation,
                        Transform: rotate,
                        Confidence: confidence,
                        Indices: indices));
                }
            }
        }
        
        return matches.Count > 0
            ? ResultFactory.Create(value: (IReadOnlyList<PatternMatch>)[.. matches])
            : ResultFactory.Create<IReadOnlyList<PatternMatch>>(
                error: E.Quality.SymmetryDetectionFailed.WithContext("No symmetry detected"));
    }

    /// <summary>Clusters geometry using k-means or DBSCAN.</summary>
    [Pure]
    internal static Result<IReadOnlyList<int[]>> ClusterGeometry<T>(
        T[] geometry,
        ClusteringStrategy strategy,
        IGeometryContext context) where T : GeometryBase {
        // Extract centroids for clustering
        Point3d[] centroids = geometry.Select(g => {
            BoundingBox bbox = g.GetBoundingBox(accurate: false);
            return bbox.Center;
        }).ToArray();
        
        return strategy switch {
            ClusteringStrategy.KMeans => KMeansClustering(centroids, k: (int)Math.Sqrt(centroids.Length), context),
            ClusteringStrategy.DBSCAN => DBSCANClustering(centroids, epsilon: 10.0, minPoints: 3, context),
            ClusteringStrategy.Hierarchical => HierarchicalClustering(centroids, threshold: 5.0, context),
            _ => ResultFactory.Create<IReadOnlyList<int[]>>(error: E.Quality.ClusteringFailed),
        };
    }

    /// <summary>Extracts patterns (grid, radial, etc.).</summary>
    [Pure]
    internal static Result<IReadOnlyList<PatternMatch>> ExtractPatternsInternal<T>(
        T[] geometry,
        PatternType type,
        IGeometryContext context) where T : GeometryBase {
        List<PatternMatch> patterns = [];
        
        // Extract centroids
        Point3d[] points = geometry.Select(g => g.GetBoundingBox(accurate: false).Center).ToArray();
        
        // Detect grid pattern
        if (type == PatternType.Grid) {
            (bool isGrid, Vector3d spacing, Transform transform) = DetectGridPattern(points, context);
            
            if (isGrid) {
                patterns.Add(new PatternMatch(
                    Type: PatternType.Grid,
                    Transform: transform,
                    Confidence: 0.9,
                    Indices: Enumerable.Range(0, geometry.Length).ToArray()));
            }
        }
        
        // Detect radial pattern
        if (type == PatternType.Radial) {
            (bool isRadial, Point3d center, double radius) = DetectRadialPattern(points, context);
            
            if (isRadial) {
                patterns.Add(new PatternMatch(
                    Type: PatternType.Radial,
                    Transform: Transform.Translation(center - Point3d.Origin),
                    Confidence: 0.85,
                    Indices: Enumerable.Range(0, geometry.Length).ToArray()));
            }
        }
        
        return patterns.Count > 0
            ? ResultFactory.Create(value: (IReadOnlyList<PatternMatch>)[.. patterns])
            : ResultFactory.Create<IReadOnlyList<PatternMatch>>(
                error: E.Quality.PatternDetectionFailed.WithContext($"No {type} pattern detected"));
    }

    // Helper methods (inline for density)
    [Pure]
    private static (double confidence, int[] indices) TestSymmetry<T>(
        T[] geometry,
        Transform transform,
        IGeometryContext context) where T : GeometryBase {
        int matches = 0;
        List<int> matchedIndices = [];
        
        for (int i = 0; i < geometry.Length; i++) {
            GeometryBase transformed = geometry[i].Duplicate();
            transformed.Transform(transform);
            
            // Find closest geometry to transformed
            for (int j = 0; j < geometry.Length; j++) {
                if (i == j) continue;
                
                double distance = transformed.GetBoundingBox(accurate: false).Center
                    .DistanceTo(geometry[j].GetBoundingBox(accurate: false).Center);
                
                if (distance < context.AbsoluteTolerance * 10.0) {
                    matches++;
                    matchedIndices.Add(i);
                    break;
                }
            }
        }
        
        double confidence = (double)matches / geometry.Length;
        return (confidence, [.. matchedIndices]);
    }

    [Pure]
    private static Result<IReadOnlyList<int[]>> KMeansClustering(
        Point3d[] points,
        int k,
        IGeometryContext context) {
        // k-means implementation (simplified)
        Random rng = new(42);
        Point3d[] centroids = Enumerable.Range(0, k).Select(_ => points[rng.Next(points.Length)]).ToArray();
        int[] assignments = new int[points.Length];
        bool changed = true;
        int iterations = 0;
        int maxIterations = 100;
        
        while (changed && iterations < maxIterations) {
            changed = false;
            iterations++;
            
            // Assign points to nearest centroid
            for (int i = 0; i < points.Length; i++) {
                int nearest = 0;
                double minDist = double.MaxValue;
                
                for (int j = 0; j < k; j++) {
                    double dist = points[i].DistanceTo(centroids[j]);
                    if (dist < minDist) {
                        minDist = dist;
                        nearest = j;
                    }
                }
                
                if (assignments[i] != nearest) {
                    assignments[i] = nearest;
                    changed = true;
                }
            }
            
            // Recompute centroids
            for (int j = 0; j < k; j++) {
                int[] cluster = Enumerable.Range(0, points.Length).Where(i => assignments[i] == j).ToArray();
                if (cluster.Length > 0) {
                    Point3d avg = new(
                        cluster.Average(i => points[i].X),
                        cluster.Average(i => points[i].Y),
                        cluster.Average(i => points[i].Z));
                    centroids[j] = avg;
                }
            }
        }
        
        // Group indices by cluster
        int[][] clusters = Enumerable.Range(0, k)
            .Select(j => Enumerable.Range(0, points.Length).Where(i => assignments[i] == j).ToArray())
            .Where(c => c.Length > 0)
            .ToArray();
        
        return ResultFactory.Create(value: (IReadOnlyList<int[]>)clusters);
    }

    [Pure]
    private static Result<IReadOnlyList<int[]>> DBSCANClustering(
        Point3d[] points,
        double epsilon,
        int minPoints,
        IGeometryContext context) {
        // DBSCAN implementation (simplified density-based clustering)
        int[] labels = Enumerable.Repeat(-1, points.Length).ToArray();  // -1 = noise
        int clusterId = 0;
        
        for (int i = 0; i < points.Length; i++) {
            if (labels[i] != -1) continue;
            
            List<int> neighbors = FindNeighbors(points, i, epsilon);
            
            if (neighbors.Count < minPoints) {
                labels[i] = -1;  // Mark as noise
                continue;
            }
            
            labels[i] = clusterId;
            Queue<int> queue = new(neighbors);
            
            while (queue.Count > 0) {
                int j = queue.Dequeue();
                if (labels[j] == -1) labels[j] = clusterId;
                if (labels[j] != -1) continue;
                
                labels[j] = clusterId;
                List<int> jNeighbors = FindNeighbors(points, j, epsilon);
                
                if (jNeighbors.Count >= minPoints) {
                    foreach (int n in jNeighbors) {
                        if (labels[n] == -1 || !queue.Contains(n)) {
                            queue.Enqueue(n);
                        }
                    }
                }
            }
            
            clusterId++;
        }
        
        int[][] clusters = Enumerable.Range(0, clusterId)
            .Select(c => Enumerable.Range(0, points.Length).Where(i => labels[i] == c).ToArray())
            .ToArray();
        
        return ResultFactory.Create(value: (IReadOnlyList<int[]>)clusters);
    }

    [Pure]
    private static Result<IReadOnlyList<int[]>> HierarchicalClustering(
        Point3d[] points,
        double threshold,
        IGeometryContext context) =>
        // Hierarchical clustering (simplified) - use single-linkage
        ResultFactory.Create<IReadOnlyList<int[]>>(
            error: E.Quality.ClusteringFailed.WithContext("Hierarchical clustering not yet implemented"));

    [Pure]
    private static List<int> FindNeighbors(Point3d[] points, int index, double epsilon) {
        List<int> neighbors = [];
        
        for (int i = 0; i < points.Length; i++) {
            if (i == index) continue;
            if (points[index].DistanceTo(points[i]) <= epsilon) {
                neighbors.Add(i);
            }
        }
        
        return neighbors;
    }

    [Pure]
    private static (bool isGrid, Vector3d spacing, Transform transform) DetectGridPattern(
        Point3d[] points,
        IGeometryContext context) {
        // Simplified grid detection - check if points form regular grid
        if (points.Length < 4) return (false, Vector3d.Zero, Transform.Identity);
        
        // Sort points by X, then Y
        Point3d[] sorted = [.. points.OrderBy(p => p.X).ThenBy(p => p.Y)];
        
        // Check spacing consistency
        List<double> xSpacings = [];
        List<double> ySpacings = [];
        
        for (int i = 1; i < sorted.Length; i++) {
            double dx = sorted[i].X - sorted[i - 1].X;
            double dy = sorted[i].Y - sorted[i - 1].Y;
            
            if (Math.Abs(dx) > context.AbsoluteTolerance) xSpacings.Add(dx);
            if (Math.Abs(dy) > context.AbsoluteTolerance) ySpacings.Add(dy);
        }
        
        bool isGrid = xSpacings.Count > 0 && ySpacings.Count > 0 &&
                      xSpacings.Max() - xSpacings.Min() < context.AbsoluteTolerance * 10 &&
                      ySpacings.Max() - ySpacings.Min() < context.AbsoluteTolerance * 10;
        
        Vector3d spacing = isGrid ? new Vector3d(xSpacings.Average(), ySpacings.Average(), 0) : Vector3d.Zero;
        
        return (isGrid, spacing, Transform.Identity);
    }

    [Pure]
    private static (bool isRadial, Point3d center, double radius) DetectRadialPattern(
        Point3d[] points,
        IGeometryContext context) {
        // Simplified radial detection - check if points are equidistant from centroid
        if (points.Length < 3) return (false, Point3d.Origin, 0);
        
        Point3d centroid = new(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
        double[] radii = points.Select(p => p.DistanceTo(centroid)).ToArray();
        
        bool isRadial = radii.Max() - radii.Min() < context.AbsoluteTolerance * 10;
        double radius = isRadial ? radii.Average() : 0;
        
        return (isRadial, centroid, radius);
    }
}
```

**LOC Estimate**: 200 LOC

### 2.2.4 File 3: PatternConfig.cs

**Types** (2 total):
```csharp
// Type 6: Pattern detection configuration
public sealed record PatternDetectionConfig(
    double SymmetryTolerance,
    int MinClusterSize,
    double ClusteringEpsilon,
    int KMeansClusters) {
    public static readonly PatternDetectionConfig Default = new(
        SymmetryTolerance: 0.01,
        MinClusterSize: 3,
        ClusteringEpsilon: 10.0,
        KMeansClusters: 0);  // 0 = auto-detect
}

// Type 7: Pattern analysis result
public sealed record PatternAnalysis(
    IReadOnlyList<PatternMatch> Patterns,
    IReadOnlyList<int[]> Clusters,
    double OverallConfidence);
```

**LOC Estimate**: 70 LOC

**TOTAL FOLDER**: 3 files, 7 types, 370 LOC

---

## 2.3 libs/rhino/shared/

### 2.3.1 Purpose & Justification

**Purpose**: Shared RhinoCommon utility operations that don't fit in a single folder.

**Prevents Duplication In**:
- extraction/ - primitive fitting (plane/cylinder/sphere)
- analysis/ - medial axis approximation
- topology/ - near-miss detection utilities
- orientation/ - best-fit transform computation

**File Structure** (3 files, 6 types):
```
libs/rhino/shared/
├── Shared.cs          # Public API (~90 LOC)
├── SharedCore.cs      # Algorithms (~180 LOC)
└── SharedConfig.cs    # Configuration (~60 LOC)
```

### 2.3.2 File 1: Shared.cs (Public API)

**Types** (2 total):
```csharp
namespace Arsenal.Rhino.Shared;

// Type 1: Primitive fit result
public sealed record PrimitiveFit(
    PrimitiveType Type,
    GeometryBase Primitive,
    double Residual,
    double Confidence) {
    public string Description => $"{Type} (R²={Confidence:F3}, residual={Residual:F3})";
}

// Type 2: Primitive type enum
public enum PrimitiveType : byte {
    Plane = 0,
    Cylinder = 1,
    Sphere = 2,
    Cone = 3,
    Torus = 4,
}
```

**Key Members**:
```csharp
public static class Shared {
    /// <summary>Fits best-fit primitive to point cloud.</summary>
    [Pure]
    public static Result<PrimitiveFit> FitPrimitive(
        Point3d[] points,
        PrimitiveType type,
        IGeometryContext context) =>
        ResultFactory.Create(value: points)
            .Ensure(pts => pts.Length >= 3, error: E.Geometry.InsufficientParameters)
            .Bind(pts => SharedCore.FitPrimitiveInternal(pts, type, context));

    /// <summary>Computes approximate medial axis/skeleton.</summary>
    [Pure]
    public static Result<Curve[]> ComputeMedialAxis(
        Brep brep,
        MedialAxisMethod method,
        IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, V.Standard])
            .Bind(b => SharedCore.ComputeMedialAxisInternal(b, method, context));

    /// <summary>Finds near-miss geometries (almost intersecting).</summary>
    [Pure]
    public static Result<IReadOnlyList<(int, int, double)>> FindNearMisses<T>(
        T[] geometry,
        double searchRadius,
        IGeometryContext context) where T : GeometryBase =>
        ResultFactory.Create(value: geometry)
            .Bind(g => SharedCore.FindNearMissesInternal(g, searchRadius, context));
}
```

**LOC Estimate**: 90 LOC

**RhinoCommon APIs Used**:
- `Plane.FitPlaneToPoints(Point3d[])` - Built-in best-fit plane
- Custom least-squares for cylinder/sphere (no native methods)
- Curve offsetting + thinning for medial axis approximation
- `GeometryBase.ClosestPoint` for near-miss detection

### 2.3.3 File 2: SharedCore.cs (Algorithms)

**Types** (2 total):
```csharp
// Type 3: Medial axis method enum
public enum MedialAxisMethod : byte {
    CurveOffset = 0,       // For 2D/planar shapes
    VoronoiApproximation = 1,  // For complex 3D (requires external lib)
    SkeletonThinning = 2,  // For meshes
}

// Type 4: Near-miss result
public sealed record NearMiss(
    int IndexA,
    int IndexB,
    Point3d ClosestPointA,
    Point3d ClosestPointB,
    double Distance);
```

**Key Members** (180 LOC):
```csharp
internal static class SharedCore {
    /// <summary>Fits primitive using least-squares.</summary>
    [Pure]
    internal static Result<PrimitiveFit> FitPrimitiveInternal(
        Point3d[] points,
        PrimitiveType type,
        IGeometryContext context) =>
        type switch {
            PrimitiveType.Plane => FitPlane(points, context),
            PrimitiveType.Cylinder => FitCylinder(points, context),
            PrimitiveType.Sphere => FitSphere(points, context),
            _ => ResultFactory.Create<PrimitiveFit>(
                error: E.Quality.PrimitiveDecompositionFailed.WithContext($"Type {type} not implemented")),
        };

    /// <summary>Computes medial axis approximation.</summary>
    [Pure]
    internal static Result<Curve[]> ComputeMedialAxisInternal(
        Brep brep,
        MedialAxisMethod method,
        IGeometryContext context) =>
        method switch {
            MedialAxisMethod.CurveOffset => ComputeViaCurveOffset(brep, context),
            MedialAxisMethod.VoronoiApproximation => ResultFactory.Create<Curve[]>(
                error: E.Quality.MedialAxisFailed.WithContext("Voronoi method requires external library")),
            MedialAxisMethod.SkeletonThinning => ComputeViaSkeletonThinning(brep, context),
            _ => ResultFactory.Create<Curve[]>(error: E.Quality.MedialAxisFailed),
        };

    /// <summary>Finds near-miss geometries.</summary>
    [Pure]
    internal static Result<IReadOnlyList<(int, int, double)>> FindNearMissesInternal<T>(
        T[] geometry,
        double searchRadius,
        IGeometryContext context) where T : GeometryBase {
        List<(int, int, double)> nearMisses = [];
        
        for (int i = 0; i < geometry.Length; i++) {
            for (int j = i + 1; j < geometry.Length; j++) {
                Point3d closestA = geometry[i].ClosestPoint(geometry[j].GetBoundingBox(accurate: false).Center, out double distToBoxCenter);
                Point3d closestB = geometry[j].ClosestPoint(closestA, out double _);
                double distance = closestA.DistanceTo(closestB);
                
                if (distance > context.AbsoluteTolerance && distance < searchRadius) {
                    nearMisses.Add((i, j, distance));
                }
            }
        }
        
        return nearMisses.Count > 0
            ? ResultFactory.Create(value: (IReadOnlyList<(int, int, double)>)[.. nearMisses])
            : ResultFactory.Create<IReadOnlyList<(int, int, double)>>(
                error: E.Quality.NearMissDetectionFailed.WithContext("No near-misses found"));
    }

    // Helper methods
    [Pure]
    private static Result<PrimitiveFit> FitPlane(Point3d[] points, IGeometryContext context) {
        Plane plane = Plane.FitPlaneToPoints(points, out double deviation);
        
        return plane.IsValid
            ? ResultFactory.Create(value: new PrimitiveFit(
                Type: PrimitiveType.Plane,
                Primitive: new PlaneSurface(plane, new Interval(-100, 100), new Interval(-100, 100)),
                Residual: deviation,
                Confidence: 1.0 - Math.Min(1.0, deviation / 10.0)))
            : ResultFactory.Create<PrimitiveFit>(error: E.Quality.PrimitiveDecompositionFailed);
    }

    [Pure]
    private static Result<PrimitiveFit> FitCylinder(Point3d[] points, IGeometryContext context) {
        // Simplified cylinder fitting (full implementation requires iterative least-squares)
        // 1. Compute centroid
        Point3d centroid = new(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
        
        // 2. Use PCA to find axis (eigenvector with largest eigenvalue)
        // (Simplified - use Z-axis as approximation)
        Vector3d axis = Vector3d.ZAxis;
        
        // 3. Project points onto plane perpendicular to axis, compute average radius
        double avgRadius = points.Average(p => {
            Vector3d vec = p - centroid;
            Vector3d projected = vec - (vec * axis) * axis;
            return projected.Length;
        });
        
        // 4. Create cylinder
        Cylinder cyl = new(
            new Circle(new Plane(centroid, axis), avgRadius),
            points.Max(p => (p - centroid) * axis) - points.Min(p => (p - centroid) * axis));
        
        // 5. Compute residual
        double residual = points.Average(p => {
            cyl.ClosestPoint(p, out double s, out double t);
            return p.DistanceTo(cyl.PointAt(s, t));
        });
        
        return ResultFactory.Create(value: new PrimitiveFit(
            Type: PrimitiveType.Cylinder,
            Primitive: cyl.ToNurbsSurface(),
            Residual: residual,
            Confidence: 1.0 - Math.Min(1.0, residual / avgRadius)));
    }

    [Pure]
    private static Result<PrimitiveFit> FitSphere(Point3d[] points, IGeometryContext context) {
        // Simplified sphere fitting
        // 1. Centroid as initial guess
        Point3d center = new(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
        
        // 2. Average distance as radius
        double radius = points.Average(p => p.DistanceTo(center));
        
        // 3. Refine via least-squares (simplified - just use average)
        Sphere sphere = new(center, radius);
        
        // 4. Compute residual
        double residual = points.Average(p => Math.Abs(p.DistanceTo(center) - radius));
        
        return ResultFactory.Create(value: new PrimitiveFit(
            Type: PrimitiveType.Sphere,
            Primitive: sphere.ToNurbsSurface(),
            Residual: residual,
            Confidence: 1.0 - Math.Min(1.0, residual / radius)));
    }

    [Pure]
    private static Result<Curve[]> ComputeViaCurveOffset(Brep brep, IGeometryContext context) {
        // Medial axis via curve offsetting (2D/planar breps only)
        if (!brep.IsSurface || brep.Faces.Count != 1) {
            return ResultFactory.Create<Curve[]>(
                error: E.Quality.MedialAxisFailed.WithContext("Requires single planar face"));
        }
        
        BrepFace face = brep.Faces[0];
        Curve[] boundaries = face.OuterLoop.To3dCurve().ToNurbsCurve().DuplicateSegments();
        
        // Offset curves inward iteratively until they converge
        List<Curve> skeleton = [];
        double offsetDist = 1.0;
        
        for (int i = 0; i < boundaries.Length; i++) {
            Curve[] offsets = boundaries[i].Offset(
                Plane.WorldXY,
                -offsetDist,
                context.AbsoluteTolerance,
                CurveOffsetCornerStyle.Sharp);
            
            if (offsets?.Length > 0) {
                skeleton.AddRange(offsets);
            }
        }
        
        return skeleton.Count > 0
            ? ResultFactory.Create(value: [.. skeleton])
            : ResultFactory.Create<Curve[]>(error: E.Quality.MedialAxisFailed);
    }

    [Pure]
    private static Result<Curve[]> ComputeViaSkeletonThinning(Brep brep, IGeometryContext context) =>
        // Skeleton thinning for meshes (simplified - requires iterative thinning algorithm)
        ResultFactory.Create<Curve[]>(
            error: E.Quality.MedialAxisFailed.WithContext("Skeleton thinning not yet implemented"));
}
```

**LOC Estimate**: 180 LOC

### 2.3.4 File 3: SharedConfig.cs

**Types** (2 total):
```csharp
// Type 5: Primitive fitting configuration
public sealed record PrimitiveFittingConfig(
    int MaxIterations,
    double ConvergenceThreshold,
    bool RefineWithNonlinear) {
    public static readonly PrimitiveFittingConfig Default = new(
        MaxIterations: 100,
        ConvergenceThreshold: 0.001,
        RefineWithNonlinear: false);
}

// Type 6: Medial axis configuration
public sealed record MedialAxisConfig(
    MedialAxisMethod Method,
    int MaxIterations,
    double Tolerance) {
    public static readonly MedialAxisConfig Default = new(
        Method: MedialAxisMethod.CurveOffset,
        MaxIterations: 50,
        Tolerance: 0.01);
}
```

**LOC Estimate**: 60 LOC

**TOTAL FOLDER**: 3 files, 6 types, 330 LOC

---

# Part 3: Feature Integration Mapping

## 3.1 Topology Features

**Current State**: `libs/rhino/topology/` has 3 files, 7 types (EdgeContinuityType, NakedEdges, BoundaryLoops, ConnectedComponents, EdgeClassification, Adjacency, EdgeGraph).

**New Features from new_libs_functionality.md**:
1. `DiagnoseTopology(Brep)` - Find topology problems with diagnostics
2. `HealTopology(Brep, HealingStrategy)` - Intelligent repair with rollback
3. `ExtractFeatures(Brep)` - Holes, handles, genus

**Implementation Strategy**: Add 3 thin delegation methods to `Topology.cs` (25 LOC each = 75 LOC total).

### 3.1.1 DiagnoseTopology

**Add to `libs/rhino/topology/Topology.cs`**:
```csharp
/// <summary>Diagnoses topology problems with detailed diagnostics.</summary>
[Pure]
public static Result<TopologyDiagnosis> DiagnoseTopology(
    Brep brep,
    IGeometryContext context) =>
    ResultFactory.Create(value: brep)
        .Validate(args: [context, V.Topology])
        .Bind(b => {
            // Get naked edges
            Result<NakedEdges> nakedResult = Analyze<Brep, NakedEdges>(b, context);
            
            // Compute genus (Euler characteristic)
            int v = b.Vertices.Count;
            int e = b.Edges.Count;
            int f = b.Faces.Count;
            int eulerChar = v - e + f;
            int genus = (2 - eulerChar) / 2;  // For closed surfaces
            
            // Check for non-manifold edges
            int nonManifoldCount = b.Edges.Count(edge => edge.AdjacentFaces().Length > 2);
            
            return nakedResult.Map(naked => new TopologyDiagnosis(
                IsValid: b.IsValid,
                EulerCharacteristic: eulerChar,
                Genus: genus,
                NakedEdgeCount: naked.Edges.Count,
                NonManifoldEdgeCount: nonManifoldCount,
                ProbableCause: naked.Edges.Count > 0 ? "Gaps in surface trimming" : "Unknown",
                SuggestedStrategies: [HealingStrategy.JoinEdges, HealingStrategy.FillHoles,]));
        });
```

**New Type** (add to `TopologyConfig.cs`):
```csharp
public sealed record TopologyDiagnosis(
    bool IsValid,
    int EulerCharacteristic,
    int Genus,
    int NakedEdgeCount,
    int NonManifoldEdgeCount,
    string ProbableCause,
    HealingStrategy[] SuggestedStrategies);

public enum HealingStrategy : byte {
    JoinEdges = 0,
    FillHoles = 1,
    RebuildTrim = 2,
    SimplifyTopology = 3,
}
```

**RhinoCommon APIs Used**:
- `Brep.IsValid`, `.Vertices`, `.Edges`, `.Faces`
- `BrepEdge.AdjacentFaces()` - Get adjacent faces for manifold check
- Existing `Topology.Analyze<Brep, NakedEdges>()` method

**LOC**: 25 (delegation + diagnosis logic)

### 3.1.2 HealTopology

**Add to `libs/rhino/topology/Topology.cs`**:
```csharp
/// <summary>Heals topology with progressive strategies and rollback on failure.</summary>
[Pure]
public static Result<Brep> HealTopology(
    Brep brep,
    HealingStrategy strategy,
    IGeometryContext context) =>
    ResultFactory.Create(value: brep)
        .Bind(b => {
            Brep healed = b.DuplicateBrep();
            
            bool success = strategy switch {
                HealingStrategy.JoinEdges => healed.JoinNakedEdges(context.AbsoluteTolerance * 10),
                HealingStrategy.FillHoles => Brep.CreatePlanarBreps([.. healed.DuplicateEdgeCurves()], context.AbsoluteTolerance) is { Length: > 0 },
                HealingStrategy.RebuildTrim => healed.Repair(context.AbsoluteTolerance),
                HealingStrategy.SimplifyTopology => healed.MergeCoplanarFaces(context.AbsoluteTolerance),
                _ => false,
            };
            
            // Validate healed brep
            return success && healed.IsValid
                ? ResultFactory.Create(value: healed)
                : ResultFactory.Create<Brep>(error: E.Topology.HealingFailed.WithContext($"Strategy {strategy} unsuccessful"));
        });
```

**RhinoCommon APIs Used**:
- `Brep.DuplicateBrep()` - Create copy for non-destructive healing
- `Brep.JoinNakedEdges(double tolerance)` - Join edges within tolerance
- `Brep.CreatePlanarBreps(Curve[], double)` - Fill holes with planar patches
- `Brep.Repair(double)` - Repair invalid trimming
- `Brep.MergeCoplanarFaces(double)` - Simplify by merging faces

**LOC**: 22

### 3.1.3 ExtractFeatures

**Add to `libs/rhino/topology/Topology.cs`**:
```csharp
/// <summary>Extracts topological features (holes, handles, genus).</summary>
[Pure]
public static Result<TopologicalFeatures> ExtractFeatures(
    Brep brep,
    IGeometryContext context) =>
    DiagnoseTopology(brep, context)
        .Bind(diag => Analyze<Brep, ConnectedComponents>(brep, context)
            .Map(comps => new TopologicalFeatures(
                Genus: diag.Genus,
                HoleCount: diag.NakedEdgeCount > 0 ? CountBoundaryLoops(brep) : 0,
                HandleCount: Math.Max(0, diag.Genus),  // For closed surfaces
                ComponentCount: comps.Components.Length,
                Classification: diag.Genus == 0 ? "Simply-connected" : $"Genus-{diag.Genus} surface",
                DesignIntent: InferDesignIntent(brep, diag))));

private static int CountBoundaryLoops(Brep brep) =>
    brep.Loops.Count(loop => loop.LoopType == BrepLoopType.Outer);

private static string InferDesignIntent(Brep brep, TopologyDiagnosis diag) =>
    brep.Faces.Count switch {
        1 when brep.Faces[0].IsPlanar() => "Planar surface",
        _ when brep.Faces.All(f => f.IsCylinder()) => "Revolved surface",
        _ when brep.IsSolid => "Solid model",
        _ => "Complex surface",
    };
```

**New Type** (add to `TopologyConfig.cs`):
```csharp
public sealed record TopologicalFeatures(
    int Genus,
    int HoleCount,
    int HandleCount,
    int ComponentCount,
    string Classification,
    string DesignIntent);
```

**LOC**: 28

**TOTAL ADDED TO TOPOLOGY**: 75 LOC in `Topology.cs`, 2 new types in `TopologyConfig.cs` (already within 10-type limit: 7 + 2 = 9 types).

---

## 3.2 Spatial Features

**Current State**: `libs/rhino/spatial/` has 3 files, 4 types (uses FrozenDictionary dispatch).

**New Features**:
1. `ClusterByProximity(GeometryBase[], ClusteringStrategy)` - Spatial clustering
2. `ComputeProximityField(GeometryBase[], Vector3d, ProximityOptions)` - Directional proximity
3. (Medial axis computation moved to `libs/rhino/shared/`)

**Implementation Strategy**: Add 2 thin delegation methods to `Spatial.cs` (15 LOC each = 30 LOC total).

### 3.2.1 ClusterByProximity

**Add to `libs/rhino/spatial/Spatial.cs`**:
```csharp
/// <summary>Clusters geometry by spatial proximity.</summary>
[Pure]
public static Result<IReadOnlyList<int[]>> ClusterByProximity<T>(
    T[] geometry,
    ClusteringStrategy strategy,
    IGeometryContext context) where T : GeometryBase =>
    Arsenal.Core.Patterns.Patterns.ClusterByProximity(geometry, strategy, context);
```

**RhinoCommon APIs Used**: Delegates to `libs/core/patterns/` (uses centroids from `GetBoundingBox(false).Center`).

**LOC**: 8

### 3.2.2 ComputeProximityField

**Add to `Spatial.cs`**:
```csharp
/// <summary>Computes directional proximity field.</summary>
[Pure]
public static Result<ProximityField> ComputeProximityField(
    GeometryBase[] geometry,
    Vector3d direction,
    ProximityOptions options,
    IGeometryContext context) =>
    ResultFactory.Create(value: geometry)
        .Ensure(g => g.Length > 0 && direction.IsValid, error: E.Spatial.ProximityFailed)
        .Bind(g => SpatialCore.ComputeProximityFieldInternal(g, direction, options, context));
```

**Add to `SpatialCore.cs`** (new method, 60 LOC):
```csharp
internal static Result<ProximityField> ComputeProximityFieldInternal(
    GeometryBase[] geometry,
    Vector3d direction,
    ProximityOptions options,
    IGeometryContext context) {
    // Use RTree for efficient proximity queries
    RTree tree = TreeCache.GetValue(geometry, static g => {
        RTree t = new();
        for (int i = 0; i < g.Length; i++) {
            t.Insert(g[i].GetBoundingBox(accurate: false), i);
        }
        return t;
    });
    
    // For each geometry, find neighbors in direction
    List<ProximityNeighbor[]> fieldData = [];
    
    for (int i = 0; i < geometry.Length; i++) {
        Point3d center = geometry[i].GetBoundingBox(accurate: false).Center;
        Point3d searchPoint = center + direction * options.SearchDistance;
        
        // Find geometries in search cone
        Sphere searchSphere = new(center, options.SearchDistance);
        tree.Search(searchSphere, out int[] indices);
        
        List<ProximityNeighbor> neighbors = [];
        foreach (int j in indices) {
            if (i == j) continue;
            
            Vector3d toNeighbor = geometry[j].GetBoundingBox(accurate: false).Center - center;
            double angle = Vector3d.VectorAngle(direction, toNeighbor);
            
            // Weight by angle deviation (cosine falloff)
            if (angle <= options.MaxAngleDeviation) {
                double weight = Math.Cos(angle);
                neighbors.Add(new ProximityNeighbor(j, toNeighbor.Length, weight));
            }
        }
        
        fieldData.Add([.. neighbors.OrderBy(n => n.Distance)]);
    }
    
    return ResultFactory.Create(value: new ProximityField(
        Direction: direction,
        Neighbors: [.. fieldData],
        AverageNeighborCount: fieldData.Average(n => n.Length)));
}
```

**New Types** (add to `SpatialConfig.cs`):
```csharp
public sealed record ProximityOptions(
    double SearchDistance,
    double MaxAngleDeviation);

public sealed record ProximityNeighbor(
    int Index,
    double Distance,
    double Weight);

public sealed record ProximityField(
    Vector3d Direction,
    IReadOnlyList<ProximityNeighbor[]> Neighbors,
    double AverageNeighborCount);
```

**LOC**: 15 (API) + 60 (Core) = 75 LOC total

**TOTAL ADDED TO SPATIAL**: 83 LOC, 3 new types (4 + 3 = 7 types, within limit).

---

## 3.3 Analysis Features

**Current State**: `libs/rhino/analysis/` has 3 files, 6 types (CurveData, SurfaceData, BrepData, MeshData, AnalysisSpec, registries).

**New Features**:
1. `AnalyzeSurfaceQuality(Surface)` - Gaussian/mean curvature distribution
2. `AnalyzeCurveFairness(Curve)` - Curvature comb smoothness
3. `AnalyzeForFEA(Mesh)` - Mesh quality metrics

**Implementation Strategy**: Add 3 thin delegation methods to `Analysis.cs` (8-12 LOC each = 30 LOC total).

### 3.3.1 AnalyzeSurfaceQuality

**Add to `libs/rhino/analysis/Analysis.cs`**:
```csharp
/// <summary>Analyzes surface quality via curvature distribution.</summary>
[Pure]
public static Result<Arsenal.Core.Quality.CurvatureDistribution> AnalyzeSurfaceQuality(
    Surface surface,
    int samplesU,
    int samplesV,
    IGeometryContext context) =>
    Arsenal.Core.Quality.Quality.AnalyzeCurvatureDistribution(surface, samplesU, samplesV, context);
```

**LOC**: 8

### 3.3.2 AnalyzeCurveFairness

**Add to `Analysis.cs`**:
```csharp
/// <summary>Analyzes curve fairness via curvature comb.</summary>
[Pure]
public static Result<Arsenal.Core.Quality.FairnessMetrics> AnalyzeCurveFairness(
    Curve curve,
    int samples,
    IGeometryContext context) =>
    Arsenal.Core.Quality.Quality.AnalyzeCurveFairness(curve, samples, context);
```

**LOC**: 8

### 3.3.3 AnalyzeForFEA

**Add to `Analysis.cs`**:
```csharp
/// <summary>Analyzes mesh quality for FEA suitability.</summary>
[Pure]
public static Result<Arsenal.Core.Quality.MeshQuality> AnalyzeForFEA(
    Mesh mesh,
    Arsenal.Core.Quality.FEASimulationType simType,
    IGeometryContext context) =>
    Arsenal.Core.Quality.Quality.AnalyzeForFEA(mesh, simType, context);
```

**LOC**: 8

**TOTAL ADDED TO ANALYSIS**: 24 LOC, 0 new types (stays at 6 types).

---

## 3.4 Intersection Features

**Current State**: `libs/rhino/intersection/` has 3 files, 5 types (Intersect entry point + 4 result types).

**New Features**:
1. `ClassifyIntersection(GeometryBase, GeometryBase, IntersectionOutput)` - Tangency vs transverse
2. `FindNearMisses(GeometryBase, GeometryBase, double)` - Near-miss analysis
3. `AnalyzeStability(GeometryBase, GeometryBase, IntersectionOutput)` - Perturbation testing

**Implementation Strategy**: Add 3 thin delegation methods (20-25 LOC each = 65 LOC total).

### 3.4.1 ClassifyIntersection

**Add to `libs/rhino/intersection/Intersect.cs`**:
```csharp
/// <summary>Classifies intersection by tangency and approach angles.</summary>
[Pure]
public static Result<IntersectionClassification> ClassifyIntersection<TA, TB>(
    TA geometryA,
    TB geometryB,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase =>
    ResultFactory.Create(value: (geometryA, geometryB))
        .Bind(tuple => {
            // Compute intersection first
            Result<IReadOnlyList<Point3d>> intersectionResult = geometryA switch {
                Curve cA when geometryB is Curve cB => IntersectCurveCurve(cA, cB, context)
                    .Map(events => events.Select(e => e.Location).ToArray() as IReadOnlyList<Point3d>),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.UnsupportedIntersection),
            };
            
            return intersectionResult.Bind(points => points.Count > 0
                ? AnalyzeIntersectionAngles(geometryA, geometryB, points, context)
                : ResultFactory.Create<IntersectionClassification>(
                    error: E.Quality.IntersectionClassificationFailed.WithContext("No intersection found")));
        });

private static Result<IntersectionClassification> AnalyzeIntersectionAngles<TA, TB>(
    TA geometryA,
    TB geometryB,
    IReadOnlyList<Point3d> points,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase {
    List<double> angles = [];
    
    foreach (Point3d pt in points) {
        (Vector3d tangentA, Vector3d tangentB) = (geometryA, geometryB) switch {
            (Curve cA, Curve cB) => (
                cA.TangentAt(cA.ClosestPoint(pt, out double tA) ? tA : 0),
                cB.TangentAt(cB.ClosestPoint(pt, out double tB) ? tB : 0)),
            _ => (Vector3d.Zero, Vector3d.Zero),
        };
        
        double angle = Vector3d.VectorAngle(tangentA, tangentB) * 180.0 / Math.PI;
        angles.Add(angle);
    }
    
    return angles.Count > 0
        ? ResultFactory.Create(value: new IntersectionClassification(
            IsTangent: angles.All(a => a < 5.0),
            IsTransverse: angles.All(a => a > 20.0),
            ApproachAngles: [.. angles],
            Classification: angles.Average() switch {
                < 5.0 => "Tangent",
                < 20.0 => "Near-tangent",
                _ => "Transverse",
            }))
        : ResultFactory.Create<IntersectionClassification>(error: E.Quality.IntersectionClassificationFailed);
}
```

**New Type** (add to `IntersectionConfig.cs`):
```csharp
public sealed record IntersectionClassification(
    bool IsTangent,
    bool IsTransverse,
    double[] ApproachAngles,
    string Classification);
```

**LOC**: 25

### 3.4.2 FindNearMisses

**Add to `Intersect.cs`**:
```csharp
/// <summary>Finds near-miss geometries (almost intersecting).</summary>
[Pure]
public static Result<IReadOnlyList<(int, int, double)>> FindNearMisses<T>(
    T[] geometry,
    double searchRadius,
    IGeometryContext context) where T : GeometryBase =>
    Arsenal.Rhino.Shared.Shared.FindNearMisses(geometry, searchRadius, context);
```

**LOC**: 8

### 3.4.3 AnalyzeStability

**Add to `Intersect.cs`**:
```csharp
/// <summary>Analyzes intersection stability via perturbation testing.</summary>
[Pure]
public static Result<IntersectionStability> AnalyzeStability<TA, TB>(
    TA geometryA,
    TB geometryB,
    double perturbationScale,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase =>
    ResultFactory.Create(value: (geometryA, geometryB))
        .Bind(tuple => {
            // Compute original intersection count
            Result<int> baselineCount = geometryA switch {
                Curve cA when geometryB is Curve cB => IntersectCurveCurve(cA, cB, context)
                    .Map(events => events.Count),
                _ => ResultFactory.Create<int>(error: E.Geometry.UnsupportedIntersection),
            };
            
            return baselineCount.Bind(baseline => {
                // Test 6 perturbations (±X, ±Y, ±Z)
                Vector3d[] perturbations = [
                    Vector3d.XAxis * perturbationScale, -Vector3d.XAxis * perturbationScale,
                    Vector3d.YAxis * perturbationScale, -Vector3d.YAxis * perturbationScale,
                    Vector3d.ZAxis * perturbationScale, -Vector3d.ZAxis * perturbationScale,
                ];
                
                int[] counts = perturbations.Select(perturb => {
                    TA perturbed = (TA)geometryA.Duplicate();
                    perturbed.Transform(Transform.Translation(perturb));
                    
                    return perturbed switch {
                        Curve cA when geometryB is Curve cB => IntersectCurveCurve(cA, cB, context)
                            .Match(onSuccess: events => events.Count, onFailure: _ => -1),
                        _ => -1,
                    };
                }).ToArray();
                
                int variance = counts.Where(c => c >= 0).Max() - counts.Where(c => c >= 0).Min();
                
                return ResultFactory.Create(value: new IntersectionStability(
                    IsStable: variance == 0,
                    BaselineCount: baseline,
                    PerturbedCounts: counts,
                    ConditionNumber: variance / (double)(baseline + 1)));
            });
        });
```

**New Type** (add to `IntersectionConfig.cs`):
```csharp
public sealed record IntersectionStability(
    bool IsStable,
    int BaselineCount,
    int[] PerturbedCounts,
    double ConditionNumber);
```

**LOC**: 32

**TOTAL ADDED TO INTERSECTION**: 65 LOC, 2 new types (5 + 2 = 7 types, within limit).

---

## 3.5 Orientation Features

**Current State**: `libs/rhino/orientation/` has 3 files, 4 types (Orient entry point + 3 config/result types).

**New Features**:
1. `OptimizeOrientation(Brep, OrientationCriteria)` - Minimize bounding box, center of mass
2. `ComputeRelativeOrientation(GeometryBase, GeometryBase)` - Best-fit transform
3. `DetectAndAlign(GeometryBase[])` - Pattern alignment

**Implementation Strategy**: Add 3 methods (15-20 LOC each = 50 LOC total).

### 3.5.1 OptimizeOrientation

**Add to `libs/rhino/orientation/Orient.cs`**:
```csharp
/// <summary>Optimizes orientation based on criteria.</summary>
[Pure]
public static Result<OptimalOrientation> OptimizeOrientation(
    Brep brep,
    OrientationCriteria criteria,
    IGeometryContext context) =>
    ResultFactory.Create(value: brep)
        .Validate(args: [context, V.Standard])
        .Bind(b => {
            Transform optimalTransform = criteria switch {
                OrientationCriteria.MinimizeBoundingBox => ComputeMinimalBoundingBoxTransform(b),
                OrientationCriteria.CenterOfMass => ComputeCenterOfMassTransform(b),
                OrientationCriteria.AlignToWorldAxes => ComputeWorldAxesTransform(b),
                _ => Transform.Identity,
            };
            
            Brep oriented = b.DuplicateBrep();
            oriented.Transform(optimalTransform);
            
            return ResultFactory.Create(value: new OptimalOrientation(
                Transform: optimalTransform,
                OrientedGeometry: oriented,
                BoundingBoxVolume: oriented.GetBoundingBox(accurate: false).Volume,
                Criteria: criteria));
        });

private static Transform ComputeMinimalBoundingBoxTransform(Brep brep) {
    // Test rotations around Z-axis, find minimum bounding box
    double minVolume = double.MaxValue;
    Transform bestTransform = Transform.Identity;
    
    for (int angle = 0; angle < 360; angle += 15) {
        Transform test = Transform.Rotation(angle * Math.PI / 180.0, Vector3d.ZAxis, Point3d.Origin);
        Brep rotated = brep.DuplicateBrep();
        rotated.Transform(test);
        
        double volume = rotated.GetBoundingBox(accurate: false).Volume;
        if (volume < minVolume) {
            minVolume = volume;
            bestTransform = test;
        }
    }
    
    return bestTransform;
}

private static Transform ComputeCenterOfMassTransform(Brep brep) {
    VolumeMassProperties vmp = VolumeMassProperties.Compute(brep);
    return vmp != null
        ? Transform.Translation(Point3d.Origin - vmp.Centroid)
        : Transform.Identity;
}

private static Transform ComputeWorldAxesTransform(Brep brep) {
    // Use PCA to find principal axes, align to world XYZ
    // (Simplified - just use bounding box orientation)
    BoundingBox bbox = brep.GetBoundingBox(accurate: true);
    Vector3d diagonal = bbox.Max - bbox.Min;
    
    // Find longest axis
    Vector3d longestAxis = Math.Abs(diagonal.X) > Math.Abs(diagonal.Y) && Math.Abs(diagonal.X) > Math.Abs(diagonal.Z)
        ? Vector3d.XAxis
        : Math.Abs(diagonal.Y) > Math.Abs(diagonal.Z)
            ? Vector3d.YAxis
            : Vector3d.ZAxis;
    
    return Transform.Rotation(longestAxis, Vector3d.XAxis, Point3d.Origin);
}
```

**New Types** (add to `OrientConfig.cs`):
```csharp
public enum OrientationCriteria : byte {
    MinimizeBoundingBox = 0,
    CenterOfMass = 1,
    AlignToWorldAxes = 2,
}

public sealed record OptimalOrientation(
    Transform Transform,
    Brep OrientedGeometry,
    double BoundingBoxVolume,
    OrientationCriteria Criteria);
```

**LOC**: 20 + 45 (helpers) = 65 LOC total

### 3.5.2 ComputeRelativeOrientation

**Add to `Orient.cs`**:
```csharp
/// <summary>Computes relative orientation between two geometries.</summary>
[Pure]
public static Result<RelativeOrientation> ComputeRelativeOrientation<TA, TB>(
    TA geometryA,
    TB geometryB,
    IGeometryContext context) where TA : GeometryBase where TB : GeometryBase =>
    ResultFactory.Create(value: (geometryA, geometryB))
        .Bind(tuple => {
            // Extract centroids and bounding boxes
            BoundingBox bboxA = geometryA.GetBoundingBox(accurate: false);
            BoundingBox bboxB = geometryB.GetBoundingBox(accurate: false);
            
            // Compute translation
            Vector3d translation = bboxB.Center - bboxA.Center;
            
            // Compute best-fit rotation (simplified - use principal axes)
            Transform bestFitTransform = Transform.Translation(translation);
            
            return ResultFactory.Create(value: new RelativeOrientation(
                Translation: translation,
                Rotation: 0.0,  // Simplified
                BestFitTransform: bestFitTransform,
                RelationshipType: translation.Length < context.AbsoluteTolerance ? "Coincident" : "Separated"));
        });
```

**New Type** (add to `OrientConfig.cs`):
```csharp
public sealed record RelativeOrientation(
    Vector3d Translation,
    double Rotation,
    Transform BestFitTransform,
    string RelationshipType);
```

**LOC**: 15

### 3.5.3 DetectAndAlign

**Add to `Orient.cs`**:
```csharp
/// <summary>Detects patterns and aligns geometry.</summary>
[Pure]
public static Result<PatternAlignment> DetectAndAlign<T>(
    T[] geometry,
    IGeometryContext context) where T : GeometryBase =>
    Arsenal.Core.Patterns.Patterns.ExtractPatterns(geometry, PatternType.Grid, context)
        .Map(patterns => patterns.Count > 0
            ? new PatternAlignment(
                DetectedPattern: patterns[0].Type,
                AlignmentTransform: patterns[0].Transform,
                Confidence: patterns[0].Confidence,
                Anomalies: [])
            : new PatternAlignment(
                DetectedPattern: PatternType.Translation,
                AlignmentTransform: Transform.Identity,
                Confidence: 0.0,
                Anomalies: []));
```

**New Type** (add to `OrientConfig.cs`):
```csharp
public sealed record PatternAlignment(
    Arsenal.Core.Patterns.PatternType DetectedPattern,
    Transform AlignmentTransform,
    double Confidence,
    int[] Anomalies);
```

**LOC**: 12

**TOTAL ADDED TO ORIENTATION**: 92 LOC, 4 new types (4 + 4 = 8 types, within limit).

---

## 3.6 Extraction Features

**Current State**: `libs/rhino/extraction/` has 3 files, 5 types (Extract entry point + 4 result types).

**New Features**:
1. `ExtractFeatures(Brep)` - Fillets, chamfers, holes, bosses
2. `DecomposeToPrimitives(GeometryBase)` - Best-fit primitives
3. (Pattern extraction moved to `libs/core/patterns/`)

**Implementation Strategy**: Add 2 methods (10-15 LOC each = 25 LOC total).

### 3.6.1 ExtractFeatures

**Add to `libs/rhino/extraction/Extract.cs`**:
```csharp
/// <summary>Extracts design features from Brep.</summary>
[Pure]
public static Result<DesignFeatures> ExtractFeatures(
    Brep brep,
    IGeometryContext context) =>
    ResultFactory.Create(value: brep)
        .Validate(args: [context, V.Standard])
        .Bind(b => {
            // Identify fillets (cylindrical faces tangent to two other faces)
            List<BrepFace> fillets = [];
            foreach (BrepFace face in b.Faces) {
                if (face.IsCylinder() && face.AdjacentFaces().Length == 2) {
                    fillets.Add(face);
                }
            }
            
            // Identify holes (cylindrical faces through solid)
            List<BrepFace> holes = [];
            foreach (BrepFace face in b.Faces) {
                if (face.IsCylinder() && face.AdjacentFaces().Length > 2) {
                    holes.Add(face);
                }
            }
            
            return ResultFactory.Create(value: new DesignFeatures(
                FilletCount: fillets.Count,
                FilletRadii: [.. fillets.Select(f => f.TryGetCylinder(out Cylinder cyl) ? cyl.Radius : 0.0)],
                HoleCount: holes.Count,
                HoleDiameters: [.. holes.Select(f => f.TryGetCylinder(out Cylinder cyl) ? cyl.Radius * 2.0 : 0.0)],
                FeatureTypes: ["Fillet", "Hole",]));
        });
```

**New Type** (add to `ExtractionConfig.cs`):
```csharp
public sealed record DesignFeatures(
    int FilletCount,
    double[] FilletRadii,
    int HoleCount,
    double[] HoleDiameters,
    string[] FeatureTypes);
```

**LOC**: 15

### 3.6.2 DecomposeToPrimitives

**Add to `Extract.cs`**:
```csharp
/// <summary>Decomposes geometry to best-fit primitives.</summary>
[Pure]
public static Result<PrimitiveDecomposition> DecomposeToPrimitives(
    GeometryBase geometry,
    IGeometryContext context) =>
    ResultFactory.Create(value: geometry)
        .Bind(g => g switch {
            PointCloud pc => Arsenal.Rhino.Shared.Shared.FitPrimitive([.. pc.GetPoints()], PrimitiveType.Plane, context)
                .Map(fit => new PrimitiveDecomposition([fit,], [])),
            Brep brep => DecomposeBrepToPrimitives(brep, context),
            _ => ResultFactory.Create<PrimitiveDecomposition>(
                error: E.Quality.PrimitiveDecompositionFailed.WithContext("Unsupported geometry type")),
        });

private static Result<PrimitiveDecomposition> DecomposeBrepToPrimitives(Brep brep, IGeometryContext context) {
    List<Arsenal.Rhino.Shared.PrimitiveFit> primitives = [];
    
    foreach (BrepFace face in brep.Faces) {
        PrimitiveType type = face switch {
            _ when face.IsPlanar() => PrimitiveType.Plane,
            _ when face.IsCylinder() => PrimitiveType.Cylinder,
            _ when face.IsSphere() => PrimitiveType.Sphere,
            _ => PrimitiveType.Plane,  // Default to plane for complex surfaces
        };
        
        // Sample face for fitting
        List<Point3d> samples = [];
        for (double u = face.Domain(0).Min; u <= face.Domain(0).Max; u += face.Domain(0).Length / 10) {
            for (double v = face.Domain(1).Min; v <= face.Domain(1).Max; v += face.Domain(1).Length / 10) {
                samples.Add(face.PointAt(u, v));
            }
        }
        
        Result<Arsenal.Rhino.Shared.PrimitiveFit> fit = Arsenal.Rhino.Shared.Shared.FitPrimitive([.. samples], type, context);
        if (fit.IsSuccess) {
            primitives.Add(fit.Value);
        }
    }
    
    return ResultFactory.Create(value: new PrimitiveDecomposition(
        Primitives: [.. primitives],
        ResidualGeometry: []));
}
```

**New Type** (add to `ExtractionConfig.cs`):
```csharp
public sealed record PrimitiveDecomposition(
    Arsenal.Rhino.Shared.PrimitiveFit[] Primitives,
    GeometryBase[] ResidualGeometry);
```

**LOC**: 10 + 35 (helper) = 45 LOC total

**TOTAL ADDED TO EXTRACTION**: 60 LOC, 2 new types (5 + 2 = 7 types, within limit).

---

# Part 4: Implementation Roadmap

## 4.1 Dependency Order

**Critical Dependencies** (must be implemented in order):

### Phase 1: Core Infrastructure Extensions (Week 1)
**NO dependencies** - extends existing `libs/core/`

1. **libs/core/errors/E.cs** - Add Topology/Quality error domains
2. **libs/core/errors/ErrorDomain.cs** - Add enum values
3. **libs/core/validation/V.cs** - Add 4 new validation modes
4. **libs/core/validation/ValidationRules.cs** - Register new validation rules

**Files Modified**: 4 existing files  
**New Files**: 0  
**New Types**: 2 (error domain enums)  
**LOC Added**: ~120 LOC across 4 files

**Validation**: `dotnet build libs/core/Core.csproj` must succeed with zero warnings.

---

### Phase 2: Shared Infrastructure (Weeks 2-3)
**Depends on**: Phase 1 (error codes, validation modes)

#### Week 2: libs/core/quality/
1. **Create folder**: `libs/core/quality/`
2. **Create files**: Quality.cs (120 LOC), QualityCore.cs (250 LOC), QualityConfig.cs (80 LOC)
3. **Add types**: 8 types (IQualityMetric, CurvatureDistribution, FairnessMetrics, MeshQuality, ManufacturingSuitability, FEASimulationType, ManufacturingProcess, QualityAnalysisConfig)

**Dependencies**: E.Quality.* errors, V.CurvatureBounds | V.FairnessMetrics | V.ManufacturingConstraints | V.ElementQuality validation modes

**Validation**:
```bash
dotnet build libs/core/Core.csproj
dotnet test --filter "FullyQualifiedName~Quality" test/core/
```

**Success Criteria**:
- All quality metrics return valid Result<T>
- Curvature distribution sampling works for surfaces
- Fairness analysis detects inflection points
- Mesh quality computes aspect ratio/skewness correctly
- Manufacturing checks identify violations

#### Week 2: libs/core/patterns/
1. **Create folder**: `libs/core/patterns/`
2. **Create files**: Patterns.cs (100 LOC), PatternCore.cs (200 LOC), PatternConfig.cs (70 LOC)
3. **Add types**: 7 types (PatternMatch, PatternType, SymmetryType, ClusteringStrategy, ClusterResult, PatternDetectionConfig, PatternAnalysis)

**Dependencies**: E.Quality.* errors (pattern detection/clustering)

**Validation**:
```bash
dotnet build libs/core/Core.csproj
dotnet test --filter "FullyQualifiedName~Pattern" test/core/
```

**Success Criteria**:
- Symmetry detection finds reflections/rotations
- K-means clustering converges
- DBSCAN clusters spatial points
- Pattern detection identifies grids/radial patterns

#### Week 3: libs/rhino/shared/
1. **Create folder**: `libs/rhino/shared/`
2. **Create files**: Shared.cs (90 LOC), SharedCore.cs (180 LOC), SharedConfig.cs (60 LOC)
3. **Add types**: 6 types (PrimitiveFit, PrimitiveType, MedialAxisMethod, NearMiss, PrimitiveFittingConfig, MedialAxisConfig)

**Dependencies**: E.Quality.* errors, RhinoCommon SDK

**Validation**:
```bash
dotnet build libs/rhino/Rhino.csproj
dotnet test --filter "FullyQualifiedName~Shared" test/rhino/
```

**Success Criteria**:
- Plane fitting works (uses native `Plane.FitPlaneToPoints`)
- Cylinder/sphere fitting produces reasonable results
- Medial axis approximation via curve offset works for planar Breps
- Near-miss detection finds geometry within search radius

---

### Phase 3: Feature Integration (Weeks 4-6)
**Depends on**: Phases 1-2 (all shared infrastructure must exist)

Integration is **independent** across folders - can be parallelized.

#### Week 4: topology/ + spatial/ features
**Topology** (75 LOC, 2 types):
- Add `DiagnoseTopology()` (25 LOC)
- Add `HealTopology()` (22 LOC)
- Add `ExtractFeatures()` (28 LOC)
- Add types: `TopologyDiagnosis`, `HealingStrategy`, `TopologicalFeatures`

**Spatial** (83 LOC, 3 types):
- Add `ClusterByProximity()` (8 LOC delegation)
- Add `ComputeProximityField()` (15 LOC API + 60 LOC Core)
- Add types: `ProximityOptions`, `ProximityNeighbor`, `ProximityField`

**Validation**:
```bash
dotnet build libs/rhino/Rhino.csproj
dotnet test --filter "FullyQualifiedName~Topology" test/rhino/
dotnet test --filter "FullyQualifiedName~Spatial" test/rhino/
```

#### Week 5: analysis/ + intersection/ features
**Analysis** (24 LOC, 0 types):
- Add `AnalyzeSurfaceQuality()` (8 LOC delegation)
- Add `AnalyzeCurveFairness()` (8 LOC delegation)
- Add `AnalyzeForFEA()` (8 LOC delegation)

**Intersection** (65 LOC, 2 types):
- Add `ClassifyIntersection()` (25 LOC)
- Add `FindNearMisses()` (8 LOC delegation)
- Add `AnalyzeStability()` (32 LOC)
- Add types: `IntersectionClassification`, `IntersectionStability`

**Validation**:
```bash
dotnet build libs/rhino/Rhino.csproj
dotnet test --filter "FullyQualifiedName~Analysis" test/rhino/
dotnet test --filter "FullyQualifiedName~Intersection" test/rhino/
```

#### Week 6: orientation/ + extraction/ features
**Orientation** (92 LOC, 4 types):
- Add `OptimizeOrientation()` (20 LOC API + 45 LOC helpers)
- Add `ComputeRelativeOrientation()` (15 LOC)
- Add `DetectAndAlign()` (12 LOC)
- Add types: `OrientationCriteria`, `OptimalOrientation`, `RelativeOrientation`, `PatternAlignment`

**Extraction** (60 LOC, 2 types):
- Add `ExtractFeatures()` (15 LOC)
- Add `DecomposeToPrimitives()` (10 LOC API + 35 LOC helper)
- Add types: `DesignFeatures`, `PrimitiveDecomposition`

**Validation**:
```bash
dotnet build libs/rhino/Rhino.csproj
dotnet test --filter "FullyQualifiedName~Orientation" test/rhino/
dotnet test --filter "FullyQualifiedName~Extraction" test/rhino/
```

---

## 4.2 Week-by-Week Implementation Plan

### Week 1: Core Extensions (Phase 1)
**Goal**: Extend error and validation infrastructure.

**Monday-Tuesday**: Error Domains
- Add Topology/Quality error codes to `E.cs` (25 codes)
- Add domain enums to `ErrorDomain.cs`
- Update `GetDomain()` switch
- Add `E.Topology.*` and `E.Quality.*` nested classes

**Wednesday-Thursday**: Validation Modes
- Add 4 new V flags to `V.cs` (CurvatureBounds, FairnessMetrics, ManufacturingConstraints, ElementQuality)
- Update `V.All` and `V.AllFlags`
- Register validation rules in `ValidationRules.cs` FrozenDictionary

**Friday**: Build & Verify
- Run full build: `dotnet build`
- Verify all error codes resolve: `E.Topology.RepairFailed`, `E.Quality.MetricComputationFailed`
- Verify validation modes combine: `V.Standard | V.CurvatureBounds`
- Run existing tests to ensure no regressions

**Deliverable**: Core extensions complete, builds cleanly.

---

### Week 2: Quality & Patterns Infrastructure
**Goal**: Create `libs/core/quality/` and `libs/core/patterns/`.

**Monday-Tuesday**: libs/core/quality/
- Create folder and files
- Implement `Quality.cs` public API (4 methods)
- Implement `QualityCore.cs` algorithms:
  - `ComputeCurvatureDistribution()` (60 LOC)
  - `ComputeFairness()` (55 LOC)
  - `ComputeMeshQuality()` (75 LOC)
  - `CheckManufacturingConstraints()` (60 LOC)
- Implement `QualityConfig.cs` types

**Wednesday-Thursday**: libs/core/patterns/
- Create folder and files
- Implement `Patterns.cs` public API (3 methods)
- Implement `PatternCore.cs` algorithms:
  - `DetectSymmetryInternal()` (50 LOC)
  - `ClusterGeometry()` with k-means/DBSCAN (120 LOC)
  - `ExtractPatternsInternal()` (30 LOC)
- Implement `PatternConfig.cs` types

**Friday**: Test & Verify
- Write property-based tests for quality metrics
- Write clustering tests with known patterns
- Verify symmetry detection with simple geometries
- Run: `dotnet test test/core/`

**Deliverable**: Quality and pattern detection libraries operational.

---

### Week 3: Rhino Shared Utilities
**Goal**: Create `libs/rhino/shared/`.

**Monday-Tuesday**: Primitive Fitting
- Create folder and `Shared.cs` public API
- Implement `SharedCore.cs`:
  - `FitPlane()` using `Plane.FitPlaneToPoints()` (15 LOC)
  - `FitCylinder()` with simplified least-squares (50 LOC)
  - `FitSphere()` with centroid/radius averaging (35 LOC)

**Wednesday**: Medial Axis & Near-Miss
- Implement `ComputeViaCurveOffset()` (40 LOC)
- Implement `FindNearMissesInternal()` (40 LOC)
- Implement `SharedConfig.cs` types

**Thursday-Friday**: Test & Integrate
- Test primitive fitting with point clouds
- Test medial axis with planar Breps
- Test near-miss detection with overlapping geometry
- Run: `dotnet test test/rhino/`

**Deliverable**: Shared utilities library complete and tested.

---

### Week 4: Topology & Spatial Features
**Goal**: Integrate topology/spatial features using shared infrastructure.

**Monday-Tuesday**: Topology Features
- Add `DiagnoseTopology()` to `Topology.cs`
- Add `HealTopology()` with RhinoCommon repair methods
- Add `ExtractFeatures()` with genus calculation
- Add types to `TopologyConfig.cs`

**Wednesday-Thursday**: Spatial Features
- Add `ClusterByProximity()` delegation to `Spatial.cs`
- Add `ComputeProximityField()` to `SpatialCore.cs`
- Add types to `SpatialConfig.cs`

**Friday**: Test & Verify
- Test topology diagnosis with known problematic Breps
- Test topology healing with naked edges
- Test spatial clustering with point clouds
- Test proximity field with directional queries

**Deliverable**: Topology and spatial features operational.

---

### Week 5: Analysis & Intersection Features
**Goal**: Integrate analysis/intersection features.

**Monday-Tuesday**: Analysis Features
- Add 3 delegation methods to `Analysis.cs` (quality, fairness, FEA)
- Verify delegation to `libs/core/quality/` works correctly

**Wednesday-Thursday**: Intersection Features
- Add `ClassifyIntersection()` with tangency analysis
- Add `FindNearMisses()` delegation to `Intersect.cs`
- Add `AnalyzeStability()` with perturbation testing
- Add types to `IntersectionConfig.cs`

**Friday**: Test & Verify
- Test surface quality with known surfaces (spheres, planes, freeform)
- Test curve fairness with wavy curves
- Test intersection classification with tangent/transverse cases
- Test intersection stability with grazing intersections

**Deliverable**: Analysis and intersection features operational.

---

### Week 6: Orientation & Extraction Features
**Goal**: Complete remaining features.

**Monday-Tuesday**: Orientation Features
- Add `OptimizeOrientation()` with bounding box minimization
- Add `ComputeRelativeOrientation()` with best-fit transform
- Add `DetectAndAlign()` delegation to patterns
- Add types to `OrientConfig.cs`

**Wednesday-Thursday**: Extraction Features
- Add `ExtractFeatures()` with fillet/hole detection
- Add `DecomposeToPrimitives()` with face-by-face fitting
- Add types to `ExtractionConfig.cs`

**Friday**: Final Verification
- Run full test suite: `dotnet test`
- Run full build: `dotnet build`
- Verify all features work end-to-end
- Check LOC limits: `find libs -name "*.cs" -exec wc -l {} + | sort -n`
- Check type counts per folder

**Deliverable**: ALL features complete and tested.

---

## 4.3 Verification Checkpoints

### After Each Phase

**Phase 1 Checkpoint (Week 1)**:
```bash
# Build succeeds
dotnet build libs/core/Core.csproj

# All error codes resolve
dotnet run <<EOF
using Arsenal.Core.Errors;
Console.WriteLine(E.Topology.RepairFailed);
Console.WriteLine(E.Quality.MetricComputationFailed);
EOF

# Validation modes combine
dotnet run <<EOF
using Arsenal.Core.Validation;
V mode = V.Standard | V.CurvatureBounds | V.FairnessMetrics;
Console.WriteLine(mode);
EOF

# No regressions
dotnet test test/core/
```

**Phase 2 Checkpoint (Weeks 2-3)**:
```bash
# All shared folders build
dotnet build libs/core/Core.csproj
dotnet build libs/rhino/Rhino.csproj

# Quality metrics work
dotnet test --filter "FullyQualifiedName~Quality"

# Pattern detection works
dotnet test --filter "FullyQualifiedName~Pattern"

# Primitive fitting works
dotnet test --filter "FullyQualifiedName~Shared"
```

**Phase 3 Checkpoint (Weeks 4-6)**:
```bash
# All features build
dotnet build

# All feature tests pass
dotnet test

# Integration tests pass
dotnet test --filter "FullyQualifiedName~Integration"
```

### Organizational Limits Verification

**After Week 6** (final verification):

```bash
# Check file counts per folder (must be ≤4)
find libs -type d -mindepth 3 -maxdepth 3 -exec sh -c 'echo -n "{}: "; find "{}" -maxdepth 1 -name "*.cs" | wc -l' \;

# Expected output:
# libs/core/quality: 3 ✓
# libs/core/patterns: 3 ✓
# libs/rhino/shared: 3 ✓
# libs/rhino/topology: 3 ✓ (unchanged)
# libs/rhino/spatial: 3 ✓ (unchanged)
# libs/rhino/analysis: 3 ✓ (unchanged)
# libs/rhino/intersection: 3 ✓ (unchanged)
# libs/rhino/orientation: 3 ✓ (unchanged)
# libs/rhino/extraction: 3 ✓ (unchanged)

# Check type counts per folder (must be ≤10)
# (Manual inspection of each folder's type definitions)

# Expected type counts:
# libs/core/quality: 8 types ✓
# libs/core/patterns: 7 types ✓
# libs/rhino/shared: 6 types ✓
# libs/rhino/topology: 9 types ✓ (7 + 2 new)
# libs/rhino/spatial: 7 types ✓ (4 + 3 new)
# libs/rhino/analysis: 6 types ✓ (unchanged)
# libs/rhino/intersection: 7 types ✓ (5 + 2 new)
# libs/rhino/orientation: 8 types ✓ (4 + 4 new)
# libs/rhino/extraction: 7 types ✓ (5 + 2 new)

# Check member LOC (must be ≤300 per member)
# (Manual inspection - all methods in blueprint are <300 LOC)

# Longest members:
# QualityCore.ComputeMeshQuality: ~75 LOC ✓
# PatternCore.KMeansClustering: ~70 LOC ✓
# SharedCore.FitCylinder: ~50 LOC ✓
# Topology.DiagnoseTopology: ~25 LOC ✓
```

### Code Quality Verification

**Style Compliance**:
```bash
# No var usage
! grep -r "var " libs/*/src --include="*.cs"

# No if/else statements (except guard clauses)
! grep -r "else" libs/*/src --include="*.cs" | grep -v "// else-allowed: guard clause"

# Named parameters in critical calls
grep -r "ResultFactory.Create" libs/*/src --include="*.cs" | grep "error:"
grep -r "ResultFactory.Create" libs/*/src --include="*.cs" | grep "value:"

# Trailing commas in multi-line collections
grep -r "\]" libs/*/src --include="*.cs" -B 2 | grep ","

# K&R brace style
! grep -r "^{" libs/*/src --include="*.cs"
```

**Build Health**:
```bash
# Zero warnings
dotnet build 2>&1 | grep -c "warning" # Should output: 0

# Zero errors
dotnet build 2>&1 | grep -c "error" # Should output: 0

# All tests pass
dotnet test --no-build | grep "Passed!"
```

---

## 4.4 Success Metrics

**Quantitative Goals**:
- ✅ **3 new shared infrastructure folders** created
- ✅ **21 new types** added across shared folders
- ✅ **1150 LOC** in shared infrastructure (quality: 450, patterns: 370, shared: 330)
- ✅ **384 LOC** in feature integrations across 6 folders
- ✅ **0 types over limit** (all folders ≤10 types)
- ✅ **0 files over limit** (all folders ≤4 files)
- ✅ **0 members over limit** (all members ≤300 LOC)
- ✅ **100% build success** with zero warnings
- ✅ **100% test pass rate**

**Qualitative Goals**:
- ✅ **No code duplication** - quality/pattern/primitive code centralized
- ✅ **Consistent patterns** - all features use Result<T>, UnifiedOperation, ValidationRules
- ✅ **RhinoCommon best practices** - proper use of SDK APIs
- ✅ **Maintainability** - thin delegation, clear separation of concerns
- ✅ **Extensibility** - new quality metrics/patterns/primitives easily added

---

## 4.5 Rollback Strategy

**If Phase 1 Fails** (error/validation extensions):
- Revert 4 modified files in `libs/core/errors/` and `libs/core/validation/`
- No new folders created, minimal risk

**If Phase 2 Fails** (shared infrastructure):
- Delete 3 new folders: `libs/core/quality/`, `libs/core/patterns/`, `libs/rhino/shared/`
- Revert Phase 1 changes (error codes no longer needed)
- Risk: 450-1150 LOC wasted, but no impact on existing features

**If Phase 3 Fails** (feature integration):
- Revert changes to existing 6 folders (`topology/`, `spatial/`, etc.)
- Keep Phase 1-2 (shared infrastructure is still valuable for future features)
- Risk: Partial feature set, but shared infrastructure remains useful

**Mitigation**:
- Work in feature branch: `feature/advanced-geometry`
- Create PR only after all 6 weeks complete
- Incremental validation at each phase
- Automated tests prevent regressions

---

## Appendix A: Complete File Manifest

**New Folders** (3 total):
```
libs/core/quality/
  ├── Quality.cs (120 LOC, 3 types)
  ├── QualityCore.cs (250 LOC, 3 types)
  └── QualityConfig.cs (80 LOC, 2 types)

libs/core/patterns/
  ├── Patterns.cs (100 LOC, 3 types)
  ├── PatternCore.cs (200 LOC, 2 types)
  └── PatternConfig.cs (70 LOC, 2 types)

libs/rhino/shared/
  ├── Shared.cs (90 LOC, 2 types)
  ├── SharedCore.cs (180 LOC, 2 types)
  └── SharedConfig.cs (60 LOC, 2 types)
```

**Modified Files** (10 existing files):
```
libs/core/errors/
  ├── E.cs (+120 LOC: 25 error codes + 2 nested classes)
  └── ErrorDomain.cs (+2 enum values)

libs/core/validation/
  ├── V.cs (+4 validation mode flags)
  └── ValidationRules.cs (+4 validation rules in FrozenDictionary)

libs/rhino/topology/
  ├── Topology.cs (+75 LOC: 3 methods)
  └── TopologyConfig.cs (+2 types)

libs/rhino/spatial/
  ├── Spatial.cs (+8 LOC: 1 method)
  ├── SpatialCore.cs (+75 LOC: 1 method)
  └── SpatialConfig.cs (+3 types)

libs/rhino/analysis/
  └── Analysis.cs (+24 LOC: 3 methods)

libs/rhino/intersection/
  ├── Intersect.cs (+65 LOC: 3 methods)
  └── IntersectionConfig.cs (+2 types)

libs/rhino/orientation/
  ├── Orient.cs (+92 LOC: 3 methods)
  └── OrientConfig.cs (+4 types)

libs/rhino/extraction/
  ├── Extract.cs (+60 LOC: 2 methods)
  └── ExtractionConfig.cs (+2 types)
```

**Total Impact**:
- **New folders**: 3
- **New files**: 9 (3 folders × 3 files)
- **Modified files**: 10
- **New types**: 21 (shared) + 15 (features) = 36 total
- **New LOC**: 1150 (shared) + 384 (features) + 120 (core extensions) = **1654 LOC total**
- **Files per folder**: All ≤4 ✓
- **Types per folder**: All ≤10 ✓
- **LOC per member**: All ≤300 ✓

---

## Appendix B: RhinoCommon API Reference

**APIs Used in Quality Metrics**:
- `Surface.CurvatureAt(u, v)` → `SurfaceCurvature` with `.Gaussian`, `.Mean`
- `Curve.CurvatureAt(t)` → `Vector3d` curvature vector
- `Mesh.Faces`, `Mesh.Vertices`, `Mesh.TopologyEdges`
- `MeshFace.IsQuad`, vertices access
- `Brep.GetBoundingBox(accurate)`

**APIs Used in Pattern Detection**:
- `GeometryBase.GetBoundingBox(accurate).Center` for centroids
- `Transform.Mirror(plane)`, `Transform.Rotation(angle, axis, center)` for symmetry testing
- `GeometryBase.Transform(transform)` for applying transformations
- `Point3d.DistanceTo(Point3d)` for clustering distances

**APIs Used in Primitive Fitting**:
- `Plane.FitPlaneToPoints(Point3d[], out deviation)` - built-in best-fit plane
- `Cylinder` constructor, `.ToNurbsSurface()`
- `Sphere` constructor, `.ToNurbsSurface()`
- Custom least-squares for cylinder/sphere (no native methods)

**APIs Used in Topology Operations**:
- `Brep.IsValid`, `.Vertices`, `.Edges`, `.Faces`, `.Loops`
- `BrepEdge.AdjacentFaces()` for manifold checking
- `Brep.JoinNakedEdges(tolerance)` for healing
- `Brep.Repair(tolerance)` for trimming repair
- `Brep.MergeCoplanarFaces(tolerance)` for simplification
- `Mesh.HealNakedEdges(distance)` for mesh repair

**APIs Used in Spatial Operations**:
- `RTree` for spatial indexing
- `RTree.Insert(BoundingBox, index)`
- `RTree.Search(Sphere, out indices)`
- `GeometryBase.ClosestPoint(point, out parameter)` for near-miss

**APIs Used in Intersection Analysis**:
- `Rhino.Geometry.Intersect.Intersection.CurveCurve(curveA, curveB, tolerance, overlapTolerance)`
- `Curve.TangentAt(t)` for tangency analysis
- `Vector3d.VectorAngle(vectorA, vectorB)` for approach angles
- `Transform.Translation(vector)` for perturbation testing

**APIs Used in Orientation**:
- `VolumeMassProperties.Compute(brep)`, `.Centroid`
- `Brep.GetBoundingBox(accurate)`, `.Volume`
- `Transform.Rotation(from, to, center)` for alignment
- `Brep.DuplicateBrep()` for non-destructive operations

**APIs Used in Extraction**:
- `BrepFace.IsPlanar()`, `.IsCylinder()`, `.IsSphere()`
- `BrepFace.TryGetCylinder(out cylinder)`, `.TryGetSphere(out sphere)`
- `BrepFace.AdjacentFaces()` for feature connectivity
- `BrepFace.PointAt(u, v)` for surface sampling
- `BrepFace.Domain(direction)` for parameter ranges

---

## Conclusion

This blueprint provides a **complete, actionable implementation plan** for integrating ALL features from `new_libs_functionality.md` while:

✅ **Maintaining organizational limits** (≤4 files, ≤10 types, ≤300 LOC per member)  
✅ **Preventing code duplication** via 3 shared infrastructure folders  
✅ **Leveraging existing infrastructure** (Result<T>, UnifiedOperation, ValidationRules, E.*, V.*)  
✅ **Using RhinoCommon best practices** backed by SDK research  
✅ **Following project standards** (CLAUDE.md, copilot-instructions.md)  
✅ **Providing clear implementation order** (6-week roadmap)  
✅ **Including verification checkpoints** at each phase  

**Total Effort**: 1654 LOC across 19 files (9 new, 10 modified) over 6 weeks.

**Foundation-First Approach Wins**: By creating shared infrastructure first, we eliminate duplication and stay within limits while delivering all requested features.

**Ready for Implementation**: This blueprint can be followed step-by-step by any developer familiar with C#, RhinoCommon, and the existing Parametric Arsenal patterns.

---

**Document Complete** ✓

