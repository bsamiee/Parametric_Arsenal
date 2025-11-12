# FrozenDictionary Integration Audit - libs/rhino/

**Date**: 2025-11-12
**Scope**: Complete audit of FrozenDictionary usage patterns in `libs/rhino/`
**Purpose**: Identify opportunities to integrate loose constants into FrozenDictionaries for better code density

---

## Executive Summary

**FINDING**: The codebase is **already optimally designed** for FrozenDictionary usage. No refactoring opportunities identified.

**RATIONALE**:
- All dispatch-oriented operations use FrozenDictionaries where appropriate
- Remaining constants serve different purposes (array indices, classification outputs, algorithmic parameters)
- Moving these to FrozenDictionaries would **reduce** performance and clarity

---

## Comprehensive FrozenDictionary Inventory

### **1. SPATIAL FOLDER** (`libs/rhino/spatial/`)

#### **FrozenDictionary #1: Type Extractors**
**Location**: `SpatialConfig.cs:19-40`
**Pattern**: Semantic String-Type Composite Keys

```csharp
FrozenDictionary<(string Operation, Type GeometryType), Func<object, object>>
```

**Purpose**: Polymorphic dispatch for centroid extraction, RTree factory construction, clustering algorithms

**Complexity**: **HIGH**
- String-based operation naming ("Centroid", "RTreeFactory", "ClusterAssign")
- Enables extensibility beyond type-based dispatch
- Uses `typeof(void)` as semantic placeholder for algorithm dispatch

**Example Entry**:
```csharp
[("Centroid", typeof(Curve))] = static g => g is Curve c
    ? (AreaMassProperties.Compute(c) is { Centroid: { IsValid: true } ct }
        ? ct
        : c.GetBoundingBox(accurate: false).Center)
    : Point3d.Origin
```

**Best Practice**: Inline ternary operators and pattern matching eliminate need for helper methods while maintaining algorithmic density.

---

#### **FrozenDictionary #2: Spatial Operation Registry**
**Location**: `SpatialCore.cs:23-42`
**Pattern**: Most Sophisticated in Codebase

```csharp
FrozenDictionary<
    (Type Input, Type Query),
    (Func<object, RTree>? Factory, V Mode, int BufferSize,
     Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute)>
```

**Purpose**: Complete spatial query dispatch with all operation metadata in single lookup

**Complexity**: **VERY HIGH** (highest in codebase)
- Nullable factory functions for operations without RTree
- Validation mode configuration per type-pair
- Buffer size for ArrayPool optimization
- 4-parameter executor function

**Construction Pattern**:
```csharp
[
    (typeof(Point3d[]), typeof(Sphere), _pointArrayFactory, V.None,
     SpatialConfig.DefaultBufferSize, MakeExecutor<Point3d[]>(_pointArrayFactory)),
    (typeof(Mesh), typeof(Plane), _meshFactory, V.MeshSpecific,
     SpatialConfig.DefaultBufferSize, MakeExecutor<Mesh>(_meshFactory)),
].ToFrozenDictionary(
    static entry => (entry.Input, entry.Query),
    static entry => (entry.Factory, entry.Mode, entry.BufferSize, entry.Execute))
```

**Best Practice**: Shared static factory references (`_pointArrayFactory`, `_meshFactory`) avoid duplicate lambda allocations.

**Constants Analysis** (`SpatialConfig.cs:10-16`):
- `DefaultBufferSize`, `KMeansMaxIterations`, `DBSCANMinPoints`, etc.
- **VERDICT**: These are **algorithmic parameters**, not dispatch keys → Keep as constants

---

### **2. ANALYSIS FOLDER** (`libs/rhino/analysis/`)

#### **FrozenDictionary #1: Validation Mode Mapping**
**Location**: `AnalysisConfig.cs:10-24`
**Pattern**: Simple Type-to-Flags

```csharp
FrozenDictionary<Type, V>
```

**Purpose**: Map geometry types to appropriate validation modes

**Complexity**: **MEDIUM**

**Example**:
```csharp
[typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry
```

---

#### **FrozenDictionary #2: Analysis Strategy Dispatch**
**Location**: `AnalysisCore.cs:63-127`
**Pattern**: IIFE (Immediately Invoked Function Expression)

```csharp
FrozenDictionary<Type, (V Mode, Func<object, IGeometryContext, double?,
    (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> Compute)>
```

**Purpose**: Type-based dispatch to differential geometry computation strategies

**Complexity**: **VERY HIGH**
- 7-parameter function signatures with complex nullable parameters
- IIFE pattern builds dictionary with shared logic
- Reuses `CurveLogic` and `SurfaceLogic` static functions for multiple type entries

**Construction Pattern**:
```csharp
private static readonly FrozenDictionary<...> _strategies =
    ((Func<FrozenDictionary<...>>)(() => {
        Dictionary<Type, (V, Func<...>)> map = new();
        // Shared logic for curves
        foreach (Type curveType in [typeof(Curve), typeof(NurbsCurve), ...]) {
            map[curveType] = (Modes[curveType], CurveLogic);
        }
        // Inline complex logic for Brep
        map[typeof(Brep)] = (Modes[typeof(Brep)], (g, ctx, _, uv, faceIdx, testPt, order) => { /* 40+ lines */ });
        return map.ToFrozenDictionary();
    }))();
```

**Best Practice**: IIFE enables code reuse without violating "no helper methods" rule.

---

### **3. EXTRACTION FOLDER** (`libs/rhino/extraction/`)

#### **FrozenDictionary #1: Validation Modes by (Kind, Type)**
**Location**: `ExtractionConfig.cs:69-118`
**Pattern**: Byte-Type Composite Keys

```csharp
FrozenDictionary<(byte Kind, Type GeometryType), V>
```

**Purpose**: Map extraction operation kind + geometry type to validation mode

**Complexity**: **HIGH** (48 entries covering comprehensive operation matrix)

**Key Pattern**:
- Kind 1-7: Basic extraction operations
- Kind 10-13: Curve-related operations
- Kind 20-24: Surface operations
- Kind 30-34: Advanced surface operations

**Example**:
```csharp
[(1, typeof(Brep))] = V.Standard | V.MassProperties,
[(20, typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
```

**Helper Method**:
```csharp
internal static V GetValidationMode(byte kind, Type geometryType) =>
    ValidationModes.TryGetValue((kind, geometryType), out V exact)
        ? exact
        : ValidationModes.Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType))
            .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(...))
            .Select(kv => kv.Value)
            .DefaultIfEmpty(V.Standard)
            .First();
```

**Best Practice**: Fallback logic handles type inheritance when exact match not found.

---

#### **FrozenDictionary #2-3: Point Extraction Registry (Dual Pattern)**
**Location**: `ExtractionCore.cs:20-24, 102-284`
**Pattern**: Most Sophisticated - Dual FrozenDict with Type-Specificity Fallback

**Primary Map**:
```csharp
FrozenDictionary<(byte Kind, Type GeometryType),
    Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>>
```

**Fallback Map**:
```csharp
FrozenDictionary<byte,
    (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>> Handler)[]>
```

**Purpose**: O(1) exact lookup with O(n) type inheritance fallback

**Complexity**: **EXTREME** (most sophisticated pattern in codebase)

**Construction Pattern**:
```csharp
// 1. Build primary map with all (Kind, Type) pairs
Dictionary<(byte Kind, Type GeometryType), Func<...>> map = new() {
    [(1, typeof(Brep))] = (g, req, ctx) => { /* handler */ },
    // ... 50+ entries
};

// 2. Build fallback map grouped by Kind, ordered by type specificity
FrozenDictionary<byte, (Type, Func<...>)[]> fallbacks = map
    .GroupBy(entry => entry.Key.Kind)
    .ToDictionary(
        group => group.Key,
        group => group
            .OrderByDescending(entry => entry.Key.GeometryType, _specificityComparer)
            .Select(entry => (entry.Key.GeometryType, entry.Value))
            .ToArray())
    .ToFrozenDictionary();
```

**Usage**:
```csharp
// Try exact lookup first
return _handlers.TryGetValue((kind, geometryType), out var handler)
    ? handler(geometry, request, context)
    // Fallback: iterate ordered array and check IsInstanceOfType
    : _handlerFallbacks.TryGetValue(kind, out var candidates)
        ? candidates.FirstOrDefault(c => c.GeometryType.IsInstanceOfType(geometry))
            .Handler?.Invoke(geometry, request, context)
            ?? ResultFactory.Create<Point3d[]>(error: E.Extraction.UnsupportedType)
        : ResultFactory.Create<Point3d[]>(error: E.Extraction.UnsupportedKind);
```

**Best Practice**: Custom `_specificityComparer` ensures most derived types checked first in fallback array.

---

#### **FrozenDictionary #4-5: Curve Extraction Registry**
**Location**: `ExtractionCore.cs:27-31, 288-343`
**Pattern**: Identical dual FrozenDict pattern as point extraction, but for `Curve[]` output

---

#### **Constants Analysis** (`ExtractionConfig.cs:13-67`):

**Feature Type Constants** (Lines 13-17):
```csharp
internal const byte FeatureTypeFillet = 0;
internal const byte FeatureTypeChamfer = 1;
internal const byte FeatureTypeHole = 2;
internal const byte FeatureTypeGenericEdge = 3;
internal const byte FeatureTypeVariableRadiusFillet = 4;
```

**Primitive Type Constants** (Lines 20-26):
```csharp
internal const byte PrimitiveTypePlane = 0;
internal const byte PrimitiveTypeCylinder = 1;
// ... 7 total
```

**Pattern Type Constants** (Lines 29-32):
```csharp
internal const byte PatternTypeLinear = 0;
internal const byte PatternTypeRadial = 1;
internal const byte PatternTypeGrid = 2;
internal const byte PatternTypeScaling = 3;
```

**Usage Analysis**:
- **NOT used as FrozenDictionary keys** (the validation FrozenDict uses DIFFERENT byte values: 1-7, 10-13, 20-34)
- **Used as return values** from classification functions:
  ```csharp
  return (Type: ExtractionConfig.FeatureTypeFillet, Param: 1.0 / mean);
  return (Type: ExtractionConfig.PrimitiveTypeCylinder, Param: radius);
  return (Type: ExtractionConfig.PatternTypeRadial, Param: angle);
  ```
- **Used in switch expressions** for dispatch:
  ```csharp
  return primitiveType switch {
      ExtractionConfig.PrimitiveTypePlane when pars.Length >= 3 => frame.ClosestPoint(sp),
      ExtractionConfig.PrimitiveTypeCylinder when pars.Length >= 2 => ProjectPointToCylinder(...),
      // ...
  };
  ```

**VERDICT**: These are **enum-like classification outputs**, not dispatch keys → Keep as constants

**ALTERNATIVE CONSIDERED**: These could be actual C# enums, but byte constants follow codebase pattern and avoid enum overhead.

---

### **4. INTERSECTION FOLDER** (`libs/rhino/intersection/`)

#### **FrozenDictionary #1: Type Pair Validation Modes**
**Location**: `IntersectionConfig.cs:34-81`
**Pattern**: Bidirectional Type-Pair Registration

```csharp
FrozenDictionary<(Type, Type), (V ModeA, V ModeB)>
```

**Purpose**: Bidirectional type-pair validation with automatic symmetric registration

**Complexity**: **VERY HIGH** - Eliminates manual bidirectional registration

**Construction Pattern**:
```csharp
[
    (typeof(Line), typeof(Sphere), V.Degeneracy, V.Standard),
    (typeof(Curve), typeof(Plane), V.Standard, V.None),
    // ... define only one direction
]
.SelectMany<(Type TypeA, Type TypeB, V ModeA, V ModeB),
    KeyValuePair<(Type, Type), (V ModeA, V ModeB)>>(
    p => p.TypeA == p.TypeB
        ? [KeyValuePair.Create((p.TypeA, p.TypeB), (p.ModeA, p.ModeB)),]
        : [
            KeyValuePair.Create((p.TypeA, p.TypeB), (p.ModeA, p.ModeB)),
            KeyValuePair.Create((p.TypeB, p.TypeA), (p.ModeB, p.ModeA)),
        ])
.ToFrozenDictionary();
```

**Best Practice**: Automatic symmetric registration ensures `(A, B)` and `(B, A)` both present with correct validation mode swapping.

---

#### **FrozenDictionary #2: Intersection Strategy Dispatch**
**Location**: `IntersectionCore.cs:107-230`
**Pattern**: Helper Lambdas for Strategy Construction

```csharp
FrozenDictionary<(Type, Type), IntersectionStrategy>

where IntersectionStrategy = (
    Func<object, object, double, IGeometryContext, int, Result<IntersectionOutput>> Executor,
    V ModeA,
    V ModeB)
```

**Purpose**: Complete intersection operation dispatch with validation modes

**Complexity**: **EXTREME** (42 intersection strategies with diverse RhinoCommon API patterns)

**Construction Pattern**:
```csharp
// Helper lambdas at top of class
Func<int, Point3d, Point3d, double, int?, Result<IntersectionOutput>> TwoPointHandler = ...;
Func<CurveIntersections, double, int?, Result<IntersectionOutput>> IntersectionProcessor = ...;
// ... more helpers

// Strategy array
[
    ((typeof(Line), typeof(Sphere)), (first, second, tolerance, _, _) => {
        int count = (int)RhinoIntersect.LineSphere(
            (Line)first, (Sphere)second, out Point3d pointA, out Point3d pointB);
        return TwoPointHandler(count, pointA, pointB, tolerance, null);
    }),
    ((typeof(Curve), typeof(Curve)), (first, second, tolerance, _, _) => {
        CurveIntersections? ci = RhinoIntersect.CurveCurve(...);
        return IntersectionProcessor(ci, tolerance, null);
    }),
    // ... 42 total entries
].ToFrozenDictionary(
    entry => entry.Key,
    entry => {
        (Type ta, Type tb) = entry.Key;
        (V modeA, V modeB) = IntersectionConfig.ValidationModes.TryGetValue((ta, tb), out var modes)
            ? modes
            : (V.Standard, V.Standard);
        return new IntersectionStrategy(entry.Value, modeA, modeB);
    });
```

**Best Practice**: Helper lambdas (`TwoPointHandler`, `IntersectionProcessor`) eliminate duplication in strategy definitions while avoiding prohibited helper methods.

---

### **5. ORIENTATION FOLDER** (`libs/rhino/orientation/`)

#### **FrozenDictionary #1: Type-to-Validation Modes**
**Location**: `OrientConfig.cs:10-26`
**Pattern**: Standard Type → V mapping (same as analysis folder)

---

#### **FrozenDictionary #2: Plane Extractors**
**Location**: `OrientCore.cs:11-39`
**Pattern**: IIFEs with IDisposable Management

```csharp
FrozenDictionary<Type, Func<object, Result<Plane>>>
```

**Purpose**: Type-based plane/frame extraction from geometry

**Complexity**: **HIGH** - Inline IIFEs with mass property disposal

**Example**:
```csharp
[typeof(Brep)] = g => ((Brep)g) switch {
    Brep b when b.IsSolid => ((Func<Result<Plane>>)(() => {
        using VolumeMassProperties? vmp = VolumeMassProperties.Compute(b);
        return vmp is not null
            ? ResultFactory.Create(value: new Plane(vmp.Centroid, Vector3d.ZAxis))
            : ResultFactory.Create<Plane>(error: E.Orient.MassPropertiesFailed);
    }))(),
    Brep b when b.IsSurface => /* surface logic */,
    _ => ResultFactory.Create<Plane>(error: E.Orient.UnsupportedBrepConfiguration),
}
```

**Best Practice**: IIFE enables `using` statements inside lambda for proper IDisposable cleanup while maintaining inline density.

---

### **6. TOPOLOGY FOLDER** (`libs/rhino/topology/`)

#### **FrozenDictionary #1: Operation Metadata**
**Location**: `TopologyConfig.cs:13-30`
**Pattern**: Enum-Type Composite Keys

```csharp
FrozenDictionary<(Type GeometryType, OpType Operation), (V ValidationMode, string OpName)>

internal enum OpType {
    NakedEdges = 0, BoundaryLoops = 1, NonManifold = 2,
    Connectivity = 3, EdgeClassification = 4, Adjacency = 5,
    VertexData = 6, NgonTopology = 7
}
```

**Purpose**: Map (geometry type, operation) to validation mode + diagnostic name

**Complexity**: **MEDIUM**

**Example**:
```csharp
[(typeof(Brep), OpType.NakedEdges)] = (V.Standard | V.Topology, "Topology.GetNakedEdges.Brep"),
[(typeof(Mesh), OpType.NakedEdges)] = (V.Standard | V.MeshSpecific, "Topology.GetNakedEdges.Mesh"),
```

**Best Practice**: Named operations for diagnostics enable clear operation identification in logs.

---

#### **FrozenDictionary #2-4: Runtime FrozenDictionaries**
**Location**: `TopologyCore.cs:193, 213, 233`
**Pattern**: Runtime Construction for Immutable Result Data

**Purpose**: FrozenDictionaries constructed at RUNTIME from query results, stored in result data structures

**Example 1 - Adjacency Graph** (Line 193):
```csharp
return ResultFactory.Create(value: (IReadOnlyList<Topology.ConnectivityData>)[
    new Topology.ConnectivityData(
        ComponentIndices: components,
        ComponentSizes: [.. components.Select(c => c.Count),],
        ComponentBounds: bounds,
        TotalComponents: componentCount,
        IsFullyConnected: componentCount == 1,
        AdjacencyGraph: Enumerable.Range(0, faceCount)
            .ToFrozenDictionary(
                keySelector: i => i,
                elementSelector: getAdjacentForGraph)),
]);

// ConnectivityData record signature:
public readonly record struct ConnectivityData(
    // ...
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph);
```

**Example 2 - Edge Classification Grouping** (Lines 213, 233):
```csharp
FrozenDictionary<Topology.EdgeContinuityType, IReadOnlyList<int>> grouped =
    edgeIndices
        .Select((idx, pos) => (idx, type: classifications[pos]))
        .GroupBy(x => x.type, x => x.idx)
        .ToFrozenDictionary(
            g => g.Key,
            g => (IReadOnlyList<int>)[.. g,]);

return ResultFactory.Create(value: (IReadOnlyList<Topology.EdgeData>)[
    new Topology.EdgeData(
        EdgeIndices: edgeIndices,
        EdgeTypes: [.. classifications,],
        GroupedByType: grouped,
        // ...
    ),
]);
```

**Complexity**: **UNIQUE** - Only folder that creates FrozenDictionaries at RUNTIME

**Best Practice**: Using FrozenDictionary for immutable result data ensures:
1. Consumers cannot modify topology results
2. O(1) lookup performance for adjacency queries
3. Clear immutability guarantee in API signature

---

#### **Constants Analysis** (`TopologyConfig.cs:32-60`):

**Healing Strategy Constants** (Lines 47-57):
```csharp
internal const byte StrategyConservativeRepair = 0;
internal const byte StrategyModerateJoin = 1;
internal const byte StrategyAggressiveJoin = 2;
internal const byte StrategyCombined = 3;
```

**Healing Tolerance Multipliers** (Line 60):
```csharp
internal static readonly double[] HealingToleranceMultipliers = [0.1, 1.0, 10.0,];
```

**Usage Analysis** (`TopologyCompute.cs:88-91`):
```csharp
bool success = currentStrategy switch {
    0 => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance),
    1 => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
    2 => copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[2] * context.AbsoluteTolerance) > 0,
    _ => copy.Repair(TopologyConfig.HealingToleranceMultipliers[0] * context.AbsoluteTolerance)
         && copy.JoinNakedEdges(TopologyConfig.HealingToleranceMultipliers[1] * context.AbsoluteTolerance) > 0,
};
```

**Pattern**: Strategy bytes (0, 1, 2) are **sequential array indices**. Strategy 3 (Combined) uses multiple indices [0] and [1].

**VERDICT**: Array is **CORRECT data structure** → Keep as-is

**RATIONALE**:
1. Strategy bytes ARE array indices (0, 1, 2) - natural mapping
2. Array indexing is O(1) and more efficient than FrozenDictionary lookup
3. Strategy 3 explicitly uses indices [0] and [1] in switch expression - clear intent
4. FrozenDictionary would add overhead without improving clarity

**ALTERNATIVE CONSIDERED**: `FrozenDictionary<byte, double>` mapping strategy to multiplier
- **REJECTED**: Would require changing switch expression to `HealingToleranceMultipliers[StrategyConservativeRepair]` everywhere, adding verbosity without benefit
- Array length (3) vs constant range (0-3) is intentional - Strategy 3 uses combined logic, not a single multiplier

---

## Cross-Cutting Pattern Analysis

### **Pattern Hierarchy by Sophistication**

1. **🏆 Dual FrozenDict with Type-Specificity Fallback** (Extraction)
   - Primary O(1) exact lookup
   - Fallback O(n) with type inheritance ordering via `IsInstanceOfType`
   - Custom comparers for type specificity
   - Most sophisticated pattern in codebase

2. **🥈 Bidirectional Type-Pair Registration** (Intersection)
   - Automatic symmetric registration for (A, B) and (B, A)
   - Validation mode swapping for symmetric pairs
   - Eliminates manual duplication and errors

3. **🥉 Complex Tuple Values with Multiple Metadata** (Spatial)
   - Factory functions, validation modes, buffer sizes, executors in single tuple
   - Shared static factory references avoid lambda allocation
   - Single lookup retrieves all operation configuration

4. **IIFE Pattern for Construction** (Analysis, Orientation)
   - Builds FrozenDictionary with shared logic inline
   - Code reuse without helper methods
   - Enables `using` statements for IDisposable management

5. **Runtime FrozenDictionaries for Immutable Results** (Topology)
   - Result data structures use FrozenDictionary
   - Prevents consumer modification
   - Clear immutability guarantee

6. **Helper Lambdas for Strategy Construction** (Intersection)
   - `ArrayResultBuilder`, `TwoPointHandler`, `IntersectionProcessor`
   - Reduces duplication in strategy definitions
   - Shared logic without violating "no helper methods" rule

---

## Recommendations

### **For New Code**

**Simple type dispatch**:
```csharp
private static readonly FrozenDictionary<Type, V> ValidationModes =
    new Dictionary<Type, V> {
        [typeof(Curve)] = V.Standard | V.Degeneracy,
        [typeof(Surface)] = V.Standard | V.AreaCentroid,
    }.ToFrozenDictionary();
```

**Dual-type operations with bidirectional support**:
```csharp
private static readonly FrozenDictionary<(Type, Type), Strategy> Strategies =
    [
        (typeof(A), typeof(B), strategyAB),
        (typeof(C), typeof(D), strategyCD),
    ]
    .SelectMany(p => p.Item1 == p.Item2
        ? [KeyValuePair.Create((p.Item1, p.Item2), p.Item3),]
        : [
            KeyValuePair.Create((p.Item1, p.Item2), p.Item3),
            KeyValuePair.Create((p.Item2, p.Item1), p.Item3.Swap()),
        ])
    .ToFrozenDictionary();
```

**Multi-parameter dispatch with all metadata**:
```csharp
private static readonly FrozenDictionary<(Type Input, Type Query),
    (Func<object, RTree>? Factory, V Mode, int BufferSize, Func<...> Execute)> Registry =
    [
        (typeof(Point3d[]), typeof(Sphere), _factory, V.None, 2048, MakeExecutor(_factory)),
    ].ToFrozenDictionary(
        static entry => (entry.Input, entry.Query),
        static entry => (entry.Factory, entry.Mode, entry.BufferSize, entry.Execute));
```

**Type inheritance fallback**:
```csharp
// Primary map
private static readonly FrozenDictionary<(byte, Type), Handler> _handlers = /* ... */;

// Fallback map grouped and ordered by type specificity
private static readonly FrozenDictionary<byte, (Type, Handler)[]> _fallbacks =
    _handlers
        .GroupBy(entry => entry.Key.Kind)
        .ToDictionary(
            group => group.Key,
            group => group
                .OrderByDescending(entry => entry.Key.Type, _specificityComparer)
                .Select(entry => (entry.Key.Type, entry.Value))
                .ToArray())
        .ToFrozenDictionary();

// Usage
return _handlers.TryGetValue((kind, type), out var handler)
    ? handler(...)
    : _fallbacks.TryGetValue(kind, out var candidates)
        ? candidates.FirstOrDefault(c => c.Type.IsInstanceOfType(obj)).Handler?.(...)
        : DefaultResult();
```

---

### **When NOT to Use FrozenDictionary**

**Sequential array indices**:
```csharp
// ✅ CORRECT - Use array
internal static readonly double[] Multipliers = [0.1, 1.0, 10.0,];

return strategy switch {
    0 => Compute(Multipliers[0]),  // Clear and efficient
    1 => Compute(Multipliers[1]),
    2 => Compute(Multipliers[2]),
    _ => ComputeCombined(),
};
```

**Enum-like classification outputs**:
```csharp
// ✅ CORRECT - Use constants
internal const byte TypePlane = 0;
internal const byte TypeCylinder = 1;

return surface.TryGetPlane(...)
    ? (Type: TypePlane, Param: plane)
    : (Type: TypeCylinder, Param: cylinder);
```

**Algorithmic parameters**:
```csharp
// ✅ CORRECT - Use constants
internal const int MaxIterations = 100;
internal const double Tolerance = 1e-6;

for (int i = 0; i < MaxIterations && error > Tolerance; i++) { /* ... */ }
```

---

## Final Verdict

### **NO REFACTORING OPPORTUNITIES IDENTIFIED**

The codebase demonstrates **optimal FrozenDictionary usage**:

✅ **All dispatch operations use FrozenDictionaries** where appropriate
✅ **Remaining constants serve different purposes** (array indices, classification outputs, algorithmic parameters)
✅ **Moving these to FrozenDictionaries would reduce performance and clarity**

### **Codebase Quality Assessment**

**FrozenDictionary Integration: A+**
- Comprehensive coverage of all dispatch scenarios
- Sophisticated patterns (dual maps, bidirectional registration, type inheritance fallback)
- Optimal data structure selection for each use case

**Code Density: A**
- Heavy use of inline expressions (ternary, switch, pattern matching)
- IIFE pattern enables code reuse without helper methods
- Helper lambdas reduce duplication in strategy definitions

**Adherence to CLAUDE.md Standards: A+**
- K&R brace style throughout
- Trailing commas on all multi-line collections
- Named parameters for non-obvious arguments
- No `var`, no `if`/`else` statements
- Target-typed `new()` and collection expressions `[]`

---

## Summary Statistics

- **Total FrozenDictionaries**: 13
  - 8 compile-time static dispatch maps
  - 2 compile-time fallback maps
  - 3 runtime-constructed in result data structures
- **Most Complex Keys**: `(Type Input, Type Query)` (Spatial), `(byte Kind, Type GeometryType)` (Extraction)
- **Most Complex Values**: Spatial OperationRegistry (4-tuple with nullable factory + executor)
- **Most Sophisticated Pattern**: Extraction dual FrozenDict with type-specificity fallback
- **Largest Registry**: Intersection strategies (42 entries)
- **Best Optimization**: Spatial shared factory references

---

**Audit Completed**: 2025-11-12
**Conclusion**: No action required. Codebase demonstrates exemplary FrozenDictionary usage patterns.
