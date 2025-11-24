# libs/rhino/fields

Scalar and vector field operations for computational field analysis in Rhino 8.

## API Surface

### Core Entry Point

```csharp
public static class Fields
```

**Execute field operation and return discriminated union result**:
```csharp
Result<FieldResult> Execute(FieldOperation operation, IGeometryContext context)
```

### Algebraic Domain Types

#### FieldResult (Discriminated Union)
```csharp
// Value results
FieldResult.ScalarValue(double Value)                           // Single scalar
FieldResult.VectorValue(Vector3d Value)                         // Single vector

// Field results
FieldResult.Scalar(ScalarFieldSamples Value)                    // Scalar field
FieldResult.Vector(VectorFieldSamples Value)                    // Vector field
FieldResult.Hessian(HessianFieldSamples Value)                  // Hessian tensor field

// Geometric results
FieldResult.Curves(Curve[] Value)                               // Streamlines
FieldResult.Meshes(Mesh[] Value)                                // Isosurfaces

// Analysis results
FieldResult.CriticalPoints(CriticalPoint[] Value)               // Critical point set
FieldResult.Statistics(FieldStatistics Value)                   // Field statistics
```

#### FieldOperation (19 Request Types)

**Distance and gradient fields**:
```csharp
DistanceFieldRequest(GeometryBase Geometry, FieldSampling? Sampling)
GradientFieldRequest(GeometryBase Geometry, FieldSampling? Sampling)
```

**Differential operators**:
```csharp
CurlFieldRequest(Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds)
DivergenceFieldRequest(Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds)
LaplacianFieldRequest(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds)
HessianFieldRequest(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds)
```

**Vector potential**:
```csharp
VectorPotentialFieldRequest(Vector3d[] MagneticField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds)
```

**Field arithmetic**:
```csharp
FieldMagnitudeRequest(Vector3d[] VectorField, Point3d[] GridPoints)
NormalizeFieldRequest(Vector3d[] VectorField, Point3d[] GridPoints)
DirectionalDerivativeFieldRequest(Vector3d[] GradientField, Point3d[] GridPoints, Vector3d Direction)
ScalarVectorProductRequest(double[] ScalarField, Vector3d[] VectorField, Point3d[] GridPoints, VectorComponent Component)
VectorDotProductRequest(Vector3d[] FirstField, Vector3d[] SecondField, Point3d[] GridPoints)
```

**Interpolation**:
```csharp
InterpolateScalarRequest(Point3d Query, double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds, InterpolationMode? Mode)
InterpolateVectorRequest(Point3d Query, Vector3d[] VectorField, Point3d[] GridPoints, FieldSampling Sampling, BoundingBox Bounds, InterpolationMode? Mode)
```

**Streamline integration**:
```csharp
StreamlinesRequest(Vector3d[] VectorField, Point3d[] GridPoints, Point3d[] Seeds, FieldSampling Sampling, BoundingBox Bounds, IntegrationScheme? Scheme)
```

**Isosurface extraction**:
```csharp
IsosurfacesRequest(double[] ScalarField, Point3d[] GridPoints, FieldSampling Sampling, double[] Isovalues)
```

**Critical point analysis**:
```csharp
CriticalPointsRequest(double[] ScalarField, Vector3d[] GradientField, double[,][] Hessian, Point3d[] GridPoints, FieldSampling Sampling)
```

**Field statistics**:
```csharp
ComputeStatisticsRequest(double[] ScalarField, Point3d[] GridPoints)
```

#### InterpolationMode Hierarchy
```csharp
InterpolationMode                                               // Base type
    ├─ NearestInterpolationMode                                 // O(1) nearest neighbor
    └─ TrilinearInterpolationMode                               // C⁰ continuous trilinear
```

#### IntegrationScheme Hierarchy
```csharp
IntegrationScheme                                               // Base type
    ├─ EulerIntegrationScheme                                   // Explicit Euler (RK1)
    ├─ MidpointIntegrationScheme                                // Midpoint method (RK2)
    └─ RungeKutta4IntegrationScheme                             // Classical RK4
```

#### VectorComponent Hierarchy
```csharp
VectorComponent                                                 // Base type
    ├─ XComponent                                               // Extract X component
    ├─ YComponent                                               // Extract Y component
    └─ ZComponent                                               // Extract Z component
```

#### CriticalPointKind Hierarchy
```csharp
CriticalPointKind                                               // Base type
    ├─ MinimumCriticalPoint                                     // Local minimum (λ₁,λ₂ > 0)
    ├─ MaximumCriticalPoint                                     // Local maximum (λ₁,λ₂ < 0)
    └─ SaddleCriticalPoint                                      // Saddle point (λ₁·λ₂ < 0)
```

### Data Structures

#### Field Samples
```csharp
ScalarFieldSamples(Point3d[] Grid, double[] Values)
VectorFieldSamples(Point3d[] Grid, Vector3d[] Vectors)
HessianFieldSamples(Point3d[] Grid, double[,][] Hessian)        // 3×3 symmetric matrices
```

#### Critical Point
```csharp
CriticalPoint(
    Point3d Location,
    CriticalPointKind Kind,
    double Value,
    Vector3d[] Eigenvectors,                                    // Principal directions
    double[] Eigenvalues)                                       // λ₁, λ₂ (2D Hessian)
```

#### Field Statistics
```csharp
FieldStatistics(
    double Min,
    double Max,
    double Mean,
    double StdDev,
    Point3d MinLocation,
    Point3d MaxLocation)
```

#### Field Sampling
```csharp
FieldSampling(
    int? resolution = null,                                     // Grid resolution [8,256], default 32
    BoundingBox? bounds = null,                                 // Sample region, null uses geometry
    double? stepSize = null)                                    // Integration step [√ε,1.0], default 0.01
```

**Static instance**:
```csharp
FieldSampling.Default                                           // resolution: 32, stepSize: 0.01
```

## Usage Examples

### Distance and Gradient Fields
```csharp
IGeometryContext context = new GeometryContext();

// Distance field from mesh
Result<FieldResult> distResult = Fields.Execute(
    operation: new Fields.DistanceFieldRequest(
        Geometry: mesh,
        Sampling: new FieldSampling(resolution: 64)),
    context: context);

// Gradient field from Brep
Result<FieldResult> gradResult = Fields.Execute(
    operation: new Fields.GradientFieldRequest(
        Geometry: brep,
        Sampling: FieldSampling.Default),
    context: context);
```

### Streamline Integration and Isosurfaces
```csharp
IGeometryContext context = new GeometryContext();

// Streamlines with RK4 integration
Result<FieldResult> streamResult = Fields.Execute(
    operation: new Fields.StreamlinesRequest(
        VectorField: vectorField,
        GridPoints: gridPoints,
        Seeds: [new Point3d(0, 0, 0), new Point3d(1, 0, 0),],
        Sampling: new FieldSampling(resolution: 32, stepSize: 0.005),
        Bounds: bounds,
        Scheme: new Fields.RungeKutta4IntegrationScheme()),
    context: context);

// Isosurface extraction (marching cubes)
Result<FieldResult> isoResult = Fields.Execute(
    operation: new Fields.IsosurfacesRequest(
        ScalarField: scalarField,
        GridPoints: gridPoints,
        Sampling: new FieldSampling(resolution: 64),
        Isovalues: [0.0, 0.5, 1.0,]),
    context: context);
```

### Critical Points, Interpolation, and Statistics
```csharp
IGeometryContext context = new GeometryContext();

// Critical point detection with eigenanalysis
Result<FieldResult> critResult = Fields.Execute(
    operation: new Fields.CriticalPointsRequest(
        ScalarField: scalarField,
        GradientField: gradientField,
        Hessian: hessian,
        GridPoints: gridPoints,
        Sampling: new FieldSampling(resolution: 32)),
    context: context);

CriticalPoint[] minima = (critResult.Value as FieldResult.CriticalPoints)!.Value
    .Where(p => p.Kind is Fields.MinimumCriticalPoint)
    .ToArray();

// Trilinear interpolation at query point
Result<FieldResult> interpResult = Fields.Execute(
    operation: new Fields.InterpolateScalarRequest(
        Query: new Point3d(5.5, 3.2, 1.8),
        ScalarField: scalarField,
        GridPoints: gridPoints,
        Sampling: new FieldSampling(resolution: 32),
        Bounds: bounds,
        Mode: new Fields.TrilinearInterpolationMode()),
    context: context);

// Field statistics (min, max, mean, stddev)
Result<FieldResult> statsResult = Fields.Execute(
    operation: new Fields.ComputeStatisticsRequest(
        ScalarField: scalarField,
        GridPoints: gridPoints),
    context: context);
```

### Differential Operators (Curl, Divergence, Laplacian)
```csharp
IGeometryContext context = new GeometryContext();
FieldSampling sampling = new(resolution: 32);

// Curl: ∇×F, Divergence: ∇·F, Laplacian: ∇²f
Result<FieldResult> curlResult = Fields.Execute(
    operation: new Fields.CurlFieldRequest(VectorField: vectorField, GridPoints: gridPoints, Sampling: sampling, Bounds: bounds),
    context: context);

Result<FieldResult> divResult = Fields.Execute(
    operation: new Fields.DivergenceFieldRequest(VectorField: vectorField, GridPoints: gridPoints, Sampling: sampling, Bounds: bounds),
    context: context);

Result<FieldResult> laplaceResult = Fields.Execute(
    operation: new Fields.LaplacianFieldRequest(ScalarField: scalarField, GridPoints: gridPoints, Sampling: sampling, Bounds: bounds),
    context: context);
```

## Integration with libs/core

### Result Monad
All operations return `Result<FieldResult>` for monadic error handling:
```csharp
Result<FieldResult> result = Fields.Execute(operation, context);

FieldResult value = result
    .Map(fr => ProcessFieldResult(fr))
    .Match(
        onSuccess: processed => processed,
        onFailure: errors => throw new InvalidOperationException(errors[0].ToString()));
```

### IGeometryContext
Required for all field operations. Provides tolerance, RTree indexing, and validation:
```csharp
IGeometryContext context = new GeometryContext(
    absoluteTolerance: RhinoDoc.ActiveDoc.ModelAbsoluteTolerance,
    angleTolerance: RhinoDoc.ActiveDoc.ModelAngleToleranceRadians);

Result<FieldResult> result = Fields.Execute(operation, context);
```

### Validation
Field operations validate inputs using `libs/core/validation/V`:
- `V.Standard`: Null checks, empty arrays, tolerance validation
- `V.Degeneracy`: Zero-length vectors, degenerate geometry
- `V.BoundingBox`: Valid bounds, non-empty regions

### Error Codes (E.Geometry)
Field operations return typed errors from `libs/core/errors/E.Geometry`:
```csharp
E.Geometry.InvalidFieldBounds                   // 2700
E.Geometry.InvalidStreamlineSeeds               // 2701
E.Geometry.InvalidIsovalue                      // 2702
E.Geometry.InvalidScalarField                   // 2703
E.Geometry.InvalidCurlComputation               // 2704
E.Geometry.InvalidDivergenceComputation         // 2705
E.Geometry.InvalidLaplacianComputation          // 2706
E.Geometry.InvalidVectorPotentialComputation    // 2707
E.Geometry.InvalidFieldInterpolation            // 2708
E.Geometry.InvalidHessianComputation            // 2709
E.Geometry.InvalidDirectionalDerivative         // 2710
E.Geometry.InvalidFieldMagnitude                // 2711
E.Geometry.InvalidFieldNormalization            // 2712
E.Geometry.InvalidCriticalPointDetection        // 2713
E.Geometry.InvalidFieldStatistics               // 2714
E.Geometry.InvalidFieldComposition              // 2715
```

## Implementation Details

### Files
- `Fields.cs` - Public API with 19 operation types and algebraic domain
- `FieldsCompute.cs` - Computational algorithms (finite differences, integration, marching cubes)
- `FieldsConfig.cs` - Constants and configuration tables
- `FieldsCore.cs` - Core execution engine with operation dispatch

### Configuration Limits (FieldsConfig)
```csharp
MinResolution: 8                                // Minimum grid resolution
MaxResolution: 256                              // Maximum grid resolution
DefaultResolution: 32                           // Default grid resolution
MinStepSize: √ε                                 // Minimum integration step (RhinoMath.SqrtEpsilon)
MaxStepSize: 1.0                                // Maximum integration step
DefaultStepSize: 0.01                           // Default integration step
MaxStreamlineSteps: 10000                       // Maximum integration steps per streamline
VectorPotentialIterations: 512                  // Poisson solver iterations for vector potential
```

### Performance Characteristics
- **Distance fields**: O(n·log(m)) using RTree spatial indexing (n = grid points, m = geometry elements)
- **Gradient computation**: O(n) via finite differences on signed distance field
- **Curl/divergence/Laplacian**: O(n) central differences with O(1) neighbor lookup
- **Hessian**: O(n) second-order finite differences
- **Interpolation**: O(1) nearest neighbor, O(8) trilinear (8 grid neighbors)
- **Streamline integration**: O(k·i) where k = seeds, i = steps per streamline (max 10000)
- **Isosurface extraction**: O(n) marching cubes with lookup table dispatch
- **Critical point detection**: O(n) eigenanalysis of 2×2 Hessian matrices
- **Field statistics**: O(n) single-pass computation with running statistics

### Numerical Methods
- **Finite differences**: Central differences with O(h²) accuracy
- **Integration**: Euler (O(h)), RK2 (O(h²)), RK4 (O(h⁴)) schemes
- **Eigenanalysis**: Direct 2×2 matrix eigendecomposition for critical point classification
- **Vector potential**: Iterative Poisson solver with convergence tolerance √ε
- **Marching cubes**: 256-case lookup table with complementary triangle winding

## LOC Budget
Total: 1523 lines across 4 files (within 4-file organizational limit)
