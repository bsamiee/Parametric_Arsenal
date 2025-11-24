# Topology Analysis and Healing

Polymorphic topology analysis, diagnosis, and progressive healing for Brep/Mesh geometry with validation-driven dispatch.

---

## API Surface

```csharp
// Adjacency: face indices, normals, dihedral angle for edge (Brep/Mesh)
Result<AdjacencyData> GetAdjacency<T>(T geometry, IGeometryContext context, int edgeIndex)

// Naked edges: boundary curves, indices, valences, ordering (Brep/Mesh)
Result<NakedEdgeData> GetNakedEdges<T>(T geometry, IGeometryContext context, bool orderLoops = false)

// Vertex topology: connected edges/faces, valence, boundary/manifold (Brep/Mesh)
Result<VertexData> GetVertexData<T>(T geometry, IGeometryContext context, int vertexIndex)

// Boundary loops: joined naked edges with closure diagnostics (Brep/Mesh)
Result<BoundaryLoopData> GetBoundaryLoops<T>(T geometry, IGeometryContext context, double? tolerance = null)

// Connectivity: BFS components with adjacency graph (Brep/Mesh)
Result<ConnectivityData> GetConnectivity<T>(T geometry, IGeometryContext context)

// Non-manifold: edge/vertex detection with valences (Brep/Mesh)
Result<NonManifoldData> GetNonManifoldData<T>(T geometry, IGeometryContext context)

// Ngon topology: face membership, boundaries, centroids (Mesh only)
Result<NgonTopologyData> GetNgonTopology<T>(T geometry, IGeometryContext context)

// Diagnosis: edge gaps, near-misses, strategy suggestions (Brep only)
Result<TopologyDiagnosis> DiagnoseTopology(Brep brep, IGeometryContext context)

// Edge classification: G0/G1/G2 continuity with grouping (Brep/Mesh)
Result<EdgeClassificationData> ClassifyEdges<T>(
    T geometry, IGeometryContext context,
    Continuity minimumContinuity = Continuity.G1_continuous,
    double? angleThreshold = null)

// Topological features: genus, loops, solid status via Euler (Brep only)
Result<TopologicalFeatures> ExtractTopologicalFeatures(Brep brep, IGeometryContext context)

// Progressive healing: automatic rollback and strategy selection (Brep only)
Result<HealingResult> HealTopology(Brep brep, IReadOnlyList<Strategy> strategies, IGeometryContext context)
```

---

## Usage Examples

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Adjacency analysis
Result<Topology.AdjacencyData> adjacency = Topology.GetAdjacency(
    geometry: brep, context: context, edgeIndex: 42);
adjacency.Match(
    onSuccess: data => Console.WriteLine($"Angle={RhinoMath.ToDegrees(data.DihedralAngle):F1}°"),
    onFailure: error => Handle(error));

// Edge classification
Result<Topology.EdgeClassificationData> classification = Topology.ClassifyEdges(
    geometry: brep, context: context,
    minimumContinuity: Continuity.G1_continuous,
    angleThreshold: RhinoMath.ToRadians(20.0));
IReadOnlyList<int> sharpEdges = classification
    .Map(data => data.GroupedByType[Topology.EdgeContinuityType.Sharp])
    .Match(onSuccess: edges => edges, onFailure: _ => []);

// Naked edges and boundary loops
Result<Topology.BoundaryLoopData> loops = Topology.GetNakedEdges(brep, context, orderLoops: true)
    .Bind(_ => Topology.GetBoundaryLoops(brep, context, tolerance: 0.01));

// Connected components
Result<Topology.ConnectivityData> connectivity = Topology.GetConnectivity(brep, context);
connectivity.Match(
    onSuccess: data => Console.WriteLine(data.IsFullyConnected
        ? "Single component"
        : $"{data.TotalComponents} components"),
    onFailure: error => Handle(error));

// Non-manifold detection
Result<Topology.NonManifoldData> nonManifold = Topology.GetNonManifoldData(brep, context);

// Vertex topology
Result<Topology.VertexData> vertex = Topology.GetVertexData(brep, context, vertexIndex: 10);

// Ngon topology (Mesh only)
Result<Topology.NgonTopologyData> ngons = Topology.GetNgonTopology(mesh, context);

// Diagnosis and progressive healing
Result<Topology.HealingResult> healing = Topology.DiagnoseTopology(brep, context)
    .Bind(diag => Topology.HealTopology(brep, diag.SuggestedStrategies, context));

// Manual healing strategy
Topology.Strategy[] strategies = [
    new Topology.ConservativeRepairStrategy(),
    new Topology.TargetedJoinStrategy(),
];
Result<Topology.HealingResult> healed = Topology.HealTopology(brep, strategies, context);

// Topological features (genus, handles)
Result<Topology.TopologicalFeatures> features = Topology.ExtractTopologicalFeatures(brep, context);
features.Match(
    onSuccess: data => Console.WriteLine($"Genus={data.Genus}, Handles={data.HandleCount}"),
    onFailure: error => Handle(error));
```

---

## Algebraic Domain Types

**Strategy Hierarchy** (healing tolerance multipliers):
```csharp
abstract record Strategy;
sealed record ConservativeRepairStrategy() : Strategy;  // 0.1×
sealed record ModerateJoinStrategy() : Strategy;        // 1.0×
sealed record AggressiveJoinStrategy() : Strategy;      // 10.0×
sealed record CombinedStrategy() : Strategy;            // Conservative + Moderate
sealed record TargetedJoinStrategy() : Strategy;        // Near-miss pairs, 100 iter max
sealed record ComponentJoinStrategy() : Strategy;       // Component-level, 1.0×
```

**Edge Continuity**:
```csharp
readonly record struct EdgeContinuityType(byte Value);
// Sharp (G0), Smooth (G1), Curvature (G2), Interior, Boundary, NonManifold
```

**Result Types** (all `IResult`):
```csharp
TopologyDiagnosis(EdgeGaps, NearMisses, SuggestedStrategies)
HealingResult(Healed, AppliedStrategy, Success)
NakedEdgeData(EdgeCurves, EdgeIndices, Valences, IsOrdered, TotalEdgeCount, TotalLength)
TopologicalFeatures(Genus, Loops, IsSolid, HandleCount)
EdgeClassificationData(EdgeIndices, Classifications, ContinuityMeasures, GroupedByType, MinimumContinuity)
BoundaryLoopData(Loops, EdgeIndicesPerLoop, LoopLengths, IsClosedPerLoop, JoinTolerance, FailedJoins)
VertexData(VertexIndex, Location, ConnectedEdgeIndices, ConnectedFaceIndices, Valence, IsBoundary, IsManifold)
ConnectivityData(ComponentIndices, ComponentSizes, ComponentBounds, TotalComponents, IsFullyConnected, AdjacencyGraph)
NonManifoldData(EdgeIndices, VertexIndices, Valences, Locations, IsManifold, IsOrientable, MaxValence)
NgonTopologyData(NgonIndices, FaceIndicesPerNgon, BoundaryEdgesPerNgon, NgonCenters, EdgeCountPerNgon, TotalNgons, TotalFaces)
AdjacencyData(EdgeIndex, AdjacentFaceIndices, FaceNormals, DihedralAngle, IsManifold, IsBoundary)
```

---

## Architecture Integration

**Result Monad**: All operations return `Result<T>` (`libs/core/results/Result.cs`) for monadic composition.

**Validation Modes** (`libs/core/validation/V.cs`): `V.Standard` (baseline), `V.Topology` (Brep edges/faces/vertices), `V.MeshSpecific` (Mesh faces/vertices/ngons), `V.BrepGranular` (diagnosis), `V.MassProperties` (topological features).

**Error Codes** (`libs/core/errors/E.cs`): `E.Topology.EdgeIndexOutOfRange`, `E.Topology.VertexIndexOutOfRange`, `E.Topology.NoNakedEdges`, `E.Topology.JoinFailed`, `E.Topology.HealingFailed`, `E.Topology.NoNgonsFound`, `E.Topology.InvalidStrategy`, `E.Geometry.InvalidCount`, `E.Validation.GeometryInvalid`.

---

## Configuration

**Healing Tolerance Multipliers**: Conservative (0.1×), Moderate (1.0×), Aggressive (10.0×), Combined (1.0×), Targeted (10.0×, 100 iter), Component (1.0×).

**Diagnosis**: Near-miss multiplier 100.0×, max 100 edges, curvature threshold 0.1 for G2.

**Implementation**: O(1) FrozenDictionary dispatch `(Type, OpType) → OperationMetadata`. `TopologyCore` routes via `UnifiedOperation.Apply()`. `TopologyCompute` implements BFS traversal, edge gap analysis, progressive healing with rollback.

---

## File Organization

```
topology/
├── Topology.cs          # Public API with algebraic domain types (311 LOC)
├── TopologyCore.cs      # UnifiedOperation orchestration (328 LOC)
├── TopologyCompute.cs   # Topology algorithms (231 LOC)
└── TopologyConfig.cs    # Dispatch tables and constants (76 LOC)
```

**Files**: 4 (✓ within limit)  
**Types**: 20 (strategy hierarchy + result types + internal implementations)  
**LOC**: ~946 total

---

## Dependencies

- `libs/core/results` - Result monad, ResultFactory
- `libs/core/context` - IGeometryContext, GeometryContext
- `libs/core/validation` - V flags, ValidationRules
- `libs/core/errors` - E error registry, SystemError
- `libs/core/operations` - UnifiedOperation, OperationConfig
- `RhinoCommon` - Brep, Mesh, Curve, BrepEdge, BrepVertex, MeshTopology

---

## Testing

```bash
dotnet test --filter "FullyQualifiedName~Arsenal.Rhino.Topology"
```

See `test/rhino/topology/` for NUnit + Rhino.Testing integration tests.

---

## See Also

- `libs/core/operations/UnifiedOperation.cs` - Polymorphic dispatch engine
- `libs/core/validation/ValidationRules.cs` - Expression tree validation
- `libs/rhino/analysis/` - Differential geometry and curvature analysis
- `libs/rhino/spatial/` - RTree spatial indexing
- `CLAUDE.md` - Coding standards and architectural patterns
