# Topology Analysis and Healing

Polymorphic topology analysis, diagnosis, and progressive healing for Brep and Mesh geometry.

> **Related Modules**: For differential geometry (curvature, derivatives), see [`Analysis`](../analysis/README.md). For point/curve extraction, see [`Extraction`](../extraction/README.md).

---

## API

```csharp
Result<AdjacencyData> GetAdjacency<T>(T geometry, IGeometryContext context, int edgeIndex)
Result<NakedEdgeData> GetNakedEdges<T>(T geometry, IGeometryContext context, bool orderLoops = false)
Result<VertexData> GetVertexData<T>(T geometry, IGeometryContext context, int vertexIndex)
Result<BoundaryLoopData> GetBoundaryLoops<T>(T geometry, IGeometryContext context, double? tolerance = null)
Result<ConnectivityData> GetConnectivity<T>(T geometry, IGeometryContext context)
Result<NonManifoldData> GetNonManifoldData<T>(T geometry, IGeometryContext context)
Result<NgonTopologyData> GetNgonTopology<T>(T geometry, IGeometryContext context)
Result<TopologyDiagnosis> DiagnoseTopology(Brep brep, IGeometryContext context)
Result<EdgeClassificationData> ClassifyEdges<T>(T geometry, IGeometryContext context, Continuity minimumContinuity = Continuity.G1_continuous, double? angleThreshold = null)
Result<TopologicalFeatures> ExtractTopologicalFeatures(Brep brep, IGeometryContext context)
Result<HealingResult> HealTopology(Brep brep, IReadOnlyList<Strategy> strategies, IGeometryContext context)
```

---

## Operations/Types

**Strategies**: `ConservativeRepairStrategy` (0.1×), `ModerateJoinStrategy` (1.0×), `AggressiveJoinStrategy` (10.0×), `CombinedStrategy`, `TargetedJoinStrategy` (100 iter max), `ComponentJoinStrategy`

**EdgeContinuityType**: `Sharp` (G0), `Smooth` (G1), `Curvature` (G2), `Interior`, `Boundary`, `NonManifold`

**Results** (`IResult`): `AdjacencyData`, `NakedEdgeData`, `VertexData`, `BoundaryLoopData`, `ConnectivityData`, `NonManifoldData`, `NgonTopologyData`, `TopologyDiagnosis`, `EdgeClassificationData`, `TopologicalFeatures`, `HealingResult`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);
// Edge classification
Result<Topology.EdgeClassificationData> edges = Topology.ClassifyEdges(
    geometry: brep,
    context: context,
    minimumContinuity: Continuity.G1_continuous);

// Diagnosis and healing
Result<Topology.HealingResult> healed = Topology.DiagnoseTopology(brep, context)
    .Bind(diag => Topology.HealTopology(brep, diag.SuggestedStrategies, context));

// Ngon topology (Mesh only)
Result<Topology.NgonTopologyData> ngons = Topology.GetNgonTopology(mesh, context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<T>` implementing `IResult`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.Topology` (Breps), `V.Standard | V.MeshSpecific` (meshes), `V.Standard | V.Topology | V.BrepGranular` (diagnosis)
- **Errors**: `E.Topology.EdgeIndexOutOfRange`, `E.Topology.VertexIndexOutOfRange`, `E.Topology.NoNakedEdges`, `E.Topology.JoinFailed`, `E.Topology.HealingFailed`, `E.Topology.NoNgonsFound`

---

## Internals

**Files**: `Topology.cs` (API, 311 LOC), `TopologyCore.cs` (dispatch, 328 LOC), `TopologyCompute.cs` (algorithms, 231 LOC), `TopologyConfig.cs` (config, 76 LOC)

**Dispatch**: `FrozenDictionary<(Type, OpType), OperationMetadata>` mapping geometry type and operation to validation mode

**Tolerance multipliers**: Conservative 0.1×, Moderate 1.0×, Aggressive 10.0×; targeted join 100 iterations max

**Diagnosis**: Near-miss multiplier 100×, max 100 edges, curvature threshold 0.1 for G2 detection

**Healing**: Progressive strategies with automatic rollback; BFS for connectivity; bidirectional edge search for near-misses

**Supported operations by geometry**: Breps (all except NgonTopology), Meshes (all except Diagnose, ExtractFeatures, Heal)
