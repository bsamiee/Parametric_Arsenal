# Morphology Library Blueprint

## Overview
Scalar and vector field operations for computational morphology: signed distance fields (SDF), gradient fields, streamline tracing via RK4 integration, and isosurface extraction via marching cubes. Enables distance-based shape analysis, flow field visualization, and implicit surface modeling for generative design and computational analysis.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage

**Result<T> Monad**: All operations return Result<T> for explicit error handling. Use Map for value transforms, Bind for chaining field computations, Ensure for validation, Match for consumption.

**UnifiedOperation**: Polymorphic dispatch for field operations across geometry types. Handles collections, validation, parallelism, and error accumulation. Primary integration point for all public API methods.

**ValidationRules**: Expression tree compilation for zero-allocation geometry validation. Will use existing V.Standard, V.BoundingBox, V.Degeneracy, V.Topology modes. No new validation modes required.

**Error Registry**: Existing E.Geometry.* codes (2000-2999) sufficient. Will allocate:
- 2700-2719: Distance field operations
- 2720-2729: Gradient field operations  
- 2730-2739: Streamline operations
- 2740-2749: Isosurface operations

**Context**: IGeometryContext provides AbsoluteTolerance, RelativeTolerance, AngleTolerance. Critical for field sampling, integration step sizes, and convergence criteria.

### Similar libs/rhino/ Implementations

**`libs/rhino/spatial/`**: 
- RTree spatial indexing with ArrayPool buffers - REUSE for closest point queries in distance field computation
- FrozenDictionary dispatch pattern (type pairs → operations) - REPLICATE for field operations
- ConditionalWeakTable caching - NOT NEEDED (fields are stateless computations, no geometry-keyed caching)
- BufferSize configuration constants - ADOPT similar patterns for grid sampling buffers
- **ProximityField already exists** - DO NOT duplicate this functionality

**`libs/rhino/extraction/`**: 
- Polymorphic spec pattern (int/double/Vector3d/Semantic) - ADAPT for field parameters
- Request struct normalization - REPLICATE for field query specs
- Batch operations with error accumulation - LEVERAGE for field sampling

**`libs/rhino/analysis/`**:
- Differential geometry computation (curvature, derivatives) - INTEGRATE for gradient computation
- IResult marker interface - CONSIDER for field sample results
- Multi-geometry analysis with caching - APPLY for field evaluation

**No Duplication**: Confirmed no existing signed distance, gradient field, streamline, or isosurface functionality in codebase.

## SDK Research Summary

### RhinoCommon APIs Used

**Mesh.ClosestPoint(Point3d, double)**: O(n) closest point with optional max distance for early termination. Use with RTree indexing for O(log n) acceleration across large meshes.

**Mesh.ClosestMeshPoint(Point3d)**: Returns MeshPoint with face index, parameters, and distance. Essential for signed distance computation with inside/outside determination.

**Curve.ClosestPoint(Point3d, out double t)**: Returns boolean success, outputs parameter t. Use Curve.PointAt(t), Curve.TangentAt(t), Curve.CurvatureAt(t) for gradient field construction.

**Surface.ClosestPoint(Point3d, out double u, out double v)**: Returns boolean success, outputs UV parameters. Use Surface.PointAt(u,v), Surface.NormalAt(u,v) for distance fields and gradient computation.

**Brep.ClosestPoint(Point3d)**: Returns ComponentIndex, Point3d, UV parameters. Critical for complex geometry distance queries.

**RTree.Search(Sphere/BoundingBox, callback)**: Spatial acceleration. Insert geometries with bounding boxes, query with spheres for radius-limited searches.

**Point3d.DistanceTo(Point3d)**: Euclidean distance. Core primitive for all distance computations.

**Vector3d.Unitize()**: Normalize to length 1. Essential for gradient field directions and streamline tangents.

**RhinoMath.ZeroTolerance**: 2.32e-10 absolute tolerance. Use for comparing distances, gradients, and vector magnitudes to zero.

**RhinoMath.IsValidDouble(double)**: Validity check. Guard all field values against NaN, infinity, UnsetValue.

**System.Math.Sqrt/Pow/Abs**: Standard math operations. Use for distance metrics, field interpolation, and numerical integration.

### Key Insights

**Signed Distance Implementation**: No native SDF API. Implement via:
- ClosestPoint queries for unsigned distance
- Inside/outside determination via Brep.IsPointInside or ray casting winding number
- Negate distance for interior points

**Gradient Computation**: Central difference approximation (O(h²) accuracy):
- Sample field at x±δx, y±δy, z±δz where δ = RhinoMath.SqrtEpsilon (≈1.5e-8)
- Gradient = [(f(x+h)-f(x-h))/(2h), (f(y+h)-f(y-h))/(2h), (f(z+h)-f(z-h))/(2h)]
- Forward/backward difference at grid boundaries (O(h) accuracy)
- Use RhinoMath.SqrtEpsilon for optimal numerical stability vs. accuracy tradeoff

**Streamline Integration**: Runge-Kutta 4th order (RK4):
- k1 = f(p)
- k2 = f(p + 0.5*step*k1)
- k3 = f(p + 0.5*step*k2)
- k4 = f(p + step*k3)
- p_next = p + (step/6)*(k1 + 2*k2 + 2*k3 + k4)
- Adaptive step sizing based on field curvature

**Isosurface Extraction**: Marching Cubes algorithm:
- Sample field on 3D grid (resolution from user spec)
- For each cube: evaluate field at 8 corners to determine configuration (0-255)
- Lookup table (256 cases) determines triangle edge indices
- Linear interpolation: t = (isovalue - f1) / (f2 - f1) for edge vertex position
- Output: Mesh constructed via Mesh.Vertices.AddVertices() and Mesh.Faces.AddFaces()
- Mesh.Normals.ComputeNormals() for proper shading, Mesh.Compact() to optimize

**Performance Critical**: 
- Use ArrayPool<T>.Shared for temporary buffers
- Use for loops with index access in hot paths (2-3x faster than LINQ)
- Cache field evaluations via ConditionalWeakTable
- RTree spatial indexing for geometry queries

### SDK Version Requirements
- Minimum: RhinoCommon 8.0
- Tested: RhinoCommon 8.24+

## CRITICAL: NO ENUMS - Byte-Based Dispatch Pattern

**ABSOLUTE REQUIREMENT**: This library **MUST NOT** use enum types. Following established patterns in extraction/, spatial/, and orientation/ folders:

1. **Operation Classification**: Use byte constants (NOT enums)
   - `const byte OperationDistance = 0;`
   - `const byte OperationGradient = 1;`
   - `const byte OperationStreamline = 2;`
   - `const byte OperationIsosurface = 3;`

2. **Integration Methods**: Use byte constants (NOT enums)
   - `const byte IntegrationEuler = 0;`
   - `const byte IntegrationRK2 = 1;`
   - `const byte IntegrationRK4 = 2;`
   - `const byte IntegrationAdaptiveRK4 = 3;`

3. **Dispatch via FrozenDictionary**: All operation-type mappings use `FrozenDictionary<(byte, Type), T>`

4. **Why NO ENUMS**: 
   - Enums are strongly typed, preventing flexible polymorphic dispatch
   - Byte constants enable O(1) FrozenDictionary lookup
   - Matches project architectural standards across ALL libs/rhino/ folders
   - Enables runtime operation composition without boxing/unboxing overhead

**This is non-negotiable** - any use of enums violates core architectural principles.

## File Organization

### Pattern C (4 files - Maximum Complexity Domain)

Justification: Four distinct operation categories (distance, gradient, streamline, isosurface) with complex algorithms requiring separation for readability while maintaining density.

### File 1: `Morphology.cs`
**Purpose**: Public API surface with UnifiedOperation dispatch (NO ENUMS - byte-based classification only)

**Types** (2 total):
- `Morphology` (static class): Primary API entry point dispatching to MorphologyCore via byte operation codes
- `FieldSpec` (readonly struct): Field specification with resolution, bounds, step size (NO MODE ENUMS)

**Key Members**:
- `DistanceField<T>(T geometry, FieldSpec spec, IGeometryContext)`: Signed distance field → (Point3d[] grid, double[] distances)
- `GradientField<T>(T geometry, FieldSpec spec, IGeometryContext)`: Gradient field → (Point3d[] grid, Vector3d[] gradients)
- `Streamlines(Vector3d[] field, Point3d[] seeds, FieldSpec spec, IGeometryContext)`: Flow integration → Curve[]
- `Isosurfaces(double[] field, FieldSpec spec, double isovalue, IGeometryContext)`: Iso-contours → Mesh[]

**Code Style Example**:
```csharp
public static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
    T geometry,
    FieldSpec spec,
    IGeometryContext context) where T : GeometryBase =>
    MorphologyCore.OperationRegistry.TryGetValue(typeof(T), out (V mode, Func<object, FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> execute) config) switch {
        true => UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<(Point3d[], double[])>>>)(item => 
                config.execute(item, spec, context).Map(result => (IReadOnlyList<(Point3d[], double[])>)[result])),
            config: new OperationConfig<T, (Point3d[], double[])> {
                Context = context,
                ValidationMode = config.mode,
                OperationName = $"Morphology.DistanceField.{typeof(T).Name}",
                EnableDiagnostics = false,
            }).Map(results => results[0]),
        false => ResultFactory.Create<(Point3d[], double[])>(
            error: E.Geometry.UnsupportedAnalysis.WithContext($"Distance field not supported for {typeof(T).Name}")),
    };
```

**LOC Estimate**: 150-200

### File 2: `MorphologyCore.cs`
**Purpose**: FrozenDictionary dispatch and spatial acceleration

**Types** (3 total):
- `MorphologyCore` (internal static class): Dispatch registry and RTree factories
- `SampleGrid` (internal readonly struct): 3D grid sampling configuration (origin, bounds, resolution)
- `DistanceQuery` (internal readonly struct): Cached distance query result (point, distance, normal)

**Key Members**:
- `OperationRegistry` (FrozenDictionary): (Type → V, Func) mapping for geometry type dispatch
- `BuildDistanceRTree<T>(T)`: Construct spatial index for O(log n) queries
- `SampleGrid(BoundingBox, int resolution)`: Generate Point3d[] grid for field sampling
- `EvaluateDistance(object, Point3d, RTree, IGeometryContext)`: Core distance computation with spatial acceleration

**Code Style Example**:
```csharp
internal static readonly FrozenDictionary<Type, (V Mode, Func<object, FieldSpec, IGeometryContext, Result<(Point3d[], double[])>> Execute)> OperationRegistry =
    new (Type, V, Func<object, FieldSpec, IGeometryContext, Result<(Point3d[], double[])>>)[] {
        (typeof(Mesh), V.MeshSpecific, ExecuteDistanceField<Mesh>),
        (typeof(Brep), V.Topology, ExecuteDistanceField<Brep>),
        (typeof(Curve), V.Degeneracy, ExecuteDistanceField<Curve>),
        (typeof(Surface), V.BoundingBox, ExecuteDistanceField<Surface>),
    }.ToFrozenDictionary(
        static entry => entry.Item1,
        static entry => (entry.Item2, entry.Item3));

private static RTree BuildDistanceRTree<T>(T geometry) where T : GeometryBase {
    RTree tree = new();
    BoundingBox box = geometry.GetBoundingBox(accurate: true);
    _ = tree.Insert(box, index: 0);
    return tree;
}
```

**LOC Estimate**: 200-250

### File 3: `MorphologyCompute.cs`
**Purpose**: Core algorithms - distance, gradient, streamline, isosurface

**Types** (3 total):
- `MorphologyCompute` (internal static class): Computational kernels
- `RK4State` (internal readonly struct): Runge-Kutta 4th order integration state
- `MarchingCube` (internal readonly struct): Marching cubes configuration (8 corners, case index)

**Key Members**:
- `ComputeSignedDistance(GeometryBase, Point3d[], IGeometryContext)`: SDF via closest point + inside/outside
- `ComputeGradient(double[], SampleGrid, IGeometryContext)`: Finite difference gradient approximation
- `IntegrateStreamline(Func<Point3d, Vector3d>, Point3d seed, double step, int maxSteps)`: RK4 flow integration
- `ExtractIsosurface(double[], SampleGrid, double isovalue)`: Marching cubes mesh generation

**Code Style Example**:
```csharp
internal static Result<double[]> ComputeSignedDistance(
    GeometryBase geometry,
    Point3d[] grid,
    IGeometryContext context) {
    double[] distances = ArrayPool<double>.Shared.Rent(grid.Length);
    try {
        for (int i = 0; i < grid.Length; i++) {
            Point3d closest = geometry switch {
                Mesh m => m.ClosestPoint(grid[i]),
                Brep b => b.ClosestPoint(grid[i]),
                Curve c => c.ClosestPoint(grid[i], out double t) ? c.PointAt(t) : grid[i],
                Surface s => s.ClosestPoint(grid[i], out double u, out double v) ? s.PointAt(u, v) : grid[i],
                _ => grid[i],
            };
            double unsignedDist = grid[i].DistanceTo(closest);
            bool inside = geometry is Brep brep 
                ? brep.IsPointInside(grid[i], tolerance: context.AbsoluteTolerance, strictlyIn: false)
                : false;
            distances[i] = inside ? -unsignedDist : unsignedDist;
        }
        return ResultFactory.Create(value: [.. distances[..grid.Length]]);
    } finally {
        ArrayPool<double>.Shared.Return(distances, clearArray: true);
    }
}
```

**LOC Estimate**: 250-300

### File 4: `MorphologyConfig.cs`
**Purpose**: Configuration constants, byte-based operation IDs, and FrozenDictionary dispatch tables

**Types** (1 total):
- `MorphologyConfig` (internal static class): Constants, byte operation codes, marching cubes lookup, buffer size dispatch

**Key Members**:
- **Operation Type Identifiers** (byte constants): Distance=0, Gradient=1, Streamline=2, Isosurface=3
- **Integration Method Identifiers** (byte constants): Euler=0, RK2=1, RK4=2, AdaptiveRK4=3
- **Grid Resolution Constants**: DefaultResolution=32, MinResolution=8, MaxResolution=256
- **Integration Constants**: DefaultStepSize=0.01, MaxStreamlineSteps=10000, AdaptiveStepTolerance=1e-6
- **Buffer Size Dispatch** (FrozenDictionary<(byte, Type), int>): Operation-type pairs to buffer sizes
- **Validation Mode Dispatch** (FrozenDictionary<(byte, Type), V>): Operation-type pairs to validation modes
- **Marching Cubes Lookup** (static readonly int[][]): 256-case edge-to-triangle configuration table
- **RK4 Coefficients** (static readonly double[]): [1/6, 1/3, 1/3, 1/6] weights for RK4 integration

**Code Style Example**:
```csharp
internal static class MorphologyConfig {
    // Operation type identifiers (NO ENUMS - use byte constants)
    internal const byte OperationDistance = 0;
    internal const byte OperationGradient = 1;
    internal const byte OperationStreamline = 2;
    internal const byte OperationIsosurface = 3;

    // Integration method identifiers (NO ENUMS - use byte constants)
    internal const byte IntegrationEuler = 0;
    internal const byte IntegrationRK2 = 1;
    internal const byte IntegrationRK4 = 2;
    internal const byte IntegrationAdaptiveRK4 = 3;

    // Grid resolution bounds (use RhinoMath.Clamp for validation)
    internal const int DefaultResolution = 32;
    internal const int MinResolution = 8;
    internal const int MaxResolution = 256;

    // Integration step parameters (computed from context.AbsoluteTolerance where possible)
    internal const double DefaultStepSize = 0.01;
    internal const double MinStepSize = 1e-8;
    internal const double MaxStepSize = 1.0;
    internal const int MaxStreamlineSteps = 10000;
    internal static readonly double AdaptiveStepTolerance = RhinoMath.SqrtEpsilon;
    internal static readonly double GradientFiniteDifferenceStep = RhinoMath.SqrtEpsilon;

    // RK4 integration weights (exact fractions for numerical accuracy)
    internal static readonly double[] RK4Weights = [1.0 / 6.0, 1.0 / 3.0, 1.0 / 3.0, 1.0 / 6.0,];
    internal static readonly double[] RK4HalfStep = [0.5, 0.5, 1.0,];

    // Buffer size dispatch: (operation, geometry type) → buffer size
    internal static readonly FrozenDictionary<(byte Operation, Type GeometryType), int> BufferSizes =
        new Dictionary<(byte, Type), int> {
            [(OperationDistance, typeof(Mesh))] = 2048,
            [(OperationDistance, typeof(Brep))] = 4096,
            [(OperationDistance, typeof(Curve))] = 1024,
            [(OperationDistance, typeof(Surface))] = 2048,
            [(OperationGradient, typeof(Mesh))] = 4096,
            [(OperationGradient, typeof(Brep))] = 8192,
            [(OperationStreamline, typeof(void))] = 2048,
            [(OperationIsosurface, typeof(void))] = 8192,
        }.ToFrozenDictionary();

    // Validation mode dispatch: (operation, geometry type) → validation flags
    internal static readonly FrozenDictionary<(byte Operation, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(OperationDistance, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(OperationDistance, typeof(Brep))] = V.Standard | V.Topology,
            [(OperationDistance, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(OperationDistance, typeof(Surface))] = V.Standard | V.BoundingBox,
            [(OperationGradient, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(OperationGradient, typeof(Brep))] = V.Standard | V.Topology,
            [(OperationGradient, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(OperationGradient, typeof(Surface))] = V.Standard | V.BoundingBox,
        }.ToFrozenDictionary();

    // Marching cubes edge-to-triangle lookup (256 cases, compressed representation)
    // Each entry: cube corner configuration (8 bits) → triangle vertex indices on edges
    internal static readonly int[][] MarchingCubesTable = [
        [],  // Case 0: all corners outside
        [0, 8, 3,],  // Case 1: corner 0 inside
        [0, 1, 9,],  // Case 2: corner 1 inside
        [1, 8, 3, 9, 8, 1,],  // Case 3: corners 0,1 inside
        // ... 252 more cases (actual table in implementation)
    ];

    // Marching cubes edge intersection lookup: edge index → (vertex1, vertex2) pair
    internal static readonly (int V1, int V2)[] EdgeVertexPairs = [
        (0, 1), (1, 2), (2, 3), (3, 0),  // Bottom face edges
        (4, 5), (5, 6), (6, 7), (7, 4),  // Top face edges
        (0, 4), (1, 5), (2, 6), (3, 7),  // Vertical edges
    ];
}
```

**LOC Estimate**: 180-220 (including 256-case marching cubes table compressed with pattern repetition)

## Adherence to Limits

- **Files**: 4 files (✓ at maximum, justified by distinct algorithm categories)
- **Types**: 9 types total (✓ within limit: 2+3+3+1 - removed FieldSpec validation helper)
- **Estimated Total LOC**: 700-880 (well within reasonable range for complex computational geometry)

**Justification for 4 files**: Distance fields, gradient fields, streamline integration, and isosurface extraction are algorithmically distinct operations that benefit from separation. Each algorithm requires 150-300 LOC of dense computational code. Consolidation into 3 files would create members exceeding 300 LOC limit.

**Justification for 9 types**: Minimal type count given complexity:
- 1 public API class (Morphology)
- 1 public spec struct (FieldSpec)
- 3 core algorithm structs (SampleGrid, RK4State, MarchingCube)
- 3 internal implementation classes (Core, Compute, Config)
- **NO internal helper types** - validation inline with switch expressions

All types justified and necessary. **NO ENUMS** - all classification via byte constants.

## Algorithmic Density Strategy

**Eliminate Loops via FrozenDictionary Dispatch**: Type-based operation lookup replaces manual type checking and branching.

**ArrayPool for Zero Allocation**: All temporary buffers (distance arrays, streamline points, triangle vertices) use ArrayPool<T>.Shared to eliminate GC pressure in hot paths.

**Inline Field Evaluation**: No helper methods for field sampling - inline switch expressions with pattern matching for geometry type discrimination.

**RTree Spatial Indexing**: O(log n) closest point queries instead of O(n) brute force. Build once, query many pattern.

**ConditionalWeakTable Caching**: Cache expensive field evaluations (distance computations, gradient calculations) with automatic GC when geometry is collected.

**For Loops in Hot Paths**: Grid sampling and marching cubes use index-based for loops (2-3x faster than LINQ) with aggressive inlining.

**Marching Cubes Lookup Table**: 256-case switch expression replaced with static readonly int[][] table for O(1) triangle configuration retrieval.

**Runge-Kutta Inline Computation**: RK4 integration inlined with tuple deconstruction, no intermediate allocations: `(k1, k2, k3, k4) = (f(p), f(p + 0.5*h*k1), f(p + 0.5*h*k2), f(p + h*k3))`

## Dispatch Architecture

### CRITICAL: NO ENUMS - Byte-Based Classification Only

**Following extraction/, spatial/, orientation/ patterns:**
- Operation types: byte constants (0=Distance, 1=Gradient, 2=Streamline, 3=Isosurface)
- Integration methods: byte constants (0=Euler, 1=RK2, 2=RK4, 3=AdaptiveRK4)
- All dispatch via FrozenDictionary<(byte, Type), T> lookups
- **NEVER use enum types** - violates project architectural standards

### Type-Operation Pair Dispatch

```csharp
// MorphologyCore.cs dispatch registry
internal static readonly FrozenDictionary<(byte Operation, Type Geometry), (V Mode, Func<object, FieldSpec, IGeometryContext, Result<T>> Execute)> OperationRegistry =
    new Dictionary<(byte, Type), (V, Func<object, FieldSpec, IGeometryContext, Result<T>>)> {
        [(MorphologyConfig.OperationDistance, typeof(Mesh))] = (V.Standard | V.MeshSpecific, ExecuteDistanceField<Mesh>),
        [(MorphologyConfig.OperationDistance, typeof(Brep))] = (V.Standard | V.Topology, ExecuteDistanceField<Brep>),
        [(MorphologyConfig.OperationDistance, typeof(Curve))] = (V.Standard | V.Degeneracy, ExecuteDistanceField<Curve>),
        [(MorphologyConfig.OperationDistance, typeof(Surface))] = (V.Standard | V.BoundingBox, ExecuteDistanceField<Surface>),
        [(MorphologyConfig.OperationGradient, typeof(Mesh))] = (V.Standard | V.MeshSpecific, ExecuteGradientField<Mesh>),
        // ... additional operation-type pairs
    }.ToFrozenDictionary();
```

**Mesh**: V.MeshSpecific → Mesh.ClosestMeshPoint for O(n) queries, RTree for acceleration
**Brep**: V.Topology → Brep.ClosestPoint + IsPointInside for signed distance
**Curve**: V.Degeneracy → Curve.ClosestPoint + tangent/curvature for gradient
**Surface**: V.BoundingBox → Surface.ClosestPoint + normal for distance field

### Byte-Based Operation Switch (NOT string/enum)

```csharp
// Public API dispatches to Core via byte operation codes
byte operationType = MorphologyConfig.OperationDistance;  // NOT enum, NOT string
MorphologyCore.OperationRegistry.TryGetValue((operationType, typeof(T)), out (V mode, Func<...> execute) config) switch {
    true => UnifiedOperation.Apply(...),
    false => ResultFactory.Create(error: E.Geometry.UnsupportedAnalysis),
};
```

## Public API Surface

### Primary Operations

```csharp
// Signed distance field: (x,y,z) → distance to surface
public static Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(
    T geometry,
    FieldSpec spec,
    IGeometryContext context) where T : GeometryBase;

// Gradient field: (x,y,z) → ∇f direction of steepest ascent
public static Result<(Point3d[] Grid, Vector3d[] Gradients)> GradientField<T>(
    T geometry,
    FieldSpec spec,
    IGeometryContext context) where T : GeometryBase;

// Streamline tracing: integrate curves along vector field
public static Result<Curve[]> Streamlines(
    Vector3d[] vectorField,
    Point3d[] gridPoints,
    Point3d[] seeds,
    FieldSpec spec,
    IGeometryContext context);

// Isosurface extraction: f(x,y,z) = c → triangle mesh
public static Result<Mesh[]> Isosurfaces(
    double[] scalarField,
    Point3d[] gridPoints,
    FieldSpec spec,
    double[] isovalues,
    IGeometryContext context);
```

### Configuration Types

```csharp
// Field specification with defaults and validation
public readonly struct FieldSpec {
    public FieldSpec(
        int resolution = MorphologyConfig.DefaultResolution,
        BoundingBox? bounds = null,
        double? stepSize = null);

    public readonly int Resolution;  // Grid resolution (cube root of sample count)
    public readonly BoundingBox? Bounds;  // Sample region (null = geometry bbox)
    public readonly double StepSize;  // Integration/sampling step size
}
```

## Code Style Adherence Verification

- [x] **NO ENUMS** - All classification uses byte constants with FrozenDictionary dispatch
- [x] All examples use pattern matching (no if/else statements)
- [x] All examples use explicit types (no var)
- [x] All examples use named parameters where non-obvious
- [x] All examples use trailing commas in multi-line collections
- [x] All examples use K&R brace style (opening brace same line)
- [x] All examples use target-typed new() where applicable
- [x] All examples use collection expressions [] where applicable
- [x] One type per file organization planned
- [x] All member estimates under 300 LOC
- [x] All patterns match existing libs/ exemplars
- [x] Byte-based dispatch matches extraction/, spatial/, orientation/ patterns
- [x] FrozenDictionary for all type-operation mappings

## Error Allocation Strategy

**Range 2700-2749** (Morphology operations within Geometry domain 2000-2999):

```csharp
// Add to libs/core/errors/E.cs in E.Geometry class:

// Distance Field Operations (2700-2709)
public static readonly SystemError InvalidFieldResolution = Get(2700);
public static readonly SystemError InvalidFieldBounds = Get(2701);
public static readonly SystemError DistanceFieldComputationFailed = Get(2702);
public static readonly SystemError InsideOutsideTestFailed = Get(2703);

// Gradient Field Operations (2720-2729)
public static readonly SystemError GradientComputationFailed = Get(2720);
public static readonly SystemError InvalidGradientSampling = Get(2721);
public static readonly SystemError ZeroGradientRegion = Get(2722);

// Streamline Operations (2730-2739)
public static readonly SystemError StreamlineIntegrationFailed = Get(2730);
public static readonly SystemError InvalidStreamlineSeeds = Get(2731);
public static readonly SystemError StreamlineDivergence = Get(2732);
public static readonly SystemError MaxStepsExceeded = Get(2733);

// Isosurface Operations (2740-2749)
public static readonly SystemError IsosurfaceExtractionFailed = Get(2740);
public static readonly SystemError InvalidIsovalue = Get(2741);
public static readonly SystemError MarchingCubesFailed = Get(2742);
public static readonly SystemError InvalidScalarField = Get(2743);
```

**Message Dictionary Additions** to `E._m` FrozenDictionary:

```csharp
[2700] = "Field resolution must be positive integer",
[2701] = "Field bounds invalid or degenerate",
[2702] = "Distance field computation failed",
[2703] = "Inside/outside determination failed for signed distance",

[2720] = "Gradient computation failed",
[2721] = "Invalid gradient field sampling parameters",
[2722] = "Zero gradient magnitude in critical region",

[2730] = "Streamline integration failed to converge",
[2731] = "Invalid seed points for streamline tracing",
[2732] = "Streamline divergence detected - unstable flow",
[2733] = "Maximum integration steps exceeded",

[2740] = "Isosurface extraction failed",
[2741] = "Isovalue outside scalar field range",
[2742] = "Marching cubes algorithm failure",
[2743] = "Scalar field data invalid or insufficient",
```

## Validation Strategy

**No New Validation Modes Required**: Existing V.* flags sufficient.

**Distance Fields**:
- V.Standard: IsValid check on geometry
- V.MeshSpecific: Mesh validity for mesh distance fields
- V.Topology: Brep manifold/solid checks for signed distance
- V.BoundingBox: Valid bounding box for field bounds

**Gradient Fields**:
- V.Degeneracy: Check for zero-length curves, degenerate surfaces
- V.Standard: General geometry validity

**Streamlines**:
- V.None: Vector fields are pre-computed, no geometry validation needed
- Runtime checks: vector magnitude > ZeroTolerance, seed points valid

**Isosurfaces**:
- V.None: Operates on scalar arrays, not geometry
- Runtime checks: field array length matches grid dimensions, isovalues in range

## Implementation Sequence

1. Read this blueprint thoroughly - **pay special attention to NO ENUMS requirement**
2. **Study byte-based patterns**: Read extraction/ExtractionConfig.cs, spatial/SpatialConfig.cs thoroughly
3. **Create MorphologyConfig.cs FIRST**: Byte operation codes, FrozenDictionary dispatch, marching cubes table
4. **Add error codes**: Add morphology errors to E.cs (2700-2749 range)
5. **Create MorphologyCore.cs**: Byte-based FrozenDictionary dispatch registry, RTree factories
6. **Create MorphologyCompute.cs**: Core algorithms with ArrayPool buffers
7. **Implement distance field**: ComputeSignedDistance with RTree acceleration
8. **Implement gradient field**: ComputeGradient using central difference with RhinoMath.SqrtEpsilon step
9. **Implement streamline tracing**: IntegrateStreamline using RK4 with weights from config
10. **Implement isosurface extraction**: ExtractIsosurface using marching cubes 256-case lookup
11. **Create Morphology.cs**: Public API dispatching via byte operation codes
12. **VERIFY NO ENUMS**: `grep -r "enum " morphology/ --include="*.cs"` must return zero results
13. **Verify byte dispatch**: All FrozenDictionary keys use (byte, Type) tuples
14. **Verify RhinoMath usage**: All constants from RhinoMath (ZeroTolerance, SqrtEpsilon, IsValidDouble)
15. **Verify ArrayPool**: All temporary buffers use ArrayPool<T>.Shared
16. **Verify patterns**: Check all code matches exemplars (no var, no if/else, K&R, pattern matching)
17. **Verify LOC limits**: Ensure all members ≤300 LOC
18. **Verify file/type limits**: Confirm 4 files, 9 types (NOT 10 - removed extra helper type)
19. **Build and validate**: Ensure compilation succeeds

## Advanced Features (Future Considerations)

**Adaptive Sampling**: Sparse grids near boundaries, dense in regions of high curvature.

**GPU Acceleration**: Compute shaders for parallel field evaluation (10-100x speedup).

**Hierarchical Distance Fields**: Octree-based multi-resolution SDFs for LOD rendering.

**Dual Contouring**: Alternative to marching cubes preserving sharp features.

**Vector Field Topology**: Critical point detection, separatrix extraction, vortex identification.

## References

### SDK Documentation
- [RhinoCommon Mesh.ClosestPoint](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Mesh_ClosestPoint.htm)
- [RhinoCommon Brep.IsPointInside](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Brep_IsPointInside.htm)
- [RhinoMath Constants](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_RhinoMath.htm)

### Algorithmic References
- Paul Bourke: [Polygonising a scalar field (Marching Cubes)](http://www.paulbourke.net/geometry/polygonise/)
- Signed Distance Functions: [Wikipedia](https://en.wikipedia.org/wiki/Signed_distance_function)
- Vector Field Integration: [Runge-Kutta Methods](https://en.wikipedia.org/wiki/Runge%E2%80%93Kutta_methods)
- Gradient Fields: [Computational Geometry](https://www.numberanalytics.com/blog/ultimate-guide-to-gradient-field-in-computational-geometry)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/` - Result monad patterns, Map/Bind/Ensure composition
- `libs/core/operations/` - UnifiedOperation dispatch, OperationConfig usage
- `libs/core/validation/` - V.* validation modes, expression tree compilation
- `libs/core/errors/` - E.* error registry, error code allocation patterns
- `libs/rhino/spatial/` - RTree indexing, ArrayPool buffers, FrozenDictionary dispatch
- `libs/rhino/extraction/` - Polymorphic specs, Request normalization, batch operations
- `libs/rhino/analysis/` - Differential geometry, IResult interface, caching strategies

---

**Blueprint Version**: 1.0  
**Created**: 2025-11-14  
**Last Updated**: 2025-11-14  
**Status**: Ready for Implementation
