# Scalar and Vector Field Operations

Computational field analysis: distance fields, differential operators, streamlines, isosurfaces, and critical points.

> **Related Module**: For geometry-based analysis (curvature, quality metrics), see [`Analysis`](../analysis/README.md). Use `Fields` for computational field operations on scalar/vector data sampled on grids.

---

## API

```csharp
Result<FieldResult> Execute(FieldOperation operation, IGeometryContext context)
```

---

## Operations/Types

**Field Operations** (19 request types): `DistanceFieldRequest`, `GradientFieldRequest`, `CurlFieldRequest`, `DivergenceFieldRequest`, `LaplacianFieldRequest`, `HessianFieldRequest`, `VectorPotentialFieldRequest`, `FieldMagnitudeRequest`, `NormalizeFieldRequest`, `DirectionalDerivativeFieldRequest`, `ScalarVectorProductRequest`, `VectorDotProductRequest`, `InterpolateScalarRequest`, `InterpolateVectorRequest`, `StreamlinesRequest`, `IsosurfacesRequest`, `CriticalPointsRequest`, `ComputeStatisticsRequest`

**FieldResult** (discriminated union): `ScalarValue`, `VectorValue`, `Scalar`, `Vector`, `Hessian`, `Curves`, `Meshes`, `CriticalPoints`, `Statistics`

**InterpolationMode**: `NearestInterpolationMode`, `TrilinearInterpolationMode`

**IntegrationScheme**: `EulerIntegrationScheme`, `MidpointIntegrationScheme`, `RungeKutta4IntegrationScheme`

**CriticalPointKind**: `MinimumCriticalPoint`, `MaximumCriticalPoint`, `SaddleCriticalPoint`

**VectorComponent**: `XComponent`, `YComponent`, `ZComponent`

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Distance field from geometry
Result<Fields.FieldResult> distResult = Fields.Execute(
    operation: new Fields.DistanceFieldRequest(Geometry: mesh, Sampling: new Fields.FieldSampling(resolution: 64)),
    context: context);

// Streamlines with RK4 integration
Result<Fields.FieldResult> streamResult = Fields.Execute(
    operation: new Fields.StreamlinesRequest(
        VectorField: vectorField, GridPoints: gridPoints,
        Seeds: [new Point3d(0, 0, 0),], Sampling: new Fields.FieldSampling(resolution: 32, stepSize: 0.005),
        Bounds: bounds, Scheme: new Fields.RungeKutta4IntegrationScheme()),
    context: context);

// Isosurface extraction (marching cubes)
Result<Fields.FieldResult> isoResult = Fields.Execute(
    operation: new Fields.IsosurfacesRequest(
        ScalarField: scalarField, GridPoints: gridPoints,
        Sampling: new Fields.FieldSampling(resolution: 64), Isovalues: [0.0, 0.5, 1.0,]),
    context: context);
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - returns `Result<FieldResult>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.MeshSpecific` (meshes), `V.Standard | V.Topology` (Breps), `V.Standard | V.Degeneracy` (curves), `V.Standard | V.BoundingBox` (surfaces)
- **Errors**: `E.Geometry.InvalidFieldBounds`, `E.Geometry.InvalidStreamlineSeeds`, `E.Geometry.InvalidIsovalue`, `E.Geometry.InvalidCurlComputation`, `E.Geometry.InvalidFieldInterpolation`, `E.Geometry.InvalidCriticalPointDetection`

---

## Internals

**Files**: `Fields.cs` (API, 177 LOC), `FieldsCore.cs` (dispatch, 313 LOC), `FieldsCompute.cs` (algorithms, 812 LOC), `FieldsConfig.cs` (config, 222 LOC)

**Grid limits**: Resolution [8, 256], default 32; step size [√ε, 1.0], default 0.01; max streamline steps: 10000

**Numerical methods**: Central differences O(h²), Euler/RK2/RK4 integration, 256-case marching cubes lookup table, iterative Poisson solver (512 iterations) for vector potential

**Performance**: Distance fields O(n·log(m)) with RTree; curl/divergence/Laplacian O(n) central differences; streamlines O(k·i) per seed; isosurfaces O(n) marching cubes

**Critical point detection**: Eigenanalysis of 3×3 Hessian matrices; classify by eigenvalue signs (minimum: all positive, maximum: all negative, saddle: mixed)
