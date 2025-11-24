# libs/rhino/analysis

Polymorphic differential geometry and quality analysis for Rhino curves, surfaces, Breps, meshes, and extrusions using unified algebraic dispatch.

## Features

- **Differential Geometry Analysis** - Derivatives, curvature (Gaussian, mean, principal), torsion, frames, discontinuities
- **Quality Analysis** - Surface uniformity, curve fairness, mesh FEA metrics (aspect ratio, skewness, Jacobian)
- **Topology Analysis** - Manifold detection, closure, vertices, edges, proximity queries
- **Batch Operations** - Unified error handling across heterogeneous geometry collections
- **Result Monad Integration** - All operations return `Result<T>` with explicit error handling
- **ValidationRules Integration** - Automatic geometry validation via `V` flags
- **UnifiedOperation Dispatch** - Single execution path with configurable validation, caching, diagnostics

## File Structure

```
libs/rhino/analysis/
├── Analysis.cs           # Public API entry point with polymorphic dispatch
├── AnalysisConfig.cs     # FrozenDictionary dispatch tables and constants
├── AnalysisCore.cs       # UnifiedOperation orchestration layer
└── AnalysisCompute.cs    # Dense computational algorithms
```

## API Surface

### Differential Geometry Analysis

```csharp
// Curve differential geometry at parameter
Result<CurveData> Analyze(
    Curve curve,
    IGeometryContext context,
    double? parameter = null,
    int derivativeOrder = 2)

// Surface differential geometry at UV parameters
Result<SurfaceData> Analyze(
    Surface surface,
    IGeometryContext context,
    (double u, double v)? uvParameter = null,
    int derivativeOrder = 2)

// Brep surface, topology, and proximity analysis
Result<BrepData> Analyze(
    Brep brep,
    IGeometryContext context,
    (double u, double v)? uvParameter = null,
    int faceIndex = 0,
    Point3d? testPoint = null,
    int derivativeOrder = 2)

// Mesh topology and manifold properties
Result<MeshData> Analyze(
    Mesh mesh,
    IGeometryContext context,
    int vertexIndex = 0)

// Batch analysis for heterogeneous geometry collections
Result<IReadOnlyList<IResult>> AnalyzeMultiple<T>(
    IReadOnlyList<T> geometries,
    IGeometryContext context,
    double? parameter = null,
    (double u, double v)? uvParameter = null,
    int? index = null,
    Point3d? testPoint = null,
    int derivativeOrder = 2) where T : GeometryBase
```

### Quality Analysis

```csharp
// Surface curvature uniformity and singularity detection
Result<SurfaceQualityResult> AnalyzeSurfaceQuality(
    Surface surface,
    IGeometryContext context)

// Curve fairness via curvature variation and inflection detection
Result<CurveFairnessResult> AnalyzeCurveFairness(
    Curve curve,
    IGeometryContext context)

// Mesh FEA quality metrics (aspect ratio, skewness, Jacobian)
Result<MeshQualityResult> AnalyzeMeshForFEA(
    Mesh mesh,
    IGeometryContext context)
```

## Type Hierarchy

### Request Types (Algebraic Domain)

```csharp
// Differential geometry requests
abstract record DifferentialRequest(GeometryBase Geometry, int DerivativeOrder)
sealed record CurveAnalysis(Curve, double Parameter, int DerivativeOrder) : DifferentialRequest
sealed record SurfaceAnalysis(Surface, double U, double V, int DerivativeOrder) : DifferentialRequest
sealed record BrepAnalysis(Brep, int FaceIndex, double U, double V, Point3d TestPoint, int DerivativeOrder) : DifferentialRequest
sealed record ExtrusionAnalysis(Extrusion, int FaceIndex, double U, double V, Point3d TestPoint, int DerivativeOrder) : DifferentialRequest
sealed record MeshAnalysis(Mesh, int VertexIndex) : DifferentialRequest
sealed record BatchAnalysis<T>(IReadOnlyList<T> Geometries, double? Parameter, (double U, double V)? UV, int? Index, Point3d? TestPoint, int DerivativeOrder) : DifferentialRequest

// Quality analysis requests
abstract record QualityRequest(GeometryBase Geometry)
sealed record SurfaceQualityAnalysis(Surface) : QualityRequest
sealed record CurveFairnessAnalysis(Curve) : QualityRequest
sealed record MeshQualityAnalysis(Mesh) : QualityRequest
```

### Result Types

```csharp
interface IResult  // Marker for polymorphic dispatch

// Differential geometry results
sealed record CurveData(Point3d Location, Vector3d[] Derivatives, double Curvature, Plane Frame, Plane[] PerpendicularFrames, double Torsion, double[] DiscontinuityParameters, Continuity[] DiscontinuityTypes, double Length, Point3d Centroid) : IResult
sealed record SurfaceData(Point3d Location, Vector3d[] Derivatives, double Gaussian, double Mean, double K1, double K2, Vector3d PrincipalDir1, Vector3d PrincipalDir2, Plane Frame, Vector3d Normal, bool AtSeam, bool AtSingularity, double Area, Point3d Centroid) : IResult
sealed record BrepData(Point3d Location, Vector3d[] Derivatives, double Gaussian, double Mean, double K1, double K2, Vector3d PrincipalDir1, Vector3d PrincipalDir2, Plane Frame, Vector3d Normal, (int Index, Point3d Point)[] Vertices, (int Index, Line Geometry)[] Edges, bool IsManifold, bool IsSolid, Point3d ClosestPoint, double Distance, ComponentIndex Component, (double U, double V) SurfaceUV, double Area, double Volume, Point3d Centroid) : IResult
sealed record MeshData(Point3d Location, Plane Frame, Vector3d Normal, (int Index, Point3d Point)[] TopologyVertices, (int Index, Line Geometry)[] TopologyEdges, bool IsManifold, bool IsClosed, double Area, double Volume) : IResult

// Quality analysis results
sealed record SurfaceQualityResult(double[] GaussianCurvatures, double[] MeanCurvatures, (double U, double V)[] SingularityLocations, double UniformityScore) : IResult
sealed record CurveFairnessResult(double SmoothnessScore, double[] CurvatureValues, (double Parameter, bool IsSharp)[] InflectionPoints, double BendingEnergy) : IResult
sealed record MeshQualityResult(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaceIndices, (int Warning, int Critical) QualityFlags) : IResult
```

## Usage Examples

### Curve Differential Geometry

```csharp
IGeometryContext context = new GeometryContext();
Curve curve = new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0));

Result<Analysis.CurveData> result = Analysis.Analyze(
    curve: curve,
    context: context,
    parameter: curve.Domain.Mid,
    derivativeOrder: 2);

result.Match(
    onSuccess: data => Console.WriteLine($"κ={data.Curvature:F6}, τ={data.Torsion:F6}, L={data.Length:F3}"),
    onFailure: errors => Console.WriteLine($"Error: {errors[0].Message}"));
```

### Surface Quality Analysis

```csharp
Surface surface = new PlaneSurface(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10));

Result<Analysis.SurfaceQualityResult> result = Analysis.AnalyzeSurfaceQuality(surface, context);

result.Match(
    onSuccess: quality => Console.WriteLine($"Uniformity={quality.UniformityScore:F3}, Singularities={quality.SingularityLocations.Length}"),
    onFailure: errors => Console.WriteLine($"Error: {errors[0].Message}"));
```

### Mesh FEA Quality

```csharp
Mesh mesh = Mesh.CreateFromBox(new Box(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10), new Interval(0, 10)), 5, 5, 5);

Result<Analysis.MeshQualityResult> result = Analysis.AnalyzeMeshForFEA(mesh, context);

result.Match(
    onSuccess: quality => Console.WriteLine($"Warnings={quality.QualityFlags.Warning}, Critical={quality.QualityFlags.Critical}, MaxAR={quality.AspectRatios.Max():F3}"),
    onFailure: errors => Console.WriteLine($"Error: {errors[0].Message}"));
```

### Batch Heterogeneous Analysis

```csharp
IReadOnlyList<GeometryBase> geometries = [
    new LineCurve(new Point3d(0, 0, 0), new Point3d(10, 0, 0)),
    new PlaneSurface(Plane.WorldXY, new Interval(0, 10), new Interval(0, 10)),
    Mesh.CreateFromSphere(new Sphere(Point3d.Origin, 5.0), 10, 10),
];

Result<IReadOnlyList<Analysis.IResult>> result = Analysis.AnalyzeMultiple(geometries, context, derivativeOrder: 2);

result.Match(
    onSuccess: results => {
        foreach (Analysis.IResult r in results) {
            Console.WriteLine(r switch {
                Analysis.CurveData c => $"Curve: κ={c.Curvature:F3}",
                Analysis.SurfaceData s => $"Surface: K={s.Gaussian:F3}",
                Analysis.MeshData m => $"Mesh: V={m.Volume:F3}",
                _ => "Unknown",
            });
        }
    },
    onFailure: errors => Console.WriteLine($"Error: {errors[0].Message}"));
```

## Integration with libs/core

### Result Monad

All operations return `Result<T>` for explicit error handling and monadic composition:

```csharp
Result<double> curvature = Analysis.Analyze(curve, context)
    .Map(data => data.Curvature)
    .Ensure(k => k > 0.0, error: E.Validation.GeometryInvalid);
```

### Validation Modes

Automatic geometry validation via `V` flags (configured in `AnalysisConfig`):
- Curve: `V.Standard | V.Degeneracy`
- Surface: `V.Standard | V.UVDomain`
- Brep: `V.Standard | V.Topology`
- Mesh: `V.Standard | V.MeshSpecific`

### Error Codes

Errors use `E.*` constants from `libs/core/errors/E.cs`:
- `E.Geometry.UnsupportedAnalysis` - Unknown request type
- `E.Geometry.SurfaceAnalysisFailed` - Surface evaluation failed
- `E.Geometry.CurveAnalysisFailed` - Curve evaluation failed
- `E.Geometry.BrepAnalysisFailed` - Brep analysis failed
- `E.Geometry.MeshAnalysisFailed` - Mesh topology failed

### IGeometryContext

All operations require `IGeometryContext` for tolerance:

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001, angleTolerance: RhinoMath.ToRadians(1.0));
```

## Configuration

### Derivative Orders

Default is 2 (position, tangent, curvature). Use 1 for position+tangent, 3 for position+tangent+curvature+jerk.

### Quality Thresholds

Mesh FEA thresholds in `AnalysisConfig`:
- Aspect Ratio: Warning=3.0, Critical=10.0
- Skewness: Warning=0.5, Critical=0.85
- Jacobian: Warning=0.3, Critical=0.1

## Architecture

### Dispatch Tables

`AnalysisConfig` uses `FrozenDictionary` for O(1) operation dispatch:

```csharp
// Differential geometry dispatch: request type → metadata
FrozenDictionary<Type, DifferentialMetadata> DifferentialOperations

// Quality analysis dispatch: request type → metadata
FrozenDictionary<Type, QualityMetadata> QualityOperations
```

### Orchestration Layer

`AnalysisCore` routes requests through `UnifiedOperation.Apply()`:

1. Lookup metadata from dispatch tables
2. Route to specialized compute method
3. Apply validation via `ValidationRules`
4. Execute via `UnifiedOperation` with configuration
5. Return `Result<T>` with success value or errors

### Computation Layer

`AnalysisCompute` contains dense algorithms:

- Differential geometry: `ComputeCurve`, `ComputeSurface`, `ComputeBrep`, `ComputeMesh`
- Quality analysis: `ComputeSurfaceQuality`, `ComputeCurveFairness`, `ComputeMeshQuality`
- All methods marked `[Pure]` and `[MethodImpl(AggressiveInlining)]`

## Testing

```bash
# Run all analysis tests
dotnet test --filter "FullyQualifiedName~Analysis"

# Run specific test
dotnet test --filter "Name~Analyze_Curve_ReturnsValidData"
```

## Dependencies

- **libs/core** - `Result<T>`, `IGeometryContext`, `ValidationRules`, `UnifiedOperation`, `E` error registry
- **RhinoCommon 8.24+** - `Curve`, `Surface`, `Brep`, `Mesh`, `Extrusion`, `AreaMassProperties`, `VolumeMassProperties`

## Related

- `libs/core/results/Result.cs` - Result monad implementation
- `libs/core/validation/ValidationRules.cs` - Validation mode definitions
- `libs/core/operations/UnifiedOperation.cs` - Polymorphic dispatch engine
- `libs/core/errors/E.cs` - Error code registry
