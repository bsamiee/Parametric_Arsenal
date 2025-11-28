# Differential Geometry and Quality Analysis

Polymorphic differential geometry and quality analysis for curves, surfaces, Breps, and meshes.

---

## API

```csharp
Result<CurveData> Analyze(Curve curve, IGeometryContext context, double? parameter = null, int derivativeOrder = 2)
Result<SurfaceData> Analyze(Surface surface, IGeometryContext context, (double u, double v)? uvParameter = null, int derivativeOrder = 2)
Result<BrepData> Analyze(Brep brep, IGeometryContext context, (double u, double v)? uvParameter = null, int faceIndex = 0, Point3d? testPoint = null, int derivativeOrder = 2)
Result<MeshData> Analyze(Mesh mesh, IGeometryContext context, int vertexIndex = 0)
Result<IReadOnlyList<IResult>> AnalyzeMultiple<T>(IReadOnlyList<T> geometries, IGeometryContext context, ...) where T : GeometryBase
Result<SurfaceQualityResult> AnalyzeSurfaceQuality(Surface surface, IGeometryContext context)
Result<CurveFairnessResult> AnalyzeCurveFairness(Curve curve, IGeometryContext context)
Result<MeshQualityResult> AnalyzeMeshForFEA(Mesh mesh, IGeometryContext context)
Result<CurvatureProfileResult> AnalyzeCurvatureProfile(Curve curve, IGeometryContext context, int sampleCount = 50, bool includeTorsion = false)
Result<SurfaceCurvatureProfileResult> AnalyzeSurfaceCurvatureProfile(Surface surface, IGeometryContext context, int sampleCountU = 10, int sampleCountV = 10, CurvatureProfileDirection? direction = null)
Result<ShapeConformanceResult> AnalyzeShapeConformance(Surface surface, IGeometryContext context, ShapeTarget? target = null)
Result<CurveConformanceResult> AnalyzeCurveConformance(Curve curve, IGeometryContext context, CurveShapeTarget? target = null)
```

---

## Operations/Types

**Request Types**: `CurveAnalysis`, `SurfaceAnalysis`, `BrepAnalysis`, `ExtrusionAnalysis`, `MeshAnalysis`, `BatchAnalysis<T>`, `SurfaceQualityAnalysis`, `CurveFairnessAnalysis`, `MeshQualityAnalysis`, `CurvatureProfileAnalysis`, `SurfaceCurvatureProfileAnalysis`, `ShapeConformanceAnalysis`, `CurveConformanceAnalysis`

**Result Types**: `CurveData`, `SurfaceData`, `BrepData`, `MeshData`, `SurfaceQualityResult`, `CurveFairnessResult`, `MeshQualityResult`, `CurvatureProfileResult`, `SurfaceCurvatureProfileResult`, `ShapeConformanceResult`, `CurveConformanceResult`

**Algebraic Types**: `CurvatureProfileDirection` (`UDirection`, `VDirection`, `BothDirections`), `ShapeTarget` (`PlanarTarget`, `CylindricalTarget`, `SphericalTarget`, `ConicalTarget`, `ToroidalTarget`, `AnyTarget`), `CurveShapeTarget` (`LinearTarget`, `CircularTarget`, `AnyCurveTarget`)

---

## Usage

```csharp
IGeometryContext context = new GeometryContext(absoluteTolerance: 0.001);

// Curve differential geometry
Result<Analysis.CurveData> curveResult = Analysis.Analyze(
    curve: curve,
    context: context,
    parameter: 0.5,
    derivativeOrder: 2);

// Surface quality analysis
Result<Analysis.SurfaceQualityResult> surfaceQuality = Analysis.AnalyzeSurfaceQuality(
    surface: surface,
    context: context);

// Mesh FEA metrics
Result<Analysis.MeshQualityResult> meshQuality = Analysis.AnalyzeMeshForFEA(
    mesh: mesh,
    context: context);

// Curvature profile with statistics
Result<Analysis.CurvatureProfileResult> curvatureProfile = Analysis.AnalyzeCurvatureProfile(
    curve: curve,
    context: context,
    sampleCount: 50,
    includeTorsion: true);

// Surface curvature profile (2D grid sampling)
Result<Analysis.SurfaceCurvatureProfileResult> surfaceProfile = Analysis.AnalyzeSurfaceCurvatureProfile(
    surface: surface,
    context: context,
    sampleCountU: 10,
    sampleCountV: 10,
    direction: new Analysis.BothDirections());

// Shape conformance (surface vs ideal primitives)
Result<Analysis.ShapeConformanceResult> shapeConformance = Analysis.AnalyzeShapeConformance(
    surface: surface,
    context: context,
    target: new Analysis.CylindricalTarget());

// Curve conformance (curve vs line/arc)
Result<Analysis.CurveConformanceResult> curveConformance = Analysis.AnalyzeCurveConformance(
    curve: curve,
    context: context,
    target: new Analysis.LinearTarget());
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - all operations return `Result<T>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.Degeneracy` (curves), `V.Standard | V.UVDomain` (surfaces), `V.Standard | V.Topology` (Breps), `V.Standard | V.MeshSpecific` (meshes)
- **Errors**: `E.Geometry.UnsupportedAnalysis`, `E.Geometry.CurveAnalysisFailed`, `E.Geometry.SurfaceAnalysisFailed`, `E.Geometry.BrepAnalysisFailed`, `E.Geometry.MeshAnalysisFailed`

---

## Internals

**Files**: `Analysis.cs` (API), `AnalysisCore.cs` (orchestration), `AnalysisCompute.cs` (algorithms), `AnalysisConfig.cs` (dispatch tables)

**Dispatch**: `FrozenDictionary<Type, DifferentialMetadata>` and `FrozenDictionary<Type, QualityMetadata>` for O(1) request routing

**Quality thresholds**: Aspect ratio (warning: 3.0, critical: 10.0), skewness (warning: 0.5, critical: 0.85), Jacobian (warning: 0.3, critical: 0.1)

**Derivative orders**: 1 = position+tangent, 2 = +curvature (default), 3 = +jerk

**Curvature computation**: Gaussian K = κ₁κ₂, mean H = (κ₁+κ₂)/2, principal directions via shape operator eigenanalysis

**Curvature profile**: Multi-point sampling along curve/surface parameter space with min/max/mean/variance statistics and extrema locations

**Shape conformance**: Deviation from ideal primitives (plane, cylinder, sphere, cone, torus) with max/min/mean/RMS metrics and conformance score (1.0 = perfect fit within tolerance)
