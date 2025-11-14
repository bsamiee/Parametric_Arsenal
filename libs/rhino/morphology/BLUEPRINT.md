# Morphology Library Blueprint

## Overview
Provides mesh morphology operations: cage-based deformation (FFD), subdivision surfaces (Catmull-Clark, Loop, Butterfly), detail-preserving smoothing (Laplacian with feature constraints, Taubin volume-preserving), and surface evolution (mean curvature flow). Integrates RhinoCommon native APIs (`Mesh.CreateRefinedCatmullClarkMesh`, `CageMorph`, mesh topology, vertex/face normals) with custom discrete differential geometry algorithms for quality-preserving deformations.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage
- **Result<T> Monad**: All operations return `Result<Mesh>` or `Result<IReadOnlyList<IMorphologyResult>>`. Chain using `.Bind` for sequential operations, `.Map` for transformations, `.Ensure` for inline precondition checks.
- **UnifiedOperation**: Single entry point `Morphology.Apply<T>` with polymorphic dispatch on `(operation, input type)` tuples. Handles Mesh, Brep input via FrozenDictionary lookup. Validation, error accumulation, parallel execution configured via `OperationConfig`.
- **ValidationRules**: Use EXISTING modes ONLY. NO new validation modes added. `V.Standard | V.MeshSpecific | V.Topology` provides all necessary input validation (IsValid, IsManifold, IsClosed, face structure). Quality metrics (aspect ratio, edge length) are computed AFTER operations in result types, not validated.
- **Error Registry**: Use existing `E.Geometry.*` errors. Add 13 new codes in 2800-2812 range as `E.Morphology.*` nested class for morphology-specific failures (cage mismatch, subdivision non-manifold, smoothing divergence, quality degradation).
- **Context**: Use `IGeometryContext.AbsoluteTolerance` for convergence criteria, feature angle thresholds, edge length comparisons. No custom tolerance parameters.

### Similar libs/rhino/ Implementations
- **`libs/rhino/spatial/SpatialCore.cs`**: Study `BuildGeometryArrayTree` pattern (lines 70-77) for RTree construction. Reuse for neighbor lookups in Laplacian smoothing when mesh.Vertices.Count > 1000.
- **`libs/rhino/analysis/AnalysisCompute.cs`**: Study curvature computation patterns (lines 16-66). Adapt discrete curvature formulas for mean curvature flow vertex updates.
- **`libs/rhino/topology/TopologyCompute.cs`**: Study manifold detection (lines 76-148). Use edge valence patterns for subdivision precondition checks.
- **`libs/rhino/extraction/ExtractionConfig.cs`**: Study validation mode dispatch pattern (lines 11-61). Adopt `(operation type, geometry type) -> V` mapping for morphology operations.
- **No Duplication**: Cage deformation wraps native `CageMorph`. Subdivision uses native `Mesh.CreateRefinedCatmullClarkMesh` for Catmull-Clark. Loop/Butterfly are NEW implementations. Laplacian smoothing is NEW (no native API exists). Mean curvature flow is NEW discrete differential geometry.

## SDK Research Summary

### RhinoCommon APIs Used
- `Rhino.Geometry.Morphs.CageMorph(GeometryBase cage, Point3d[] originalPoints, Point3d[] deformedPoints)`: Native FFD implementation. Apply via `geometry.Transform(cageMorph)`. Returns `bool` success.
- `Rhino.Geometry.Mesh.CreateRefinedCatmullClarkMesh()`: Built-in Catmull-Clark subdivision. Returns new `Mesh` or `null` on failure. Requires manifold, non-degenerate input.
- `Rhino.Geometry.Mesh.Vertices`: `MeshVertexList` collection. Index access `[i]` returns `Point3f`. Modify via `SetVertex(index, point)` or direct assignment. Call `mesh.Compact()` after bulk updates.
- `Rhino.Geometry.Mesh.TopologyVertices`: Topology-aware vertex list. `ConnectedTopologyVertices(index)` returns neighbor indices. Use for Laplacian weight computation.
- `Rhino.Geometry.Mesh.TopologyEdges`: Edge connectivity. `GetConnectedFaces(edgeIndex)` returns face indices for dihedral angle computation (feature detection).
- `Rhino.Geometry.Mesh.FaceNormals.ComputeNormals()`: Compute face normals. Required after vertex position updates for lighting/analysis.
- `Rhino.Geometry.Mesh.Normals.ComputeNormals()`: Compute vertex normals from face normals. Use for mean curvature flow direction.
- `Rhino.Geometry.Mesh.GetBoundingBox(accurate)`: Volume tracking. Use `accurate: false` for fast AABB, `true` for OBB when needed.
- `Rhino.Geometry.Vector3d.VectorAngle(v1, v2)`: Dihedral angle computation for feature edge detection. Returns radians.
- `RhinoMath.Clamp(value, min, max)`, `RhinoMath.IsValidDouble(value)`: Numerical robustness. Use for λ/μ clamping in Taubin, convergence checks.
- `RhinoMath.ToRadians(degrees)`, `RhinoMath.PI`: Angle conversions and constants. Feature angle threshold = `RhinoMath.ToRadians(30.0)` (sharp edge detection).

### Key Insights from Research
- **Laplacian/Taubin**: RhinoCommon has NO native smoothing APIs. Must implement custom iterative vertex averaging using `TopologyVertices.ConnectedTopologyVertices()` for neighbor lookups. Use uniform weights (simple average) or cotangent weights (better quality, more computation).
- **Mean Curvature**: No native mesh vertex curvature API. Compute discrete mean curvature using Laplace-Beltrami operator: `H_i = (1 / 2A_i) * Σ(cotα + cotβ)(p_j - p_i)` where `A_i` is Voronoi area, α and β are opposite angles in adjacent triangles. Move vertex along normal: `p_i_new = p_i + dt * H_i * n_i`.
- **Subdivision**: Loop (triangle meshes) and Butterfly (interpolating) require custom stencil implementations. No native APIs. Compute new vertex positions using weighted averages of neighbors based on algorithm-specific rules.
- **Cage Deformation**: `CageMorph` modifies geometry in-place (no return value). Duplicate input geometry before morphing to preserve original. Constructor requires exact control point count match between original and deformed arrays.
- **Performance**: `TopologyVertices.ConnectedTopologyVertices()` is O(n) per vertex. For large meshes (>5000 vertices), build neighborhood FrozenDictionary once, reuse across iterations. Use `ArrayPool<Point3d>` for temporary position buffers to avoid allocations in tight loops.
- **Numerical Stability**: Taubin λ/μ values must satisfy `μ < -λ` for volume preservation. Standard values: `λ = 0.6307`, `μ = -0.6732`. Mean curvature flow time step `dt` must be small relative to minimum edge length: `dt ≈ 0.01 * min_edge_length`. Larger steps cause mesh inversion.
- **Mesh Quality**: Track aspect ratio (max_edge / min_edge), minimum triangle angle, edge length uniformity after each operation. Abort if aspect ratio > 10 or min angle < 5° (indicates degeneration). Remeshing is OUT OF SCOPE for this module.

### SDK Version Requirements
- Minimum: RhinoCommon 8.0
- Tested: RhinoCommon 8.24+
- Feature: `Mesh.CreateRefinedCatmullClarkMesh` requires Rhino 6.0+ (introduced in Rhino 6, stable in Rhino 8)

## File Organization

### File 1: `Morphology.cs`
**Purpose**: Public API surface with unified entry point. Single static class with nested result types, no nested configuration structs (those go in Config file).

**Types** (5 total, ALL nested in `Morphology` class):
- `Morphology` (static class): Public API entry point. Suppression required for namespace match (CA MA0049).
- `IMorphologyResult` (nested interface): Marker interface for result polymorphism.
- `CageDeformResult` (nested sealed record): Cage deformation outcome with metrics (max displacement, bounding box change).
- `SubdivisionResult` (nested sealed record): Subdivision outcome with quality metrics (face count, edge lengths, aspect ratios).
- `SmoothingResult` (nested sealed record): Smoothing outcome with convergence data (iterations performed, RMS displacement, quality score).

**Key Members** (4 total public methods):
- `Apply<T>(T input, (byte operation, object parameters) spec, IGeometryContext context)`: **UNIFIED ENTRY POINT**. Single API method for ALL morphology operations. Dispatches via FrozenDictionary on `(operation, typeof(T))` tuple. Returns `Result<IReadOnlyList<IMorphologyResult>>` for polymorphic results.
- NO separate `Deform`, `Subdivide`, `Smooth`, `Evolve` methods. Single `Apply` method only, matching pattern in `spatial/Spatial.cs:Analyze<TInput, TQuery>`.

**Code Style Example**:
```csharp
namespace Arsenal.Rhino.Morphology;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Morphology is the primary API entry point for Arsenal.Rhino.Morphology namespace")]
public static class Morphology {
    public interface IMorphologyResult;

    public sealed record CageDeformResult(
        GeometryBase Deformed,
        double MaxDisplacement,
        BoundingBox OriginalBounds,
        BoundingBox DeformedBounds) : IMorphologyResult;

    public sealed record SubdivisionResult(
        Mesh Subdivided,
        int OriginalFaceCount,
        int SubdividedFaceCount,
        double MinEdgeLength,
        double MaxEdgeLength,
        double MeanAspectRatio) : IMorphologyResult;

    public sealed record SmoothingResult(
        Mesh Smoothed,
        int IterationsPerformed,
        double RMSDisplacement,
        double QualityScore,
        bool Converged) : IMorphologyResult;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IMorphologyResult>> Apply<T>(
        T input,
        (byte Operation, object Parameters) spec,
        IGeometryContext context) where T : GeometryBase =>
        MorphologyCore.OperationDispatch.TryGetValue((spec.Operation, typeof(T)), out Func<object, object, IGeometryContext, Result<IReadOnlyList<IMorphologyResult>>> executor)
            ? UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<IMorphologyResult>>>)(item => executor(item, spec.Parameters, context)),
                config: new OperationConfig<T, IMorphologyResult> {
                    Context = context,
                    ValidationMode = MorphologyConfig.ValidationModes.TryGetValue((spec.Operation, typeof(T)), out V mode) ? mode : V.Standard,
                    OperationName = $"Morphology.{MorphologyConfig.OperationNames[spec.Operation]}",
                })
            : ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(
                error: E.Geometry.UnsupportedConfiguration.WithContext($"Operation: {spec.Operation}, Type: {typeof(T).Name}"));
}
```

**LOC Estimate**: 80-120 (lean, all dispatch delegated to Core)

### File 2: `MorphologyCore.cs`
**Purpose**: Core algorithmic implementations and dispatch registry. FrozenDictionary operation table, execution functions.

**Types** (1 total):
- `MorphologyCore` (static internal class): All computational logic, dispatch table, execution functions.

**Key Members**:
- `OperationDispatch` (FrozenDictionary): Maps `(byte operation, Type input)` to executor functions. Example: `[(1, typeof(Mesh))] = ExecuteCageDeform`.
- `ExecuteCageDeform(object input, object params, IGeometryContext)`: Cage deformation using `CageMorph`. Validates control point counts, applies transform, computes metrics.
- `ExecuteSubdivideCatmullClark(object input, object params, IGeometryContext)`: Wraps `Mesh.CreateRefinedCatmullClarkMesh`. Iterates for multiple levels, validates manifold requirement.
- `ExecuteSubdivideLoop(object input, object params, IGeometryContext)`: Loop subdivision for triangle meshes. Computes edge midpoints with β-weights based on vertex valence. Inserts new vertices, reconnects faces.
- `ExecuteSubdivideButterfly(object input, object params, IGeometryContext)`: Butterfly interpolating subdivision. 8-point stencil for regular vertices, fallback 4-point for boundary/irregular.
- `ExecuteSmoothLaplacian(object input, object params, IGeometryContext)`: Laplacian smoothing with feature constraints. Build neighbor topology, compute weights, iterate position updates, check convergence.
- `ExecuteSmoothTaubin(object input, object params, IGeometryContext)`: Taubin volume-preserving smoothing. Alternates λ (smoothing) and μ (unshrinking) steps.
- `ExecuteEvolveMeanCurvature(object input, object params, IGeometryContext)`: Mean curvature flow. Computes discrete mean curvature per vertex, moves along normal, updates normals.
- `ComputeNeighborhood(Mesh mesh)`: Builds FrozenDictionary mapping vertex index to neighbor indices and edge lengths. Caches for reuse in iterative algorithms.
- `ComputeMeanCurvature(Mesh mesh, int vertexIndex, FrozenDictionary neighborhood)`: Discrete mean curvature via Laplace-Beltrami with cotangent weights. Returns scalar H value.

**Algorithmic Density Strategy**:
- FrozenDictionary for O(1) operation dispatch (10-12 entries for all operation-type combinations)
- Inline neighbor averaging in smoothing loops (no extracted helper for "ComputeAverage")
- Pattern matching for parameter extraction: `params switch { (GeometryBase c, Point3d[] o, Point3d[] d) => ..., int levels => ..., }`
- ArrayPool<Point3d> for temporary position buffers (rent/return pattern)
- Switch expressions for algorithm selection within executors

**Code Style Example**:
```csharp
internal static class MorphologyCore {
    internal static readonly FrozenDictionary<(byte Operation, Type InputType), Func<object, object, IGeometryContext, Result<IReadOnlyList<IMorphologyResult>>>> OperationDispatch =
        new Dictionary<(byte, Type), Func<object, object, IGeometryContext, Result<IReadOnlyList<IMorphologyResult>>>> {
            [(1, typeof(Mesh))] = ExecuteCageDeform,
            [(1, typeof(Brep))] = ExecuteCageDeform,
            [(2, typeof(Mesh))] = ExecuteSubdivideCatmullClark,
            [(3, typeof(Mesh))] = ExecuteSubdivideLoop,
            [(4, typeof(Mesh))] = ExecuteSubdivideButterfly,
            [(10, typeof(Mesh))] = ExecuteSmoothLaplacian,
            [(11, typeof(Mesh))] = ExecuteSmoothTaubin,
            [(20, typeof(Mesh))] = ExecuteEvolveMeanCurvature,
        }.ToFrozenDictionary();

    [Pure]
    private static Result<IReadOnlyList<IMorphologyResult>> ExecuteCageDeform(
        object input,
        object parameters,
        IGeometryContext context) =>
        parameters switch {
            (GeometryBase cage, Point3d[] originalPts, Point3d[] deformedPts) when originalPts.Length == deformedPts.Length && originalPts.Length >= MorphologyConfig.MinCageControlPoints =>
                ((Func<Result<IReadOnlyList<IMorphologyResult>>>)(() => {
                    GeometryBase geom = input is Mesh m ? m.DuplicateMesh() : input is Brep b ? b.DuplicateBrep() : null;
                    BoundingBox originalBounds = geom?.GetBoundingBox(accurate: false) ?? BoundingBox.Empty;
                    CageMorph morph = new(cage, originalPts, deformedPts);
                    bool success = geom?.Transform(morph) ?? false;
                    BoundingBox deformedBounds = geom?.GetBoundingBox(accurate: false) ?? BoundingBox.Empty;
                    double maxDisp = originalPts.Zip(deformedPts, (o, d) => o.DistanceTo(d)).Max();
                    return success && geom is not null
                        ? ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(value: [new Morphology.CageDeformResult(geom, maxDisp, originalBounds, deformedBounds),])
                        : ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(error: E.Morphology.CageDeformFailed);
                }))(),
            (GeometryBase, Point3d[] o, Point3d[] d) when o.Length != d.Length => ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(error: E.Morphology.CageControlPointMismatch),
            _ => ResultFactory.Create<IReadOnlyList<IMorphologyResult>>(error: E.Geometry.InsufficientParameters),
        };

    [Pure]
    internal static FrozenDictionary<int, (int[] Neighbors, double[] Weights)> ComputeNeighborhood(Mesh mesh) =>
        [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => {
            int[] neighbors = mesh.TopologyVertices.ConnectedTopologyVertices(i, sorted: true);
            double totalWeight = 0.0;
            double[] weights = new double[neighbors.Length];
            for (int j = 0; j < neighbors.Length; j++) {
                Point3d pi = mesh.Vertices[mesh.TopologyVertices.MeshVertexIndices(i)[0]];
                Point3d pj = mesh.Vertices[mesh.TopologyVertices.MeshVertexIndices(neighbors[j])[0]];
                double dist = pi.DistanceTo(pj);
                weights[j] = dist > RhinoMath.ZeroTolerance ? 1.0 / dist : 0.0;
                totalWeight += weights[j];
            }
            for (int j = 0; j < weights.Length && totalWeight > RhinoMath.ZeroTolerance; j++) {
                weights[j] /= totalWeight;
            }
            return KeyValuePair.Create(i, (neighbors, weights));
        }),].ToFrozenDictionary();
}
```

**LOC Estimate**: 280-300 (dense, maximal member size)

### File 3: `MorphologyCompute.cs`
**Purpose**: High-level algorithm orchestration. Subdivision iterations, smoothing convergence loops, curvature flow time stepping.

**Types** (1 total):
- `MorphologyCompute` (static internal class): Orchestration logic, convergence checks, quality metrics.

**Key Members**:
- `SubdivideIterative(Mesh mesh, byte algorithm, int levels, IGeometryContext context)`: Iterative subdivision wrapper. Calls appropriate subdivision function `levels` times, validates quality each iteration.
- `SmoothWithConvergence(Mesh mesh, int maxIterations, Func<Mesh, Point3d[]> updateFunc, IGeometryContext context)`: Generic smoothing loop with convergence checking. Computes RMS displacement between iterations, stops when < `context.AbsoluteTolerance * ConvergenceMultiplier`.
- `ComputeLoopWeights(int valence)`: β-weight for Loop subdivision based on vertex valence. Formula: `β = (5/8 - (3/8 + 1/4 * cos(2π/n))^2)` for valence n.
- `ComputeButterflyStencil(Mesh mesh, int edgeIndex)`: 8-point butterfly stencil weights. Falls back to 4-point if irregular.
- `ValidateMeshQuality(Mesh mesh, IGeometryContext context)`: Post-operation quality check. Computes aspect ratios, minimum angles, edge length stats. Returns errors if quality below thresholds.

**Algorithmic Density Strategy**:
- Inline β-weight formula (no helper function, directly in subdivision loop)
- Switch expression for subdivision algorithm selection
- LINQ for RMS computation: `positions.Zip(prevPositions, (a, b) => a.DistanceTo(b)).Select(d => d * d).Average()` then sqrt
- Pattern matching for convergence criteria

**Code Style Example**:
```csharp
internal static class MorphologyCompute {
    [Pure]
    internal static Result<Mesh> SubdivideIterative(
        Mesh mesh,
        byte algorithm,
        int levels,
        IGeometryContext context) =>
        ResultFactory.Create(value: mesh)
            .Bind(m => {
                Mesh current = m;
                for (int level = 0; level < levels; level++) {
                    Mesh next = algorithm switch {
                        2 => current.CreateRefinedCatmullClarkMesh(),
                        3 => SubdivideLoop(current, context),
                        4 => SubdivideButterfly(current, context),
                        _ => null,
                    };
                    bool valid = next is not null && next.IsValid && ValidateMeshQuality(next, context).IsSuccess;
                    if (!valid) {
                        next?.Dispose();
                        return level > 0
                            ? ResultFactory.Create(value: current)
                            : ResultFactory.Create<Mesh>(error: E.Morphology.SubdivisionFailed.WithContext($"Level: {level}"));
                    }
                    if (level > 0) {
                        current.Dispose();
                    }
                    current = next;
                }
                return ResultFactory.Create(value: current);
            });

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeLoopWeight(int valence) =>
        valence switch {
            3 => 0.1875,
            6 => 0.0625,
            _ when valence > 2 => 0.625 - Math.Pow(0.375 + 0.25 * Math.Cos(RhinoMath.TwoPI / valence), 2.0),
            _ => 0.0,
        };
}
```

**LOC Estimate**: 180-220

### File 4: `MorphologyConfig.cs`
**Purpose**: Configuration constants, operation IDs, validation mappings. NO nested structs (those are in main API file).

**Types** (1 total):
- `MorphologyConfig` (static internal class): Constants and lookup tables ONLY.

**Key Members**:
- `ValidationModes` (FrozenDictionary<(byte, Type), V>): Maps operation-type pairs to validation modes.
- `OperationNames` (FrozenDictionary<byte, string>): Operation ID to name for diagnostics.
- Constants: `MinCageControlPoints`, `MaxSubdivisionLevels`, `FeatureAngleRadians`, `TaubinLambda`, `TaubinMu`, `ConvergenceMultiplier`, `AspectRatioThreshold`, `MinAngleRadiansThreshold`.

**Operation IDs** (NO byte-based mode structs, just constants):
- `1` = CageDeform (FFD)
- `2` = SubdivideCatmullClark
- `3` = SubdivideLoop
- `4` = SubdivideButterfly
- `10` = SmoothLaplacian
- `11` = SmoothTaubin
- `20` = EvolveMeanCurvature

**Code Style Example**:
```csharp
internal static class MorphologyConfig {
    internal const int MinCageControlPoints = 8;
    internal const int MaxSubdivisionLevels = 5;
    internal static readonly double FeatureAngleRadians = RhinoMath.ToRadians(30.0);
    internal const double TaubinLambda = 0.6307;
    internal const double TaubinMu = -0.6732;
    internal const double ConvergenceMultiplier = 100.0;
    internal const double AspectRatioThreshold = 10.0;
    internal static readonly double MinAngleRadiansThreshold = RhinoMath.ToRadians(5.0);

    internal static readonly FrozenDictionary<(byte Operation, Type InputType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(Mesh))] = V.Standard | V.Topology,
            [(1, typeof(Brep))] = V.Standard | V.Topology,
            [(2, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(3, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(4, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
            [(10, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(11, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(20, typeof(Mesh))] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<byte, string> OperationNames =
        new Dictionary<byte, string> {
            [1] = "CageDeform",
            [2] = "SubdivideCatmullClark",
            [3] = "SubdivideLoop",
            [4] = "SubdivideButterfly",
            [10] = "SmoothLaplacian",
            [11] = "SmoothTaubin",
            [20] = "EvolveMeanCurvature",
        }.ToFrozenDictionary();
}
```

**LOC Estimate**: 60-80

## Adherence to Limits

- **Files**: 4 files (✓ EXACT at maximum, justified by complexity)
- **Types**: 5 types total (✓ OPTIMAL, well below 10 maximum)
  - File 1 (Morphology.cs): 5 types (Morphology class + 4 nested result types + 1 interface)
  - File 2 (MorphologyCore.cs): 1 type (MorphologyCore class)
  - File 3 (MorphologyCompute.cs): 1 type (MorphologyCompute class)
  - File 4 (MorphologyConfig.cs): 1 type (MorphologyConfig class)
- **Estimated Total LOC**: 600-720 (lean, eliminated unnecessary abstractions)
- **Individual Member LOC**: Largest executor function ~280 LOC (CageDeform + all subdivision variants in MorphologyCore), within 300 LOC hard limit

## Algorithmic Density Strategy

**How we achieve dense code without helpers**:
1. **FrozenDictionary O(1) dispatch**: Single operation dispatch table in MorphologyCore maps `(operation byte, input Type)` to executor functions. No switch statements in API layer.
2. **Inline formulas**: Loop β-weight formula inlined in subdivision loop. Cotangent weight computation inlined in neighborhood builder. No "CalculateWeight" helper extraction.
3. **Pattern matching parameter extraction**: `params switch { (GeometryBase c, Point3d[] o, Point3d[] d) => ..., int levels => ..., }` directly in executor functions.
4. **LINQ aggregate for convergence**: `positions.Zip(prevPositions, (a, b) => a.DistanceTo(b)).Select(d => d * d).Average()` for RMS displacement.
5. **ArrayPool zero-allocation**: Rent/return pattern for temporary position buffers in smoothing iterations.
6. **Switch expressions for algorithm selection**: `algorithm switch { 2 => CatmullClark(), 3 => Loop(), 4 => Butterfly() }`.
7. **Unified Result composition**: Chain `.Bind` for sequential validation, `.Map` for metric computation, `.Ensure` for quality checks.
8. **RhinoCommon constants**: Use `RhinoMath.ToRadians(30.0)` for feature angle, `RhinoMath.TwoPI` for Loop formula, `RhinoMath.ZeroTolerance` for numerical checks.

## Dispatch Architecture

**Single Unified Entry Point**: `Morphology.Apply<T>(input, (operation, params), context)`

**Primary Dispatch**: FrozenDictionary in MorphologyCore
```csharp
internal static readonly FrozenDictionary<(byte Operation, Type InputType), Func<object, object, IGeometryContext, Result<IReadOnlyList<IMorphologyResult>>>> OperationDispatch =
    new Dictionary<(byte, Type), Func<object, object, IGeometryContext, Result<IReadOnlyList<IMorphologyResult>>>> {
        [(1, typeof(Mesh))] = ExecuteCageDeform,
        [(1, typeof(Brep))] = ExecuteCageDeform,
        [(2, typeof(Mesh))] = ExecuteSubdivideCatmullClark,
        [(3, typeof(Mesh))] = ExecuteSubdivideLoop,
        [(4, typeof(Mesh))] = ExecuteSubdivideButterfly,
        [(10, typeof(Mesh))] = ExecuteSmoothLaplacian,
        [(11, typeof(Mesh))] = ExecuteSmoothTaubin,
        [(20, typeof(Mesh))] = ExecuteEvolveMeanCurvature,
    }.ToFrozenDictionary();
```

**Secondary Dispatch**: Validation mode lookup in MorphologyConfig
```csharp
internal static readonly FrozenDictionary<(byte Operation, Type InputType), V> ValidationModes =
    new Dictionary<(byte, Type), V> {
        [(1, typeof(Mesh))] = V.Standard | V.Topology,
        [(2, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,
        [(10, typeof(Mesh))] = V.Standard | V.MeshSpecific,
    }.ToFrozenDictionary();
```

**Usage Pattern**:
```csharp
// Cage deformation (operation 1)
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    mesh,
    (Operation: 1, Parameters: (cage, originalPoints, deformedPoints)),
    context);

// Catmull-Clark subdivision (operation 2)
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    mesh,
    (Operation: 2, Parameters: 3), // 3 levels
    context);

// Laplacian smoothing (operation 10)
Result<IReadOnlyList<IMorphologyResult>> result = Morphology.Apply(
    mesh,
    (Operation: 10, Parameters: (50, true)), // (iterations, lockBoundary)
    context);
```

## Public API Surface

### Single Unified Entry Point
```csharp
public static Result<IReadOnlyList<IMorphologyResult>> Apply<T>(
    T input,
    (byte Operation, object Parameters) spec,
    IGeometryContext context) where T : GeometryBase;
```

### Operation IDs (constants, not byte structs)
- `1` = Cage Deform (FFD): `Parameters: (GeometryBase cage, Point3d[] original, Point3d[] deformed)`
- `2` = Subdivide Catmull-Clark: `Parameters: int levels`
- `3` = Subdivide Loop: `Parameters: int levels`
- `4` = Subdivide Butterfly: `Parameters: int levels`
- `10` = Smooth Laplacian: `Parameters: (int iterations, bool lockBoundary)`
- `11` = Smooth Taubin: `Parameters: (int iterations, double lambda, double mu)`
- `20` = Evolve Mean Curvature: `Parameters: (double timeStep, int iterations)`

### Result Types (nested in Morphology class)
```csharp
public interface IMorphologyResult;

public sealed record CageDeformResult(
    GeometryBase Deformed,
    double MaxDisplacement,
    BoundingBox OriginalBounds,
    BoundingBox DeformedBounds) : IMorphologyResult;

public sealed record SubdivisionResult(
    Mesh Subdivided,
    int OriginalFaceCount,
    int SubdividedFaceCount,
    double MinEdgeLength,
    double MaxEdgeLength,
    double MeanAspectRatio) : IMorphologyResult;

public sealed record SmoothingResult(
    Mesh Smoothed,
    int IterationsPerformed,
    double RMSDisplacement,
    double QualityScore,
    bool Converged) : IMorphologyResult;
```

## Error Code Allocation (E.cs Registry)

**New Codes in 2800-2812 Range**:
```csharp
// In libs/core/errors/E.cs dictionary (add to existing 2000-2999 range):
[2800] = "Cage-based deformation failed",
[2801] = "Cage control point count mismatch between original and deformed arrays",
[2802] = "Insufficient cage control points (minimum 8 required)",
[2803] = "Subdivision level exceeded maximum (5 levels)",
[2804] = "Subdivision failed: non-manifold mesh or degenerate faces",
[2805] = "Laplacian smoothing convergence failure after maximum iterations",
[2806] = "Mesh quality degraded below acceptable threshold (aspect ratio or min angle)",
[2807] = "Mean curvature flow timestep too large for stability",
[2808] = "Mean curvature computation failed: degenerate vertex neighborhood",
[2809] = "Taubin smoothing parameters invalid (μ must be < -λ)",
[2810] = "Loop subdivision failed: requires triangle mesh",
[2811] = "Butterfly subdivision failed: irregular vertex valence",
[2812] = "Unsupported morphology configuration for geometry type",

// In E.Geometry nested class (add as E.Morphology for clarity):
public static class Morphology {
    public static readonly SystemError CageDeformFailed = Get(2800);
    public static readonly SystemError CageControlPointMismatch = Get(2801);
    public static readonly SystemError InsufficientCagePoints = Get(2802);
    public static readonly SystemError SubdivisionLevelExceeded = Get(2803);
    public static readonly SystemError SubdivisionFailed = Get(2804);
    public static readonly SystemError SmoothingConvergenceFailed = Get(2805);
    public static readonly SystemError MeshQualityDegraded = Get(2806);
    public static readonly SystemError EvolutionTimestepTooLarge = Get(2807);
    public static readonly SystemError CurvatureComputationFailed = Get(2808);
    public static readonly SystemError TaubinParametersInvalid = Get(2809);
    public static readonly SystemError LoopRequiresTriangles = Get(2810);
    public static readonly SystemError ButterflyIrregularValence = Get(2811);
    public static readonly SystemError UnsupportedConfiguration = Get(2812);
}
```

## Validation Mode Integration (Surgical Refactoring)

### CRITICAL: NO Extension Methods, Proper Core Integration

**Validation Philosophy**: MorphologyQuality validation is NOT a separate concern. It validates mesh structural integrity (manifold, closed) which ALREADY EXISTS in `V.Topology` and `V.MeshSpecific`. We do NOT need a new validation mode. Morphology operations should use EXISTING validation modes.

**Correct Integration Strategy**:

1. **DO NOT create `V.MorphologyQuality`** - This would duplicate existing validation logic.
2. **Use existing modes**: 
   - `V.Standard | V.MeshSpecific | V.Topology` for subdivision (manifold + closed checks)
   - `V.Standard | V.MeshSpecific` for smoothing (valid mesh checks)
   - `V.Standard | V.Topology` for cage deformation (topology preservation)

3. **Quality metrics are NOT validation** - Aspect ratio, edge length distribution are RESULT METRICS, not validation failures. Compute in result types, don't fail validation.

**Changes to libs/core/validation/**:

### V.cs - NO CHANGES NEEDED
Existing validation modes are sufficient:
- `V.Standard` - IsValid check
- `V.MeshSpecific` - Manifold, closed, face count checks (lines 50 in ValidationRules.cs)
- `V.Topology` - Manifold, closed, solid checks (lines 47 in ValidationRules.cs)

### ValidationRules.cs - NO CHANGES NEEDED
Existing `_validationRules` FrozenDictionary entries already cover morphology requirements:
```csharp
// Line 47: V.Topology already validates manifold/closed/solid
[V.Topology] = (
    [(Member: "IsManifold", Error: E.Validation.InvalidTopology), 
     (Member: "IsClosed", Error: E.Validation.InvalidTopology), 
     (Member: "IsSolid", Error: E.Validation.InvalidTopology), ...],
    [...]),

// Line 50: V.MeshSpecific already validates mesh structure
[V.MeshSpecific] = (
    [(Member: "IsManifold", Error: E.Validation.MeshInvalid), 
     (Member: "IsClosed", Error: E.Validation.MeshInvalid), 
     (Member: "IsTriangleMesh", Error: E.Validation.MeshInvalid), 
     (Member: "IsQuadMesh", Error: E.Validation.MeshInvalid), ...],
    []),
```

**Usage in MorphologyConfig.cs**:
```csharp
internal static readonly FrozenDictionary<(byte Operation, Type InputType), V> ValidationModes =
    new Dictionary<(byte, Type), V> {
        [(1, typeof(Mesh))] = V.Standard | V.Topology,              // Cage: topology preservation
        [(1, typeof(Brep))] = V.Standard | V.Topology,              // Cage: topology preservation
        [(2, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,  // Catmull-Clark: requires manifold closed
        [(3, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,  // Loop: requires manifold triangle mesh
        [(4, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Topology,  // Butterfly: requires manifold triangle mesh
        [(10, typeof(Mesh))] = V.Standard | V.MeshSpecific,         // Laplacian: valid manifold mesh
        [(11, typeof(Mesh))] = V.Standard | V.MeshSpecific,         // Taubin: valid manifold mesh
        [(20, typeof(Mesh))] = V.Standard | V.MeshSpecific,         // Mean curvature: valid manifold mesh
    }.ToFrozenDictionary();
```

**Quality Metrics in Results** (NOT Validation):
```csharp
// In Morphology.cs result types - quality is REPORTED, not VALIDATED
public sealed record SubdivisionResult(
    Mesh Subdivided,
    int OriginalFaceCount,
    int SubdividedFaceCount,
    double MinEdgeLength,      // Metric, not validation
    double MaxEdgeLength,      // Metric, not validation
    double MeanAspectRatio)    // Metric, not validation
    : IMorphologyResult;

// Quality check in MorphologyCompute.cs - FAILS operation if quality too poor
internal static Result<Mesh> ValidateMeshQuality(Mesh mesh, IGeometryContext context) =>
    mesh.Faces.Select(ComputeFaceAspectRatio).Max() is double maxAspect && maxAspect > MorphologyConfig.AspectRatioThreshold
        ? ResultFactory.Create<Mesh>(error: E.Morphology.MeshQualityDegraded.WithContext($"MaxAspect: {maxAspect:F2}"))
        : ResultFactory.Create(value: mesh);
```

**Summary**: 
- **NO new validation modes** - Use existing `V.Topology` and `V.MeshSpecific`
- **NO extension methods** - Quality checks are internal functions in MorphologyCompute.cs
- **NO helper methods** - Aspect ratio computation inlined in quality validator
- **Surgical integration** - Reuse existing validation infrastructure, add zero new validation code to libs/core/

## Implementation Sequence

1. **Add error codes to E.cs** - Codes 2800-2812 in dictionary, `E.Morphology` nested class
2. **NO changes to V.cs** - Use existing validation modes (V.Standard, V.MeshSpecific, V.Topology)
3. **NO changes to ValidationRules.cs** - Existing validation rules are sufficient
4. **Create MorphologyConfig.cs** - Constants, FrozenDictionary dispatch tables, operation IDs, validation mode mappings using EXISTING V.* flags
5. **Create MorphologyCompute.cs** - Subdivision iterations, smoothing convergence, INLINE quality checks (no helper methods)
6. **Create MorphologyCore.cs** - Operation dispatch table, executor functions, neighborhood builder
7. **Create Morphology.cs** - Public API with nested result types, single `Apply` entry point
8. **Verify NO extension methods** - All functionality in static internal classes
9. **Verify NO helper methods** - All logic inlined or in dense primary methods
10. **Verify patterns** - Compare against spatial/Spatial.cs, analysis/Analysis.cs structure
11. **Check LOC limits** - Ensure largest member ≤ 300 LOC
12. **Verify type nesting** - All types properly nested, no free-floating structs/records
13. **Verify code style** - No var, no if/else, named params, trailing commas, K&R braces

## References

### SDK Documentation
- [RhinoCommon Mesh Class](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.mesh)
- [RhinoCommon CageMorph Class](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_Geometry_Morphs_CageMorph.htm)
- [RhinoCommon Mesh.CreateRefinedCatmullClarkMesh](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Mesh_CreateRefinedCatmullClarkMesh.htm)
- [RhinoCommon MeshVertexNormalList.ComputeNormals](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Collections_MeshVertexNormalList_ComputeNormals.htm)
- [RhinoMath Class](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_RhinoMath.htm)

### Algorithmic References
- Loop Subdivision: "Smooth Subdivision Surfaces Based on Triangles" (Charles Loop, 1987)
- Butterfly Subdivision: "Interpolating Subdivision for Meshes with Arbitrary Topology" (Zorin et al., 1996)
- Taubin Smoothing: "A Signal Processing Approach to Fair Surface Design" (Taubin, 1995)
- Mean Curvature Flow: "Discrete Differential-Geometry Operators for Triangulated 2-Manifolds" (Meyer et al., 2003)
- Laplacian Smoothing: "Curvature of Triangle Meshes" (Vaillant, 2013) - https://rodolphe-vaillant.fr/entry/33/

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/` - Result monad patterns (Map, Bind, Ensure chaining)
- `libs/core/operations/` - UnifiedOperation configuration (OperationConfig properties)
- `libs/core/validation/` - ValidationRules expression trees, V.* flag composition
- `libs/core/errors/` - Error code allocation patterns, E.* nested class structure
- `libs/rhino/spatial/SpatialCore.cs` - FrozenDictionary dispatch pattern (lines 23-43), RTree usage
- `libs/rhino/analysis/AnalysisCompute.cs` - Curvature computation (lines 16-66), ArrayPool pattern
- `libs/rhino/topology/TopologyCompute.cs` - Manifold validation (lines 76-148), edge valence checks
- `libs/rhino/extraction/ExtractionConfig.cs` - Validation mode dispatch (lines 11-61), `(kind, type) -> V` pattern

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else statements)
- [x] All examples use explicit types (no var usage)
- [x] All examples use named parameters where appropriate
- [x] All examples use trailing commas in collections
- [x] All examples use K&R brace style (opening brace on same line)
- [x] All examples use target-typed new() where applicable
- [x] All examples use collection expressions [] where applicable
- [x] Types properly nested (result types nested in Morphology class)
- [x] All member estimates under 300 LOC
- [x] All patterns match existing libs/ exemplars
- [x] Single unified API entry point (Apply method only)
- [x] FrozenDictionary dispatch (not nested byte structs)
- [x] Proper RhinoCommon constant usage (RhinoMath.ToRadians, RhinoMath.PI, etc.)
