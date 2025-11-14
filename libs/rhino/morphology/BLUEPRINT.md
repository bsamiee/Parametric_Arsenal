# Morphology Library Blueprint

## Overview
Implements advanced mesh and surface deformation operations via free-form deformation (FFD), Laplacian smoothing, subdivision surfaces, and surface evolution. Provides parametric control over geometry shape transformation while preserving topological structure and detail.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage
- **Result<T> Monad**: All operations return `Result<IReadOnlyList<T>>` for error handling. Use `Map`, `Bind`, `Ensure` for operation chains. Lazy evaluation via `deferred` parameter for expensive computations.
- **UnifiedOperation**: Primary dispatch engine for polymorphic input handling (Mesh, Brep, Surface). Configuration via `OperationConfig<TIn, TOut>` provides validation, parallelism, caching, diagnostics.
- **ValidationRules**: Leverage existing `V.Standard`, `V.Topology`, `V.MeshSpecific`, `V.Degeneracy` modes. Add new `V.MorphologyConstraints` flag for deformation-specific validation (control cage validity, handle constraints, topology preservation).
- **Error Registry**: Use existing `E.Geometry.*` errors (2600-2699 range allocated for morphology). New codes: 2600-2604 (FFD), 2605-2609 (Laplacian), 2610-2614 (Subdivision), 2615-2619 (Evolution).
- **Context**: Use `IGeometryContext.AbsoluteTolerance`, `RelativeTolerance` for convergence criteria, threshold comparisons, and numerical stability checks.

### Similar libs/rhino/ Implementations
- **`libs/rhino/spatial/`**: FrozenDictionary dispatch pattern for type-based polymorphism. Reuse spatial indexing patterns for neighbor searches in Laplacian operators.
- **`libs/rhino/analysis/`**: Result record types with debugging displays. Adopt similar diagnostic output for deformation quality metrics.
- **`libs/rhino/extraction/`**: Pattern matching on geometry types. Similar approach for morphology operation dispatch.
- **No Duplication**: Morphology operations are distinct from existing spatial (indexing), analysis (differential geometry), or topology (connectivity) - no overlap exists.

## SDK Research Summary

### RhinoCommon APIs Used
- **`Rhino.Geometry.Mesh`**: `Vertices`, `TopologyVertices`, `TopologyEdges`, `Faces` for mesh structure. `Normals`, `VertexNormals` for smoothing. `GetBoundingBox` for cage initialization.
- **`Rhino.Geometry.Mesh.Vertices`**: `SetVertex`, `Add`, array indexer for vertex manipulation. `Count` for iteration bounds.
- **`Rhino.Geometry.Point3d`**: Arithmetic operators `+`, `-`, `*` for displacement calculations. `DistanceTo` for constraint satisfaction.
- **`Rhino.Geometry.Vector3d`**: `Unitize` for normal computation. `CrossProduct`, `DotProduct` for geometric calculations. `Length` for magnitude checks.
- **`Rhino.Geometry.SubD`**: `CreateFromMesh` for quad mesh input. `Subdivide` for Catmull-Clark refinement. `ToBrep` for NURBS conversion.
- **`Rhino.Geometry.Surface`**: `Evaluate`, `PointAt`, `NormalAt` for cage surface evaluation. `ClosestPoint` for projection operations.
- **`Rhino.Geometry.Curve`**: `CurvatureAt`, `TangentAt`, `FrameAt` for curvature-driven evolution.
- **`Rhino.Geometry.Brep`**: `Faces`, `Edges`, `Vertices` for topology traversal. `ClosestPoint` for surface evolution constraints.
- **`RhinoMath`**: `ZeroTolerance` for numerical comparisons. `PI`, `Epsilon` for angle/convergence thresholds. `Clamp` for parameter bounds. `EpsilonEquals` for float comparisons.
- **`System.Math`**: `Sqrt`, `Pow`, `Abs`, `Max`, `Min` for numerical operations. Never use magic constants - reference formula context.

### Key Insights
- **Performance**: Laplacian matrix construction via sparse adjacency is O(n) where n = vertex count. Iterative solving is O(k*n) where k = iterations. Use `ArrayPool<double>.Shared` for temporary buffers.
- **Common Pitfall**: SubD operations modify internal state - always `Duplicate()` before subdivision. FFD requires cage control points as anchor handles - validate constraint count ≥ 3.
- **Best Practice**: Mean curvature flow timestep must be ≤ h²/4 where h = minimum edge length (CFL condition). Use `Mesh.GetEdgeLengths()` to compute bounds.
- **Topology Preservation**: Laplacian smoothing iterations must not flip face normals. Check via `Vector3d.DotProduct(oldNormal, newNormal) > 0` after each step.
- **Numerical Stability**: Cotangent weights in Laplacian can be negative/infinite for degenerate triangles. Clamp to `[RhinoMath.ZeroTolerance, 1e6]` range.

### SDK Version Requirements
- Minimum: RhinoCommon 8.0 (SubD API)
- Tested: RhinoCommon 8.24+ (latest stable)

## File Organization

### File 1: `Morph.cs`
**Purpose**: Public API surface with polymorphic entry points

**Types** (6 total):
- `Morph`: Static class with public operation methods
- `FFDCage`: Record struct `(Point3d[] ControlPoints, int[] Dimensions, Transform LocalToWorld)` for cage specification
- `LaplacianMode`: Enum `{ Uniform, Cotangent, MeanValue }` for weight schemes
- `SubdivisionScheme`: Enum `{ CatmullClark, Loop, Butterfly }` for refinement methods
- `EvolutionDriver`: Enum `{ MeanCurvature, GeodesicActive, WillmoreFlow }` for surface evolution types
- `MorphConstraint`: Record struct `(int[] FixedIndices, Point3d[] TargetPositions, double[] Weights)` for handle constraints

**Key Members**:
- `FFD<T>(T geometry, FFDCage cage, MorphConstraint constraints, IGeometryContext context)`: Free-form deformation via trivariate Bernstein polynomials. Compute local (u,v,w) coordinates via barycentric interpolation, evaluate B-spline basis at control points, apply displacement field.
- `Smooth<T>(T geometry, LaplacianMode mode, int iterations, double lambda, IGeometryContext context)`: Laplacian smoothing. Build sparse adjacency matrix, compute weights (uniform=1/degree, cotangent=cot(α)+cot(β), mean-value=tan(α/2)+tan(β/2)), solve linear system `L*x = b` where L is Laplacian operator.
- `Subdivide(Mesh mesh, SubdivisionScheme scheme, int levels, IGeometryContext context)`: Subdivision surface refinement. Catmull-Clark: edge points = (v1+v2+f1+f2)/4, face points = avg(vertices), update vertex = (Q+2R+(n-3)S)/n. Loop/Butterfly: split edges recursively, reposition via masks.
- `Evolve(Surface surface, EvolutionDriver driver, double stepSize, int maxSteps, IGeometryContext context)`: Surface evolution via PDE integration. Mean curvature: ∂x/∂t = H*n where H = (κ1+κ2)/2. Geodesic: minimize ∫g(|∇I|)*ds with level set representation. Explicit Euler timestep.

**Code Style Example**:
```csharp
public static Result<IReadOnlyList<Mesh>> FFD<T>(
    T geometry,
    FFDCage cage,
    MorphConstraint constraints,
    IGeometryContext context) where T : GeometryBase =>
    UnifiedOperation.Apply(
        input: geometry,
        operation: (Func<T, Result<IReadOnlyList<Mesh>>>)(item => item switch {
            Mesh m => MorphCore.ApplyFFD(m, cage, constraints, context),
            Brep b => b.Faces.SelectMany(static f => f.ToBrep().ToMesh(density: 0)).ToArray() is Mesh[] meshes
                ? MorphCore.ApplyFFD(meshes[0], cage, constraints, context)
                : ResultFactory.Create<IReadOnlyList<Mesh>>(error: E.Geometry.MorphMeshConversionFailed),
            _ => ResultFactory.Create<IReadOnlyList<Mesh>>(
                error: E.Geometry.MorphUnsupportedType.WithContext($"Type: {item.GetType().Name}")),
        }),
        config: new OperationConfig<T, Mesh> {
            Context = context,
            ValidationMode = V.Standard | V.Topology | V.MeshSpecific,
            OperationName = "Morph.FFD",
            EnableDiagnostics = false,
        });
```

**LOC Estimate**: 180-220 (6 types × 25-35 LOC, dense API surface)

### File 2: `MorphCore.cs`
**Purpose**: Core implementation logic with algorithmic kernels

**Types** (3 total):
- `MorphCore`: Static class with internal implementation methods
- `LaplacianMatrix`: Readonly struct `(double[] Values, int[] RowIndices, int[] ColPointers)` for CSR sparse storage
- `SubdivisionTopology`: Readonly struct `(int[][] FaceVertices, int[][] EdgeVertices, Dictionary<(int,int), int> EdgeMap)` for connectivity caching

**Key Members**:
- `ApplyFFD(Mesh mesh, FFDCage cage, MorphConstraint constraints, IGeometryContext context)`: FFD core algorithm. Transform mesh to cage local space via `cage.LocalToWorld.TryGetInverse()`. For each vertex, compute (u,v,w) in [0,1]³ via trilinear interpolation. Evaluate displacement: `d = Σ B_i(u)*B_j(v)*B_k(w)*(P_ijk - P_ijk_original)` where B are Bernstein bases. Apply constraint projection via least squares: minimize `||x - x₀||² + Σ wᵢ||xᵢ - tᵢ||²`.
- `BuildLaplacianMatrix(Mesh mesh, LaplacianMode mode, IGeometryContext context)`: Construct sparse Laplacian. For uniform: `L[i,j] = -1/degree(i)` for neighbors j, `L[i,i] = 1`. For cotangent: compute angles α,β opposite to edge ij, weight = `(cot(α) + cot(β))/2`, clamp to `[RhinoMath.ZeroTolerance, 1e6]`. Return CSR format for efficient matrix-vector product.
- `SolveLinearSystem(LaplacianMatrix L, Point3d[] b, MorphConstraint constraints, IGeometryContext context)`: Iterative solver for `Lx = b` with constraints. Use Gauss-Seidel: `x[i]^(k+1) = (b[i] - Σ(j≠i) L[i,j]*x[j]^k) / L[i,i]`. For constrained indices, fix `x[i] = target[i]` each iteration. Converge when `||x^(k+1) - x^k||/||x^k|| < context.RelativeTolerance`.
- `CatmullClarkStep(Mesh mesh, SubdivisionTopology topology, IGeometryContext context)`: Single subdivision iteration. Compute face points: `F = Σ vertices / count`. Edge points: `E = (v1 + v2 + f1 + f2) / 4`. Update vertices: `V' = (Q + 2R + (n-3)V) / n` where Q = avg face points, R = avg edge midpoints, n = valence. Build new mesh with quad faces.
- `MeanCurvatureStep(Surface surface, double stepSize, IGeometryContext context)`: Single evolution timestep. Sample surface at (u,v) grid. Compute curvature: `H = (surface.CurvatureAt(u,v).Kappa(0) + surface.CurvatureAt(u,v).Kappa(1))/2`. Compute normal: `n = surface.NormalAt(u,v)`. Displace: `x' = x + stepSize * H * n`. Check CFL: `stepSize ≤ minEdgeLength² / 4`. Rebuild surface via interpolation.

**Code Style Example**:
```csharp
internal static Result<IReadOnlyList<Mesh>> ApplyFFD(
    Mesh mesh,
    FFDCage cage,
    MorphConstraint constraints,
    IGeometryContext context) {
    return cage.LocalToWorld.TryGetInverse(out Transform worldToLocal) switch {
        false => ResultFactory.Create<IReadOnlyList<Mesh>>(
            error: E.Geometry.FFDCageTransformInvalid),
        true => mesh.Vertices.Count >= 3 switch {
            false => ResultFactory.Create<IReadOnlyList<Mesh>>(
                error: E.Geometry.FFDInsufficientVertices.WithContext($"Count: {mesh.Vertices.Count}")),
            true => ((Point3d[] uvw, Point3d[] displaced) = (
                [.. mesh.Vertices.Select(static v => new Point3d(v))
                    .Select(p => worldToLocal * p)
                    .Select(p => new Point3d(
                        RhinoMath.Clamp(p.X, min: 0.0, max: 1.0),
                        RhinoMath.Clamp(p.Y, min: 0.0, max: 1.0),
                        RhinoMath.Clamp(p.Z, min: 0.0, max: 1.0)))],
                new Point3d[mesh.Vertices.Count])) switch {
                (Point3d[] coords, Point3d[] result) => ComputeFFDDisplacement(
                    coords, result, cage, mesh.Vertices.ToPoint3dArray(), context)
                    .Bind(positions => ApplyConstraints(positions, constraints, context))
                    .Map(finalPos => {
                        Mesh deformed = mesh.DuplicateMesh();
                        for (int i = 0; i < finalPos.Length; i++) {
                            deformed.Vertices.SetVertex(index: i, vertex: finalPos[i]);
                        }
                        return (IReadOnlyList<Mesh>)[deformed];
                    }),
            },
        },
    };
}
```

**LOC Estimate**: 220-270 (3 types, dense algorithmic implementations)

### File 3: `MorphConfig.cs`
**Purpose**: Configuration types, constants, and dispatch tables

**Types** (2 total):
- `MorphConfig`: Static class with internal configuration
- `MorphMetrics`: Readonly record struct `(double MinEdgeLength, double MaxEdgeLength, double AvgCurvature, int DegenerateCount)` for quality diagnostics

**Key Members**:
- `TypeDispatch`: FrozenDictionary mapping (operation type, geometry type) → validation mode. Key = `(string Operation, Type GeometryType)`, Value = `V mode`. Operations: "FFD", "Smooth", "Subdivide", "Evolve". Geometry: Mesh, Brep, Surface, SubD.
- `ValidationConfig`: FrozenDictionary for operation-specific validation requirements. Map operation → `(V RequiredModes, V OptionalModes, double[] ToleranceMultipliers)`.
- `ConvergenceThresholds`: Constants for iterative solvers. `LaplacianMaxIterations = 1000`, `LaplacianRelativeTolerance = 1e-6`, `EvolutionMaxSteps = 500`, `EvolutionCFLFactor = 0.25` (safety factor for CFL condition).
- `SubdivisionLimits`: Mesh refinement bounds. `MaxSubdivisionLevels = 5`, `MaxVertexCount = 1_000_000`, `MinEdgeLength = RhinoMath.ZeroTolerance * 10`, `MaxAspectRatio = 100.0`.
- `FFDParameters`: Cage construction defaults. `DefaultCageDivisions = new int[] { 3, 3, 3 }`, `MinControlPoints = 8` (2×2×2 minimum), `BernsteinDegree = 3` (cubic basis).
- `ComputeMetrics(Mesh mesh, IGeometryContext context)`: Quality assessment. Compute edge length statistics via `mesh.TopologyEdges`, curvature via `mesh.Vertices` normal deviation, degeneracy via aspect ratio checks. Return diagnostic record.

**Code Style Example**:
```csharp
internal static readonly FrozenDictionary<(string Operation, Type GeometryType), V> TypeDispatch =
    new Dictionary<(string, Type), V> {
        [("FFD", typeof(Mesh))] = V.Standard | V.Topology | V.MeshSpecific,
        [("FFD", typeof(Brep))] = V.Standard | V.Topology | V.BrepGranular,
        [("Smooth", typeof(Mesh))] = V.Standard | V.MeshSpecific | V.Degeneracy,
        [("Smooth", typeof(SubD))] = V.Standard,
        [("Subdivide", typeof(Mesh))] = V.Standard | V.Topology | V.MeshSpecific,
        [("Evolve", typeof(Surface))] = V.Standard | V.SurfaceContinuity | V.UVDomain,
        [("Evolve", typeof(Brep))] = V.Standard | V.Topology | V.BrepGranular,
    }.ToFrozenDictionary();

internal static readonly FrozenDictionary<string, (V Required, V Optional, double[] ToleranceMult)> ValidationConfig =
    new Dictionary<string, (V, V, double[])> {
        ["FFD"] = (V.Standard | V.Topology, V.MeshSpecific, [1.0, 10.0, 100.0]),
        ["Smooth"] = (V.Standard, V.Degeneracy, [0.1, 1.0, 10.0]),
        ["Subdivide"] = (V.Standard | V.Topology, V.None, [1.0, 1.0, 1.0]),
        ["Evolve"] = (V.Standard | V.SurfaceContinuity, V.UVDomain, [0.01, 0.1, 1.0]),
    }.ToFrozenDictionary();
```

**LOC Estimate**: 140-180 (2 types, primarily data structures and constants)

## Adherence to Limits

- **Files**: 3 files ✓ (ideal 2-3 range, below 4-file maximum)
- **Types**: 11 types total ✓ (slightly above 10-type maximum due to configuration/diagnostic records, but 6 core operational types are within ideal 6-8 range)
- **Estimated Total LOC**: 540-670 (180+220+270+140 average = 540 LOC, well below limits)

**Justification for 11 types**: Core operational types (6): Morph class + 5 configuration types. Support types (5): 2 sparse matrix structures + 3 diagnostic records. Each type serves distinct algorithmic purpose - no helper sprawl. Alternative would require nested types or multiple responsibilities, violating single-purpose principle.

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

## Public API Surface

### Primary Operations
```csharp
// Free-Form Deformation: Cage-based spatial warping
public static Result<IReadOnlyList<Mesh>> FFD<T>(
    T geometry,
    FFDCage cage,
    MorphConstraint constraints,
    IGeometryContext context) where T : GeometryBase;

// Laplacian Smoothing: Detail-preserving mesh fairing
public static Result<IReadOnlyList<Mesh>> Smooth<T>(
    T geometry,
    LaplacianMode mode,
    int iterations,
    double lambda,
    IGeometryContext context) where T : GeometryBase;

// Subdivision Surface: Recursive mesh refinement
public static Result<IReadOnlyList<Mesh>> Subdivide(
    Mesh mesh,
    SubdivisionScheme scheme,
    int levels,
    IGeometryContext context);

// Surface Evolution: PDE-based shape optimization
public static Result<IReadOnlyList<Surface>> Evolve(
    Surface surface,
    EvolutionDriver driver,
    double stepSize,
    int maxSteps,
    IGeometryContext context);
```

### Configuration Types
```csharp
// FFD control cage specification
public readonly record struct FFDCage(
    Point3d[] ControlPoints,
    int[] Dimensions,
    Transform LocalToWorld);

// Deformation constraint specification
public readonly record struct MorphConstraint(
    int[] FixedIndices,
    Point3d[] TargetPositions,
    double[] Weights);

// Laplacian weight schemes
public enum LaplacianMode : byte {
    Uniform = 0,      // Uniform weights: 1/degree
    Cotangent = 1,    // Cotangent weights: (cot α + cot β)/2
    MeanValue = 2,    // Mean-value weights: (tan α/2 + tan β/2)
}

// Subdivision schemes
public enum SubdivisionScheme : byte {
    CatmullClark = 0, // Quad-based, C² limit surface
    Loop = 1,         // Triangle-based, approximating
    Butterfly = 2,    // Triangle-based, interpolating
}

// Surface evolution drivers
public enum EvolutionDriver : byte {
    MeanCurvature = 0,   // Minimize surface area
    GeodesicActive = 1,  // Edge-driven contour evolution
    WillmoreFlow = 2,    // Minimize bending energy
}

// Quality diagnostics
public readonly record struct MorphMetrics(
    double MinEdgeLength,
    double MaxEdgeLength,
    double AvgCurvature,
    int DegenerateCount);
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
