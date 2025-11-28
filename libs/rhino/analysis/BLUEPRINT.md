# Analysis Library Blueprint: Extended Capabilities

## Overview

This blueprint defines 2 genuinely new SDK-backed analysis capabilities for the existing `libs/rhino/analysis/` folder that extend differential geometry and quality analysis without duplicating adjacent folder responsibilities.

## Existing Infrastructure Analysis

### Current Analysis Folder Architecture (4 files)

| File | Purpose | LOC |
|------|---------|-----|
| `Analysis.cs` | Public API + nested domain types (requests, results) | ~253 |
| `AnalysisConfig.cs` | FrozenDictionary dispatch + metadata records | ~120 |
| `AnalysisCore.cs` | UnifiedOperation orchestration | ~238 |
| `AnalysisCompute.cs` | Raw SDK computation algorithms | ~366 |

### Existing Capabilities

**Differential Geometry (single-point analysis)**:
- `CurveData`: Location, derivatives, curvature, frame, torsion, discontinuities, length, centroid
- `SurfaceData`: Location, derivatives, Gaussian/mean curvature, principal directions, frame, singularities
- `BrepData`: Surface differential geometry + topology (vertices, edges, manifold, solid, closest point)
- `MeshData`: Topology vertices/edges, manifold state, area, volume

**Quality Analysis (aggregate metrics)**:
- `SurfaceQualityResult`: Curvature samples, singularities, uniformity score
- `CurveFairnessResult`: Smoothness, curvature values, inflections, bending energy
- `MeshQualityResult`: FEA metrics (aspect ratios, skewness, Jacobians)

### Adjacent Folder Responsibilities (Must NOT Duplicate)

| Folder | Responsibility | Key Capabilities |
|--------|---------------|------------------|
| `orientation/` | Alignment, symmetry, patterns | ToPlane, ToBestFit, Mirror, ToCanonical, DetectAndAlign |
| `spatial/` | RTree indexing, clustering, proximity | Range/Proximity queries, K-means, DBSCAN, ConvexHull |
| `fields/` | Scalar/vector fields, differential operators | Gradient, Curl, Divergence, Laplacian, Streamlines, Critical points |
| `topology/` | Edge/vertex connectivity, healing | Naked edges, continuity classification, healing strategies |
| `extraction/` | Point/curve extraction, features | Boundary, Isocurves, Greville, Discontinuity, Primitive decomposition |

### libs/core/ Infrastructure We Leverage

- **Result<T> Monad**: Map, Bind, Ensure for error handling and composition
- **UnifiedOperation**: Polymorphic dispatch with OperationConfig
- **ValidationRules**: V.* modes (V.Standard, V.Degeneracy, V.UVDomain, etc.)
- **Error Registry**: E.Geometry.* errors (2300-2304 range for analysis)

## SDK Research Summary

### Capability 1: Curvature Profile Analysis

**Problem Addressed**: Existing analysis provides single-point curvature data. Parametric design workflows often need curvature **profiles** along curves or across surfaces to evaluate fairness, identify problem areas, and guide refinement.

**SDK Methods**:
- `Curve.CurvatureAt(double t)` → Vector3d curvature vector
- `Curve.TorsionAt(double t)` → double torsion value
- `Curve.GetNextDiscontinuity()` → discontinuity detection
- `Surface.CurvatureAt(double u, double v)` → SurfaceCurvature
- `Surface.IsoCurve(int direction, double param)` → extract parameter lines

**Output**: Sampled curvature/torsion arrays with statistical metrics (min, max, mean, variance, extrema locations).

**Use Cases**:
- Evaluate curve smoothness for CNC machining
- Identify curvature discontinuities in blended surfaces
- Compare curvature profiles between design iterations

### Capability 2: Shape Conformance Analysis

**Problem Addressed**: Manufacturing and quality control require measuring how closely geometry conforms to ideal primitives (planarity, cylindricity, sphericity). This is distinct from primitive **decomposition** (in extraction/) which classifies surfaces—conformance measures **deviation**.

**SDK Methods**:
- `Surface.TryGetPlane(out Plane)` → planarity test
- `Surface.TryGetCylinder(out Cylinder)` → cylindricity test
- `Surface.TryGetSphere(out Sphere)` → sphericity test
- `Surface.TryGetCone(out Cone)` → conicity test
- `Surface.TryGetTorus(out Torus)` → toroidal test
- `Curve.TryGetLine(out Line)` → linearity test
- `Curve.TryGetArc(out Arc)` → circularity test
- `Surface.ClosestPoint()` / `Curve.ClosestPoint()` → deviation measurement

**Output**: Deviation metrics (max, min, mean, RMS), conformance score, ideal primitive parameters.

**Use Cases**:
- QC validation: "Is this surface planar within 0.01mm?"
- Manufacturing prep: "What's the cylindricity deviation for this revolved surface?"
- Design optimization: "How close is this freeform to a sphere?"

### Why These Don't Duplicate Adjacent Folders

| Capability | Why Not in Adjacent Folder |
|------------|---------------------------|
| Curvature Profiles | **fields/**: Operates on explicit vector fields, not geometry curvature. **analysis/**: Curvature is differential geometry, existing theme. |
| Shape Conformance | **extraction/**: Primitive decomposition classifies types. **analysis/**: Conformance measures deviation from ideal—a quality metric. |

## Proposed Type Architecture

### New Request Types (in Analysis.cs)

```csharp
/// <summary>Curvature profile analysis along curve parameter space.</summary>
public sealed record CurvatureProfileAnalysis(
    Curve Curve,
    int SampleCount,
    bool IncludeTorsion) : DifferentialRequest(Curve, 2);

/// <summary>Curvature profile analysis across surface parameter space.</summary>
public sealed record SurfaceCurvatureProfileAnalysis(
    Surface Surface,
    int SampleCountU,
    int SampleCountV,
    CurvatureProfileDirection Direction) : DifferentialRequest(Surface, 2);

/// <summary>Shape conformance analysis against ideal primitives.</summary>
public sealed record ShapeConformanceAnalysis(
    Surface Surface,
    ShapeTarget Target) : QualityRequest(Surface);

/// <summary>Curve conformance analysis (linearity, circularity).</summary>
public sealed record CurveConformanceAnalysis(
    Curve Curve,
    CurveShapeTarget Target) : QualityRequest(Curve);
```

### New Result Types (in Analysis.cs)

```csharp
/// <summary>Curvature profile direction for surface sampling.</summary>
public abstract record CurvatureProfileDirection;
public sealed record UDirection : CurvatureProfileDirection;
public sealed record VDirection : CurvatureProfileDirection;
public sealed record BothDirections : CurvatureProfileDirection;

/// <summary>Target shape for conformance analysis.</summary>
public abstract record ShapeTarget;
public sealed record PlanarTarget : ShapeTarget;
public sealed record CylindricalTarget : ShapeTarget;
public sealed record SphericalTarget : ShapeTarget;
public sealed record ConicalTarget : ShapeTarget;
public sealed record ToroidalTarget : ShapeTarget;
public sealed record AnyTarget : ShapeTarget;  // Auto-detect best fit

/// <summary>Target shape for curve conformance.</summary>
public abstract record CurveShapeTarget;
public sealed record LinearTarget : CurveShapeTarget;
public sealed record CircularTarget : CurveShapeTarget;
public sealed record AnyCurveTarget : CurveShapeTarget;

/// <summary>Curvature profile along curve: sampled values with statistics.</summary>
[DebuggerDisplay("CurvatureProfile | Samples={CurvatureValues.Length} | Max={MaxCurvature:F4}")]
public sealed record CurvatureProfileResult(
    double[] Parameters,
    double[] CurvatureValues,
    double[]? TorsionValues,
    (double Parameter, double Value)[] ExtremaLocations,
    double MinCurvature,
    double MaxCurvature,
    double MeanCurvature,
    double Variance) : IResult;

/// <summary>Surface curvature profile: 2D grid samples with statistics.</summary>
[DebuggerDisplay("SurfaceCurvatureProfile | Grid={GaussianValues.Length}x{GaussianValues[0].Length}")]
public sealed record SurfaceCurvatureProfileResult(
    (double U, double V)[] SampleLocations,
    double[] GaussianValues,
    double[] MeanValues,
    (double U, double V, double Value)[] GaussianExtrema,
    (double U, double V, double Value)[] MeanExtrema,
    double GaussianRange,
    double MeanRange,
    double UniformityScore) : IResult;

/// <summary>Shape conformance result with deviation metrics.</summary>
[DebuggerDisplay("Conformance | Target={DetectedShape} | MaxDev={MaxDeviation:F6} | Score={ConformanceScore:F3}")]
public sealed record ShapeConformanceResult(
    ShapeTarget DetectedShape,
    object? IdealPrimitive,  // Plane, Cylinder, Sphere, Cone, or Torus
    double MaxDeviation,
    double MinDeviation,
    double MeanDeviation,
    double RmsDeviation,
    Point3d MaxDeviationLocation,
    double ConformanceScore,
    bool WithinTolerance) : IResult;

/// <summary>Curve conformance result.</summary>
[DebuggerDisplay("CurveConformance | Target={DetectedShape} | MaxDev={MaxDeviation:F6}")]
public sealed record CurveConformanceResult(
    CurveShapeTarget DetectedShape,
    object? IdealPrimitive,  // Line or Arc
    double MaxDeviation,
    double MeanDeviation,
    double RmsDeviation,
    double ConformanceScore) : IResult;
```

### New Public API Methods (in Analysis.cs)

```csharp
/// <summary>Analyzes curvature profile along curve parameter space.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<CurvatureProfileResult> AnalyzeCurvatureProfile(
    Curve curve,
    IGeometryContext context,
    int sampleCount = 50,
    bool includeTorsion = false) =>
    AnalysisCore.ExecuteQuality<CurvatureProfileResult>(
        request: new CurvatureProfileAnalysis(
            Curve: curve,
            SampleCount: sampleCount,
            IncludeTorsion: includeTorsion),
        context: context);

/// <summary>Analyzes curvature profile across surface parameter space.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<SurfaceCurvatureProfileResult> AnalyzeSurfaceCurvatureProfile(
    Surface surface,
    IGeometryContext context,
    int sampleCountU = 10,
    int sampleCountV = 10,
    CurvatureProfileDirection? direction = null) =>
    AnalysisCore.ExecuteQuality<SurfaceCurvatureProfileResult>(
        request: new SurfaceCurvatureProfileAnalysis(
            Surface: surface,
            SampleCountU: sampleCountU,
            SampleCountV: sampleCountV,
            Direction: direction ?? new BothDirections()),
        context: context);

/// <summary>Analyzes shape conformance against ideal primitives.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<ShapeConformanceResult> AnalyzeShapeConformance(
    Surface surface,
    IGeometryContext context,
    ShapeTarget? target = null) =>
    AnalysisCore.ExecuteQuality<ShapeConformanceResult>(
        request: new ShapeConformanceAnalysis(
            Surface: surface,
            Target: target ?? new AnyTarget()),
        context: context);

/// <summary>Analyzes curve conformance to line or arc.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<CurveConformanceResult> AnalyzeCurveConformance(
    Curve curve,
    IGeometryContext context,
    CurveShapeTarget? target = null) =>
    AnalysisCore.ExecuteQuality<CurveConformanceResult>(
        request: new CurveConformanceAnalysis(
            Curve: curve,
            Target: target ?? new AnyCurveTarget()),
        context: context);
```

## Dispatch Configuration (in AnalysisConfig.cs)

```csharp
// Add to QualityOperations FrozenDictionary:
[typeof(Analysis.CurvatureProfileAnalysis)] = new(
    ValidationMode: V.Standard | V.Degeneracy,
    OperationName: "Analysis.CurvatureProfile",
    SampleCount: 50,
    GridDimension: 0,
    BoundaryFraction: 0.0,
    ProximityFactor: 0.0,
    CurvatureMultiplier: 0.0,
    InflectionThreshold: 0.0,
    SmoothnessSensitivity: 0.0),

[typeof(Analysis.SurfaceCurvatureProfileAnalysis)] = new(
    ValidationMode: V.Standard | V.UVDomain,
    OperationName: "Analysis.SurfaceCurvatureProfile",
    SampleCount: 100,
    GridDimension: 10,
    BoundaryFraction: 0.0,
    ProximityFactor: 0.0,
    CurvatureMultiplier: 0.0,
    InflectionThreshold: 0.0,
    SmoothnessSensitivity: 0.0),

[typeof(Analysis.ShapeConformanceAnalysis)] = new(
    ValidationMode: V.Standard | V.BoundingBox,
    OperationName: "Analysis.ShapeConformance",
    SampleCount: 100,
    GridDimension: 10,
    BoundaryFraction: 0.0,
    ProximityFactor: 0.0,
    CurvatureMultiplier: 0.0,
    InflectionThreshold: 0.0,
    SmoothnessSensitivity: 0.0),

[typeof(Analysis.CurveConformanceAnalysis)] = new(
    ValidationMode: V.Standard | V.Degeneracy,
    OperationName: "Analysis.CurveConformance",
    SampleCount: 50,
    GridDimension: 0,
    BoundaryFraction: 0.0,
    ProximityFactor: 0.0,
    CurvatureMultiplier: 0.0,
    InflectionThreshold: 0.0,
    SmoothnessSensitivity: 0.0),
```

## Core Algorithm Sketches (for AnalysisCompute.cs)

### CurvatureProfile Computation

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<Analysis.CurvatureProfileResult> ComputeCurvatureProfile(
    Curve curve,
    int sampleCount,
    bool includeTorsion,
    IGeometryContext context) {
    int count = Math.Max(2, sampleCount);
    double[] parameters = new double[count];
    double[] curvatures = new double[count];
    double[]? torsions = includeTorsion ? new double[count] : null;
    double divisor = count - 1.0;
    double sum = 0.0;
    double min = double.MaxValue;
    double max = double.MinValue;
    int minIdx = 0;
    int maxIdx = 0;

    for (int i = 0; i < count; i++) {
        double t = curve.Domain.ParameterAt(i / divisor);
        parameters[i] = t;
        Vector3d curvatureVec = curve.CurvatureAt(t);
        double k = curvatureVec.IsValid ? curvatureVec.Length : 0.0;
        curvatures[i] = k;
        sum += k;
        (min, minIdx) = k < min ? (k, i) : (min, minIdx);
        (max, maxIdx) = k > max ? (k, i) : (max, maxIdx);
        torsions = includeTorsion && torsions is not null
            ? (torsions[i] = curve.TorsionAt(t), torsions).Item2
            : torsions;
    }

    double mean = sum / count;
    double varianceSum = 0.0;
    for (int i = 0; i < count; i++) {
        double diff = curvatures[i] - mean;
        varianceSum += diff * diff;
    }

    return ResultFactory.Create(value: new Analysis.CurvatureProfileResult(
        Parameters: parameters,
        CurvatureValues: curvatures,
        TorsionValues: torsions,
        ExtremaLocations: [
            (parameters[minIdx], min),
            (parameters[maxIdx], max),
        ],
        MinCurvature: min,
        MaxCurvature: max,
        MeanCurvature: mean,
        Variance: varianceSum / count));
}
```

### ShapeConformance Computation

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static Result<Analysis.ShapeConformanceResult> ComputeShapeConformance(
    Surface surface,
    Analysis.ShapeTarget target,
    int sampleCount,
    IGeometryContext context) =>
    (target, TryFitPrimitive(surface, target, context)) switch {
        (_, null) => ResultFactory.Create<Analysis.ShapeConformanceResult>(
            error: E.Geometry.SurfaceAnalysisFailed.WithContext("No conforming primitive detected")),
        (_, (Analysis.ShapeTarget detected, object primitive, double[] deviations, Point3d maxLoc)) =>
            ResultFactory.Create(value: new Analysis.ShapeConformanceResult(
                DetectedShape: detected,
                IdealPrimitive: primitive,
                MaxDeviation: deviations.Max(),
                MinDeviation: deviations.Min(),
                MeanDeviation: deviations.Average(),
                RmsDeviation: Math.Sqrt(deviations.Sum(d => d * d) / deviations.Length),
                MaxDeviationLocation: maxLoc,
                ConformanceScore: ComputeConformanceScore(deviations, context.AbsoluteTolerance),
                WithinTolerance: deviations.Max() <= context.AbsoluteTolerance)),
    };

private static (Analysis.ShapeTarget, object, double[], Point3d)? TryFitPrimitive(
    Surface surface,
    Analysis.ShapeTarget target,
    IGeometryContext context) =>
    target switch {
        Analysis.PlanarTarget => surface.TryGetPlane(out Plane plane)
            ? (new Analysis.PlanarTarget(), plane, ComputeDeviations(surface, plane, context), FindMaxDeviationPoint(surface, plane, context))
            : null,
        Analysis.CylindricalTarget => surface.TryGetCylinder(out Cylinder cyl)
            ? (new Analysis.CylindricalTarget(), cyl, ComputeDeviations(surface, cyl, context), FindMaxDeviationPoint(surface, cyl, context))
            : null,
        Analysis.SphericalTarget => surface.TryGetSphere(out Sphere sph)
            ? (new Analysis.SphericalTarget(), sph, ComputeDeviations(surface, sph, context), FindMaxDeviationPoint(surface, sph, context))
            : null,
        Analysis.ConicalTarget => surface.TryGetCone(out Cone cone)
            ? (new Analysis.ConicalTarget(), cone, ComputeDeviations(surface, cone, context), FindMaxDeviationPoint(surface, cone, context))
            : null,
        Analysis.ToroidalTarget => surface.TryGetTorus(out Torus torus)
            ? (new Analysis.ToroidalTarget(), torus, ComputeDeviations(surface, torus, context), FindMaxDeviationPoint(surface, torus, context))
            : null,
        Analysis.AnyTarget => TryFitBestPrimitive(surface, context),
        _ => null,
    };
```

## Adherence to Limits

| Metric | Current | After Addition | Limit | Status |
|--------|---------|----------------|-------|--------|
| Files | 4 | 4 | 4 max | ✓ Within limit |
| Types (requests) | 8 | 12 | - | Nested in Analysis.cs |
| Types (results) | 7 | 11 | - | Nested in Analysis.cs |
| Types (total in folder) | ~15 | ~23 | - | All nested types |
| Analysis.cs LOC | ~253 | ~350 est. | 300/method | ✓ Methods stay small |
| AnalysisCompute.cs LOC | ~366 | ~500 est. | 300/method | ✓ Methods stay small |

**Note**: The 10-type limit applies to top-level types per folder. Nested types within `Analysis.cs` (request/result records) don't count against this limit as they're part of the `Analysis` class.

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else)
- [x] All examples use explicit types (no var)
- [x] All examples use named parameters
- [x] All examples use trailing commas
- [x] All examples use K&R brace style
- [x] All examples use target-typed new()
- [x] All examples use collection expressions []
- [x] Nested types within Analysis.cs (existing pattern)
- [x] All member estimates under 300 LOC
- [x] All patterns match existing libs/ exemplars

## Implementation Sequence

1. Read this blueprint thoroughly
2. Verify SDK method availability in RhinoCommon 8.24+
3. Add new request/result types to `Analysis.cs` (nested records)
4. Add dispatch entries to `AnalysisConfig.cs` FrozenDictionary
5. Add orchestration methods to `AnalysisCore.cs`
6. Implement computation algorithms in `AnalysisCompute.cs`
7. Add public API methods to `Analysis.cs`
8. Verify integration with existing UnifiedOperation pattern
9. Test with representative geometry samples
10. Verify code style compliance
11. Verify LOC limits (≤300 per method)

## Error Code Allocations

Use existing error codes from `E.Geometry.*`:

| Error | Code | Usage |
|-------|------|-------|
| `E.Geometry.CurveAnalysisFailed` | 2301 | Curvature profile computation failure |
| `E.Geometry.SurfaceAnalysisFailed` | 2302 | Surface curvature profile or conformance failure |
| `E.Geometry.UnsupportedAnalysis` | 2300 | Unsupported target shape or geometry type |

No new error codes required—existing analysis error codes cover these cases.

## References

### SDK Documentation
- [Curve.CurvatureAt](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Curve_CurvatureAt.htm)
- [Curve.TorsionAt](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Curve_TorsionAt.htm)
- [Surface.CurvatureAt](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Surface_CurvatureAt.htm)
- [Surface.TryGetPlane](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Surface_TryGetPlane.htm)
- [Surface.TryGetCylinder](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Surface_TryGetCylinder.htm)
- [Surface.TryGetSphere](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Surface_TryGetSphere.htm)
- [Mesh.ComputeCurvatureApproximation](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Mesh_ComputeCurvatureApproximation.htm)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/Result.cs` - Result monad patterns
- `libs/core/results/ResultFactory.cs` - Creation patterns
- `libs/core/operations/UnifiedOperation.cs` - Dispatch engine
- `libs/rhino/analysis/Analysis.cs` - Existing type patterns
- `libs/rhino/analysis/AnalysisCompute.cs` - Algorithm density examples
- `libs/rhino/extraction/Extraction.cs` - Primitive decomposition (don't duplicate)

### Forum Discussions
- [Curvature Graph RhinoCommon](https://discourse.mcneel.com/t/curvature-graph-rhinocommon/90591)
- [Surface Types in RhinoCommon](https://discourse.mcneel.com/t/surface-types-in-rhinocommon/61688)
