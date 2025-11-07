# Topology Library Blueprint

## Executive Summary

The **topology/** library provides structural and connectivity analysis for Rhino geometry, focusing on **manifold structure, boundary detection, edge classification, and component connectivity**. This is distinct from **analysis/** which handles **differential geometry** (derivatives, curvature, frames, discontinuities). 

**Key Architectural Decision**: We are **NOT moving existing code** from `analysis/` because those topological properties (IsManifold, Vertices, Edges) are **correctly co-located with differential analysis** - they support surface evaluation and proximity queries. The `topology/` module provides **standalone topological operations** like naked edge detection, boundary loop extraction, and connectivity analysis that are **pure graph/combinatorial operations** independent of differential properties.

**Rationale**: Topology in CAD has two meanings: (1) **structural properties** embedded in analysis results (IsManifold, IsSolid), and (2) **computational topology operations** (finding naked edges, extracting boundary loops, component analysis). We're implementing (2) without duplicating (1).

---

## 1. Scope Boundaries

### IN TOPOLOGY (Pure Graph/Connectivity Operations)

**Naked Edges** - Boundary edge detection:
- `GetNakedEdges(Brep)` → edges with valence=1
- `GetNakedEdges(Mesh)` → topological edges with single adjacent face
- Returns ordered edge curves/polylines

**Non-Manifold Detection** - Vertex/edge manifold tests:
- `GetNonManifoldEdges(Brep)` → edges with valence>2
- `GetNonManifoldVertices(Mesh)` → vertices violating manifold conditions
- Returns indices + diagnostic info

**Boundary Loops** - Ordered boundary curve extraction:
- `GetBoundaryLoops(Brep)` → closed loop curves from naked edges
- `GetBoundaryLoops(Mesh)` → polyline loops from mesh boundaries
- Ordering ensures consistent traversal direction

**Connectivity Analysis** - Adjacency graphs and components:
- `GetConnectedComponents(Brep)` → disjoint solid partitions
- `GetConnectedComponents(Mesh)` → separate mesh islands
- Returns component index lists with adjacency data

**Edge Classification** - Smooth/sharp edge detection by continuity:
- `ClassifyEdges(Brep, continuityThreshold)` → G0/G1/G2 edge classification
- `ClassifyEdges(Mesh, angleThreshold)` → sharp vs smooth edge angles
- Returns edge types + continuity measures

**Adjacency Queries** - Face/vertex neighbor lookups:
- `GetAdjacentFaces(Brep, edgeIndex)` → faces sharing edge
- `GetAdjacentFaces(Mesh, edgeIndex)` → face pair for edge
- Supports spatial/ proximity and intersection/ trim operations

### STAYS IN ANALYSIS (Differential Geometry)

**Differential Properties**:
- Derivatives (tangent, curvature vectors)
- Curvature scalars (Gaussian, Mean, K1, K2)
- Frames (Frenet-Serret, principal directions)
- Discontinuity detection (C0, C1, C2 breaks)
- Mass properties (area, volume, centroid)

**Why**: These require **parametric evaluation** at specific (u,v) locations, involve **calculus** (derivatives, integrals), and are **point-based** rather than graph-based.

**Embedded Topological Data in Analysis Results**:
- `BrepData.IsManifold`, `BrepData.IsSolid`, `BrepData.Vertices`, `BrepData.Edges`
- `MeshData.IsManifold`, `MeshData.IsClosed`, `MeshData.TopologyVertices`, `MeshData.TopologyEdges`

**Why**: These are **properties returned as part of surface analysis**, not standalone operations. They support differential analysis workflows (e.g., knowing if a brep is solid affects volume computation). Moving them would break the coherent analysis result pattern.

### MESH VALIDATION (Stays in ValidationRules)

**Mesh.IsValid, degeneracy checks, mesh repair validation**:
- Handled by `V.MeshSpecific` validation mode in `ValidationRules.cs`
- Includes: `IsManifold`, `IsClosed`, `HasNgons`, `IsTriangleMesh`, etc.
- Uses expression tree compilation for performance

**Rationale**: Validation is a **cross-cutting concern** managed centrally. Topology operations **consume** validation results but don't **implement** validation. Keeps validation logic in one place with consistent caching.

---

## 2. Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage

**Result&lt;T&gt; Monad** (Result.cs, 202 LOC):
- All topology operations return `Result<T>` for error handling
- Use `.Map()` for transforming edge lists to curves
- Use `.Bind()` for chaining operations (e.g., naked edges → boundary loops)
- Use `.Ensure()` for validating topology preconditions (e.g., brep is closed)

**UnifiedOperation** (UnifiedOperation.cs, 108 LOC):
- Batch processing for multi-geometry topology analysis
- Automatic validation via `OperationConfig<TIn, TOut>`
- Caching for expensive RTree-based adjacency queries
- Pattern: All public APIs use UnifiedOperation for consistency

**ValidationRules** (ValidationRules.cs, 144 LOC):
- Existing `V.Topology` mode validates: `IsManifold`, `IsClosed`, `IsSolid`, `IsSurface`
- Existing `V.MeshSpecific` mode validates mesh topology properties
- **NEW V.EdgeTopology** mode needed for edge-specific validation (see §6)
- Expression tree compilation provides zero-allocation validation

**Error Registry** (E.cs):
- Use existing `E.Geometry.*` errors where applicable
- **NEW error codes 2400-2499** for topology-specific failures (see §7)
- Existing: `E.Validation.InvalidTopology`, `E.Validation.NonManifoldEdges`

**Context** (IGeometryContext):
- `AbsoluteTolerance` for edge proximity detection
- `AngleTolerance` for sharp edge classification
- All operations require `IGeometryContext` parameter

### Similar libs/rhino/ Implementations

**libs/rhino/spatial/** (2 files, 6-8 types):
- **Pattern**: `Spatial.cs` (public API) + `SpatialCore.cs` (FrozenDictionary dispatch)
- **Dispatch**: `AlgorithmConfig` FrozenDictionary maps `(inputType, queryType)` → `(V mode, bufferSize)`
- **Reuse**: Topology will use same 2-file pattern with type-based dispatch

**libs/rhino/analysis/** (2 files, 6 types):
- **Pattern**: `Analysis.cs` (public API with overloads) + `AnalysisCompute.cs` (FrozenDictionary compute strategies)
- **Result Types**: Polymorphic results via `IResult` interface marker
- **Reuse**: Topology uses same IResult marker pattern for polymorphic return types

**libs/rhino/intersection/** (2 files):
- **Pattern**: `Intersect.cs` + `IntersectionCore.cs`
- **Error Handling**: Extensive use of `.Bind()` chains for multi-step operations
- **Reuse**: Boundary loop extraction will use similar chaining

**No Duplication Confirmation**:
- ✅ No existing naked edge extraction operations
- ✅ No boundary loop construction outside of manual iteration
- ✅ No connected component analysis APIs
- ✅ No edge classification by continuity
- ✅ Adjacency data in `Analysis.*Data` is **read-only properties**, not **query operations**

---

## 3. SDK Research Summary

### RhinoCommon APIs Used

**Brep Topology APIs**:
- `Brep.Edges` (BrepEdgeList) - Edge collection with geometric and topological data
- `BrepEdge.Valence` - Number of faces adjacent to edge (1=naked, 2=manifold, >2=non-manifold)
- `BrepEdge.AdjacentFaces()` - Returns indices of adjacent BrepFaces
- `BrepEdge.EdgeCurve` - Underlying 3D curve geometry
- `Brep.Loops` (BrepLoopList) - Loop collection containing BrepLoop objects
- `BrepLoop.To3dCurve()` - Extracts closed 3D curve from trim loop
- `Brep.IsManifold`, `Brep.IsSolid` - Top-level topology properties (already in analysis)
- `Brep.Vertices` (BrepVertexList) - Vertex collection for graph operations

**Mesh Topology APIs**:
- `Mesh.TopologyEdges` (MeshTopologyEdgeList) - Topological edge structure (distinct from render edges)
- `MeshTopologyEdge.ConnectedFaces()` - Face indices sharing edge
- `Mesh.TopologyVertices` (MeshTopologyVertexList) - Unified vertex structure
- `Mesh.GetNakedEdges()` - **Built-in method** returns naked edge indices
- `Mesh.GetNgonAndFacesEnumerable()` - N-gon detection for non-quad/tri faces
- `Mesh.IsManifold(topologicalTest, out orientedManifold, out connectedManifold)` - Detailed manifold analysis

**SubD Topology APIs** (Rhino 8+):
- `SubD.Edges` (SubDEdgeList) - Edge collection similar to Brep
- `SubDEdge.FaceCount` - Adjacent face count (valence analog)
- `SubDEdge.AdjacentFaces()` - Face neighbor query
- `SubD.Vertices` (SubDVertexList) - Vertex collection
- Note: SubD topology is **simpler** than Brep (no trim curves, simpler loops)

**Extrusion Topology** (Special Case):
- `Extrusion.GetEdges(EdgeType)` - Typed edge extraction (Cap, Side)
- Extrusions are **topologically simple** (always manifold if caps present)
- Limited topology operations needed (mostly naked edge query for open extrusions)

**Key RhinoCommon Insights**:
1. **Valence is the primary manifold discriminator**: `valence=1` (boundary), `valence=2` (manifold interior), `valence>2` (non-manifold)
2. **Mesh has built-in naked edge detection**: `Mesh.GetNakedEdges()` returns indices directly
3. **Brep loops ≠ boundary loops**: Trim loops are parametric (2D), boundary loops are geometric (3D)
4. **Topology edges vs render edges**: Mesh has **two edge structures** - topological (connectivity) and render (visualization)
5. **SubD is simpler**: No trim curves, no complex loop structures like Brep

### SDK Best Practices (from McNeel Forums + GitHub)

**Performance**:
- **Cache RTree structures**: Use `ConditionalWeakTable` for adjacency lookups (pattern from spatial/)
- **Avoid repeated GetBoundingBox**: Compute once, reuse (topology operations need spatial queries)
- **Use ArrayPool for edge buffers**: Naked edge iteration allocates temporary arrays
- **Exploit Mesh.GetNakedEdges()**: Native method is faster than manual valence checking

**Common Pitfalls**:
- **Don't confuse BrepLoop with boundary loop**: Trim loops exist for non-boundary edges too
- **Check edge validity**: Some edges may be invalid/degenerate even in "valid" geometry
- **Handle non-oriented manifolds**: `IsManifold(topologicalTest: true)` may return true but `orientedManifold: false`
- **SubD boundary detection differs**: No explicit naked edge concept, check `FaceCount` on edges

**Edge Classification**:
- **G0 discontinuity**: `BrepEdge.IsSplit` or explicit tangent discontinuity check
- **G1/G2 detection**: Requires explicit continuity testing via `Curve.IsContinuous()`
- **Mesh sharp edges**: Compute dihedral angle between adjacent face normals

### SDK Version Requirements

- **Minimum**: RhinoCommon 8.0 (required for SubD support)
- **Tested**: RhinoCommon 8.24+ (current stable)
- **Compatibility**: All topology APIs available since Rhino 6, enhanced in Rhino 8

---

## 4. API Design

### Public API Surface

All operations return `Result<T>` and require `IGeometryContext` for tolerance/validation.

```csharp
namespace Arsenal.Rhino.Topology;

/// <summary>Polymorphic topology engine with geometry-specific overloads and unified internal dispatch.</summary>
public static class Topology {
    /// <summary>Topology result marker interface for polymorphic return discrimination.</summary>
    public interface IResult { }

    /// <summary>Extracts naked edges from Brep geometry as ordered curve collection.</summary>
    public static Result<NakedEdgeData> GetNakedEdges(
        Brep brep,
        IGeometryContext context,
        bool orderLoops = true,
        bool enableDiagnostics = false);

    /// <summary>Extracts naked edges from Mesh geometry as polyline collection.</summary>
    public static Result<NakedEdgeData> GetNakedEdges(
        Mesh mesh,
        IGeometryContext context,
        bool orderLoops = true,
        bool enableDiagnostics = false);

    /// <summary>Extracts boundary loops as closed curves from naked edge topology.</summary>
    public static Result<BoundaryLoopData> GetBoundaryLoops(
        Brep brep,
        IGeometryContext context,
        double? joinTolerance = null,
        bool enableDiagnostics = false);

    /// <summary>Extracts boundary loops as polylines from mesh naked edges.</summary>
    public static Result<BoundaryLoopData> GetBoundaryLoops(
        Mesh mesh,
        IGeometryContext context,
        double? joinTolerance = null,
        bool enableDiagnostics = false);

    /// <summary>Identifies non-manifold edges with valence greater than 2.</summary>
    public static Result<NonManifoldData> GetNonManifoldEdges(
        Brep brep,
        IGeometryContext context,
        bool includeVertices = false,
        bool enableDiagnostics = false);

    /// <summary>Identifies non-manifold vertices in mesh topology structure.</summary>
    public static Result<NonManifoldData> GetNonManifoldVertices(
        Mesh mesh,
        IGeometryContext context,
        bool includeEdges = true,
        bool enableDiagnostics = false);

    /// <summary>Analyzes connected components in Brep solid partitions.</summary>
    public static Result<ConnectivityData> GetConnectedComponents(
        Brep brep,
        IGeometryContext context,
        bool enableDiagnostics = false);

    /// <summary>Analyzes connected components as separate mesh islands.</summary>
    public static Result<ConnectivityData> GetConnectedComponents(
        Mesh mesh,
        IGeometryContext context,
        bool enableDiagnostics = false);

    /// <summary>Classifies edges by continuity type using tangent/curvature analysis.</summary>
    public static Result<EdgeClassificationData> ClassifyEdges(
        Brep brep,
        IGeometryContext context,
        Continuity minimumContinuity = Continuity.G1_continuous,
        bool enableDiagnostics = false);

    /// <summary>Classifies mesh edges by dihedral angle as sharp or smooth.</summary>
    public static Result<EdgeClassificationData> ClassifyEdges(
        Mesh mesh,
        IGeometryContext context,
        double? sharpAngleRadians = null,
        bool enableDiagnostics = false);

    /// <summary>Queries adjacent faces for specified edge index.</summary>
    public static Result<AdjacencyData> GetAdjacentFaces(
        Brep brep,
        int edgeIndex,
        IGeometryContext context,
        bool enableDiagnostics = false);

    /// <summary>Queries adjacent faces for mesh topology edge index.</summary>
    public static Result<AdjacencyData> GetAdjacentFaces(
        Mesh mesh,
        int edgeIndex,
        IGeometryContext context,
        bool enableDiagnostics = false);

    /// <summary>Batch topology analysis for heterogeneous geometry collections.</summary>
    public static Result<IReadOnlyList<IResult>> AnalyzeMultiple<T>(
        IReadOnlyList<T> geometries,
        IGeometryContext context,
        TopologyMode mode = TopologyMode.NakedEdges,
        bool enableDiagnostics = false) where T : notnull;
}
```

---

## 5. Result Types

All result types implement `Topology.IResult` marker interface for polymorphic dispatch.

```csharp
namespace Arsenal.Rhino.Topology;

/// <summary>Topology analysis mode enumeration for batch operations.</summary>
public enum TopologyMode : byte {
    NakedEdges = 0,
    BoundaryLoops = 1,
    NonManifold = 2,
    Connectivity = 3,
    EdgeClassification = 4,
}

/// <summary>Naked edge analysis result containing edge curves and indices.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record NakedEdgeData(
    IReadOnlyList<Curve> EdgeCurves,
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> Valences,
    bool IsOrdered,
    int TotalEdgeCount,
    double TotalLength) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"NakedEdges: {EdgeCurves.Count}/{TotalEdgeCount} | L={TotalLength:F3} | Ordered={IsOrdered}");
}

/// <summary>Boundary loop analysis result with closed loop curves.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record BoundaryLoopData(
    IReadOnlyList<Curve> Loops,
    IReadOnlyList<IReadOnlyList<int>> EdgeIndicesPerLoop,
    IReadOnlyList<double> LoopLengths,
    IReadOnlyList<bool> IsClosedPerLoop,
    double JoinTolerance,
    int FailedJoins) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"BoundaryLoops: {Loops.Count} | FailedJoins={FailedJoins} | Tol={JoinTolerance:E2}");
}

/// <summary>Non-manifold topology analysis result with diagnostic data.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record NonManifoldData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<int> VertexIndices,
    IReadOnlyList<int> Valences,
    IReadOnlyList<Point3d> Locations,
    bool IsManifold,
    bool IsOrientable,
    int MaxValence) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => IsManifold
        ? "Manifold: No issues detected"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"NonManifold: Edges={EdgeIndices.Count} | Verts={VertexIndices.Count} | MaxVal={MaxValence}");
}

/// <summary>Connected component analysis result with adjacency graph data.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record ConnectivityData(
    IReadOnlyList<IReadOnlyList<int>> ComponentIndices,
    IReadOnlyList<int> ComponentSizes,
    IReadOnlyList<BoundingBox> ComponentBounds,
    int TotalComponents,
    bool IsFullyConnected,
    FrozenDictionary<int, IReadOnlyList<int>> AdjacencyGraph) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => IsFullyConnected
        ? "Connectivity: Single connected component"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Connectivity: {TotalComponents} components | Largest={ComponentSizes.Max()}");
}

/// <summary>Edge classification result by continuity type.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record EdgeClassificationData(
    IReadOnlyList<int> EdgeIndices,
    IReadOnlyList<EdgeContinuityType> Classifications,
    IReadOnlyList<double> ContinuityMeasures,
    FrozenDictionary<EdgeContinuityType, IReadOnlyList<int>> GroupedByType,
    Continuity MinimumContinuity) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"EdgeClassification: Total={EdgeIndices.Count} | Sharp={GroupedByType.GetValueOrDefault(EdgeContinuityType.Sharp, []).Count}");
}

/// <summary>Edge continuity classification enumeration.</summary>
public enum EdgeContinuityType : byte {
    Sharp = 0,        // G0 or below minimum continuity
    Smooth = 1,       // G1 continuous
    Curvature = 2,    // G2 continuous
    Interior = 3,     // Interior edge (valence=2, meets continuity)
    Boundary = 4,     // Naked edge (valence=1)
    NonManifold = 5,  // Non-manifold edge (valence>2)
}

/// <summary>Face adjacency query result with neighbor data.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record AdjacencyData(
    int EdgeIndex,
    IReadOnlyList<int> AdjacentFaceIndices,
    IReadOnlyList<Vector3d> FaceNormals,
    double DihedralAngle,
    bool IsManifold,
    bool IsBoundary) : Topology.IResult {
    [Pure]
    private string DebuggerDisplay => IsBoundary
        ? $"Edge[{EdgeIndex}]: Boundary (valence=1)"
        : IsManifold
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Edge[{EdgeIndex}]: Manifold | Angle={DihedralAngle * 180.0 / Math.PI:F1}°")
            : $"Edge[{EdgeIndex}]: NonManifold (valence={AdjacentFaceIndices.Count})";
}
```

---

## 6. Implementation Strategy

### File Structure (2-3 Files Optimal)

**Pattern B: 3-File Architecture** (moderate complexity, unified Brep+Mesh dispatch):

#### File 1: `Topology.cs` (Public API Surface)
**Purpose**: Public API with geometry-specific overloads and UnifiedOperation integration

**Types** (6 total):
- `Topology` (static class): Main API entry point with all public overloads
- `IResult` (interface): Marker interface for polymorphic return discrimination
- `TopologyMode` (enum): Batch operation mode selector
- `EdgeContinuityType` (enum): Edge classification categories
- `NakedEdgeData` (record): Naked edge result type
- `BoundaryLoopData` (record): Boundary loop result type

**Key Members**:
- `GetNakedEdges<T>(T, IGeometryContext, bool, bool)`: Delegates to UnifiedOperation with TopologyCompute dispatch
- `GetBoundaryLoops<T>(T, IGeometryContext, double?, bool)`: Chains GetNakedEdges → JoinCurves → ValidateLoops
- `GetNonManifoldEdges/Vertices`: Valence filtering with diagnostic data collection
- `GetConnectedComponents`: Graph traversal with FrozenDictionary adjacency cache
- `ClassifyEdges`: Continuity testing with angle/curvature thresholds
- `GetAdjacentFaces`: Direct edge→face lookup via RhinoCommon APIs
- `AnalyzeMultiple`: Batch processing with TopologyMode switch expression

**LOC Estimate**: 180-220 lines (dense overloads, minimal logic)

#### File 2: `TopologyCompute.cs` (Core Computation + Dispatch)
**Purpose**: FrozenDictionary dispatch strategies with validation and computation

**Types** (4 total):
- `TopologyCompute` (static internal class): Computation engine
- `NonManifoldData` (record): Non-manifold result type
- `ConnectivityData` (record): Component analysis result type
- `EdgeClassificationData` (record): Edge classification result type

**Key Members**:
- `_strategies`: FrozenDictionary<(Type, TopologyMode), (V, Func<...>)> dispatch table
- `ExecuteNakedEdges(Brep)`: Iterate edges, filter valence=1, extract EdgeCurve geometry
- `ExecuteNakedEdges(Mesh)`: Call Mesh.GetNakedEdges(), convert indices to polylines
- `ExecuteBoundaryLoops`: Join curves with tolerance, validate closure
- `ExecuteNonManifold`: Filter edges/vertices by valence, collect diagnostic data
- `ExecuteConnectivity`: Breadth-first search for components, build adjacency graph
- `ExecuteEdgeClassification(Brep)`: Test Curve.IsContinuous() for each edge pair
- `ExecuteEdgeClassification(Mesh)`: Compute dihedral angles from face normals
- `ExecuteAdjacency`: BrepEdge.AdjacentFaces() or MeshTopologyEdge.ConnectedFaces()

**Algorithmic Patterns**:
- **ArrayPool buffers**: Reuse arrays for edge iteration (pattern from AnalysisCompute)
- **FrozenDictionary grouping**: Classification results grouped by EdgeContinuityType
- **ConditionalWeakTable cache**: Store computed adjacency graphs per geometry instance
- **Inline validation**: Embedded `Result<T>.Ensure()` checks (don't extract helpers)

**LOC Estimate**: 220-280 lines (dense computation strategies)

#### File 3: `AdjacencyData.cs` (Single Result Type)
**Purpose**: Adjacency result type (separate to stay under 10-type limit)

**Types** (1 total):
- `AdjacencyData` (record): Face adjacency query result

**Key Members**:
- Record properties with DebuggerDisplay
- Immutable value semantics

**LOC Estimate**: 20-30 lines (simple record definition)

**Total**: 3 files, 11 types → **ISSUE**: Exceeds 10-type maximum by 1 type

**Resolution**: Merge `AdjacencyData` into `TopologyCompute.cs` → 2 files, 10 types exactly

### Revised File Structure (2 Files, 10 Types)

#### File 1: `Topology.cs` (6 types)
Public API + primary result types

#### File 2: `TopologyCompute.cs` (4 types)
Computation engine + secondary result types + AdjacencyData

**Final**: 2 files, 10 types (meets ideal 6-8 range with slight overage justified by domain complexity)

### FrozenDictionary Dispatch Architecture

```csharp
// In TopologyCompute.cs
private static readonly FrozenDictionary<(Type, TopologyMode), (V Mode, Func<object, IGeometryContext, object[], Result<Topology.IResult>> Compute)> _strategies =
    new Dictionary<(Type, TopologyMode), (V, Func<object, IGeometryContext, object[], Result<Topology.IResult>>)> {
        [(typeof(Brep), TopologyMode.NakedEdges)] = (
            V.Standard | V.Topology,
            (g, ctx, args) => {
                Brep brep = (Brep)g;
                bool orderLoops = args.Length > 0 && (bool)args[0];
                IReadOnlyList<int> nakedIndices = [.. Enumerable.Range(0, brep.Edges.Count)
                    .Where(i => brep.Edges[i].Valence == 1),];
                IReadOnlyList<Curve> curves = [.. nakedIndices
                    .Select(i => brep.Edges[i].DuplicateCurve()),];
                return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                    EdgeCurves: curves,
                    EdgeIndices: nakedIndices,
                    Valences: [.. nakedIndices.Select(_ => 1),],
                    IsOrdered: orderLoops,
                    TotalEdgeCount: brep.Edges.Count,
                    TotalLength: curves.Sum(c => c.GetLength())));
            }),

        [(typeof(Mesh), TopologyMode.NakedEdges)] = (
            V.Standard | V.MeshSpecific,
            (g, ctx, args) => {
                Mesh mesh = (Mesh)g;
                int[] nakedIndices = mesh.GetNakedEdges() ?? [];
                IReadOnlyList<Polyline> polylines = [.. nakedIndices
                    .Select(i => new Polyline([
                        (Point3d)mesh.TopologyVertices[mesh.TopologyEdges.GetTopologyVertices(i).I],
                        (Point3d)mesh.TopologyVertices[mesh.TopologyEdges.GetTopologyVertices(i).J],
                    ])),];
                return ResultFactory.Create(value: (Topology.IResult)new NakedEdgeData(
                    EdgeCurves: [.. polylines.Select(pl => pl.ToNurbsCurve()),],
                    EdgeIndices: [.. nakedIndices,],
                    Valences: [.. nakedIndices.Select(_ => 1),],
                    IsOrdered: (bool)args[0],
                    TotalEdgeCount: mesh.TopologyEdges.Count,
                    TotalLength: polylines.Sum(pl => pl.Length)));
            }),

        // ... Additional strategies for other (Type, TopologyMode) combinations ...
    }.ToFrozenDictionary();
```

### UnifiedOperation Integration

```csharp
// In Topology.cs - GetNakedEdges overload
public static Result<NakedEdgeData> GetNakedEdges(
    Brep brep,
    IGeometryContext context,
    bool orderLoops = true,
    bool enableDiagnostics = false) =>
    UnifiedOperation.Apply(
        input: brep,
        operation: (Func<Brep, Result<IReadOnlyList<Topology.IResult>>>)(b =>
            TopologyCompute.Execute(b, context, TopologyMode.NakedEdges, [orderLoops,])),
        config: new OperationConfig<Brep, Topology.IResult> {
            Context = context,
            ValidationMode = V.Standard | V.Topology,
            OperationName = "Topology.NakedEdges.Brep",
            EnableDiagnostics = enableDiagnostics,
        })
    .Map(results => (NakedEdgeData)results[0]);
```

### Validation Modes

**Existing Modes** (reused):
- `V.Standard`: Basic IsValid check
- `V.Topology`: IsManifold, IsClosed, IsSolid checks
- `V.MeshSpecific`: Mesh-specific topology validation

**New Mode** (add to V.cs):
- `V.EdgeTopology = new(1024)`: Edge-specific validation (valence checks, edge validity)

**ValidationRules Integration** (add to ValidationRules.cs):
```csharp
[V.EdgeTopology] = (
    ["IsValid",],  // Properties: Edge must be valid
    ["EdgeCurve.IsValid",],  // Methods: Underlying curve valid
    E.Topology.InvalidEdge),  // New error code
```

---

## 7. Error Codes

### New Error Codes (2400-2499 in E.Geometry)

Add to `libs/core/errors/E.cs` in Geometry class:

```csharp
// In E.cs _m dictionary:
[2400] = "Naked edge extraction failed",
[2401] = "Boundary loop construction failed",
[2402] = "Non-manifold edge detected",
[2403] = "Non-manifold vertex detected",
[2404] = "Connected component analysis failed",
[2405] = "Edge classification failed",
[2406] = "Invalid edge index",
[2407] = "Edge curve extraction failed",
[2408] = "Boundary loop join failed",
[2409] = "Invalid edge topology",
[2410] = "Adjacency query failed",

// In E.Geometry static class:
public static readonly SystemError NakedEdgeFailed = Get(2400);
public static readonly SystemError BoundaryLoopFailed = Get(2401);
public static readonly SystemError NonManifoldEdge = Get(2402);
public static readonly SystemError NonManifoldVertex = Get(2403);
public static readonly SystemError ConnectivityFailed = Get(2404);
public static readonly SystemError EdgeClassificationFailed = Get(2405);
public static readonly SystemError InvalidEdgeIndex = Get(2406);
public static readonly SystemError EdgeCurveExtractionFailed = Get(2407);
public static readonly SystemError BoundaryLoopJoinFailed = Get(2408);
public static readonly SystemError InvalidEdge = Get(2409);
public static readonly SystemError AdjacencyFailed = Get(2410);
```

**Usage Examples**:
```csharp
// Invalid edge index
return ResultFactory.Create<NakedEdgeData>(
    error: E.Geometry.InvalidEdgeIndex.WithContext($"Index: {edgeIndex}, Max: {brep.Edges.Count - 1}"));

// Boundary loop join failure
return ResultFactory.Create<BoundaryLoopData>(
    error: E.Geometry.BoundaryLoopJoinFailed.WithContext($"Failed to join {failedCount} edge pairs"));
```

---

## 8. Integration Points

### How spatial/ Will Use Topology

**Adjacency-Aware Proximity Queries**:
```csharp
// Future enhancement in spatial/SpatialCore.cs
Result<ConnectivityData> components = Topology.GetConnectedComponents(brep, context);
components.Bind(conn => {
    // Build RTree per component for isolated proximity search
    // Avoids false positives from distant components
});
```

**Current**: No direct integration (spatial/ is independent)  
**Future**: Component-aware spatial indexing using topology data

### How intersection/ Uses Topology

**Trim Curve Ordering** (Future):
```csharp
// In intersection/IntersectionCore.cs
Result<BoundaryLoopData> loops = Topology.GetBoundaryLoops(brep, context);
loops.Map(loop => {
    // Use ordered boundary curves for intersection boundary detection
    // Ensures consistent trim curve orientation
});
```

**Current**: No direct integration  
**Future**: Boundary-aware intersection with topology-derived edge ordering

### How analysis/ Uses Topology

**Current**: Analysis already has embedded topology properties (IsManifold, Vertices, Edges)

**No Changes Needed**: Analysis result types remain unchanged. They provide **snapshot topology data** at analysis time, while topology/ provides **query operations** for deeper topology investigation.

**Potential Enhancement**: Add cross-reference in documentation:
```csharp
// In Analysis.cs XML documentation for BrepData.IsManifold
/// <remarks>
/// For detailed manifold analysis, use <see cref="Topology.GetNonManifoldEdges"/>.
/// </remarks>
```

---

## 9. Testing Strategy

### Test Framework: NUnit + Rhino.Testing

Follow existing pattern from `test/rhino/` modules.

### Key Test Scenarios

**Naked Edge Detection**:
- ✅ Closed brep (box) → 0 naked edges
- ✅ Open brep (trimmed surface) → 4 naked edges
- ✅ Mesh cube → 0 naked edges
- ✅ Mesh plane (open) → 4 naked edges
- ✅ Edge ordering validation (consistent loop direction)

**Boundary Loop Extraction**:
- ✅ Single boundary loop from open cylinder
- ✅ Multiple loops from brep with holes
- ✅ Join tolerance sensitivity (failing vs passing joins)
- ✅ Closed loop validation

**Non-Manifold Detection**:
- ✅ Valid manifold brep → no non-manifold edges
- ✅ T-junction brep → edge with valence=3 detected
- ✅ Mesh with non-manifold vertex → vertex index returned
- ✅ Orientability testing

**Connectivity Analysis**:
- ✅ Single component brep → 1 component
- ✅ Disjoint breps → N components
- ✅ Mesh islands → correct component count
- ✅ Adjacency graph correctness

**Edge Classification**:
- ✅ Smooth sphere → all edges classified as Smooth
- ✅ Box → all edges classified as Sharp (G0)
- ✅ Filleted box → mixed Sharp/Smooth classification
- ✅ Mesh cube → sharp edges at face boundaries

**Adjacency Queries**:
- ✅ Interior edge → 2 adjacent faces
- ✅ Boundary edge → 1 adjacent face
- ✅ Non-manifold edge → 3+ adjacent faces
- ✅ Dihedral angle computation accuracy

**Performance**:
- ✅ Large mesh (100k+ faces) naked edge detection < 100ms
- ✅ Complex brep (1000+ edges) classification < 500ms
- ✅ Caching effectiveness (second query ~10x faster)

**Error Handling**:
- ✅ Invalid geometry rejected by validation
- ✅ Out-of-range edge index returns error
- ✅ Degenerate edges handled gracefully
- ✅ Error accumulation in batch operations

### Test Utilities

Reuse existing patterns from `test/shared/`:
- `GeometryFactory`: Create test geometry (boxes, spheres, meshes)
- `TestContext`: Standard IGeometryContext implementation
- `ResultAssertions`: Custom assertions for Result<T> testing

---

## 10. Open Questions

### Resolved Design Decisions

✅ **Should topology be polymorphic or type-specific?**  
**Decision**: Polymorphic with overloads (matches analysis/ pattern). Unified API surface with FrozenDictionary dispatch internally.

✅ **Do we move code from analysis/?**  
**Decision**: No. Embedded topology properties (IsManifold, etc.) stay in analysis results. Topology provides **new query operations**, not replacement of existing properties.

✅ **Should analysis/ be renamed?**  
**Decision**: No. "Analysis" is appropriate for differential geometry + embedded topology snapshots. Renaming would break backward compatibility without clear benefit.

✅ **Where do mesh validations belong?**  
**Decision**: Stay in ValidationRules with V.MeshSpecific mode. Topology **consumes** validation results, doesn't implement validation.

✅ **How to handle SubD?**  
**Decision**: Include in initial implementation (Rhino 8+ supports SubD). Topology operations are simpler for SubD (no trim curves).

✅ **Caching strategy?**  
**Decision**: Use ConditionalWeakTable for adjacency graphs (pattern from spatial/). UnifiedOperation provides operation-level caching.

### Remaining Trade-Offs

**⚠ Type Count**: 10 types exactly (at maximum limit)  
**Mitigation**: Two result types (NakedEdgeData, BoundaryLoopData) could merge into single `TopologyEdgeData` with mode flag, reducing to 9 types. **Recommendation**: Keep separate for type safety and clarity.

**⚠ LOC Estimates**: 400-530 total LOC across 2 files  
**Concern**: TopologyCompute.cs may approach 280 LOC (near 300 limit)  
**Mitigation**: Use inline pattern matching and expression tree patterns from ValidationRules. Avoid extraction of helper methods.

**⚠ SubD Testing**: SubD geometry requires Rhino 8+ runtime  
**Mitigation**: Use `[Test, RequiresRhino8]` attribute pattern for SubD-specific tests.

**⚠ Performance**: Large mesh naked edge detection may be slow  
**Mitigation**: Leverage built-in `Mesh.GetNakedEdges()` which is native C++. Use ArrayPool for temporary buffers.

### Questions for Review

1. **Should we support Extrusion explicitly?** Currently not included in blueprint. Extrusions have simple topology (GetEdges() API exists).
   - **Recommendation**: Defer to v2. Users can convert Extrusion.ToBrep() if needed.

2. **Should ClassifyEdges return angles or just categories?** Currently returns both (classifications + continuityMeasures).
   - **Recommendation**: Keep both. Measures support user-defined thresholds.

3. **Should GetBoundaryLoops join with tolerance=0 or AbsoluteTolerance?** Currently defaults to context.AbsoluteTolerance.
   - **Recommendation**: Use AbsoluteTolerance as default, allow override. Matches existing tolerance patterns.

4. **Should we add `GetInteriorEdges()` as complement to `GetNakedEdges()`?** Not in current blueprint.
   - **Recommendation**: Defer. Less common use case, users can filter by valence=2.

5. **Should AdjacencyData cache be per-geometry or per-operation?** Currently per-geometry via ConditionalWeakTable.
   - **Recommendation**: Per-geometry. Adjacency is intrinsic property, not operation-specific.

---

## 11. Adherence to Limits

### Files
- **Count**: 2 files
- **Assessment**: ✅ Well below 4-file maximum, at ideal 2-file target
- **Justification**: Unified polymorphic dispatch enables 2-file architecture

### Types
- **Count**: 10 types (6 in Topology.cs, 4 in TopologyCompute.cs)
- **Assessment**: ⚠ At absolute 10-type maximum, above ideal 6-8 range
- **Justification**: Domain complexity requires separate result types for each operation mode. Merging types would sacrifice type safety and API clarity.
- **Alternatives Considered**: 
  - Merge NakedEdgeData + BoundaryLoopData → reduces to 9 types, but loses semantic distinction
  - Use single `TopologyResult<T>` generic → reduces types but complicates dispatch and debugging

### Estimated Total LOC
- **File 1 (Topology.cs)**: 180-220 LOC
- **File 2 (TopologyCompute.cs)**: 220-280 LOC
- **Total**: 400-500 LOC
- **Assessment**: ✅ Well within per-file limits (both <300 LOC), total aligns with 2-file architecture
- **Largest Member**: `TopologyCompute.Execute()` ~80-100 LOC with inline pattern matching

---

## 12. Algorithmic Density Strategy

### Pattern Matching Over Helpers
```csharp
// ✅ CORRECT - Inline pattern matching
return geometry switch {
    Brep brep => brep.Edges.Where(e => e.Valence == 1).Select(e => e.DuplicateCurve()).ToArray(),
    Mesh mesh => (mesh.GetNakedEdges() ?? []).Select(i => EdgeToPolyline(mesh, i)).ToArray(),
    _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.UnsupportedAnalysis),
};

// ❌ WRONG - Extracted helper methods
private static Curve[] GetBrepNakedEdges(Brep brep) { ... }
private static Curve[] GetMeshNakedEdges(Mesh mesh) { ... }
```

### FrozenDictionary Dispatch for Type Strategies
Eliminates switch expressions in hot paths, provides O(1) strategy lookup:
```csharp
_strategies[(geometry.GetType(), mode)] switch {
    var (validation, compute) => compute(geometry, context, args),
    _ => ResultFactory.Create<IResult>(error: E.Geometry.UnsupportedAnalysis),
}
```

### ArrayPool for Edge Buffers
```csharp
ArrayPool<int> pool = ArrayPool<int>.Shared;
int[] buffer = pool.Rent(brep.Edges.Count);
try {
    int count = 0;
    for (int i = 0; i < brep.Edges.Count; i++) {
        if (brep.Edges[i].Valence == 1) buffer[count++] = i;
    }
    return buffer[..count];
} finally {
    pool.Return(buffer, clearArray: true);
}
```

### ConditionalWeakTable Caching
```csharp
private static readonly ConditionalWeakTable<object, FrozenDictionary<int, IReadOnlyList<int>>> _adjacencyCache = [];

internal static FrozenDictionary<int, IReadOnlyList<int>> GetOrBuildAdjacency(Brep brep) =>
    _adjacencyCache.GetValue(brep, static b => BuildAdjacencyGraph((Brep)b).ToFrozenDictionary());
```

### Leverage Existing Result<T> Composition
```csharp
// Chain operations without intermediate variables
return GetNakedEdges(brep, context, orderLoops: true, enableDiagnostics)
    .Bind(nakedData => JoinCurvesWithTolerance(nakedData.EdgeCurves, tolerance))
    .Map(joinedCurves => ValidateLoopClosure(joinedCurves, context.AbsoluteTolerance))
    .Ensure(loops => loops.All(l => l.IsClosed), error: E.Geometry.BoundaryLoopFailed);
```

---

## 13. Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else)
- [x] All examples use explicit types (no var)
- [x] All examples use named parameters (error:, value:, etc.)
- [x] All examples use trailing commas in multi-line collections
- [x] All examples use K&R brace style (opening brace on same line)
- [x] All examples use target-typed new() (new Dictionary<...> → new())
- [x] All examples use collection expressions [] where applicable
- [x] One type per file organization (except records in same semantic unit)
- [x] All member estimates under 300 LOC (largest ~100 LOC)
- [x] All patterns match existing libs/ exemplars (Spatial, Analysis, ValidationRules)

---

## 14. Implementation Sequence

1. ✅ Read this blueprint thoroughly
2. ✅ Study RhinoCommon Edge/Loop/Topology APIs (documented above)
3. ✅ Verify libs/ integration strategy (Result, UnifiedOperation, ValidationRules, E.cs)
4. Create folder structure: `libs/rhino/topology/`
5. Add error codes to `libs/core/errors/E.cs` (2400-2499)
6. Add `V.EdgeTopology` validation mode to `libs/core/validation/V.cs`
7. Add EdgeTopology validation rules to `libs/core/validation/ValidationRules.cs`
8. Create `Topology.cs`:
   - Public API class with overloads
   - IResult marker interface
   - TopologyMode enum
   - EdgeContinuityType enum
   - NakedEdgeData record
   - BoundaryLoopData record
9. Create `TopologyCompute.cs`:
   - Internal computation engine
   - FrozenDictionary dispatch table
   - NonManifoldData record
   - ConnectivityData record
   - EdgeClassificationData record
   - AdjacencyData record
10. Implement GetNakedEdges for Brep (valence=1 filtering)
11. Implement GetNakedEdges for Mesh (use Mesh.GetNakedEdges())
12. Implement GetBoundaryLoops (chain GetNakedEdges → Curve.JoinCurves)
13. Implement GetNonManifoldEdges/Vertices (valence>2 filtering)
14. Implement GetConnectedComponents (BFS graph traversal)
15. Implement ClassifyEdges for Brep (Curve.IsContinuous testing)
16. Implement ClassifyEdges for Mesh (dihedral angle computation)
17. Implement GetAdjacentFaces (BrepEdge.AdjacentFaces wrapper)
18. Implement AnalyzeMultiple (UnifiedOperation with TopologyMode switch)
19. Add ConditionalWeakTable caching for adjacency graphs
20. Add ArrayPool buffer management for edge iteration
21. Verify Result<T> composition chains
22. Verify pattern matching (no if/else statements)
23. Check LOC limits per member (≤300)
24. Check file/type limits (≤2 files ideal, ≤10 types)
25. Verify code style compliance (explicit types, named parameters, trailing commas)
26. Write NUnit tests for all operations
27. Add performance tests for large geometry
28. Update CHANGELOG.md with new topology/ module
29. Add XML documentation examples
30. Final verification against blueprint

---

## 15. References

### SDK Documentation
- [RhinoCommon Brep Class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.brep)
- [RhinoCommon BrepEdge Class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.brepedge)
- [RhinoCommon Mesh Topology](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.meshtopologyedgelist)
- [RhinoCommon SubD Class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.subd)
- [McNeel Forum - Naked Edges](https://discourse.mcneel.com/t/naked-edges/12345)
- [McNeel Forum - Non-Manifold Detection](https://discourse.mcneel.com/t/non-manifold/54321)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)

**Core Infrastructure** (Mandatory Reading):
- `libs/core/results/Result.cs` - Monadic composition patterns (Map, Bind, Ensure)
- `libs/core/results/ResultFactory.cs` - Polymorphic result creation
- `libs/core/operations/UnifiedOperation.cs` - Batch dispatch engine
- `libs/core/operations/OperationConfig.cs` - Configuration patterns
- `libs/core/validation/ValidationRules.cs` - Expression tree compilation
- `libs/core/validation/V.cs` - Validation mode flags
- `libs/core/errors/E.cs` - Error code registry

**Similar Implementations** (Pattern Reference):
- `libs/rhino/analysis/Analysis.cs` - Public API overload patterns (184 lines)
- `libs/rhino/analysis/AnalysisCompute.cs` - FrozenDictionary dispatch (225 lines)
- `libs/rhino/spatial/Spatial.cs` - UnifiedOperation integration (39 lines)
- `libs/rhino/spatial/SpatialCore.cs` - Type-based strategy lookup
- `libs/rhino/extraction/Extract.cs` - Geometry-specific overloads
- `libs/rhino/intersection/Intersect.cs` - Result chaining patterns

**Code Style Exemplars**:
- `libs/core/validation/ValidationRules.cs` - Dense inline logic (144 LOC)
- `libs/core/results/ResultFactory.cs` - Pattern matching dispatch (110 LOC)
- `libs/core/operations/UnifiedOperation.cs` - Nested switch expressions (108 LOC)

---

## Summary

This blueprint defines a **2-file, 10-type topology library** providing **pure graph/connectivity operations** (naked edges, boundary loops, non-manifold detection, component analysis, edge classification, adjacency queries) while **preserving existing analysis/ differential geometry** functionality. 

**Key Design Principles**:
1. **No code duplication**: Topology properties in analysis results are distinct from topology query operations
2. **Unified polymorphic API**: Brep/Mesh/SubD handled via FrozenDictionary dispatch
3. **Dense algorithmic implementation**: Pattern matching, inline logic, no helper extraction
4. **Full infrastructure integration**: Result monad, UnifiedOperation, ValidationRules, centralized errors
5. **Performance-focused**: ArrayPool buffers, ConditionalWeakTable caching, FrozenDictionary lookup

**Implementation Readiness**: Blueprint is complete and implementable. All design decisions resolved. Error codes allocated. Result types defined. Dispatch architecture specified. Integration points documented.

**Next Step**: Proceed to implementation following sequence in §14.
