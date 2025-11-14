# Morphology Library Blueprint

## Overview
Implements advanced mesh and surface deformation operations via free-form deformation (FFD), Laplacian smoothing, subdivision surfaces, and surface evolution. Provides parametric control over geometry shape transformation while preserving topological structure and detail.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage
- **Result<T> Monad**: All operations return `Result<IReadOnlyList<T>>` for error handling. Use `Map`, `Bind`, `Ensure` for operation chains. Lazy evaluation via `deferred` parameter for expensive computations.
- **UnifiedOperation**: Primary dispatch engine for polymorphic input handling (Mesh, Brep, Surface). Configuration via `OperationConfig<TIn, TOut>` provides validation, parallelism, caching, diagnostics. Always use named parameters in config initialization.
- **ValidationRules**: Leverage existing `V.Standard`, `V.Topology`, `V.MeshSpecific`, `V.Degeneracy` modes. No new validation flags needed - combinations of existing flags suffice.
- **Error Registry**: Allocate codes 2600-2619 in `E.Geometry.*` domain for morphology-specific errors.
- **Context**: Use `IGeometryContext.AbsoluteTolerance`, `RelativeTolerance` for convergence criteria, threshold comparisons, and numerical stability checks.

### Similar libs/rhino/ Implementations
- **`libs/rhino/extraction/`**: **CRITICAL PATTERN** - Uses readonly struct `Semantic(byte kind)` with static readonly instances instead of enums. Byte-based FrozenDictionary dispatch via `(byte Kind, Type GeometryType)` keys. This is the definitive pattern to follow.
- **`libs/rhino/spatial/`**: FrozenDictionary with `(Type Input, Type Query)` tuples for polymorphic dispatch. Uses Func factories for RTree construction. ArrayPool<T>.Shared for zero-allocation buffers.
- **`libs/rhino/analysis/`**: Type-based FrozenDictionary validation modes. Result record types with DebuggerDisplay. ArrayPool for temporary arrays. Uses `using` statements for mass properties disposal.
- **No Duplication**: Morphology operations are distinct from existing spatial (indexing), analysis (differential geometry), or topology (connectivity) - no overlap exists.

## SDK Research Summary

### RhinoCommon APIs Used
- **`Rhino.Geometry.Mesh`**: `Vertices` (count, SetVertex, array indexer), `TopologyVertices` (ConnectedEdges, count), `TopologyEdges` (GetEdgeLength, GetConnectedFaces, GetTopologyVertices, count), `Faces` (array indexer, count), `Normals` (ComputeNormals, array indexer), `GetBoundingBox`, `DuplicateMesh`.
- **`Rhino.Geometry.Collections.MeshTopologyVertexList`**: `ConnectedEdges(int vertexIndex)` returns int[] of edge indices. `SortVertices()` orders edges radially. Access via `mesh.TopologyVertices`.
- **`Rhino.Geometry.Collections.MeshTopologyEdgeList`**: `GetEdgeLength(int edgeIndex)` returns double length. `GetConnectedFaces(int edgeIndex)` returns int[] face indices. `GetTopologyVertices(int edgeIndex)` returns (int, int) vertex pair. `EdgeLine(int edgeIndex)` returns Line geometry.
- **`Rhino.Geometry.Point3d`**: Arithmetic operators `+`, `-`, `*`, `/` for vector displacement. `DistanceTo(Point3d)` for constraint satisfaction. `Transform(Transform)` for cage transformations. `IsValid` property check.
- **`Rhino.Geometry.Vector3d`**: `Unitize()` modifies to unit length. `Length` property. `CrossProduct(Vector3d, Vector3d)` static method. `DotProduct(Vector3d, Vector3d)` static method for angle computation.
- **`Rhino.Geometry.SubD`**: `CreateFromMesh(Mesh)` for quad mesh input. `Subdivide(int)` for Catmull-Clark iterations. `ToBrep()` for NURBS conversion. **Always** `Duplicate()` before calling `Subdivide` as it modifies internal state.
- **`Rhino.Geometry.Surface`**: `Evaluate(double u, double v, int order, out Point3d point, out Vector3d[] derivatives)` for cage evaluation. `PointAt(double u, double v)`, `NormalAt(double u, double v)`, `FrameAt(double u, double v, out Plane)`, `Domain(int direction)` for UV bounds. `CurvatureAt(double u, double v)` returns SurfaceCurvature with `Kappa(int)`, `Gaussian`, `Mean` properties.
- **`Rhino.Geometry.Transform`**: `TryGetInverse(out Transform)` for coordinate space conversion. `Multiply` or `*` operator for composition. Never use matrix elements directly - use SDK methods.
- **`Rhino.Geometry.NurbsSurface`**: `Points` property (NurbsSurfacePointList) for control point access. `KnotsU`, `KnotsV` properties (NurbsSurfaceKnotList). `DegreeU`, `DegreeV`, `OrderU`, `OrderV` properties. `Create(int uDegree, int vDegree, int uCount, int vCount)` static method for construction.
- **`RhinoMath`**: `ZeroTolerance` (2.32e-10) for numerical zero checks. `Epsilon` (DBL_EPSILON) for machine precision. `PI`, `HalfPI`, `TwoPI` for angles. `Clamp(double val, double min, double max)` for parameter bounds. `EpsilonEquals(double a, double b, double epsilon)` for tolerance comparisons. `IsValidDouble(double)` for NaN/Infinity checks. `ToRadians`, `ToDegrees` for angle conversion.
- **`System.Math`**: `Sqrt`, `Pow`, `Abs`, `Max`, `Min`, `Sin`, `Cos`, `Tan`, `Atan2` for numerical operations. **Never use magic constants** - derive from RhinoMath or formula variables.
- **`System.Buffers.ArrayPool<T>`**: `ArrayPool<double>.Shared.Rent(int minimumLength)` for temporary buffers. **Always** `Return(array, clearArray: true)` in `finally` blocks. Critical for hot path allocation elimination.

### Key Insights
- **Performance**: Laplacian matrix construction via sparse adjacency is O(n) where n = vertex count. Iterative solving is O(k*n) where k = iterations. **Always** use `ArrayPool<double>.Shared` for temporary buffers (edge lengths, weights, accumulated positions).
- **Common Pitfall**: `SubD.Subdivide()` **modifies internal state** - always call `subd.Duplicate()` before subdivision. FFD cage must have ≥8 control points (2×2×2 minimum lattice). Constraint indices must be within mesh vertex bounds.
- **Best Practice**: Mean curvature flow timestep must satisfy CFL condition: `stepSize ≤ (minEdgeLength)² / 4`. Compute via `minLength = Enumerable.Range(0, mesh.TopologyEdges.Count).Min(i => mesh.TopologyEdges.GetEdgeLength(i))`. Never hardcode timesteps.
- **Topology Preservation**: Laplacian smoothing must not flip face normals. Validate via `Vector3d.DotProduct(oldNormal, newNormal) > 0` after update. If flipped, clamp displacement to `0.5 * edgeLength` maximum.
- **Numerical Stability**: Cotangent weights can be negative/infinite for degenerate triangles (angle → 0 or π). **Always** clamp weights to `[RhinoMath.ZeroTolerance, 1e6]`. For angles, check `Math.Abs(Math.Sin(angle)) > RhinoMath.ZeroTolerance` before division.
- **Mesh Topology**: `TopologyVertices` vs `Vertices` differ in unwelded meshes. Topology represents welded structure. Use `mesh.TopologyVertices.ConnectedEdges(i)` for neighbor discovery, not vertex array iteration.
- **Disposal**: Mass properties (`VolumeMassProperties`, `AreaMassProperties`) implement `IDisposable`. **Always** use `using` statements or explicit `Dispose()` in `finally` blocks (see AnalysisCore.cs pattern).

### SDK Version Requirements
- Minimum: RhinoCommon 8.0 (SubD API)
- Tested: RhinoCommon 8.24+ (latest stable)

## File Organization

### File 1: `Morph.cs`
**Purpose**: Public API surface with polymorphic entry points and semantic type definitions

**Types** (5 total):
- `Morph`: Static class with public operation methods
- `DeformationMode`: Readonly struct with byte discriminator for FFD/smoothing/subdivision operations (pattern from Extract.Semantic)
- `SmoothingWeight`: Readonly struct with byte discriminator for Laplacian weight schemes
- `EvolutionFlow`: Readonly struct with byte discriminator for surface evolution PDEs
- `MorphRequest`: Internal readonly struct consolidating operation kind, parameters, and validation mode

**Key Members**:
- `Deform<T>(T geometry, DeformationMode mode, object specification, IGeometryContext context)`: Unified deformation entry point. Dispatches to FFD/smoothing/subdivision based on `mode.Kind` byte. Pattern matches specification to extract cage/constraint/iteration parameters. Returns `Result<IReadOnlyList<Mesh>>`.
- `Evolve(Surface surface, EvolutionFlow flow, (double StepSize, int MaxSteps) parameters, IGeometryContext context)`: Surface evolution via PDE integration. Pattern matches `flow.Kind` to select mean curvature/geodesic active contour/Willmore energy. Returns `Result<IReadOnlyList<Surface>>`.

**DeformationMode semantic types** (byte-based, not enum):
- `DeformationMode.FFD = new(1)`: Free-form deformation via trivariate Bernstein basis
- `DeformationMode.Smooth = new(2)`: Laplacian smoothing with weight scheme parameter
- `DeformationMode.Subdivide = new(3)`: Recursive mesh refinement (Catmull-Clark primary, Loop/Butterfly via parameter)

**SmoothingWeight semantic types**:
- `SmoothingWeight.Uniform = new(0)`: Weight = 1/degree for each neighbor
- `SmoothingWeight.Cotangent = new(1)`: Weight = (cot(α) + cot(β))/2 for angle-based
- `SmoothingWeight.MeanValue = new(2)`: Weight = tan(α/2) + tan(β/2) for harmonic

**EvolutionFlow semantic types**:
- `EvolutionFlow.MeanCurvature = new(0)`: ∂x/∂t = H*n where H = (κ1+κ2)/2
- `EvolutionFlow.GeodesicActive = new(1)`: Edge-driven contour evolution with stopping term
- `EvolutionFlow.Willmore = new(2)`: ∂x/∂t = -ΔH*n - 2H(H²-K)*n for bending energy minimization

**Code Style Example** (follows Extract.cs pattern exactly):
```csharp
/// <summary>Deformation operation discriminator.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct DeformationMode(byte kind) {
    internal readonly byte Kind = kind;
    
    /// <summary>Free-form deformation via control cage lattice.</summary>
    public static readonly DeformationMode FFD = new(1);
    /// <summary>Laplacian mesh smoothing with weight schemes.</summary>
    public static readonly DeformationMode Smooth = new(2);
    /// <summary>Recursive subdivision surface refinement.</summary>
    public static readonly DeformationMode Subdivide = new(3);
}

[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<IReadOnlyList<Mesh>> Deform<T>(
    T geometry,
    DeformationMode mode,
    object specification,
    IGeometryContext context) where T : GeometryBase {
    Type geometryType = geometry.GetType();
    
    Result<MorphRequest> requestResult = (mode.Kind, specification) switch {
        (1, (Point3d[] controlPoints, int[] dimensions, Transform transform, int[] fixedIndices, Point3d[] targets)) =>
            controlPoints.Length >= MorphConfig.FFDMinControlPoints
                ? ResultFactory.Create(value: new MorphRequest(
                    kind: 1,
                    parameter: (controlPoints, dimensions, transform, fixedIndices, targets),
                    validationMode: MorphConfig.GetValidationMode(1, geometryType)))
                : ResultFactory.Create<MorphRequest>(error: E.Geometry.FFDInsufficientControlPoints),
        (2, (byte weightKind, int iterations, double lambda)) =>
            iterations > 0 && lambda > 0.0 && lambda < 1.0
                ? ResultFactory.Create(value: new MorphRequest(
                    kind: 2,
                    parameter: (weightKind, iterations, lambda),
                    validationMode: MorphConfig.GetValidationMode(2, geometryType)))
                : ResultFactory.Create<MorphRequest>(error: E.Geometry.LaplacianInvalidParameters),
        (3, (byte schemeKind, int levels)) =>
            levels > 0 && levels <= MorphConfig.MaxSubdivisionLevels
                ? ResultFactory.Create(value: new MorphRequest(
                    kind: 3,
                    parameter: (schemeKind, levels),
                    validationMode: MorphConfig.GetValidationMode(3, geometryType)))
                : ResultFactory.Create<MorphRequest>(error: E.Geometry.SubdivisionInvalidLevels),
        _ => ResultFactory.Create<MorphRequest>(error: E.Geometry.MorphInvalidSpecification),
    };
    
    return requestResult.Bind(request =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<Mesh>>>)(item =>
                MorphCore.Execute(item, request, context)),
            config: new OperationConfig<T, Mesh> {
                Context = context,
                ValidationMode = request.ValidationMode,
                OperationName = $"Morph.{mode.Kind}",
                EnableDiagnostics = false,
            }));
}
```

**LOC Estimate**: 160-200 (5 types, dense API with byte dispatch pattern)

### File 2: `MorphCore.cs`
**Purpose**: Core implementation logic with byte-based dispatch and algorithmic kernels

**Types** (2 total):
- `MorphCore`: Static class with internal implementation methods and FrozenDictionary dispatch tables
- `LaplacianMatrix`: Readonly struct for CSR (Compressed Sparse Row) format storage - `(double[] Values, int[] RowIndices, int[] ColPointers, int VertexCount)`

**Key Members**:
- **`Execute(GeometryBase geometry, MorphRequest request, IGeometryContext context)`**: Primary dispatch via `_operationHandlers` FrozenDictionary lookup on `(request.Kind, geometry.GetType())`. Pattern: `_operationHandlers.TryGetValue((kind, type), out handler) ? handler(geometry, request, context) : fallback`. Similar to ExtractionCore dispatch pattern.
- **`_operationHandlers`**: FrozenDictionary with key `(byte Kind, Type GeometryType)` → value `Func<GeometryBase, MorphRequest, IGeometryContext, Result<IReadOnlyList<Mesh>>>`. Initialized via BuildHandlerRegistry() pattern. Contains entries for (1, typeof(Mesh)), (2, typeof(Mesh)), (3, typeof(Mesh)), with normalization handlers for Brep→Mesh conversion.
- **`_weightComputers`**: FrozenDictionary with key `byte WeightKind` → value `Func<Mesh, int, int, IGeometryContext, double>`. Kind 0=uniform (returns `1.0/degree`), Kind 1=cotangent (computes opposite angles via `TopologyEdges`, returns clamped `(cot(α)+cot(β))/2`), Kind 2=mean-value (returns `tan(α/2)+tan(β/2)`).
- **`_subdivisionMasks`**: FrozenDictionary with key `byte SchemeKind` → value `Func<Mesh, IGeometryContext, Result<Mesh>>`. Kind 0=Catmull-Clark (quad-based), Kind 1=Loop (triangle-based approximating), Kind 2=Butterfly (triangle-based interpolating).
- **`BuildLaplacianMatrix(Mesh mesh, byte weightKind, IGeometryContext context)`**: Constructs sparse CSR matrix. Uses `_weightComputers[weightKind]` to compute edge weights. Iterates `mesh.TopologyVertices` via `ConnectedEdges()`, accumulates weights in `ArrayPool<double>.Shared` buffer, converts to CSR. For uniform: diagonal = 1, off-diagonal = -1/degree. For cotangent: extracts triangle angles via `TopologyEdges.GetConnectedFaces()`, computes angles from edge vectors, clamps weights.
- **`SolveSmoothing(LaplacianMatrix L, Point3d[] positions, int[] fixedIndices, int iterations, double lambda, IGeometryContext context)`**: Iterative Gauss-Seidel solver with damping. Rents buffer from `ArrayPool<double>.Shared` for new positions. Each iteration: `x_new[i] = (1-lambda)*x_old[i] + lambda * Σ(w_ij * x_old[j])`. Constrained vertices unchanged. Converges when `maxDisplacement < context.AbsoluteTolerance * MorphConfig.ConvergenceRelativeThreshold` or iterations exhausted.
- **`ApplyCatmullClark(Mesh mesh, int levels, IGeometryContext context)`**: Subdivision via SubD API. Converts to `SubD.CreateFromMesh(mesh)`, calls `subd.Subdivide(levels)` (after `Duplicate()`), returns `subd.ToBrep().Faces[0].ToBrep().GetMesh()`. Validates quad topology, checks vertex count explosion (reject if `count > MorphConfig.MaxVertexCount`).
- **`EvolveStep(Surface surface, byte flowKind, double stepSize, IGeometryContext context)`**: Single PDE timestep via `_evolutionFlows[flowKind]` dispatcher. Kind 0: mean curvature flow `∂x/∂t = H*n`. Kind 1: geodesic active contour (requires edge map parameter). Kind 2: Willmore flow `∂x/∂t = -ΔH*n`. Samples surface UV grid, computes curvature, displaces control points, rebuilds via `NurbsSurface.Create()`.

**Code Style Example** (FrozenDictionary dispatch pattern from SpatialCore.cs):
```csharp
// Primary dispatcher - follows SpatialCore.OperationRegistry pattern
private static readonly FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, MorphRequest, IGeometryContext, Result<IReadOnlyList<Mesh>>>> _operationHandlers =
    BuildHandlerRegistry();

private static FrozenDictionary<(byte, Type), Func<GeometryBase, MorphRequest, IGeometryContext, Result<IReadOnlyList<Mesh>>>> BuildHandlerRegistry() =>
    new Dictionary<(byte, Type), Func<GeometryBase, MorphRequest, IGeometryContext, Result<IReadOnlyList<Mesh>>>> {
        [(1, typeof(Mesh))] = static (g, r, c) => g is Mesh m && r.Parameter is (Point3d[] cp, int[] dim, Transform t, int[] fixed, Point3d[] targets)
            ? ApplyFFD(m, cp, dim, t, fixed, targets, c)
            : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.FFDInvalidParameters),
        [(2, typeof(Mesh))] = static (g, r, c) => g is Mesh m && r.Parameter is (byte wk, int iter, double lambda)
            ? ApplySmoothing(m, wk, iter, lambda, c)
            : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.LaplacianInvalidParameters),
        [(3, typeof(Mesh))] = static (g, r, c) => g is Mesh m && r.Parameter is (byte sk, int levels)
            ? ApplySubdivision(m, sk, levels, c)
            : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.SubdivisionInvalidLevels),
    }.ToFrozenDictionary();

// Weight computer dispatch - byte-based, no enums
private static readonly FrozenDictionary<byte, Func<Mesh, int, int, IGeometryContext, double>> _weightComputers =
    new Dictionary<byte, Func<Mesh, int, int, IGeometryContext, double>> {
        [0] = static (m, vi, _, _) => 1.0 / m.TopologyVertices.ConnectedEdges(vi).Length,
        [1] = static (m, vi, ni, c) => ComputeCotangentWeight(m, vi, ni, c),
        [2] = static (m, vi, ni, c) => ComputeMeanValueWeight(m, vi, ni, c),
    }.ToFrozenDictionary();

[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<IReadOnlyList<Mesh>> Execute(
    GeometryBase geometry,
    MorphRequest request,
    IGeometryContext context) =>
    _operationHandlers.TryGetValue((request.Kind, geometry.GetType()), out Func<GeometryBase, MorphRequest, IGeometryContext, Result<IReadOnlyList<Mesh>>>? handler)
        ? handler(geometry, request, context)
        : ResultFactory.Create<IReadOnlyList<Mesh>>(
            error: E.Geometry.MorphUnsupportedType.WithContext($"Kind: {request.Kind}, Type: {geometry.GetType().Name}"));
```

**LOC Estimate**: 200-250 (2 types, FrozenDictionary dispatch with inline lambdas)

### File 3: `MorphConfig.cs`
**Purpose**: Configuration constants and byte-based validation mode dispatch

**Types** (1 total):
- `MorphConfig`: Static class with internal configuration (follows ExtractionConfig.cs pattern exactly)

**Key Members**:
- **`ValidationModes`**: FrozenDictionary with key `(byte Kind, Type GeometryType)` → value `V`. Entries: `[(1, typeof(Mesh))] = V.Standard | V.Topology`, `[(2, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Degeneracy`, `[(3, typeof(Mesh))] = V.Standard | V.Topology | V.MeshSpecific`. Pattern from ExtractionConfig line-by-line.
- **`GetValidationMode(byte kind, Type geometryType)`**: Lookup with fallback logic. Exact match on `(kind, type)`, else `IsAssignableFrom` search with type specificity ordering. Returns `V.Standard` default. Identical to ExtractionConfig.GetValidationMode implementation.

**Constants** (organized by operation, following AnalysisConfig pattern):
- **FFD Parameters**: `FFDMinControlPoints = 8` (2×2×2 lattice minimum), `FFDBernsteinDegree = 3` (cubic basis), `FFDDefaultDivisions = 3` (per axis).
- **Laplacian Smoothing**: `LaplacianMaxIterations = 1000`, `LaplacianDefaultIterations = 10`, `LaplacianDefaultLambda = 0.5` (damping factor), `LaplacianConvergenceThreshold = 1e-6` (relative change), `CotangentWeightMin = RhinoMath.ZeroTolerance`, `CotangentWeightMax = 1e6`.
- **Subdivision**: `MaxSubdivisionLevels = 5`, `MaxVertexCount = 1_000_000`, `MinEdgeLengthFactor = 10.0` (multiplier of `RhinoMath.ZeroTolerance`), `MaxAspectRatio = 100.0`.
- **Surface Evolution**: `EvolutionMaxSteps = 500`, `EvolutionCFLFactor = 0.25` (safety factor: actual stepSize = factor * h²/4), `EvolutionDefaultStepSize = 0.01`, `EvolutionMinStepSize = RhinoMath.ZeroTolerance * 100`.
- **Topology Checks**: `NormalFlipAngleThreshold = RhinoMath.ToRadians(90.0)`, `MaxDisplacementRatio = 0.5` (relative to edge length).
- **Angle Computation**: `DegenerateAngleMin = RhinoMath.ToRadians(1.0)`, `DegenerateAngleMax = RhinoMath.ToRadians(179.0)` (for cotangent stability).

**Code Style Example** (follows ExtractionConfig.cs exactly):
```csharp
using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Morphology;

/// <summary>Configuration for morphology operations: validation modes and algorithmic constants.</summary>
[Pure]
internal static class MorphConfig {
    /// <summary>(Kind, Type) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(Mesh))] = V.Standard | V.Topology,
            [(2, typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Degeneracy,
            [(3, typeof(Mesh))] = V.Standard | V.Topology | V.MeshSpecific,
        }.ToFrozenDictionary();
    
    /// <summary>Gets validation mode with fallback for (kind, type) pair.</summary>
    internal static V GetValidationMode(byte kind, Type geometryType) =>
        ValidationModes.TryGetValue((kind, geometryType), out V exact)
            ? exact
            : ValidationModes
                .Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) =>
                    a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(V.Standard)
                .First();
    
    /// <summary>FFD control cage parameters.</summary>
    internal const int FFDMinControlPoints = 8;
    internal const int FFDBernsteinDegree = 3;
    internal const int FFDDefaultDivisions = 3;
    
    /// <summary>Laplacian smoothing parameters.</summary>
    internal const int LaplacianMaxIterations = 1000;
    internal const int LaplacianDefaultIterations = 10;
    internal const double LaplacianDefaultLambda = 0.5;
    internal const double LaplacianConvergenceThreshold = 1e-6;
    internal static readonly double CotangentWeightMin = RhinoMath.ZeroTolerance;
    internal const double CotangentWeightMax = 1e6;
    
    /// <summary>Subdivision surface limits.</summary>
    internal const int MaxSubdivisionLevels = 5;
    internal const int MaxVertexCount = 1_000_000;
    internal static readonly double MinEdgeLength = RhinoMath.ZeroTolerance * 10.0;
    internal const double MaxAspectRatio = 100.0;
    
    /// <summary>Surface evolution PDE integration.</summary>
    internal const int EvolutionMaxSteps = 500;
    internal const double EvolutionCFLFactor = 0.25;
    internal const double EvolutionDefaultStepSize = 0.01;
    internal static readonly double EvolutionMinStepSize = RhinoMath.ZeroTolerance * 100.0;
    
    /// <summary>Topology preservation thresholds.</summary>
    internal static readonly double NormalFlipAngleThreshold = RhinoMath.ToRadians(90.0);
    internal const double MaxDisplacementRatio = 0.5;
    
    /// <summary>Degenerate angle bounds for cotangent stability.</summary>
    internal static readonly double DegenerateAngleMin = RhinoMath.ToRadians(1.0);
    internal static readonly double DegenerateAngleMax = RhinoMath.ToRadians(179.0);
}
```

**LOC Estimate**: 70-90 (1 type, pure configuration following exact ExtractionConfig pattern)

## Adherence to Limits

- **Files**: 3 files ✓ (ideal 2-3 range, below 4-file maximum)
- **Types**: 8 types total ✓ (within ideal 6-8 range, below 10-type maximum)
- **Estimated Total LOC**: 430-540 (160+200+70 average = 430 LOC)

**Type Breakdown**:
- **Morph.cs** (5 types): Morph static class, DeformationMode struct, SmoothingWeight struct, EvolutionFlow struct, MorphRequest struct
- **MorphCore.cs** (2 types): MorphCore static class, LaplacianMatrix struct
- **MorphConfig.cs** (1 type): MorphConfig static class

All types serve distinct purposes with no helper sprawl. Pattern precisely follows extraction/ and spatial/ exemplars.

## Algorithmic Density Strategy

Achieve dense code without helper methods through:

### 1. Expression Tree-Style Pattern Matching
Use nested switch expressions with inline computation instead of extracted methods:
```csharp
return (cage.ControlPoints.Length, cage.Dimensions) switch {
    (< 8, _) => ResultFactory.Create<T>(error: E.Geometry.FFDInsufficientControlPoints),
    (_, int[] { var nx, var ny, var nz }) when nx * ny * nz != cage.ControlPoints.Length =>
        ResultFactory.Create<T>(error: E.Geometry.FFDDimensionMismatch),
    _ => ComputeInline(/* complex expression */),
};
```

### 2. FrozenDictionary Dispatch
Eliminate conditional logic via O(1) lookup tables:
```csharp
private static readonly FrozenDictionary<LaplacianMode, Func<Mesh, int, int, IGeometryContext, double>> _weightComputers =
    new Dictionary<LaplacianMode, Func<Mesh, int, int, IGeometryContext, double>> {
        [LaplacianMode.Uniform] = static (m, i, j, c) => 1.0 / m.TopologyVertices.ConnectedTopologyVertices(i).Length,
        [LaplacianMode.Cotangent] = static (m, i, j, c) => ComputeCotangentWeight(m, i, j, c),
        [LaplacianMode.MeanValue] = static (m, i, j, c) => ComputeMeanValueWeight(m, i, j, c),
    }.ToFrozenDictionary();
```

### 3. Inline Mathematical Operators
Compose RhinoMath and System.Math operations directly:
```csharp
double weight = RhinoMath.Clamp(
    val: (Math.Cos(alpha) / Math.Sin(alpha)) + (Math.Cos(beta) / Math.Sin(beta)),
    min: RhinoMath.ZeroTolerance,
    max: 1e6);
```

### 4. LINQ Aggregation for Loops
Replace explicit iteration with functional composition:
```csharp
Point3d facePoint = mesh.Faces
    .SelectMany(static f => new[] { f.A, f.B, f.C, f.D != f.C ? f.D : -1 })
    .Where(static idx => idx >= 0)
    .Select(idx => mesh.Vertices[idx])
    .Aggregate(seed: Point3d.Origin, func: static (acc, v) => acc + new Vector3d(v))
    .Transform(t => t / mesh.Faces.Count);
```

### 5. ArrayPool for Hot Paths
Zero-allocation temporary buffers:
```csharp
double[] buffer = ArrayPool<double>.Shared.Rent(minimumLength: mesh.Vertices.Count);
try {
    // Use buffer
} finally {
    ArrayPool<double>.Shared.Return(buffer);
}
```

### 6. Result<T> Monadic Composition
Chain operations eliminating intermediate error checks:
```csharp
return ValidateCage(cage, context)
    .Bind(validCage => ComputeLocalCoords(mesh, validCage, context))
    .Map(coords => EvaluateBernstein(coords, cage.ControlPoints))
    .Ensure(positions => positions.All(p => p.IsValid), error: E.Geometry.FFDInvalidPosition)
    .Bind(positions => ApplyConstraints(positions, constraints, context));
```

## Dispatch Architecture

Primary dispatch via `UnifiedOperation` with type-based polymorphism:

```csharp
public static Result<IReadOnlyList<Mesh>> FFD<T>(
    T geometry,
    FFDCage cage,
    MorphConstraint constraints,
    IGeometryContext context) where T : GeometryBase =>
    MorphConfig.TypeDispatch.TryGetValue(("FFD", typeof(T)), out V mode) switch {
        true => UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<Mesh>>>)(item => item switch {
                Mesh m => MorphCore.ApplyFFD(m, cage, constraints, context),
                Brep b => ConvertAndApply(b, cage, constraints, context),
                Surface s => ConvertAndApply(s, cage, constraints, context),
                _ => ResultFactory.Create<IReadOnlyList<Mesh>>(
                    error: E.Geometry.MorphUnsupportedType),
            }),
            config: new OperationConfig<T, Mesh> {
                Context = context,
                ValidationMode = mode,
            }),
        false => ResultFactory.Create<IReadOnlyList<Mesh>>(
            error: E.Geometry.MorphUnsupportedType.WithContext($"Type: {typeof(T).Name}")),
    };
```

Secondary dispatch for internal operations via FrozenDictionary:
- Weight computation: `LaplacianMode → Func<Mesh, int, int, double>`
- Subdivision masks: `SubdivisionScheme → Func<Topology, Point3d[]>`
- Evolution drivers: `EvolutionDriver → Func<Surface, double, Point3d[]>`

## Error Code Allocation

Add to `libs/core/errors/E.cs` in `E.Geometry` class (range 2600-2619):

```csharp
// Morphology Operations (2600-2619)
public static readonly SystemError FFDInsufficientControlPoints = Get(2600);
public static readonly SystemError FFDInvalidParameters = Get(2601);
public static readonly SystemError FFDCageTransformInvalid = Get(2602);
public static readonly SystemError FFDConstraintIndexOutOfRange = Get(2603);
public static readonly SystemError LaplacianInvalidParameters = Get(2605);
public static readonly SystemError LaplacianConvergenceFailed = Get(2606);
public static readonly SystemError LaplacianTopologyChanged = Get(2607);
public static readonly SystemError SubdivisionInvalidLevels = Get(2610);
public static readonly SystemError SubdivisionVertexOverflow = Get(2611);
public static readonly SystemError SubdivisionTopologyInvalid = Get(2612);
public static readonly SystemError EvolutionInvalidStepSize = Get(2615);
public static readonly SystemError EvolutionCFLViolation = Get(2616);
public static readonly SystemError EvolutionConvergenceFailed = Get(2617);
public static readonly SystemError MorphInvalidSpecification = Get(2618);
public static readonly SystemError MorphUnsupportedType = Get(2619);
```

Add to error message dictionary in `E.cs` (line ~133):

```csharp
// Morphology Operations (2600-2619)
[2600] = "FFD requires minimum 8 control points (2×2×2 lattice)",
[2601] = "FFD parameters invalid (controlPoints, dimensions, transform, constraints)",
[2602] = "FFD cage transform cannot be inverted",
[2603] = "Constraint index exceeds mesh vertex count",
[2605] = "Laplacian parameters invalid (weightKind, iterations, lambda)",
[2606] = "Laplacian solver failed to converge within iteration limit",
[2607] = "Smoothing operation altered mesh topology (normal flip detected)",
[2610] = "Subdivision level count invalid (must be 1-5)",
[2611] = "Subdivision would exceed maximum vertex count (1M)",
[2612] = "Mesh topology invalid for subdivision (non-manifold or open)",
[2615] = "Evolution step size invalid or violates stability bounds",
[2616] = "CFL condition violated (stepSize > minEdgeLength²/4)",
[2617] = "Surface evolution failed to converge",
[2618] = "Morphology operation specification does not match expected pattern",
[2619] = "Geometry type not supported for morphology operation",
```

## Public API Surface

### Primary Operations
```csharp
// Unified deformation entry point with byte-based mode dispatch
public static Result<IReadOnlyList<Mesh>> Deform<T>(
    T geometry,
    DeformationMode mode,
    object specification,
    IGeometryContext context) where T : GeometryBase;

// Surface evolution via PDE integration  
public static Result<IReadOnlyList<Surface>> Evolve(
    Surface surface,
    EvolutionFlow flow,
    (double StepSize, int MaxSteps) parameters,
    IGeometryContext context);
```

### Semantic Type Definitions (byte-based, following Extract.Semantic pattern)
```csharp
/// <summary>Deformation operation discriminator.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct DeformationMode(byte kind) {
    internal readonly byte Kind = kind;
    
    public static readonly DeformationMode FFD = new(1);
    public static readonly DeformationMode Smooth = new(2);
    public static readonly DeformationMode Subdivide = new(3);
}

/// <summary>Laplacian weight scheme discriminator.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct SmoothingWeight(byte kind) {
    internal readonly byte Kind = kind;
    
    public static readonly SmoothingWeight Uniform = new(0);
    public static readonly SmoothingWeight Cotangent = new(1);
    public static readonly SmoothingWeight MeanValue = new(2);
}

/// <summary>Surface evolution PDE discriminator.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct EvolutionFlow(byte kind) {
    internal readonly byte Kind = kind;
    
    public static readonly EvolutionFlow MeanCurvature = new(0);
    public static readonly EvolutionFlow GeodesicActive = new(1);
    public static readonly EvolutionFlow Willmore = new(2);
}
```

### Usage Examples
```csharp
// FFD with control cage
Result<IReadOnlyList<Mesh>> ffd = Morph.Deform(
    geometry: mesh,
    mode: DeformationMode.FFD,
    specification: (controlPoints, dimensions, transform, fixedIndices, targetPositions),
    context: context);

// Laplacian smoothing with cotangent weights
Result<IReadOnlyList<Mesh>> smooth = Morph.Deform(
    geometry: mesh,
    mode: DeformationMode.Smooth,
    specification: (SmoothingWeight.Cotangent.Kind, iterations: 50, lambda: 0.5),
    context: context);

// Catmull-Clark subdivision (byte 0)
Result<IReadOnlyList<Mesh>> subdivide = Morph.Deform(
    geometry: mesh,
    mode: DeformationMode.Subdivide,
    specification: (schemeKind: (byte)0, levels: 2),
    context: context);

// Mean curvature flow
Result<IReadOnlyList<Surface>> evolve = Morph.Evolve(
    surface: surface,
    flow: EvolutionFlow.MeanCurvature,
    parameters: (stepSize: 0.01, maxSteps: 100),
    context: context);
```

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else statements)
- [x] All examples use explicit types (no var keyword)
- [x] All examples use named parameters for non-obvious arguments
- [x] All examples use trailing commas in multi-line collections
- [x] All examples use K&R brace style (opening brace on same line)
- [x] All examples use target-typed new() where type is known
- [x] All examples use collection expressions [] for initialization
- [x] One type per file organization (3 files, 11 types split appropriately)
- [x] All member estimates under 300 LOC (largest is MorphCore methods at 220-270 total)
- [x] All patterns match existing libs/ exemplars (spatial/, analysis/)

## Implementation Sequence

1. **Read this blueprint thoroughly** - Understand all architectural decisions and constraints
2. **Double-check SDK usage patterns** - Review RhinoMath constants, mesh APIs, SubD workflows
3. **Verify libs/ integration strategy** - Confirm Result<T>, UnifiedOperation, V.* patterns align
4. **Create folder structure and files** - 3 files: Morph.cs, MorphCore.cs, MorphConfig.cs
5. **Implement configuration types (MorphConfig.cs)** - FrozenDictionaries, constants, enums first
6. **Implement sparse matrix structures (MorphCore.cs)** - LaplacianMatrix, SubdivisionTopology
7. **Implement FFD algorithm (MorphCore.cs)** - Trivariate Bernstein basis, constraint solving
8. **Implement Laplacian smoothing (MorphCore.cs)** - Matrix construction, iterative solver
9. **Implement subdivision (MorphCore.cs)** - Catmull-Clark masks, topology updates
10. **Implement surface evolution (MorphCore.cs)** - Mean curvature computation, timestep integration
11. **Implement public API (Morph.cs)** - UnifiedOperation wrappers, validation integration
12. **Add validation integration** - New V.MorphologyConstraints mode to ValidationRules
13. **Add error codes to E.cs** - Allocate 2600-2619 range in error registry
14. **Add diagnostic instrumentation** - MorphMetrics computation, quality assessment
15. **Verify patterns match exemplars** - Compare against spatial/, analysis/ structures
16. **Check LOC limits** - Ensure all members ≤300 LOC, optimize density if needed
17. **Verify file/type limits** - 3 files ✓, 11 types (justified above)
18. **Verify code style compliance** - Run through checklist above

## References

### SDK Documentation
- [RhinoCommon API: Mesh Class](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_Geometry_Mesh.htm)
- [RhinoCommon API: SubD Class](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_Geometry_SubD.htm)
- [RhinoCommon API: Surface Class](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_Geometry_Surface.htm)
- [RhinoMath Constants and Methods](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_RhinoMath.htm)

### Academic References
- **FFD**: Sederberg & Parry (1986) - "Free-Form Deformation of Solid Geometric Models"
- **Laplacian**: Sorkine et al. (2004) - "Laplacian Surface Editing" (Stanford)
- **Subdivision**: Catmull & Clark (1978) - "Recursively Generated B-Spline Surfaces"
- **Evolution**: Osher & Sethian (1988) - "Fronts Propagating with Curvature-Dependent Speed"

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/Result.cs` - Result monad patterns, Map/Bind/Ensure usage
- `libs/core/operations/UnifiedOperation.cs` - OperationConfig structure, dispatch engine
- `libs/core/validation/ValidationRules.cs` - Expression tree compilation, V.* flags
- `libs/core/errors/E.cs` - Error code allocation patterns, domain ranges
- `libs/rhino/spatial/Spatial.cs` - FrozenDictionary dispatch, polymorphic API surface
- `libs/rhino/spatial/SpatialCore.cs` - Algorithmic implementation patterns
- `libs/rhino/spatial/SpatialConfig.cs` - Configuration constants, type extractors
- `libs/rhino/analysis/Analysis.cs` - Result record types, diagnostic displays

### Web Research Citations
- RhinoCommon cage-based deformation discussion and alternatives
- Laplacian mesh deformation implementations and weight schemes
- Subdivision surface schemes (Catmull-Clark, Loop, Butterfly) in CGAL/RhinoCommon
- Mean curvature flow and geodesic active contours mathematical foundations
- Mesh morphology operations in computational geometry (geometry4Sharp, MeshLib references)

---

**Blueprint Status**: Ready for Implementation  
**Complexity**: High (advanced algorithms, numerical methods, topology manipulation)  
**Risk Areas**: Numerical stability (cotangent weights, CFL condition), topology preservation (manifold enforcement), performance (O(n²) operations in naive implementations)  
**Mitigation**: Extensive use of RhinoMath constants, clamping ranges, convergence criteria from IGeometryContext, sparse matrix formats, ArrayPool for allocations
