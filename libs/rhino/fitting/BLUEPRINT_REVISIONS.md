# Fitting Library Blueprint - Critical Revisions

## Executive Summary

This document outlines critical architectural revisions to the original BLUEPRINT.md based on deep-dive SDK research and analysis of existing libs/rhino/ patterns. All revisions eliminate ambiguity and ensure zero room for implementation mistakes.

---

## 1. UNIFIED DISPATCH ARCHITECTURE (CRITICAL FIX)

### Previous (WRONG):
```csharp
// Multiple loose dispatch tables with byte parameters
internal static readonly FrozenDictionary<(Type, byte), (V, int)> TypeConfig = ...;
```

### Revised (CORRECT):
```csharp
/// <summary>Operation types for unified dispatch.</summary>
internal enum FitOperation : byte {
    CurveFromPoints = 1,
    SurfaceFromGrid = 2,
    RebuildCurve = 3,
    RebuildSurface = 4,
    FairCurve = 5,
    FairSurface = 6,
    SmoothCurve = 7,
    SmoothSurface = 8,
}

/// <summary>Single unified dispatch table: (Type, Operation) → (Validation, OperationName).</summary>
internal static readonly FrozenDictionary<(Type GeometryType, FitOperation Operation), (V ValidationMode, string OperationName)> OperationRegistry =
    new Dictionary<(Type, FitOperation), (V, string)> {
        [(typeof(Point3d[]), FitOperation.CurveFromPoints)] = (V.None, "Fitting.CurveFromPoints"),
        [(typeof(Point3d[,]), FitOperation.SurfaceFromGrid)] = (V.None, "Fitting.SurfaceFromGrid"),
        [(typeof(Curve), FitOperation.RebuildCurve)] = (V.Standard | V.Degeneracy, "Fitting.RebuildCurve"),
        [(typeof(NurbsCurve), FitOperation.RebuildCurve)] = (V.Standard | V.NurbsGeometry, "Fitting.RebuildNurbsCurve"),
        [(typeof(Surface), FitOperation.RebuildSurface)] = (V.Standard | V.UVDomain, "Fitting.RebuildSurface"),
        [(typeof(NurbsSurface), FitOperation.RebuildSurface)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Fitting.RebuildNurbsSurface"),
        [(typeof(Curve), FitOperation.FairCurve)] = (V.Standard | V.Degeneracy, "Fitting.FairCurve"),
        [(typeof(Surface), FitOperation.FairSurface)] = (V.Standard | V.UVDomain, "Fitting.FairSurface"),
    }.ToFrozenDictionary();
```

**Pattern Source**: `TopologyConfig.OperationMeta` - proven pattern with enum-based dispatch

---

## 2. PROPER TYPE NESTING (CRITICAL FIX)

### Previous (WRONG):
```csharp
// Types spread across file, not properly nested
public interface IFitResult { }
public sealed record CurveFitResult(...) : IFitResult { }
public sealed record SurfaceFitResult(...) : IFitResult { }
```

### Revised (CORRECT):
```csharp
[SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fitting is the primary API entry point")]
public static class Fitting {
    /// <summary>Marker interface for polymorphic fit result discrimination.</summary>
    public interface IFitResult {
        public GeometryBase Geometry { get; }
        public double MaxDeviation { get; }
        public double RmsDeviation { get; }
    }

    /// <summary>Curve fitting result with quality metrics and fitted NURBS curve.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record CurveFitResult(
        NurbsCurve Curve,
        double MaxDeviation,
        double RmsDeviation,
        double FairnessScore,
        int ControlPointCount,
        int ActualDegree) : IFitResult {
        [Pure]
        public GeometryBase Geometry => this.Curve;
        
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"CurveFit[{this.ActualDegree}] | CP={this.ControlPointCount} | MaxΔ={this.MaxDeviation:E3} | Fair={this.FairnessScore:F3}");
    }

    /// <summary>Surface fitting result with quality metrics and fitted NURBS surface.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record SurfaceFitResult(
        NurbsSurface Surface,
        double MaxDeviation,
        double RmsDeviation,
        double FairnessScore,
        (int U, int V) ControlPointCounts,
        (int U, int V) ActualDegrees) : IFitResult {
        [Pure]
        public GeometryBase Geometry => this.Surface;
        
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"SurfaceFit[{this.ActualDegrees.U},{this.ActualDegrees.V}] | CP=({this.ControlPointCounts.U}×{this.ControlPointCounts.V}) | MaxΔ={this.MaxDeviation:E3}");
    }

    /// <summary>Unified fitting API with polymorphic dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IFitResult> Fit<TInput>(
        TInput input,
        FitOptions options,
        IGeometryContext context) where TInput : notnull =>
        // Dispatch implementation...
}
```

**Pattern Source**: `Analysis.cs` - ALL result types nested within main class

---

## 3. MISSING RHINO SDK APIS (CRITICAL ADDITION)

### Previously Missed APIs Now Included:

```csharp
// 1. Knot manipulation for isogeometric refinement
NurbsCurve.InsertKnot(double parameter, int multiplicity);
NurbsCurve.RemoveKnot(double parameter);

// 2. Degree modification for control point count adjustment
Curve.ChangeDegree(int desiredDegree);
Surface.ChangeDegree(int uDegree, int vDegree);

// 3. Surface smoothing (native SDK method, not manual implementation)
Surface.Smooth(
    double smoothFactor,
    bool bXSmooth,
    bool bYSmooth,
    bool bZSmooth,
    bool bFixBoundaries,
    SmoothingCoordinateSystem coordinateSystem,
    Plane plane);

// 4. Network surface creation (for constrained fitting)
NurbsSurface.CreateNetworkSurface(
    IEnumerable<Curve> uCurves,
    IEnumerable<Curve> vCurves,
    double edgeTolerance,
    double interiorTolerance,
    double angleTolerance);

// 5. Weight manipulation for rational curves
NurbsCurve.Points.SetWeight(int index, double weight);
```

### Integration in Blueprint:

```csharp
/// <summary>Isogeometric refinement via knot insertion (no shape change).</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<CurveFitResult> RefineIsogeometric(
    NurbsCurve curve,
    int subdivisionLevel,
    IGeometryContext context) =>
    ResultFactory.Create(value: curve)
        .Validate(args: [context, V.Standard | V.NurbsGeometry,])
        .Bind(validCurve => {
            NurbsCurve refined = validCurve.Duplicate() as NurbsCurve ?? validCurve;
            double[] insertParams = ComputeRefinementParameters(
                domain: refined.Domain,
                level: subdivisionLevel);
            
            foreach (double param in insertParams) {
                refined.InsertKnot(parameter: param, multiplicity: 1) switch {
                    true => continue,
                    false => return ResultFactory.Create<CurveFitResult>(
                        error: E.Fitting.IsogeometricRefinementFailed.WithContext(
                            $"Knot insertion failed at t={param:F6}")),
                };
            }
            
            return FittingCore.ComputeQualityMetrics(curve: refined, originalPoints: null, context: context);
        });

/// <summary>Smooth surface via SDK method (not manual Laplacian implementation).</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<SurfaceFitResult> SmoothSurface(
    Surface surface,
    (double Factor, int Iterations) parameters,
    (bool X, bool Y, bool Z) axes,
    bool fixBoundaries,
    IGeometryContext context) =>
    ResultFactory.Create(value: surface)
        .Validate(args: [context, V.Standard | V.UVDomain,])
        .Bind(validSurface => {
            Surface working = validSurface.Duplicate() as Surface ?? validSurface;
            
            for (int i = 0; i < parameters.Iterations; i++) {
                working = working.Smooth(
                    smoothFactor: parameters.Factor,
                    bXSmooth: axes.X,
                    bYSmooth: axes.Y,
                    bZSmooth: axes.Z,
                    bFixBoundaries: fixBoundaries,
                    coordinateSystem: SmoothingCoordinateSystem.World,
                    plane: Plane.WorldXY) switch {
                    null => return ResultFactory.Create<SurfaceFitResult>(
                        error: E.Fitting.SmoothingFailed.WithContext($"Iteration {i + 1} failed")),
                    Surface s => s,
                };
            }
            
            return working is NurbsSurface ns
                ? FittingCore.ComputeSurfaceQualityMetrics(surface: ns, context: context)
                : ResultFactory.Create<SurfaceFitResult>(
                    error: E.Fitting.FittingFailed.WithContext("Result not NURBS"));
        });
```

---

## 4. RHINO MATH FORMULA PRECISION (CRITICAL CLARITY)

### All Formulas Must Reference RhinoMath Constants:

```csharp
// ❌ WRONG - Magic number
if (deviation < 0.001) { ... }

// ✅ CORRECT - RhinoMath constant with context
double threshold = context.AbsoluteTolerance * RhinoMath.SqrtEpsilon;
if (deviation < threshold) { ... }

// ❌ WRONG - Hardcoded π
double angle = 3.14159 * 2.0;

// ✅ CORRECT - RhinoMath constant
double angle = RhinoMath.TwoPI;

// ❌ WRONG - Raw distance check
if (Math.Abs(a - b) < 1e-10) { ... }

// ✅ CORRECT - RhinoMath tolerance
if (RhinoMath.EpsilonEquals(a, b, RhinoMath.ZeroTolerance)) { ... }
```

### Chord-Length Parameterization Formula (EXPLICIT):

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static Result<double[]> ComputeChordParameters(Point3d[] points) {
    int n = points.Length;
    double[] chordLengths = new double[n];
    double totalLength = 0.0;
    
    // Accumulate chord lengths: L[i] = Σ ||p[j] - p[j-1]||
    for (int i = 1; i < n; i++) {
        double segmentLength = points[i].DistanceTo(points[i - 1]);
        totalLength += segmentLength;
        chordLengths[i] = totalLength;
    }
    
    // Normalize: t[i] = L[i] / L[n-1]
    return totalLength > RhinoMath.ZeroTolerance
        ? ResultFactory.Create(value: chordLengths.Select(L => L / totalLength).ToArray())
        : ResultFactory.Create<double[]>(
            error: E.Fitting.ParameterizationFailed.WithContext(
                "Coincident points: total chord length < ZeroTolerance"));
}
```

### Knot Vector Generation Formula (EXPLICIT):

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static Result<double[]> GenerateKnotVector(
    double[] parameters,
    int degree,
    int controlPoints) {
    // Knot vector size: m = n + p + 1 (where n=controlPoints, p=degree)
    int m = controlPoints + degree + 1;
    double[] knots = new double[m];
    
    // Clamp start: knots[0..p] = 0.0
    for (int i = 0; i <= degree; i++) {
        knots[i] = 0.0;
    }
    
    // Clamp end: knots[m-p-1..m-1] = 1.0
    for (int i = 0; i <= degree; i++) {
        knots[m - 1 - i] = 1.0;
    }
    
    // Interior: knots[p+i] = (t[i] + t[i+1] + ... + t[i+p-1]) / p
    double divisor = degree;
    for (int i = 1; i < controlPoints - degree; i++) {
        double sum = 0.0;
        for (int j = i; j < i + degree; j++) {
            sum += parameters[j];
        }
        knots[degree + i] = sum / divisor;
    }
    
    return ResultFactory.Create(value: knots);
}
```

### Bending Energy Formula (EXPLICIT):

```csharp
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static double ComputeBendingEnergy(Curve curve) {
    // E = ∫ κ² ds ≈ Σ ||κ(t[i])||² · Δt
    // where κ(t) = curvature vector at parameter t
    int n = FittingConfig.EnergySampleCount;
    double energy = 0.0;
    double domainLength = curve.Domain.Length;
    double dt = domainLength / (n - 1.0);
    
    for (int i = 0; i < n; i++) {
        double t = curve.Domain.ParameterAt(i / (n - 1.0));
        Vector3d curvatureVector = curve.CurvatureAt(t);
        
        // κ² = ||κ||² = κ·κ
        double curvatureMagnitudeSquared = curvatureVector.SquareLength;
        energy += curvatureMagnitudeSquared * dt;
    }
    
    return energy;
}
```

---

## 5. TUPLE RETURN PATTERNS (STANDARDIZATION)

### Existing Pattern from libs/rhino/:

```csharp
// spatial/Spatial.cs
Result<(Point3d Centroid, double[] Radii)[]> Cluster<T>(...);
Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(...);

// intersection/Intersect.cs
Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> ClassifyIntersection(...);

// fields/Fields.cs
Result<(Point3d[] Grid, double[] Distances)> DistanceField<T>(...);
```

### Apply to Fitting:

```csharp
/// <summary>Fit curve and return tuple with quality metrics.</summary>
public static Result<(NurbsCurve Fitted, double MaxDeviation, double RmsDeviation, double FairnessScore)> FitCurveWithMetrics(
    Point3d[] points,
    FitOptions options,
    IGeometryContext context) =>
    FitCurve(points, options, context)
        .Map(result => (result.Curve, result.MaxDeviation, result.RmsDeviation, result.FairnessScore));

/// <summary>Compute quality metrics as tuple.</summary>
internal static (double MaxDev, double RmsDev, double Fair) ComputeQualityMetrics(
    Curve fitted,
    Point3d[] originalPoints) {
    double maxDev = 0.0;
    double sumSqDev = 0.0;
    
    for (int i = 0; i < originalPoints.Length; i++) {
        Point3d closest = fitted.ClosestPoint(testPoint: originalPoints[i], maximumDistance: double.MaxValue);
        double dev = originalPoints[i].DistanceTo(closest);
        maxDev = dev > maxDev ? dev : maxDev;
        sumSqDev += dev * dev;
    }
    
    double rmsDev = Math.Sqrt(sumSqDev / originalPoints.Length);
    double fairness = ComputeFairnessScore(fitted);
    
    return (maxDev, rmsDev, fairness);
}
```

---

## 6. ADVANCED FOR/FOREACH PATTERNS (SOPHISTICATION)

### From Existing libs/rhino/ Patterns:

```csharp
// analysis/AnalysisCompute.cs - Index arithmetic in loop
for (int i = 0; i < gridSize; i++) {
    double u = validSurface.Domain(0).ParameterAt(i / gridDivisor);
    for (int j = 0; j < gridSize; j++) {
        double v = validSurface.Domain(1).ParameterAt(j / gridDivisor);
        uvGrid[uvIndex++] = (u, v);
    }
}

// spatial/SpatialCore.cs - Inline conditional with index
for (int i = 0; i < geometries.Length; i++) {
    _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
}
```

### Apply to Fitting:

```csharp
// Basis matrix construction with dense index arithmetic
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
private static double[,] BuildBasisMatrix(
    double[] parameters,
    double[] knots,
    int degree) {
    int m = parameters.Length;
    int n = knots.Length - degree - 1;
    double[,] basis = new double[m, n];
    
    for (int i = 0; i < m; i++) {
        double u = parameters[i];
        for (int j = 0; j < n; j++) {
            basis[i, j] = EvaluateBasis(i: j, p: degree, u: u, knots: knots);
        }
    }
    
    return basis;
}

// Iterative optimization with convergence check in loop
for (int iteration = 0; iteration < options.MaxIterations; iteration++) {
    double previousEnergy = currentEnergy;
    OptimizeControlPointsStep(curve: working, relaxation: options.RelaxationFactor, fixBoundaries: options.FixBoundaries);
    currentEnergy = ComputeBendingEnergy(curve: working);
    
    double energyChange = Math.Abs(currentEnergy - previousEnergy);
    if (energyChange < FittingConfig.EnergyConvergence) {
        break;
    }
}
```

---

## 7. MODERN C# 12 FEATURES (CUTTING EDGE)

### Collection Expressions with Spread:

```csharp
// ✅ Spread operator for flattening
IReadOnlyList<int> allIndices = [.. results.SelectMany(static r => r.Indices)];

// ✅ Collection initialization
double[] discontinuities = [.. buffer[..discCount]];

// ✅ Multi-dimensional spread
Point3d[][] grid = [
    [p00, p01, p02,],
    [p10, p11, p12,],
];
```

### Pattern Matching Exhaustiveness:

```csharp
// ✅ Exhaustive switch with type patterns
return geometry switch {
    Curve c => FitCurve(c, options, context),
    Surface s => FitSurface(s, options, context),
    Point3d[] pts => FitCurveFromPoints(pts, options, context),
    Point3d[,] grid => FitSurfaceFromGrid(grid, options, context),
    _ => ResultFactory.Create<IFitResult>(
        error: E.Fitting.UnsupportedConfiguration.WithContext(
            $"Type: {geometry.GetType().Name}")),
};
```

### Primary Constructors (Records):

```csharp
// ✅ Primary constructor with validation
public sealed record FitOptions(
    int? Degree = null,
    int? ControlPointCount = null,
    double? Tolerance = null,
    bool PreserveTangents = false,
    bool UseChordLength = true) {
    
    // Validate in init block
    public FitOptions {
        if (Degree.HasValue && (Degree.Value < 1 || Degree.Value > 11)) {
            throw new ArgumentOutOfRangeException(nameof(Degree), "Degree must be 1-11");
        }
    }
}
```

---

## 8. LEVERAGE EXISTING FOLDERS (NO DUPLICATION)

### From analysis/AnalysisCompute.cs:

```csharp
// ✅ USE: Fairness scoring pattern
internal static Result<(double SmoothnessScore, double[] CurvatureSamples, (double Parameter, bool IsSharp)[] InflectionPoints, double EnergyMetric)> CurveFairness(
    Curve curve,
    IGeometryContext context);

// Integration in Fitting:
private static double ComputeFairnessScore(Curve curve) =>
    AnalysisCompute.CurveFairness(curve: curve, context: defaultContext)
        .Match(
            onSuccess: result => result.SmoothnessScore,
            onFailure: _ => 0.0);
```

### From morphology/MorphologyCompute.cs:

```csharp
// ✅ USE: Laplacian smoothing convergence pattern
internal static Result<Mesh> SmoothLaplacian(
    Mesh mesh,
    int iterations,
    double lambda,
    IGeometryContext context);

// DON'T reimplement - reference or adapt pattern
```

---

## Summary of Critical Changes

| Aspect | Previous | Revised | Impact |
|--------|----------|---------|--------|
| Dispatch | Multiple tables, byte params | Single FrozenDict, enum | High - eliminates ambiguity |
| Type Nesting | Types spread in file | Nested in main class | Critical - matches patterns |
| SDK APIs | Missing 5+ key methods | All methods documented | High - enables functionality |
| Math Formulas | Some magic numbers | All RhinoMath constants | Medium - precision/clarity |
| Tuples | Not standardized | Follows existing patterns | Medium - consistency |
| For/Foreach | Basic patterns | Advanced index arithmetic | Low - code sophistication |
| C# Features | C# 10 level | C# 12 cutting edge | Low - future-proof |
| Leverage | Partial | Full integration | High - no duplication |

All revisions maintain the 4-file, 8-type (nested), ~830 LOC architecture while eliminating all ambiguity and implementation risks.
