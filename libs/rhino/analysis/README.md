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
```

---

## Operations/Types

**Request Types**: `CurveAnalysis`, `SurfaceAnalysis`, `BrepAnalysis`, `ExtrusionAnalysis`, `MeshAnalysis`, `BatchAnalysis<T>`, `SurfaceQualityAnalysis`, `CurveFairnessAnalysis`, `MeshQualityAnalysis`

**Result Types**: `CurveData`, `SurfaceData`, `BrepData`, `MeshData`, `SurfaceQualityResult`, `CurveFairnessResult`, `MeshQualityResult`

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
```

---

## Integration

- **Result monad**: `libs/core/results/Result.cs` - all operations return `Result<T>`
- **IGeometryContext**: `libs/core/context/IGeometryContext.cs` - tolerance resolution
- **Validation**: `V.Standard | V.Degeneracy` (curves), `V.Standard | V.UVDomain` (surfaces), `V.Standard | V.Topology` (Breps), `V.Standard | V.MeshSpecific` (meshes)
- **Errors**: `E.Geometry.UnsupportedAnalysis`, `E.Geometry.CurveAnalysisFailed`, `E.Geometry.SurfaceAnalysisFailed`, `E.Geometry.BrepAnalysisFailed`, `E.Geometry.MeshAnalysisFailed`

---

## Internals

**Files**: `Analysis.cs` (API, 253 LOC), `AnalysisCore.cs` (orchestration), `AnalysisCompute.cs` (algorithms), `AnalysisConfig.cs` (dispatch tables, 121 LOC)

**Dispatch**: `FrozenDictionary<Type, DifferentialMetadata>` and `FrozenDictionary<Type, QualityMetadata>` for O(1) request routing

**Quality thresholds**: Aspect ratio (warning: 3.0, critical: 10.0), skewness (warning: 0.5, critical: 0.85), Jacobian (warning: 0.3, critical: 0.1)

**Derivative orders**: 1 = position+tangent, 2 = +curvature (default), 3 = +jerk

**Curvature computation**: Gaussian K = κ₁κ₂, mean H = (κ₁+κ₂)/2, principal directions via shape operator eigenanalysis
