# Mesh Morphology and Topology Operations

Mesh repair, deformation, subdivision, smoothing, reduction, remeshing, unwrapping, and conversion.

> **Related Module**: For topological analysis (edges, vertices, connectivity), see [`Topology`](../topology/README.md). Use `Morphology` for mesh-specific operations that modify geometry (repair, subdivision, smoothing).

---

## API

```csharp
Result<IReadOnlyList<IMorphologyResult>> Apply<T>(T input, Operation operation, IGeometryContext context) where T : GeometryBase
```

---

## Operations/Types

**Repair**: `FillHolesRepair`, `UnifyNormalsRepair`, `CullDegenerateFacesRepair`, `CompactRepair`, `WeldRepair`, `CompositeRepair(IReadOnlyList<MeshRepairStrategy>, double)`

**Unwrap**: `PlanarUnwrap`, `CylindricalUnwrap(Vector3d, Point3d)`, `SphericalUnwrap(Point3d, double)`

**Subdivision**: `CatmullClarkSubdivision(int)`, `LoopSubdivision(int)`, `ButterflySubdivision(int)` (max 5 levels)

**Smoothing**: `LaplacianSmoothing(int, bool)`, `TaubinSmoothing(int, double, double)`, `MeanCurvatureFlowSmoothing(double, int)` (max 1000 iterations)

**Other Operations**: `MeshOffsetOperation(double, bool)`, `MeshReductionOperation(int, bool, double)`, `IsotropicRemeshOperation(double, int, bool)`, `MeshThickenOperation(double, bool, Vector3d)`, `MeshSeparateOperation`, `MeshWeldOperation(double, bool)`, `BrepToMeshOperation(MeshingParameters?, bool)`, `CageDeformOperation(GeometryBase, Point3d[], Point3d[])`

**Results** (`IMorphologyResult`): `MeshRepairResult`, `MeshUnwrapResult`, `SubdivisionResult`, `SmoothingResult`, `OffsetResult`, `ReductionResult`, `RemeshResult`, `MeshThickenResult`, `MeshSeparationResult`, `MeshWeldResult`, `BrepToMeshResult`, `CageDeformResult`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Composite mesh repair
Result<IReadOnlyList<Morphology.IMorphologyResult>> result = Morphology.Apply(
    input: mesh,
    operation: new Morphology.CompositeRepair(
        Strategies: [new Morphology.FillHolesRepair(), new Morphology.WeldRepair(),],
        WeldTolerance: 0.01),
    context: context);

// Subdivision
Result<IReadOnlyList<Morphology.IMorphologyResult>> subdivided = Morphology.Apply(
    input: mesh,
    operation: new Morphology.CatmullClarkSubdivision(Levels: 2),
    context: context);

// Taubin smoothing (λ-μ filtering)
Result<IReadOnlyList<Morphology.IMorphologyResult>> smoothed = Morphology.Apply(
    input: mesh,
    operation: new Morphology.TaubinSmoothing(Iterations: 20, Lambda: 0.5, Mu: -0.53),
    context: context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<IReadOnlyList<IMorphologyResult>>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.MeshSpecific` (meshes), `V.Standard | V.Topology` (Breps, cage deform)
- **Errors**: `E.Geometry.InvalidCount`, `E.Geometry.InvalidConfiguration`, `E.Geometry.UnsupportedAnalysis`, `E.Validation.GeometryInvalid`

---

## Internals

**Files**: `Morphology.cs` (API, 321 LOC), `MorphologyCore.cs` (dispatch, 289 LOC), `MorphologyCompute.cs` (algorithms, 274 LOC), `MorphologyConfig.cs` (config, 131 LOC)

**Dispatch**: `FrozenDictionary<Type, MorphologyOperationMetadata>` with validation mode, algorithm code, repair flags

**Quality metrics**: Aspect ratio (ideal 1.0, threshold 10.0), triangle angles (ideal 60°, threshold 5°), convergence 1e-6 RMS

**Limits**: Subdivision max 5 levels; smoothing max 1000 iterations; reduction min 4 faces; remeshing max 100 iterations; welding tolerance 0.0001-100.0 (default 0.01); thickening offset 0.0001-10000.0

**Repair flags** (bitwise): FillHoles=1, UnifyNormals=2, CullDegenerateFaces=4, Compact=8, Weld=16

**Subdivision weights**: Loop β(n=3)=0.1875, β(n=6)=0.0625; Butterfly midpoint=0.5, opposite=0.125, wing=0.0625
