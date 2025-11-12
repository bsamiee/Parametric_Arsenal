# Compute Files Refactoring Plan: Multi-Phase Improvement Strategy

**Date**: 2025-01-11  
**Scope**: All `libs/rhino/*Compute.cs` files + associated Core/Config  
**Goal**: Reduce LOC, improve code quality, maintain 100% functionality

---

## Executive Summary

### Current State
| Folder | Files | Types | Total LOC | Compute LOC | Status |
|--------|-------|-------|-----------|-------------|--------|
| **orientation** | 4 | ~8 | 529 | 166 | ✅ At limit |
| **topology** | 4 | ~8 | 770 | 145 | ✅ At limit |
| **analysis** | 4 | ~9 | 563 | 148 | ✅ At limit |
| **intersection** | 4 | ~7 | 723 | 243 | ✅ At limit |
| **spatial** | 4 | ~8 | 693 | 438 | ⚠️ Compute needs reduction |
| **extraction** | 4 | ~11 | 963 | 384 | ⚠️ Both need reduction |

**Total Compute LOC**: 1,524 lines  
**Total Project LOC**: 4,241 lines

### Key Findings

#### Excellent Dense Code Patterns Found
1. **SpatialCompute.cs** (lines 73-138): K-means++ initialization with squared distances - 65 LOC of pure algorithmic density
2. **IntersectionCompute.Classify** (lines 14-82): Polymorphic dispatch with circular mean calculation - complex tuple patterns
3. **AnalysisCompute.MeshForFEA** (lines 68-148): ArrayPool usage with hot-path for loops - zero allocation mesh quality
4. **TopologyCompute.Diagnose** (lines 13-77): LINQ-heavy edge analysis with comprehension queries
5. **ExtractionCompute.ClassifySurface** (lines 193-216): Nested pattern matching with surface primitives

#### Major Improvement Opportunities

**1. Duplicate Validation Patterns** (Critical - 150+ LOC savings)
- Manual null checks: `geomA is null || geomB is null` appears 8+ times
- Manual IsValid checks: `!geometry.IsValid` appears 12+ times  
- **Solution**: Consolidate into UnifiedOperation with ValidationMode

**2. Repeated Type Dispatch** (High - 100+ LOC savings)
- OrientCompute: Manual type extraction with TryGetValue (lines 74-77)
- IntersectionCompute: Repeated type checking (lines 23-41, 90-108)
- **Solution**: FrozenDictionary dispatch tables in Core files

**3. Unnecessary Double Loops** (Medium - 50+ LOC savings)
- TopologyCompute naked edge analysis: nested LINQ (lines 23-45)
- **Solution**: Spatial indexing with RTree

**4. Inline Helper Logic** (Low - 30+ LOC savings)
- ExtractionCompute: Multiple `ComputeVariance`, `IsGridPoint` helpers (lines 354-385)
- OrientCompute: Symmetry detection inline (lines 95-130)
- **Solution**: Consolidate into parameterized dense operations

**5. Tuple Construction/Deconstruction Opportunities** (Medium - 40+ LOC savings)
- Many methods return tuples but don't leverage deconstruction
- ExtractionCompute: Excessive tuple element access `.Item1`, `.Item2`
- **Solution**: Named tuple deconstruction patterns

---

## Phase 1: Foundation Improvements (Target: -200 LOC, No Breaking Changes)

### Goals
- Eliminate all manual validation patterns
- Consolidate duplicate validation logic
- Introduce dispatch tables where missing
- Improve tuple usage patterns

### 1.1: Validation Consolidation (All Folders)

**Problem**: Manual validation scattered across all Compute files
```csharp
// Current pattern (repeated 8+ times)
geomA is null || geomB is null
    ? ResultFactory.Create<T>(error: E.Geometry.InsufficientIntersectionData)
    : !geomA.IsValid || !geomB.IsValid
        ? ResultFactory.Create<T>(error: E.Validation.GeometryInvalid)
        : ActualLogic(geomA, geomB)
```

**Solution**: Use UnifiedOperation validation infrastructure
```csharp
// After (single validation point)
UnifiedOperation.Apply(
    input: (geomA, geomB),
    operation: (Func<(GeometryBase, GeometryBase), Result<IReadOnlyList<T>>>)(pair =>
        ActualLogic(pair.Item1, pair.Item2)),
    config: new OperationConfig<(GeometryBase, GeometryBase), T> {
        Context = context,
        ValidationMode = V.Standard,  // Handles null + IsValid
    });
```

**Impact**:
- IntersectionCompute: -20 LOC (3 methods × ~7 LOC validation each)
- OrientCompute: -15 LOC (2 methods × ~7 LOC validation each)
- All other Computes: -10 LOC each (conservative)
- **Total**: ~65 LOC reduction

**Files to Modify**:
- `IntersectionCompute.cs` (Classify, FindNearMisses, AnalyzeStability)
- `OrientCompute.cs` (ComputeRelative)
- `TopologyCompute.cs` (Diagnose, Heal, ExtractFeatures)
- `ExtractionCompute.cs` (ExtractFeatures, DecomposeToPrimitives, ExtractPatterns)

### 1.2: Tuple Deconstruction Patterns (All Folders)

**Problem**: Excessive tuple element access bloats code
```csharp
// Current (ExtractionCompute.cs lines 162-180)
.Select(item => item.Error.HasValue
    ? ResultFactory.Create<T>(error: item.Error.Value)
    : result.IsSuccess && item.Success
        ? result.Map<T>(r => (
            Primitives: [.. r.Primitives, (item.Type, item.Frame, item.Params),],
            Residuals: [.. r.Residuals, item.Residual,]))
        : result)
```

**Solution**: Named tuple deconstruction
```csharp
// After
.Select(item => {
    (bool success, byte type, Plane frame, double[] pars, double residual, SystemError? error) = item;
    return error.HasValue
        ? ResultFactory.Create<T>(error: error.Value)
        : result.IsSuccess && success
            ? result.Map<T>(r => (Primitives: [.. r.Primitives, (type, frame, pars),], Residuals: [.. r.Residuals, residual,]))
            : result;
})
```

**Impact**:
- ExtractionCompute: -15 LOC (better readability, fewer `.Item` accesses)
- TopologyCompute: -8 LOC (edge analysis tuples)
- **Total**: ~25 LOC reduction + improved clarity

**Files to Modify**:
- `ExtractionCompute.cs` (lines 150-180, 290-310)
- `TopologyCompute.cs` (lines 23-60)
- `SpatialCompute.cs` (lines 90-120)

### 1.3: Dispatch Table Introduction (Intersection, Orientation)

**Problem**: Type checking with manual dispatch in IntersectionCompute
```csharp
// Current (IntersectionCompute.cs lines 23-82)
return geomA is null || geomB is null ? error
    : IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
        .Bind(entry => (validA, validB) switch {
            (Curve curveA, Curve curveB) => Logic1(...),
            (Curve curve, Surface surface) => Logic2(...),
            (Surface surface, Curve curve) => Logic3(...),
            _ => error,
        });
```

**Solution**: Consolidate into IntersectionCore dispatch with classifier functions
```csharp
// IntersectionCore.cs - Add classifier dispatch
private static readonly FrozenDictionary<(Type A, Type B), Func<object, object, Point3d[], double[], Result<(byte, double[], bool, double)>>> _classifiers =
    new Dictionary<(Type, Type), Func<...>> {
        [(typeof(Curve), typeof(Curve))] = (a, b, pts, params) => ClassifyCurveCurve((Curve)a, (Curve)b, pts, params),
        [(typeof(Curve), typeof(Surface))] = (a, b, pts, params) => ClassifyCurveSurface((Curve)a, (Surface)b, pts, params),
        [(typeof(Surface), typeof(Curve))] = (a, b, pts, params) => ClassifyCurveSurface((Curve)b, (Surface)a, pts, params),
    }.ToFrozenDictionary();

// IntersectionCompute.cs - Use dispatch
internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(...) =>
    UnifiedOperation.Apply(
        input: (geomA, geomB, output),
        operation: (Func<(GeometryBase, GeometryBase, Intersect.IntersectionOutput), Result<IReadOnlyList<(byte, double[], bool, double)>>>)(inputs => {
            (GeometryBase a, GeometryBase b, Intersect.IntersectionOutput o) = inputs;
            return IntersectionCore.GetClassifier(a.GetType(), b.GetType())
                .Bind(classifier => classifier(a, b, o.Points.ToArray(), [.. o.ParametersA, .. o.ParametersB]))
                .Map(result => (IReadOnlyList<(byte, double[], bool, double)>)[result]);
        }),
        config: new OperationConfig<(GeometryBase, GeometryBase, Intersect.IntersectionOutput), (byte, double[], bool, double)> {
            Context = context,
            ValidationMode = V.Standard,
        });
```

**Impact**:
- IntersectionCompute: -30 LOC (dispatch logic moves to Core + UnifiedOperation validation)
- OrientCompute: -20 LOC (PlaneExtractors pattern already exists, consolidate usage)
- **Total**: ~50 LOC reduction + better separation of concerns

**Files to Modify**:
- `IntersectionCore.cs` (add classifier dispatch table)
- `IntersectionCompute.cs` (use dispatch + UnifiedOperation)
- `OrientCore.cs` (consolidate PlaneExtractors usage)
- `OrientCompute.cs` (use consolidated dispatch)

### 1.4: Double Parameter Improvements (All Folders)

**Problem**: Tolerance comparisons repeat context.AbsoluteTolerance checks
```csharp
// Current pattern (appears 50+ times)
value > context.AbsoluteTolerance
tolerance <= 0.0 ? error : logic
distance < context.AbsoluteTolerance
```

**Solution**: Inline tolerance constants where appropriate, leverage double parameter methods
```csharp
// After - leverage method parameters with defaults
internal static Result<T> Operation(
    GeometryBase geometry,
    double tolerance,  // Already validated by caller or config
    IGeometryContext context) =>
    // Use tolerance directly, no repeated comparisons
    ProcessWithTolerance(geometry, tolerance);

// Config validation happens once
internal static double NormalizeTolerance(double? userTolerance, IGeometryContext context) =>
    userTolerance ?? context.AbsoluteTolerance;
```

**Impact**:
- All Compute files: -20 LOC (eliminate repeated tolerance guards)
- **Total**: ~20 LOC reduction

**Files to Modify**:
- All `*Compute.cs` files (consolidate tolerance handling)
- All `*Config.cs` files (add tolerance normalization if missing)

**Phase 1 Total Reduction**: ~160 LOC (conservative estimate, target was 200)

---

## Phase 2: Algorithmic Improvements (Target: -150 LOC, Enhanced Performance)

### Goals
- Eliminate unnecessary loops
- Replace nested iteration with spatial indexing
- Consolidate repeated LINQ patterns
- Improve hot-path performance

### 2.1: Spatial Indexing for Topology Edge Analysis (TopologyCompute)

**Problem**: Nested LINQ for near-miss detection is O(n²)
```csharp
// Current (TopologyCompute.cs lines 23-48) - 26 LOC, O(n²)
(int EdgeA, int EdgeB, double Distance)[] nearMisses = nakedEdges.Length < TopologyConfig.MaxEdgesForNearMissAnalysis
    ? [.. (from i in Enumerable.Range(0, nakedEdges.Length)
           from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
           let edgeI = validBrep.Edges[nakedEdges[i].Index]
           let edgeJ = validBrep.Edges[nakedEdges[j].Index]
           let result = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB)
               ? (Success: true, Distance: ptA.DistanceTo(ptB))
               : (Success: false, Distance: double.MaxValue)
           where result.Success && result.Distance < tolerance
           select (EdgeA: nakedEdges[i].Index, EdgeB: nakedEdges[j].Index, result.Distance)),
    ]
    : [];
```

**Solution**: Use RTree for O(n log n) spatial queries
```csharp
// After - 15 LOC, O(n log n)
(int EdgeA, int EdgeB, double Distance)[] nearMisses = nakedEdges.Length < TopologyConfig.MaxEdgesForNearMissAnalysis
    ? ((Func<(int, int, double)[]>)(() => {
        using RTree tree = new();
        for (int i = 0; i < nakedEdges.Length; i++) {
            _ = tree.Insert(validBrep.Edges[nakedEdges[i].Index].EdgeCurve.GetBoundingBox(accurate: true), i);
        }
        List<(int, int, double)> results = [];
        for (int i = 0; i < nakedEdges.Length; i++) {
            BrepEdge edgeI = validBrep.Edges[nakedEdges[i].Index];
            _ = tree.Search(edgeI.GetBoundingBox(accurate: true).InflateBy(tolerance), (_, args) => {
                BrepEdge edgeJ = validBrep.Edges[nakedEdges[args.Id].Index];
                double dist = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB) ? ptA.DistanceTo(ptB) : double.MaxValue;
                dist < tolerance && args.Id != i ? results.Add((i, args.Id, dist)) : 0;
            });
        }
        return [.. results];
    }))()
    : [];
```

**Impact**:
- TopologyCompute: -11 LOC + better performance
- Demonstrates spatial indexing pattern for other modules
- **Total**: ~15 LOC reduction (includes simplified LINQ elsewhere)

**Files to Modify**:
- `TopologyCompute.cs` (Diagnose method)

### 2.2: Loop Optimization in SpatialCompute (Hot Paths)

**Problem**: Some LINQ chains could be for-loops in hot paths
```csharp
// Current (SpatialCompute.cs lines 17-24) - Centroid calculation uses LINQ
double sumX = 0.0, sumY = 0.0, sumZ = 0.0;
for (int i = 0; i < arr.Length; i++) {
    sumX += pts[arr[i]].X;
    sumY += pts[arr[i]].Y;
    sumZ += pts[arr[i]].Z;
}
return new Point3d(sumX / arr.Length, sumY / arr.Length, sumZ / arr.Length);
```

**This is already optimal!** SpatialCompute demonstrates excellent hot-path patterns.

**Other hot paths to verify**:
- AnalysisCompute.MeshForFEA (lines 68-148) - ✅ Already using for-loops with ArrayPool
- SpatialCompute.KMeansAssign (lines 41-116) - ✅ Already optimal with for-loops

**Action**: Document these as exemplars, no changes needed

### 2.3: Consolidate Repeated LINQ Patterns (All Compute Files)

**Problem**: Similar LINQ patterns repeated across files
```csharp
// Pattern 1: Select + Where + ToArray (appears 15+ times)
items.Select(transform).Where(predicate).ToArray()

// Pattern 2: Enumerate + Average (appears 10+ times)
items.Average(selector)

// Pattern 3: Zip operations (appears 8+ times)
items.Zip(others, (a, b) => transform(a, b))
```

**Solution**: Leverage collection expressions with spread for clarity
```csharp
// After - more concise
[.. items.Select(transform).Where(predicate),]

// After - inline where possible
items.Sum(selector) / items.Count()  // Instead of .Average() when custom logic needed

// After - use tuple deconstruction in Zip
items.Zip(others).Select(((var a, var b)) => transform(a, b))
```

**Impact**:
- All Compute files: -15 LOC (more concise LINQ)
- **Total**: ~20 LOC reduction + improved clarity

**Files to Modify**:
- All `*Compute.cs` files (standardize LINQ patterns)

### 2.4: Eliminate Unnecessary Guard Clauses (All Compute Files)

**Problem**: Double validation when UnifiedOperation already validates
```csharp
// Current pattern (appears in OrientCompute, IntersectionCompute)
ResultFactory.Create(value: brep)
    .Validate(args: [context, V.Standard,])  // Validation 1
    .Bind(validBrep => validBrep.IsValid     // Validation 2 (redundant!)
        ? ActualLogic(validBrep)
        : error)
```

**Solution**: Trust UnifiedOperation validation
```csharp
// After - single validation point
UnifiedOperation.Apply(
    input: brep,
    operation: (Func<Brep, Result<IReadOnlyList<T>>>)ActualLogic,
    config: new OperationConfig<Brep, T> {
        ValidationMode = V.Standard,  // Only validation needed
    });
```

**Impact**:
- OrientCompute: -10 LOC (2-3 guard clauses eliminated)
- IntersectionCompute: -15 LOC (3-4 guard clauses eliminated)
- **Total**: ~30 LOC reduction

**Files to Modify**:
- `OrientCompute.cs` (OptimizeOrientation, DetectPattern)
- `IntersectionCompute.cs` (All three methods)

**Phase 2 Total Reduction**: ~80 LOC (target was 150, but many patterns already optimal!)

---

## Phase 3: Architectural Consolidation (Target: -100 LOC, Better Integration)

### Goals
- Consolidate Config constants into FrozenDictionary where beneficial
- Merge similar operations in Core files
- Improve integration between Compute/Core/Config
- Reduce total type count where possible

### 3.1: Config File Consolidation (Analysis, Extraction)

**Problem**: Scattered constants could be grouped
```csharp
// Current (AnalysisConfig.cs) - 13 individual constants
internal const int MaxDiscontinuities = 20;
internal const int DefaultDerivativeOrder = 2;
internal const int CurveFrameSampleCount = 5;
// ... 10 more constants
```

**Solution**: Group related constants into structured configuration
```csharp
// After - type-safe configuration groups
internal static class Sampling {
    internal const int MaxDiscontinuities = 20;
    internal const int CurveFrameSampleCount = 5;
    internal const int SurfaceQualitySampleCount = 100;
    internal const int CurveFairnessSampleCount = 50;
}

internal static class Thresholds {
    internal const double HighCurvatureMultiplier = 5.0;
    internal const double InflectionSharpnessThreshold = 0.5;
    internal const double SmoothnessSensitivity = 10.0;
}

internal static class MeshQuality {
    internal const double AspectRatioWarning = 3.0;
    internal const double AspectRatioCritical = 10.0;
    internal const double SkewnessWarning = 0.5;
    internal const double SkewnessCritical = 0.85;
    internal const double JacobianWarning = 0.3;
    internal const double JacobianCritical = 0.1;
}
```

**Impact**:
- Better organization, same LOC (no reduction, but improved clarity)
- Enables future optimizations (dispatch tables with config lookup)
- **Total**: 0 LOC change, +architecture improvement

**Files to Modify**:
- `AnalysisConfig.cs` (group constants)
- `ExtractionConfig.cs` (group constants)
- `IntersectionConfig.cs` (group constants)
- `SpatialConfig.cs` (already well-organized)

### 3.2: Core File Dispatch Integration (Intersection, Extraction)

**Problem**: Core files have dispatch logic that Compute files re-implement
```csharp
// IntersectionCore has ResolveStrategy but Compute doesn't fully leverage it
// ExtractionCore has primitive detection but Compute has duplicate logic
```

**Solution**: Move more logic into Core dispatch, thin out Compute files
```csharp
// IntersectionCore.cs - Expand dispatch capabilities
internal static Result<T> ExecuteClassification<T>(
    GeometryBase geomA,
    GeometryBase geomB,
    Intersect.IntersectionOutput output,
    IGeometryContext context) =>
    ResolveStrategy(geomA.GetType(), geomB.GetType())
        .Bind(entry => _classifiers.TryGetValue((geomA.GetType(), geomB.GetType()), out var classifier)
            ? classifier(geomA, geomB, output, context)
            : ResultFactory.Create<T>(error: E.Geometry.ClassificationFailed));

// IntersectionCompute.cs - Becomes thin wrapper
internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(...) =>
    IntersectionCore.ExecuteClassification<(byte, double[], bool, double)>(geomA, geomB, output, context);
```

**Impact**:
- IntersectionCompute: -40 LOC (logic moves to Core)
- ExtractionCompute: -30 LOC (primitive detection consolidation)
- Better separation: Compute = thin wrappers, Core = dispatch engine
- **Total**: ~70 LOC reduction

**Files to Modify**:
- `IntersectionCore.cs` (expand dispatch with classification)
- `IntersectionCompute.cs` (simplify using Core dispatch)
- `ExtractionCore.cs` (consolidate primitive detection)
- `ExtractionCompute.cs` (use Core for primitives)

### 3.3: Type Reduction via Consolidation (Extraction)

**Problem**: ExtractionCompute has 11 types (over ideal 6-8 target)
- Main methods: ExtractFeatures, DecomposeToPrimitives, ExtractPatterns
- Many small helper types for intermediate results

**Solution**: Consolidate helper methods into parameterized operations
```csharp
// Before: 8 separate classifier methods
ClassifyEdge, ClassifyEdgeFromCurvature, ClassifyEdgeByDihedral, ClassifyEdgeByAngle,
ClassifyHole, ClassifySurface, ComputeSurfaceResidual, ProjectPointTo*

// After: 3 parameterized classifiers + dispatch table
internal static Result<Classification> ClassifyGeometry(
    object geometry,
    ClassifierType type,
    IGeometryContext context) =>
    _classifierDispatch.TryGetValue((geometry.GetType(), type), out var classifier)
        ? classifier(geometry, context)
        : ResultFactory.Create<Classification>(error: E.Geometry.ClassificationFailed);

private static readonly FrozenDictionary<(Type, ClassifierType), Func<object, IGeometryContext, Result<Classification>>> _classifierDispatch = ...;
```

**Impact**:
- ExtractionCompute: -50 LOC (consolidation + dispatch)
- Type count: 11 → 8 (target range achieved)
- **Total**: ~50 LOC reduction + architectural improvement

**Files to Modify**:
- `ExtractionCompute.cs` (consolidate classifiers)
- `ExtractionCore.cs` (add classifier dispatch table)

**Phase 3 Total Reduction**: ~120 LOC (exceeded target of 100!)

---

## Phase 4: Advanced Optimizations (Target: -80 LOC, Maximum Density)

### Goals
- Leverage advanced C# patterns (switch on tuples, range patterns)
- Eliminate remaining procedural code
- Maximize use of expression-bodied members
- Final polish for algorithmic density

### 4.1: Switch on Tuples for Multi-Condition Logic

**Problem**: Nested ternaries in some places could be cleaner switch expressions
```csharp
// OrientCompute.cs lines 54-56 - Complex nested conditions
return (xform, twist, tilt, symmetry, relationship) is (Transform t, double tw, double ti, byte sym, byte rel)
    ? ResultFactory.Create(value: (t, tw, ti, sym, rel))
    : ResultFactory.Create<...>(error: ...)
```

**Solution**: Switch on tuple patterns for clarity
```csharp
// After - switch on tuple for exhaustive matching
return (Transform.IsValid(xform), symmetry, relationship) switch {
    (true, byte sym and >= 0 and <= 2, byte rel and >= 1 and <= 3) =>
        ResultFactory.Create(value: (xform, twist, tilt, sym, rel)),
    (false, _, _) => ResultFactory.Create<...>(error: E.Geometry.TransformFailed),
    (_, byte sym, _) when sym > 2 => ResultFactory.Create<...>(error: E.Geometry.InvalidSymmetryType),
    _ => ResultFactory.Create<...>(error: E.Geometry.OrientationFailed),
};
```

**Impact**:
- OrientCompute: -8 LOC (clearer pattern matching)
- ExtractionCompute: -10 LOC (pattern detection logic)
- **Total**: ~20 LOC reduction + improved clarity

**Files to Modify**:
- `OrientCompute.cs` (ComputeRelative, DetectPattern)
- `ExtractionCompute.cs` (pattern detection methods)

### 4.2: Range Patterns for Bounds Checking

**Problem**: Manual bounds checking is verbose
```csharp
// Current pattern (appears in multiple files)
criteria is < 1 or > 4 ? error : logic
index >= 0 && index < array.Length ? array[index] : default
```

**Solution**: Use range patterns
```csharp
// After
criteria is >= 1 and <= 4 ? logic : error
index is >= 0 and < array.Length ? array[index] : default

// Or in switch
return index switch {
    >= 0 and < array.Length => array[index],
    _ => default,
};
```

**Impact**:
- All Compute files: -10 LOC (more concise bounds checking)
- **Total**: ~15 LOC reduction

**Files to Modify**:
- All `*Compute.cs` files (standardize bounds checking)

### 4.3: Expression-Bodied Members

**Problem**: Some simple methods use block bodies
```csharp
// Current
private static double ComputeVariance(double[] values) {
    return values.Length switch {
        0 => double.MaxValue,
        1 => 0.0,
        int n => ...
    };
}
```

**Solution**: Expression-bodied where appropriate
```csharp
// After
private static double ComputeVariance(double[] values) =>
    values.Length switch {
        0 => double.MaxValue,
        1 => 0.0,
        int n => values.Average() is double mean
            ? values.Sum(v => (v - mean) * (v - mean)) / n
            : 0.0,
    };
```

**Impact**:
- All Compute files: -15 LOC (more concise method declarations)
- **Total**: ~20 LOC reduction

**Files to Modify**:
- All `*Compute.cs` files (convert applicable methods)

### 4.4: Final LINQ Optimization Pass

**Problem**: Some remaining LINQ chains can be more concise
```csharp
// Current
.Select(x => Transform(x))
.Where(x => Predicate(x))
.Select(x => FinalTransform(x))
.ToArray()
```

**Solution**: Consolidate operations
```csharp
// After
.Select(x => Transform(x))
.Where(Predicate)
.Select(FinalTransform)
.ToArray()

// Or even better with collection expression
[.. items.Select(Transform).Where(Predicate).Select(FinalTransform),]
```

**Impact**:
- All Compute files: -10 LOC (remove redundant lambda syntax)
- **Total**: ~15 LOC reduction

**Files to Modify**:
- All `*Compute.cs` files (final LINQ pass)

**Phase 4 Total Reduction**: ~70 LOC (close to target of 80!)

---

## Summary: Total Expected Improvements

| Phase | Focus | LOC Reduction | Key Improvements |
|-------|-------|---------------|------------------|
| **Phase 1** | Foundation | -160 LOC | Validation consolidation, tuple patterns, dispatch tables |
| **Phase 2** | Algorithms | -80 LOC | Spatial indexing, loop optimization, eliminate guards |
| **Phase 3** | Architecture | -120 LOC | Core dispatch expansion, type consolidation |
| **Phase 4** | Advanced | -70 LOC | Tuple switches, range patterns, expression bodies |
| **TOTAL** | **All Phases** | **-430 LOC** | **28% reduction while maintaining 100% functionality** |

### Final State Projection

| Folder | Current LOC | Target LOC | Reduction | Status |
|--------|-------------|-----------|-----------|--------|
| **orientation** | 529 | ~480 | -49 (-9%) | ✅ Well within limits |
| **topology** | 770 | ~690 | -80 (-10%) | ✅ Well within limits |
| **analysis** | 563 | ~510 | -53 (-9%) | ✅ Well within limits |
| **intersection** | 723 | ~620 | -103 (-14%) | ✅ Significant improvement |
| **spatial** | 693 | ~600 | -93 (-13%) | ✅ Compute optimized |
| **extraction** | 963 | ~800 | -163 (-17%) | ✅ Type count reduced |
| **TOTAL** | **4,241** | **~3,700** | **-541 (-13%)** | **✅ Major improvement** |

---

## Implementation Strategy

### Execution Order (Critical Path)

1. **Phase 1.1** (Validation) → Enables all other phases
2. **Phase 1.3** (Dispatch) → Required for Phase 3.2
3. **Phase 2.4** (Guards) → Clean up after Phase 1.1
4. **Phase 3.2** (Core Integration) → Major architectural shift
5. **Phase 3.3** (Type Reduction) → Extraction folder focus
6. **Phases 1.2, 2.1, 2.3, 4.1-4.4** → Polish and optimization (can be parallel)

### Per-Phase Testing Requirements

After each phase:
1. ✅ `dotnet build` - Zero warnings
2. ✅ `dotnet test` - All tests pass
3. ✅ Manual verification of key operations
4. ✅ Git commit with descriptive message

### Risk Mitigation

**Low Risk** (Phases 1.2, 2.3, 4.1-4.4):
- Syntactic improvements only
- No logic changes
- Easy to revert

**Medium Risk** (Phases 1.1, 2.1, 2.4):
- Validation consolidation changes behavior (but maintains semantics)
- Spatial indexing changes algorithm (but maintains results)
- Test thoroughly

**High Risk** (Phases 1.3, 3.2, 3.3):
- Major architectural shifts
- Move logic between files
- Multiple files changed simultaneously
- Requires comprehensive testing
- Do these phases separately with full test runs

---

## Specific Code Examples

### Example 1: IntersectionCompute.Classify Refactoring

**Before** (243 LOC file, 69 LOC method):
```csharp
internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(
    Intersect.IntersectionOutput output,
    GeometryBase geomA,
    GeometryBase geomB,
    IGeometryContext context) {

    static Result<(byte, double[], bool, double)> curveSurfaceClassifier(double[] angles) {
        double deviationSum = 0.0;
        bool grazing = false;
        for (int i = 0; i < angles.Length; i++) {
            double deviation = Math.Abs((Math.PI * 0.5) - angles[i]);
            deviationSum += deviation;
            grazing = grazing || deviation <= IntersectionConfig.GrazingAngleThreshold;
        }
        double averageDeviation = deviationSum / angles.Length;
        bool tangent = averageDeviation <= IntersectionConfig.TangentAngleThreshold;
        return ResultFactory.Create(value: (
            Type: tangent ? (byte)0 : (byte)1,
            ApproachAngles: angles,
            IsGrazing: grazing,
            BlendScore: tangent ? IntersectionConfig.CurveSurfaceTangentBlendScore : IntersectionConfig.CurveSurfacePerpendicularBlendScore));
    }

    return geomA is null || geomB is null
        ? ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData.WithContext("Geometry is null"))
        : IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
            .Bind(entry => {
                (V modeA, V modeB) = entry.Swapped ? (entry.Strategy.ModeB, entry.Strategy.ModeA) : (entry.Strategy.ModeA, entry.Strategy.ModeB);
                return (modeA == V.None ? ResultFactory.Create(value: geomA) : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                    .Bind(validA => (modeB == V.None ? ResultFactory.Create(value: geomB) : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                        .Bind(validB => (output.Points.Count, output.ParametersA.Count, output.ParametersB.Count) switch {
                            (0, _, _) => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                            (int count, int parametersA, int parametersB) => (validA, validB) switch {
                                (Curve curveA, Curve curveB) when parametersA >= count && parametersB >= count => /* 20 LOC logic */,
                                (Curve curve, Surface surface) when parametersA >= count => /* 15 LOC logic */,
                                (Surface surface, Curve curve) when parametersB >= count => /* 15 LOC logic */,
                                _ when parametersA < count || parametersB < count => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                                _ => ResultFactory.Create(value: ((byte)2, Array.Empty<double>(), false, 0.0)),
                            },
                        }));
            });
}
```

**After** (~40 LOC method):
```csharp
internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(
    Intersect.IntersectionOutput output,
    GeometryBase geomA,
    GeometryBase geomB,
    IGeometryContext context) =>
    output.Points.Count is 0
        ? ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData)
        : UnifiedOperation.Apply(
            input: (geomA, geomB, output),
            operation: (Func<(GeometryBase, GeometryBase, Intersect.IntersectionOutput), Result<IReadOnlyList<(byte, double[], bool, double)>>>)(inputs => {
                (GeometryBase a, GeometryBase b, Intersect.IntersectionOutput o) = inputs;
                return IntersectionCore.ExecuteClassification(a, b, o, context)
                    .Map(result => (IReadOnlyList<(byte, double[], bool, double)>)[result]);
            }),
            config: new OperationConfig<(GeometryBase, GeometryBase, Intersect.IntersectionOutput), (byte, double[], bool, double)> {
                Context = context,
                ValidationMode = V.Standard,
            })
        .Map(results => results[0]);  // UnifiedOperation returns list, extract single result
```

**Savings**: 29 LOC in Compute, logic moved to IntersectionCore

### Example 2: ExtractionCompute Type Consolidation

**Before** (384 LOC, 11 types):
```csharp
// 8 separate classifier methods
private static (byte Type, double Param) ClassifyEdge(BrepEdge edge, Brep brep) { /* 15 LOC */ }
private static (byte Type, double Param) ClassifyEdgeFromCurvature(...) { /* 12 LOC */ }
private static (byte Type, double Param) ClassifyEdgeByDihedral(...) { /* 10 LOC */ }
private static (byte Type, double Param) ClassifyEdgeByAngle(...) { /* 8 LOC */ }
private static (bool IsHole, double Area) ClassifyHole(BrepLoop loop) { /* 18 LOC */ }
private static (bool Success, byte Type, Plane Frame, double[] Params) ClassifySurface(Surface surface) { /* 24 LOC */ }
private static double ComputeSurfaceResidual(...) { /* 22 LOC */ }
private static Point3d ProjectPointToCylinder(...) { /* 8 LOC */ }
// + 5 more projection helpers
```

**After** (~280 LOC, 8 types):
```csharp
// Consolidated into parameterized classifier with dispatch
private static readonly FrozenDictionary<(Type GeometryType, ClassifierType Classifier), Func<object, object, IGeometryContext, Result<object>>> _classifierDispatch =
    new Dictionary<(Type, ClassifierType), Func<object, object, IGeometryContext, Result<object>>> {
        [(typeof(BrepEdge), ClassifierType.Edge)] = (g, ctx, _) => ClassifyEdgeConsolidated((BrepEdge)g, (Brep)ctx, context),
        [(typeof(BrepLoop), ClassifierType.Hole)] = (g, _, ctx) => ClassifyHoleConsolidated((BrepLoop)g, (IGeometryContext)ctx),
        [(typeof(Surface), ClassifierType.Primitive)] = (g, _, ctx) => ClassifySurfaceConsolidated((Surface)g, (IGeometryContext)ctx),
        [(typeof(Point3d), ClassifierType.Project)] = (g, target, ctx) => ProjectPointConsolidated((Point3d)g, target, (IGeometryContext)ctx),
    }.ToFrozenDictionary();

internal static Result<T> ClassifyGeometry<T>(
    object geometry,
    ClassifierType type,
    object? context,
    IGeometryContext geometryContext) =>
    _classifierDispatch.TryGetValue((geometry.GetType(), type), out var classifier)
        ? classifier(geometry, context, geometryContext).Map(result => (T)result)
        : ResultFactory.Create<T>(error: E.Geometry.ClassificationFailed);

// Only 3 consolidated methods needed
private static Result<object> ClassifyEdgeConsolidated(BrepEdge edge, Brep brep, IGeometryContext context) { /* 35 LOC - combines 4 methods */ }
private static Result<object> ClassifyHoleConsolidated(BrepLoop loop, IGeometryContext context) { /* 18 LOC - same */ }
private static Result<object> ClassifySurfaceConsolidated(Surface surface, IGeometryContext context) { /* 24 LOC - same */ }
```

**Savings**: ~104 LOC, 11 → 8 types

---

## Exemplar Patterns to Replicate

### Pattern 1: SpatialCompute.KMeansAssign (Lines 41-116)

**Why it's excellent**:
- K-means++ initialization with squared distances (mathematically sound)
- Hot-path for-loops for performance (2-3x faster than LINQ)
- Convergence detection without extra allocations
- Zero helper methods - pure algorithmic density

**Characteristics to replicate**:
```csharp
// Dense initialization logic inline
centroids[0] = pts[rng.Next(pts.Length)];
for (int i = 1; i < k; i++) {
    double[] distSq = new double[pts.Length];
    for (int j = 0; j < pts.Length; j++) {
        double minDist = pts[j].DistanceTo(centroids[0]);
        for (int c = 1; c < i; c++) {
            double dist = pts[j].DistanceTo(centroids[c]);
            minDist = dist < minDist ? dist : minDist;  // ✅ Ternary not if/else
        }
        distSq[j] = minDist * minDist;
    }
    // ... weighted selection logic inline (no helper)
}

// Lloyd's algorithm with inline convergence check
for (int iter = 0; iter < maxIter; iter++) {
    for (int i = 0; i < pts.Length; i++) {  // ✅ Hot path for-loop
        int nearest = 0;
        double minDist = pts[i].DistanceTo(centroids[0]);
        for (int j = 1; j < k; j++) {
            double dist = pts[i].DistanceTo(centroids[j]);
            (nearest, minDist) = dist < minDist ? (j, dist) : (nearest, minDist);  // ✅ Tuple ternary
        }
        assignments[i] = nearest;
    }

    // Convergence check inline (no helper method)
    double maxShift = 0.0;
    for (int i = 0; i < k; i++) {
        Point3d newCentroid = clusters[i].Count > 0 ? clusters[i].Sum / clusters[i].Count : centroids[i];
        double shift = centroids[i].DistanceTo(newCentroid);
        maxShift = shift > maxShift ? shift : maxShift;  // ✅ Inline max
        centroids[i] = newCentroid;
    }

    if (maxShift <= tol) break;  // ✅ Guard clause (acceptable if without else)
}
```

**Where to apply**: Any clustering, optimization, or iterative algorithm in other Compute files

### Pattern 2: AnalysisCompute.MeshForFEA (Lines 68-148)

**Why it's excellent**:
- ArrayPool for zero-allocation temporary buffers
- Hot-path for-loops for mesh face iteration
- Complex quality metrics computed inline (aspect ratio, skewness, Jacobian)
- No helper methods - all logic self-contained

**Characteristics to replicate**:
```csharp
// ArrayPool for temporary buffers
Point3d[] vertices = ArrayPool<Point3d>.Shared.Rent(4);
double[] edgeLengths = ArrayPool<double>.Shared.Rent(4);
try {
    (double AspectRatio, double Skewness, double Jacobian)[] metrics = [.. Enumerable.Range(0, validMesh.Faces.Count).Select(i => {
        // Complex calculation inline (no helpers)
        Point3d center = validMesh.Faces.GetFaceCenter(i);
        MeshFace face = validMesh.Faces[i];
        bool isQuad = face.IsQuad;

        // Edge length calculation inline
        for (int j = 0; j < vertCount; j++) {
            edgeLengths[j] = vertices[j].DistanceTo(vertices[(j + 1) % vertCount]);
        }

        // Quality metrics inline with ternary
        double aspectRatio = maxEdge / (minEdge + context.AbsoluteTolerance);
        double skewness = isQuad
            ? ((double[])[/* angles */]).Max(angle => Math.Abs((angle * (180.0 / Math.PI)) - 90.0)) / 90.0
            : /* triangle logic inline */;

        return (AspectRatio: aspectRatio, Skewness: skewness, Jacobian: jacobian);
    }),
    ];
} finally {
    ArrayPool<Point3d>.Shared.Return(vertices, clearArray: true);  // ✅ Always clean up
    ArrayPool<double>.Shared.Return(edgeLengths, clearArray: true);
}
```

**Where to apply**: Any compute-intensive operation with temporary allocations

### Pattern 3: IntersectionCompute.Classify - Circular Mean (Lines 55-60)

**Why it's excellent**:
- Sophisticated mathematical concept (circular mean for angles) inline
- No helper method despite complexity
- Uses switch expression with guard clauses

**Characteristics to replicate**:
```csharp
// Complex calculation inline
.Where(angle => !double.IsNaN(angle))
.ToArray() is double[] angles
    && angles.Length > 0
    && Math.Atan2(angles.Sum(Math.Sin) / angles.Length, angles.Sum(Math.Cos) / angles.Length) is double circularMean
    && (circularMean < 0.0 ? circularMean + (2.0 * Math.PI) : circularMean) is double averageAngle
    ? ResultFactory.Create(value: (
        Type: averageAngle < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1,
        ApproachAngles: angles,
        IsGrazing: angles.Any(angle => angle < IntersectionConfig.GrazingAngleThreshold),
        BlendScore: averageAngle < IntersectionConfig.TangentAngleThreshold
            ? IntersectionConfig.TangentBlendScore
            : IntersectionConfig.PerpendicularBlendScore))
    : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed)
```

**Where to apply**: Complex mathematical operations that would traditionally be extracted

### Pattern 4: TopologyCompute.Diagnose - LINQ Comprehension (Lines 53-60)

**Why it's excellent**:
- Comprehension query with from...from...let...where...select
- Complex nested logic expressed declaratively
- Returns empty array as fallback (no null checks needed)

**Characteristics to replicate**:
```csharp
(int EdgeA, int EdgeB, double Distance)[] nearMisses = nakedEdges.Length < TopologyConfig.MaxEdgesForNearMissAnalysis
    ? [.. (from i in Enumerable.Range(0, nakedEdges.Length)
           from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
           let edgeI = validBrep.Edges[nakedEdges[i].Index]
           let edgeJ = validBrep.Edges[nakedEdges[j].Index]
           let result = edgeI.EdgeCurve.ClosestPoints(edgeJ.EdgeCurve, out Point3d ptA, out Point3d ptB)
               ? (Success: true, Distance: ptA.DistanceTo(ptB))
               : (Success: false, Distance: double.MaxValue)
           where result.Success && result.Distance < context.AbsoluteTolerance * TopologyConfig.NearMissMultiplier && result.Distance > context.AbsoluteTolerance
           select (EdgeA: nakedEdges[i].Index, EdgeB: nakedEdges[j].Index, result.Distance)),
    ]
    : [];  // ✅ Empty array, not null
```

**Where to apply**: Complex queries with multiple nested loops and conditions

---

## Anti-Patterns to Eliminate

### Anti-Pattern 1: Manual Null Checks Before Validation

**Bad** (appears 8+ times):
```csharp
geomA is null || geomB is null
    ? ResultFactory.Create<T>(error: E.Geometry.InsufficientIntersectionData)
    : !geomA.IsValid || !geomB.IsValid
        ? ResultFactory.Create<T>(error: E.Validation.GeometryInvalid)
        : ActualLogic(geomA, geomB)
```

**Good** (use UnifiedOperation):
```csharp
UnifiedOperation.Apply(
    input: (geomA, geomB),
    operation: (Func<(GeometryBase, GeometryBase), Result<IReadOnlyList<T>>>)(pair => ActualLogic(pair.Item1, pair.Item2)),
    config: new OperationConfig<(GeometryBase, GeometryBase), T> {
        ValidationMode = V.Standard,  // Handles null + IsValid automatically
    });
```

### Anti-Pattern 2: Tuple Element Access

**Bad** (ExtractionCompute lines 162-180):
```csharp
.Select(item => item.Error.HasValue ? error : item.Success ? use(item.Type, item.Frame, item.Params, item.Residual) : default)
```

**Good** (use tuple deconstruction):
```csharp
.Select(item => {
    (bool success, byte type, Plane frame, double[] pars, double residual, SystemError? error) = item;
    return error.HasValue ? ResultFactory.Create<T>(error: error.Value)
        : success ? ProcessPrimitive(type, frame, pars, residual)
        : default;
})
```

### Anti-Pattern 3: Unnecessary Guard Clauses

**Bad** (OrientCompute line 23):
```csharp
ResultFactory.Create(value: brep)
    .Validate(args: [context, V.Standard | V.Topology | V.BoundingBox | V.MassProperties,])
    .Bind(validBrep => validBrep.IsValid && validBrep.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
        ? ActualLogic(validBrep)
        : error)
```

**Good** (trust validation):
```csharp
ResultFactory.Create(value: brep)
    .Validate(args: [context, V.Standard | V.Topology | V.BoundingBox | V.MassProperties,])
    .Bind(validBrep => ActualLogic(validBrep))  // Validation already checked everything
```

### Anti-Pattern 4: Nested Loops Without Spatial Indexing

**Bad** (TopologyCompute lines 53-60 - though already using LINQ comprehension):
```csharp
from i in Enumerable.Range(0, nakedEdges.Length)
from j in Enumerable.Range(i + 1, nakedEdges.Length - i - 1)
// O(n²) comparisons
```

**Good** (use RTree for large datasets):
```csharp
using RTree tree = BuildEdgeTree(nakedEdges);
for (int i = 0; i < nakedEdges.Length; i++) {
    tree.Search(nakedEdges[i].SearchBox, (_, args) => /* O(log n) queries */);
}
```

---

## Testing Strategy

### Phase 1 Tests
- Validate UnifiedOperation wrapping preserves behavior
- Confirm validation errors unchanged
- Test tuple deconstruction doesn't change results
- Verify dispatch tables return same results as switch

### Phase 2 Tests
- Spatial indexing returns same near-misses
- Performance benchmarks show improvements
- Eliminated guard clauses don't break edge cases

### Phase 3 Tests
- Core dispatch integration maintains behavior
- Type consolidation doesn't lose functionality
- Config grouping doesn't break constant references

### Phase 4 Tests
- Switch on tuples maintains logic
- Range patterns handle bounds correctly
- Expression bodies preserve semantics

### Comprehensive Test After Each Phase
```bash
dotnet build --no-incremental  # Force rebuild
dotnet test --verbosity normal # Run all tests
dotnet test --filter "FullyQualifiedName~Compute" # Focus on compute tests
```

---

## Success Metrics

### Quantitative
- ✅ Total LOC reduction: ~430 lines (target: -400)
- ✅ All folders at or below 4 files
- ✅ All folders at or below 10 types
- ✅ All methods at or below 300 LOC
- ✅ Zero build warnings
- ✅ All tests passing

### Qualitative
- ✅ Improved code density (fewer, more powerful operations)
- ✅ Better separation of concerns (Compute thin, Core dispatch-heavy)
- ✅ Consistent patterns across all folders
- ✅ Exemplar-quality code throughout
- ✅ Easier to maintain and extend

---

## Conclusion

This refactoring plan focuses on **concrete, measurable improvements** while maintaining 100% functionality. Each phase builds on the previous, with clear metrics and rollback points. The emphasis is on:

1. **Eliminating redundancy** (validation, type checks, guards)
2. **Improving algorithms** (spatial indexing, dispatch tables)
3. **Consolidating architecture** (Core dispatch, Config organization)
4. **Maximizing density** (advanced C# patterns, expression bodies)

The result will be **~430 fewer lines of code** that are **more maintainable, more performant, and easier to extend** - all while strictly adhering to the 4-file, 10-type, 300-LOC limits.

**Next Steps**: Begin Phase 1.1 (Validation Consolidation) with IntersectionCompute as the pilot implementation.
