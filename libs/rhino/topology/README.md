# Topology Analysis and Healing

Polymorphic topology analysis, diagnosis, and progressive healing for Brep and Mesh geometry.

---

## API

```csharp
// Unified query for Brep/Mesh topology operations
Result<TopologyResult> Query<T>(T geometry, QueryOperation operation, IGeometryContext context)

// Brep-specific operations (diagnosis, healing)
Result<TopologyResult> Execute(Brep brep, BrepOperation operation, IGeometryContext context)
```

---

## Query Operations (Brep/Mesh)

| Operation | Description |
|-----------|-------------|
| `ConnectivityQuery()` | Connected components via BFS |
| `NonManifoldQuery()` | Non-manifold edges/vertices |
| `NgonQuery()` | Ngon topology (Mesh only) |
| `AdjacencyQuery(edgeIndex)` | Edge-face adjacency |
| `VertexQuery(vertexIndex)` | Vertex topology |
| `NakedEdgesQuery(orderLoops)` | Naked boundary edges |
| `BoundaryLoopsQuery(tolerance)` | Joined boundary loops |
| `EdgeClassificationQuery(minContinuity, angleThreshold)` | G0/G1/G2 classification |

## Brep Operations

| Operation | Description |
|-----------|-------------|
| `DiagnoseOperation()` | Gap analysis and repair suggestions |
| `ExtractFeaturesOperation()` | Genus, loops, solid classification |
| `HealOperation(strategies)` | Progressive healing with rollback |

---

## Result Types

**TopologyResult** (discriminated union): `Connectivity`, `NonManifold`, `Ngon`, `Adjacency`, `Vertex`, `NakedEdges`, `BoundaryLoops`, `EdgeClassification`, `Diagnosis`, `Features`, `Healing`

**Strategies**: `ConservativeRepairStrategy` (0.1×), `ModerateJoinStrategy` (1.0×), `AggressiveJoinStrategy` (10.0×), `CombinedStrategy`, `TargetedJoinStrategy`, `ComponentJoinStrategy`

**EdgeContinuityType**: `Sharp` (G0), `Smooth` (G1), `Curvature` (G2), `Interior`, `Boundary`, `NonManifold`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Edge classification via unified query
Result<Topology.TopologyResult> edges = Topology.Query(
    geometry: brep,
    operation: new Topology.EdgeClassificationQuery(MinimumContinuity: Continuity.G1_continuous),
    context: context);

// Pattern match on result
_ = edges.Match(
    onSuccess: result => result switch {
        Topology.TopologyResult.EdgeClassification ec => ProcessEdges(ec.Data),
        _ => default,
    },
    onFailure: errors => HandleErrors(errors));

// Diagnosis and healing via Brep operations
Result<Topology.TopologyResult> healed = Topology.Execute(brep, new Topology.DiagnoseOperation(), context)
    .Bind(result => result switch {
        Topology.TopologyResult.Diagnosis d => Topology.Execute(brep, new Topology.HealOperation(d.Data.SuggestedStrategies), context),
        _ => ResultFactory.Create<Topology.TopologyResult>(error: E.Topology.DiagnosisFailed),
    });

// Ngon topology (Mesh only)
Result<Topology.TopologyResult> ngons = Topology.Query(mesh, new Topology.NgonQuery(), context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<TopologyResult>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.Topology` (Breps), `V.Standard | V.MeshSpecific` (meshes)
- **Errors**: `E.Topology.*`, `E.Geometry.UnsupportedAnalysis`

---

## Internals

**Files**: `Topology.cs` (API + types), `TopologyCore.cs` (dispatch), `TopologyCompute.cs` (algorithms), `TopologyConfig.cs` (config)

**Dispatch**: Pattern matching on `QueryOperation`/`BrepOperation` types, delegating to typed executors

**Tolerance multipliers**: Conservative 0.1×, Moderate 1.0×, Aggressive 10.0×; targeted join 100 iterations max

**Supported operations**: Breps (all except NgonQuery), Meshes (all except Diagnose, ExtractFeatures, Heal)
