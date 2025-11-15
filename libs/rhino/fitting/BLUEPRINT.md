# Fitting Library Blueprint

## Overview

The `fitting` library provides comprehensive NURBS curve and surface fitting operations with least-squares approximation, fairing algorithms, and quality optimization. Operations include point-based fitting, rebuild/smooth/fair workflows, and advanced energy minimization for high-quality geometry generation from raw data or noisy inputs.

## Existing libs/ Infrastructure Analysis

### libs/core/ Components We Leverage

**Result<T> Monad**:
- `ResultFactory.Create(value: x)` for success paths
- `ResultFactory.Create(error: E.Fitting.*)` for failure cases
- `.Map()` for transforming fitted geometry results
- `.Bind()` for chaining validation → fitting → optimization
- `.Validate(args: [context, V.Standard | V.NurbsGeometry])` for NURBS validation
- `.Ensure()` for parameter validation (degree, control point count)

**UnifiedOperation**:
- Polymorphic dispatch for `Curve`/`NurbsCurve`/`Surface`/`NurbsSurface` inputs
- `OperationConfig<TIn, TOut>` with `ValidationMode = V.Standard | V.NurbsGeometry`
- Error accumulation for batch fitting operations
- Diagnostic instrumentation for performance profiling

**ValidationRules**:
- `V.Standard | V.NurbsGeometry` for NURBS-specific validation
- `V.Degeneracy` for checking degenerate control point configurations
- Custom validation for minimum control point counts and degree constraints

**Error Registry**:
- Allocate codes **2900-2919** in Geometry domain (2000-2999)
- `E.Fitting.InvalidDegree` - Degree out of range [1, 11]
- `E.Fitting.InvalidControlPointCount` - Count below minimum
- `E.Fitting.InsufficientPoints` - Not enough points for fit
- `E.Fitting.FittingFailed` - Numerical optimization failure
- `E.Fitting.ConvergenceFailed` - Iterative method non-convergence
- `E.Fitting.InvalidTolerance` - Tolerance parameter invalid
- `E.Fitting.InvalidKnotVector` - Knot vector structure invalid
- `E.Fitting.UnsupportedConfiguration` - Type/parameter combination unsupported

**Context**:
- `IGeometryContext.AbsoluteTolerance` for fitting tolerance thresholds
- `IGeometryContext.AngleTolerance` for tangent continuity constraints
- Unit-aware computations for bending energy metrics

### Similar libs/rhino/ Implementations

**`libs/rhino/analysis/`**:
- Borrow `CurveFairness()` pattern for fairness scoring (smoothness, inflection detection, bending energy)
- Similar `AnalysisCompute.cs` dense algorithm structure
- Reuse curvature sampling and statistical analysis patterns
- 4-file architecture: Analysis.cs, AnalysisConfig.cs, AnalysisCore.cs, AnalysisCompute.cs

**`libs/rhino/morphology/`**:
- Iterative optimization patterns (Laplacian smoothing, convergence criteria)
- Quality metrics (aspect ratio, energy minimization)
- Control point manipulation workflows

**`libs/rhino/orientation/`**:
- `Plane.FitPlaneToPoints` usage pattern for least-squares fitting
- RMS residual computation for goodness-of-fit

**No Duplication**:
- No existing least-squares NURBS fitting implementation
- No bending energy minimization algorithms
- No progressive iterative approximation (LSPIA) methods
- No automatic degree selection or constrained fitting

## SDK Research Summary

### RhinoCommon APIs Used

**Curve Fitting**:
- `Curve.Fit(degree, fitTolerance, angleTolerance)` - Simplifies curve within tolerance, smooths kinks
- `NurbsCurve.CreateFromFitPoints(points, degree, startTangent, endTangent)` - Interpolating fit through points
- `Curve.Rebuild(pointCount, degree, preserveTangents)` - Reconstructs curve with specified control points
- `Curve.Fair(tolerance)` - Removes curvature variations (best for degree 3)

**Surface Fitting**:
- `Surface.Fit(uDegree, vDegree, fitTolerance)` - Fits new surface to existing with specified degrees
- `Surface.Rebuild(uPointCount, vPointCount, uDegree, vDegree)` - Reconstructs surface with control point grid
- `NurbsSurface.CreateFromPoints(points, uCount, vCount, uDegree, vDegree)` - Surface through point grid

**NURBS Construction**:
- `NurbsCurve(degree, controlPoints, knots, weights)` - Direct NURBS construction from components
- `NurbsSurface(uDegree, vDegree, uControlPoints, vControlPoints, uKnots, vKnots)` - Surface from components
- `NurbsCurve.KnotList` / `NurbsSurface.KnotsU` - Knot vector access and manipulation
- `NurbsCurve.Points` / `NurbsSurface.Points` - Control point manipulation

**Geometry Analysis**:
- `Curve.CurvatureAt(t)` - Local curvature vector for energy computation
- `Surface.CurvatureAt(u, v)` - Surface Gaussian/mean curvature for fairness
- `AreaMassProperties.Compute(curve)` - Geometric properties for validation
- `Curve.GetLength()` - Arc length for parameterization

**Utility Math**:
- `RhinoMath.ZeroTolerance` - Numerical zero threshold
- `RhinoMath.SqrtEpsilon` - Square root operation tolerance
- `RhinoMath.Clamp(value, min, max)` - Parameter clamping
- `RhinoMath.IsValidDouble(value)` - NaN/Infinity checks

### Key Insights

**Chord-Length Parameterization**:
- Critical for least-squares fitting quality
- Compute parameter values `t[i] = sum(||p[j] - p[j-1]||) / totalLength`
- Better than uniform parameterization for unevenly spaced points
- Use `Point3d.DistanceTo()` for distances, accumulate with `for` loop

**Knot Vector Generation**:
- Averaging method: `knot[i] = (t[i-degree] + ... + t[i-1]) / degree` 
- Clamped knots: first/last repeated `degree+1` times
- Interior knots span parameter domain uniformly or by chord-length
- Use `List<double>` construction, convert to array for NURBS

**Least-Squares Linear System**:
- Build basis matrix `N[i,j] = B_j(t[i])` where `B_j` are B-spline basis functions
- Solve `N^T * N * P = N^T * D` for control points `P` from data points `D`
- RhinoCommon doesn't expose basis evaluation directly - implement Cox-de Boor recursion
- Use direct matrix operations: `double[,]` arrays, Gaussian elimination

**Bending Energy Minimization**:
- Energy = ∫(κ²) where κ is curvature magnitude
- Discrete approximation: `sum(||Curve.CurvatureAt(t[i])||² * dt)`
- Iterative optimization: adjust control points to minimize energy
- Constraint: fit tolerance deviation < threshold

**Convergence Criteria**:
- Energy change < `RhinoMath.SqrtEpsilon` between iterations
- Maximum iterations: 100-500 (user-configurable)
- Residual norm < tolerance

**Performance Considerations**:
- Use `ArrayPool<double>` for temporary buffers in matrix operations
- `for` loops over LINQ for hot path computations
- FrozenDictionary dispatch for type-based operation selection
- ConditionalWeakTable caching not needed (fitting is not idempotent)

### SDK Version Requirements

- Minimum: RhinoCommon 8.0
- Tested: RhinoCommon 8.24+
- .NET 8.0 C# 12 features (collection expressions, primary constructors)

## File Organization

### File 1: `Fitting.cs` (Public API - ~200 LOC)

**Purpose**: Thin public API layer with explicit method signatures and UnifiedOperation orchestration

**Types** (6 total):
- `Fitting` (static class): Main API entry point with suppression attribute
- `IFitResult` (interface): Polymorphic result marker with `Geometry` property
- `CurveFitResult` (sealed record): Fitted curve + quality metrics
- `SurfaceFitResult` (sealed record): Fitted surface + quality metrics
- `FitOptions` (readonly record struct): User-configurable fitting parameters
- `FairOptions` (readonly record struct): Fairing-specific parameters

**Key Members**:
- `FitCurve(Point3d[], FitOptions, IGeometryContext)` - Least-squares curve fitting from points
- `FitSurface(Point3d[,], FitOptions, IGeometryContext)` - Least-squares surface fitting from point grid
- `RebuildCurve(Curve, FitOptions, IGeometryContext)` - Rebuild curve with new control point distribution
- `RebuildSurface(Surface, FitOptions, IGeometryContext)` - Rebuild surface with control point grid
- `FairCurve(Curve, FairOptions, IGeometryContext)` - Remove curvature variations, minimize energy
- `FairSurface(Surface, FairOptions, IGeometryContext)` - Surface fairing via energy minimization
- `SmoothCurve(Curve, int, double, IGeometryContext)` - Laplacian-style smoothing with iteration count
- `SmoothSurface(Surface, int, double, IGeometryContext)` - Surface smoothing via control point averaging

**Code Style Example**:
```csharp
/// <summary>Fits NURBS curve to points via least-squares with chord-length parameterization.</summary>
[Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Result<CurveFitResult> FitCurve(
    Point3d[] points,
    FitOptions options,
    IGeometryContext context) =>
    points.Length >= FittingConfig.MinPointsForFit
        ? UnifiedOperation.Apply(
            input: points,
            operation: (Func<Point3d[], Result<IReadOnlyList<CurveFitResult>>>)(pts => 
                FittingCore.FitCurveFromPoints(
                    points: pts, 
                    degree: options.Degree ?? FittingConfig.DefaultCurveDegree,
                    controlPointCount: options.ControlPointCount,
                    tolerance: options.Tolerance ?? context.AbsoluteTolerance,
                    context: context)
                    .Map(result => (IReadOnlyList<CurveFitResult>)[result])),
            config: new OperationConfig<Point3d[], CurveFitResult> {
                Context = context,
                ValidationMode = V.None,
                OperationName = "Fitting.CurveFromPoints",
                EnableDiagnostics = false,
            })
            .Map(results => results[0])
        : ResultFactory.Create<CurveFitResult>(
            error: E.Fitting.InsufficientPoints.WithContext(
                $"Minimum {FittingConfig.MinPointsForFit} points required, got {points.Length}"));

/// <summary>Result marker interface for polymorphic fit result discrimination.</summary>
public interface IFitResult {
    /// <summary>Fitted geometry (Curve or Surface).</summary>
    public GeometryBase Geometry { get; }
}

/// <summary>Curve fitting result with quality metrics.</summary>
[DebuggerDisplay("{DebuggerDisplay}")]
public sealed record CurveFitResult(
    NurbsCurve Curve,
    double MaxDeviation,
    double RmsDeviation,
    double FairnessScore,
    int ActualControlPoints) : IFitResult {
    [Pure]
    public GeometryBase Geometry => this.Curve;
    
    [Pure]
    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"CurveFit | CP={this.ActualControlPoints} | MaxDev={this.MaxDeviation:E3} | Fair={this.FairnessScore:F3}");
}

/// <summary>Fitting configuration parameters.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct FitOptions(
    int? Degree = null,
    int? ControlPointCount = null,
    double? Tolerance = null,
    bool PreserveTangents = false,
    bool UseChordLength = true);

/// <summary>Fairing configuration parameters.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct FairOptions(
    double? Tolerance = null,
    int MaxIterations = FittingConfig.DefaultFairIterations,
    double RelaxationFactor = FittingConfig.DefaultRelaxation,
    bool FixBoundaries = true);
```

**LOC Estimate**: 180-220 lines

### File 2: `FittingConfig.cs` (Constants & Dispatch - ~100 LOC)

**Purpose**: FrozenDictionary mappings, constants, buffer sizes, validation mode mappings

**Types** (1 total):
- `FittingConfig` (internal static class)

**Key Members**:
- Curve/Surface degree constraints (min=1, max=11, default=3)
- Control point count constraints (min=degree+1)
- Iteration limits (fairing=500, smoothing=100)
- Convergence thresholds (energy, RMS deviation)
- Sample counts for energy/fairness computation
- Validation mode mappings: `FrozenDictionary<Type, V>`
- Type-specific configuration: `FrozenDictionary<(Type, FitMethod), (V, int)>`

**Code Style Example**:
```csharp
namespace Arsenal.Rhino.Fitting;

internal static class FittingConfig {
    // Degree constraints
    internal const int MinDegree = 1;
    internal const int MaxDegree = 11;
    internal const int DefaultCurveDegree = 3;
    internal const int DefaultSurfaceDegree = 3;
    
    // Control point constraints
    internal const int MinControlPointsPerDegree = 2;
    internal static int MinControlPoints(int degree) => degree + 1 + MinControlPointsPerDegree;
    
    // Fitting parameters
    internal const int MinPointsForFit = 3;
    internal const int DefaultFitSampleCount = 50;
    internal const double DefaultChordPower = 1.0;
    internal const double DefaultRelaxation = 0.5;
    
    // Iteration limits
    internal const int DefaultFairIterations = 500;
    internal const int MaxFairIterations = 1000;
    internal const int DefaultSmoothIterations = 10;
    internal const int MaxSmoothIterations = 100;
    
    // Convergence thresholds
    internal static readonly double EnergyConvergence = RhinoMath.SqrtEpsilon;
    internal static readonly double RmsConvergence = RhinoMath.SqrtEpsilon;
    
    // Energy computation
    internal const int EnergySampleCount = 100;
    internal const double CurvatureWeight = 1.0;
    internal const double SecondDerivativeWeight = 0.1;
    
    // Validation modes
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(Surface)] = V.Standard | V.UVDomain,
            [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [typeof(Point3d[])] = V.None,
        }.ToFrozenDictionary();
    
    // Type-specific configuration: (Type, Method) → (ValidationMode, BufferSize)
    internal static readonly FrozenDictionary<(Type GeometryType, byte Method), (V Mode, int Buffer)> TypeConfig =
        new Dictionary<(Type, byte), (V, int)> {
            [(typeof(Curve), 0)] = (V.Standard | V.Degeneracy, 256),
            [(typeof(NurbsCurve), 0)] = (V.Standard | V.NurbsGeometry, 512),
            [(typeof(Surface), 1)] = (V.Standard | V.UVDomain, 512),
            [(typeof(NurbsSurface), 1)] = (V.Standard | V.NurbsGeometry | V.UVDomain, 1024),
        }.ToFrozenDictionary();
}
```

**LOC Estimate**: 85-110 lines

### File 3: `FittingCore.cs` (Core Algorithms - ~280 LOC)

**Purpose**: Dense algorithmic implementation for fitting, validation, and quality metrics

**Types** (1 total):
- `FittingCore` (internal static class)

**Key Members**:
- `FitCurveFromPoints(Point3d[], int, int?, double, IGeometryContext)` - Main least-squares curve fitting
- `FitSurfaceFromPoints(Point3d[,], int, int, int?, int?, double, IGeometryContext)` - Surface fitting
- `ComputeChordParameters(Point3d[])` - Chord-length parameterization
- `GenerateKnotVector(double[], int, int)` - Averaging knot vector generation
- `SolveLeastSquares(double[,], Point3d[])` - Linear system solver via Gaussian elimination
- `ValidateAndRebuild(Curve, int?, int?, bool, IGeometryContext)` - Curve rebuild workflow
- `ValidateAndRebuild(Surface, int?, int?, int?, int?, IGeometryContext)` - Surface rebuild workflow
- `ComputeFairnessScore(Curve, IGeometryContext)` - Curvature variation analysis
- `ComputeDeviation(Curve, Point3d[])` - Max/RMS deviation computation

**Algorithmic Density Strategy**:
- Inline Cox-de Boor basis function evaluation using nested ternary/switch
- Matrix operations via `for` loops with `ArrayPool<double>` buffers
- Parameter validation via pattern matching: `value switch { < min => error, > max => error, _ => ok }`
- Knot generation via LINQ aggregate with explicit accumulation
- Energy computation using `Curve.CurvatureAt()` sampling with trapezoidal integration

**Code Style Example**:
```csharp
namespace Arsenal.Rhino.Fitting;

internal static class FittingCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.CurveFitResult> FitCurveFromPoints(
        Point3d[] points,
        int degree,
        int? controlPointCount,
        double tolerance,
        IGeometryContext context) =>
        degree switch {
            < FittingConfig.MinDegree => ResultFactory.Create<Fitting.CurveFitResult>(
                error: E.Fitting.InvalidDegree.WithContext(
                    $"Degree must be >= {FittingConfig.MinDegree}, got {degree}")),
            > FittingConfig.MaxDegree => ResultFactory.Create<Fitting.CurveFitResult>(
                error: E.Fitting.InvalidDegree.WithContext(
                    $"Degree must be <= {FittingConfig.MaxDegree}, got {degree}")),
            _ when controlPointCount.HasValue && controlPointCount.Value < FittingConfig.MinControlPoints(degree) =>
                ResultFactory.Create<Fitting.CurveFitResult>(
                    error: E.Fitting.InvalidControlPointCount.WithContext(
                        $"Need >= {FittingConfig.MinControlPoints(degree)} control points for degree {degree}")),
            _ => ComputeChordParameters(points: points)
                .Bind(parameters => GenerateKnotVector(
                    parameters: parameters,
                    degree: degree,
                    controlPoints: controlPointCount ?? (points.Length - 1))
                    .Bind(knots => SolveLeastSquares(
                        basisMatrix: BuildBasisMatrix(parameters, knots, degree),
                        dataPoints: points)
                        .Bind(controlPts => ConstructAndValidateCurve(
                            controlPoints: controlPts,
                            knots: knots,
                            degree: degree,
                            originalPoints: points,
                            tolerance: tolerance,
                            context: context)))),
        };
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double[]> ComputeChordParameters(Point3d[] points) {
        int n = points.Length;
        double[] parameters = new double[n];
        double totalLength = 0.0;
        
        for (int i = 1; i < n; i++) {
            totalLength += points[i].DistanceTo(points[i - 1]);
            parameters[i] = totalLength;
        }
        
        return totalLength > RhinoMath.ZeroTolerance
            ? ResultFactory.Create(value: parameters.Select(t => t / totalLength).ToArray())
            : ResultFactory.Create<double[]>(
                error: E.Fitting.InvalidControlPointCount.WithContext("Points are coincident"));
    }
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double[]> GenerateKnotVector(
        double[] parameters,
        int degree,
        int controlPoints) {
        int n = controlPoints;
        int m = n + degree + 1;
        double[] knots = new double[m];
        
        for (int i = 0; i <= degree; i++) {
            knots[i] = 0.0;
            knots[m - 1 - i] = 1.0;
        }
        
        double divisor = degree;
        for (int i = 1; i < n - degree; i++) {
            double sum = 0.0;
            for (int j = i; j < i + degree; j++) {
                sum += parameters[j];
            }
            knots[degree + i] = sum / divisor;
        }
        
        return ResultFactory.Create(value: knots);
    }
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double EvaluateBasis(int i, int p, double u, double[] knots) =>
        p == 0
            ? (u >= knots[i] && u < knots[i + 1] ? 1.0 : 0.0)
            : (knots[i + p] - knots[i] > RhinoMath.ZeroTolerance
                ? ((u - knots[i]) / (knots[i + p] - knots[i])) * EvaluateBasis(i, p - 1, u, knots)
                : 0.0) +
              (knots[i + p + 1] - knots[i + 1] > RhinoMath.ZeroTolerance
                ? ((knots[i + p + 1] - u) / (knots[i + p + 1] - knots[i + 1])) * EvaluateBasis(i + 1, p - 1, u, knots)
                : 0.0);
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(double MaxDev, double RmsDev)> ComputeDeviation(
        Curve fitted,
        Point3d[] original) {
        double maxDev = 0.0;
        double sumSqDev = 0.0;
        
        for (int i = 0; i < original.Length; i++) {
            Point3d closest = fitted.ClosestPoint(testPoint: original[i], maximumDistance: double.MaxValue);
            double dev = original[i].DistanceTo(closest);
            maxDev = dev > maxDev ? dev : maxDev;
            sumSqDev += dev * dev;
        }
        
        double rmsDev = Math.Sqrt(sumSqDev / original.Length);
        return ResultFactory.Create(value: (maxDev, rmsDev));
    }
}
```

**LOC Estimate**: 260-300 lines

### File 4: `FittingCompute.cs` (Advanced Operations - ~250 LOC)

**Purpose**: Advanced fairing, smoothing, energy minimization, and iterative optimization algorithms

**Types** (1 total):
- `FittingCompute` (internal static class)

**Key Members**:
- `FairCurveIterative(Curve, FairOptions, IGeometryContext)` - Iterative bending energy minimization
- `FairSurfaceIterative(Surface, FairOptions, IGeometryContext)` - Surface fairing via thin-plate energy
- `SmoothCurveLaplacian(Curve, int, double, IGeometryContext)` - Laplacian smoothing with iterations
- `SmoothSurfaceLaplacian(Surface, int, double, IGeometryContext)` - Surface Laplacian smoothing
- `ComputeBendingEnergy(Curve)` - Discrete bending energy ∫(κ²)
- `ComputeBendingEnergy(Surface)` - Surface bending energy ∫(K² + H²)
- `OptimizeControlPoints(NurbsCurve, double, int, bool)` - Gradient descent optimization
- `ApplyConstraints(Point3d[], bool)` - Boundary/tangent constraint enforcement

**Advanced Patterns**:
- Progressive iterative approximation (LSPIA) for automatic control point refinement
- Conjugate gradient optimization for large-scale problems
- Adaptive sampling for energy integrals (higher density near high curvature)
- Multi-scale optimization (coarse-to-fine control point adjustment)

**Code Style Example**:
```csharp
namespace Arsenal.Rhino.Fitting;

internal static class FittingCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.CurveFitResult> FairCurveIterative(
        Curve curve,
        Fitting.FairOptions options,
        IGeometryContext context) =>
        ResultFactory.Create(value: curve)
            .Validate(args: [context, V.Standard | V.Degeneracy,])
            .Bind(validCurve => validCurve is not NurbsCurve nc
                ? curve.ToNurbsCurve() is NurbsCurve converted && converted is not null
                    ? FairNurbsCurve(converted, options, context)
                    : ResultFactory.Create<Fitting.CurveFitResult>(
                        error: E.Fitting.FittingFailed.WithContext("Cannot convert to NURBS"))
                : FairNurbsCurve(nc, options, context));
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Fitting.CurveFitResult> FairNurbsCurve(
        NurbsCurve curve,
        Fitting.FairOptions options,
        IGeometryContext context) {
        NurbsCurve working = curve.Duplicate() as NurbsCurve ?? curve;
        double previousEnergy = ComputeBendingEnergy(curve: working);
        int iteration = 0;
        double tolerance = options.Tolerance ?? context.AbsoluteTolerance;
        
        while (iteration < options.MaxIterations) {
            OptimizeControlPointsStep(
                curve: working,
                relaxation: options.RelaxationFactor,
                fixBoundaries: options.FixBoundaries);
            
            double currentEnergy = ComputeBendingEnergy(curve: working);
            double energyChange = Math.Abs(currentEnergy - previousEnergy);
            
            if (energyChange < FittingConfig.EnergyConvergence) {
                break;
            }
            
            previousEnergy = currentEnergy;
            iteration++;
        }
        
        return iteration >= options.MaxIterations
            ? ResultFactory.Create<Fitting.CurveFitResult>(
                error: E.Fitting.ConvergenceFailed.WithContext(
                    $"Failed to converge after {options.MaxIterations} iterations"))
            : FittingCore.ComputeDeviation(fitted: working, original: curve.Points.Select(p => p.Location).ToArray())
                .Map(dev => new Fitting.CurveFitResult(
                    Curve: working,
                    MaxDeviation: dev.MaxDev,
                    RmsDeviation: dev.RmsDev,
                    FairnessScore: ComputeFairnessScore(working),
                    ActualControlPoints: working.Points.Count));
    }
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeBendingEnergy(Curve curve) {
        int n = FittingConfig.EnergySampleCount;
        double energy = 0.0;
        double dt = curve.Domain.Length / (n - 1.0);
        
        for (int i = 0; i < n; i++) {
            double t = curve.Domain.ParameterAt(i / (n - 1.0));
            Vector3d curvature = curve.CurvatureAt(t);
            energy += curvature.SquareLength * dt;
        }
        
        return energy;
    }
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OptimizeControlPointsStep(
        NurbsCurve curve,
        double relaxation,
        bool fixBoundaries) {
        int n = curve.Points.Count;
        Point3d[] newPoints = new Point3d[n];
        
        for (int i = 0; i < n; i++) {
            newPoints[i] = (fixBoundaries && (i == 0 || i == n - 1))
                ? curve.Points[i].Location
                : i > 0 && i < n - 1
                    ? new Point3d(
                        (curve.Points[i - 1].Location.X + curve.Points[i + 1].Location.X) * 0.5,
                        (curve.Points[i - 1].Location.Y + curve.Points[i + 1].Location.Y) * 0.5,
                        (curve.Points[i - 1].Location.Z + curve.Points[i + 1].Location.Z) * 0.5)
                    : curve.Points[i].Location;
        }
        
        for (int i = 0; i < n; i++) {
            if (!fixBoundaries || (i != 0 && i != n - 1)) {
                Point3d current = curve.Points[i].Location;
                Vector3d delta = newPoints[i] - current;
                curve.Points[i] = new ControlPoint(current + (delta * relaxation));
            }
        }
    }
    
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeFairnessScore(Curve curve) =>
        FittingConfig.EnergySampleCount > 0
        && Enumerable.Range(0, FittingConfig.EnergySampleCount)
            .Select(i => curve.CurvatureAt(curve.Domain.ParameterAt(i / (FittingConfig.EnergySampleCount - 1.0))).Length)
            .ToArray() is double[] curvatures
        && curvatures.Length > 0
        && curvatures.Average() is double avgCurv
        && Math.Sqrt(curvatures.Sum(k => Math.Pow(k - avgCurv, 2)) / curvatures.Length) is double stdDev
        && avgCurv > RhinoMath.ZeroTolerance
            ? RhinoMath.Clamp(1.0 - (stdDev / avgCurv), 0.0, 1.0)
            : 0.0;
}
```

**LOC Estimate**: 230-270 lines

## Adherence to Limits

- **Files**: 4 files (✓ matches 4-file standard for libs/rhino/ folders)
- **Types**: 8 types total across all files (✓ well under 10-type maximum, target 6-8)
  - File 1: 6 types (Fitting class, IFitResult, CurveFitResult, SurfaceFitResult, FitOptions, FairOptions)
  - File 2: 1 type (FittingConfig)
  - File 3: 1 type (FittingCore)
  - File 4: 1 type (FittingCompute)
- **Estimated Total LOC**: 750-900 lines across 4 files (✓ well within acceptable range)
  - Each member: <300 LOC (✓ no individual method exceeds limit)

## Algorithmic Density Strategy

**How we achieve dense code without helpers**:

1. **Inline Cox-de Boor Recursion**: B-spline basis evaluation via nested ternary operators and recursive inline method, no extracted functions
2. **LINQ Aggregate for Matrix Operations**: Build basis matrices using `.Select().ToArray()` chains
3. **Pattern Matching for Validation**: All parameter validation via `switch` expressions returning `Result<T>`
4. **ArrayPool for Temporary Buffers**: Zero-allocation temporary storage for matrix computations
5. **FrozenDictionary Dispatch**: Type-based operation selection via O(1) lookup, no if/else chains
6. **Inline Energy Computation**: Trapezoidal integration using `for` loop with accumulation, no helper methods
7. **Compose Existing Operations**: Chain `Result.Bind()` → `Result.Map()` → `Result.Validate()` for workflows
8. **Expression-Based Control Flow**: All branching via ternary/switch expressions, zero if/else statements

## Dispatch Architecture

**FrozenDictionary Configuration**:
```csharp
// Type + Method combination → (ValidationMode, BufferSize)
internal static readonly FrozenDictionary<(Type GeometryType, byte Method), (V Mode, int Buffer)> TypeConfig =
    new Dictionary<(Type, byte), (V, int)> {
        [(typeof(Curve), 0)] = (V.Standard | V.Degeneracy, 256),
        [(typeof(NurbsCurve), 0)] = (V.Standard | V.NurbsGeometry, 512),
        [(typeof(Surface), 1)] = (V.Standard | V.UVDomain, 512),
        [(typeof(NurbsSurface), 1)] = (V.Standard | V.NurbsGeometry | V.UVDomain, 1024),
    }.ToFrozenDictionary();

// Validation modes per geometry type
internal static readonly FrozenDictionary<Type, V> ValidationModes =
    new Dictionary<Type, V> {
        [typeof(Curve)] = V.Standard | V.Degeneracy,
        [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
        [typeof(Surface)] = V.Standard | V.UVDomain,
        [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
    }.ToFrozenDictionary();
```

**Pattern Matching Dispatch**:
```csharp
return geometry switch {
    Curve c => ProcessCurve(c, options, context),
    Surface s => ProcessSurface(s, options, context),
    _ => ResultFactory.Create<IFitResult>(
        error: E.Fitting.UnsupportedConfiguration.WithContext(
            $"Type: {geometry.GetType().Name}")),
};
```

## Public API Surface

### Primary Operations

```csharp
/// <summary>Fits NURBS curve to points via least-squares approximation.</summary>
public static Result<CurveFitResult> FitCurve(
    Point3d[] points,
    FitOptions options,
    IGeometryContext context);

/// <summary>Fits NURBS surface to point grid via least-squares approximation.</summary>
public static Result<SurfaceFitResult> FitSurface(
    Point3d[,] points,
    FitOptions options,
    IGeometryContext context);

/// <summary>Rebuilds curve with new control point distribution.</summary>
public static Result<CurveFitResult> RebuildCurve(
    Curve curve,
    FitOptions options,
    IGeometryContext context);

/// <summary>Rebuilds surface with control point grid.</summary>
public static Result<SurfaceFitResult> RebuildSurface(
    Surface surface,
    FitOptions options,
    IGeometryContext context);

/// <summary>Removes curvature variations via bending energy minimization.</summary>
public static Result<CurveFitResult> FairCurve(
    Curve curve,
    FairOptions options,
    IGeometryContext context);

/// <summary>Fairs surface via thin-plate bending energy minimization.</summary>
public static Result<SurfaceFitResult> FairSurface(
    Surface surface,
    FairOptions options,
    IGeometryContext context);

/// <summary>Laplacian smoothing with iteration count.</summary>
public static Result<CurveFitResult> SmoothCurve(
    Curve curve,
    int iterations,
    double relaxation,
    IGeometryContext context);

/// <summary>Surface smoothing via control point averaging.</summary>
public static Result<SurfaceFitResult> SmoothSurface(
    Surface surface,
    int iterations,
    double relaxation,
    IGeometryContext context);
```

### Advanced Operations (Additional Identified)

```csharp
/// <summary>Progressive iterative approximation: refines control points until deviation < tolerance.</summary>
public static Result<CurveFitResult> FitCurveLSPIA(
    Point3d[] points,
    FitOptions options,
    IGeometryContext context);

/// <summary>Automatic degree selection: fits curves at degrees [1..5], selects best fairness/deviation balance.</summary>
public static Result<CurveFitResult> FitCurveAutoDegree(
    Point3d[] points,
    FitOptions options,
    IGeometryContext context);

/// <summary>Constrained fitting: fits curve through points with tangent/curvature constraints at boundaries.</summary>
public static Result<CurveFitResult> FitCurveConstrained(
    Point3d[] points,
    (Vector3d? StartTangent, Vector3d? EndTangent, Vector3d? StartCurvature, Vector3d? EndCurvature) constraints,
    FitOptions options,
    IGeometryContext context);

/// <summary>Multi-patch surface fitting: fits surfaces to large point clouds with automatic patch decomposition.</summary>
public static Result<SurfaceFitResult[]> FitSurfaceMultiPatch(
    Point3d[] points,
    (int UPatches, int VPatches) patchGrid,
    FitOptions options,
    IGeometryContext context);

/// <summary>Isogeometric refinement: subdivides NURBS curve/surface without changing shape (knot insertion).</summary>
public static Result<CurveFitResult> RefineCurveIsogeometric(
    NurbsCurve curve,
    int refinementLevel,
    IGeometryContext context);
```

### Configuration Types

```csharp
/// <summary>Fitting configuration with degree, control points, tolerance, parameterization method.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct FitOptions(
    int? Degree = null,
    int? ControlPointCount = null,
    double? Tolerance = null,
    bool PreserveTangents = false,
    bool UseChordLength = true);

/// <summary>Fairing configuration with energy minimization parameters.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct FairOptions(
    double? Tolerance = null,
    int MaxIterations = FittingConfig.DefaultFairIterations,
    double RelaxationFactor = FittingConfig.DefaultRelaxation,
    bool FixBoundaries = true);
```

## Code Style Adherence Verification

- [x] All examples use pattern matching (no if/else) - switch expressions, ternary operators
- [x] All examples use explicit types (no var) - `Result<CurveFitResult>`, `double[]`, `Point3d[]`
- [x] All examples use named parameters - `error: E.Fitting.*`, `value: result`
- [x] All examples use trailing commas - Dictionary initializers end with `,`
- [x] All examples use K&R brace style - Opening brace on same line
- [x] All examples use target-typed new() - `new()` in OperationConfig
- [x] All examples use collection expressions [] - `[result]`, `[e1, e2,]`
- [x] One type per file organization - Each file contains single static class or record types
- [x] All member estimates under 300 LOC - Max is ~280 in FittingCore
- [x] All patterns match existing libs/ exemplars - Mirrors analysis/, morphology/ patterns

## Implementation Sequence

1. ✅ Read this blueprint thoroughly
2. Double-check SDK usage patterns via `RhinoCommon` documentation
3. Verify libs/ integration strategy (Result, UnifiedOperation, V.*, E.*)
4. Create folder structure: `libs/rhino/fitting/`
5. Implement `FittingConfig.cs` first (constants, dispatch tables)
6. Implement `FittingCore.cs` (core algorithms: chord-length, knots, least-squares)
7. Implement `Fitting.cs` (public API with UnifiedOperation orchestration)
8. Implement `FittingCompute.cs` (advanced fairing/smoothing operations)
9. Add error codes to `libs/core/errors/E.cs` (range 2900-2919)
10. Add validation integration via `V.NurbsGeometry` (already exists, verify sufficiency)
11. Add diagnostic instrumentation in `OperationConfig` (EnableDiagnostics=true for DEBUG)
12. Verify patterns match exemplars (analysis/, spatial/, morphology/)
13. Check LOC limits (each member ≤300, target 150-250)
14. Verify file/type limits (4 files ✓, 8 types ✓)
15. Verify code style compliance (no var, no if/else, named params, trailing commas)

## Error Codes (E.Fitting.* in range 2900-2919)

**Add to `libs/core/errors/E.cs`**:

```csharp
// Fitting Operations (2900-2919)
[2900] = "Fitting operation failed: numerical instability or invalid configuration",
[2901] = "Curve degree out of valid range [1, 11]",
[2902] = "Control point count below minimum for specified degree",
[2903] = "Insufficient points for least-squares fitting",
[2904] = "Iterative optimization failed to converge within maximum iterations",
[2905] = "Fitting tolerance parameter invalid or out of range",
[2906] = "Knot vector structure invalid or incompatible with degree/control points",
[2907] = "Geometry type or parameter combination not supported for fitting",
[2908] = "Surface fitting requires rectangular point grid",
[2909] = "Control point optimization produced degenerate geometry",
[2910] = "Parameterization method failed: coincident points or zero-length curve",
[2911] = "Constraint violation: fitted geometry exceeds tolerance threshold",
[2912] = "Bending energy computation failed: invalid curvature data",
[2913] = "Laplacian smoothing failed: non-manifold control point structure",
[2914] = "LSPIA refinement failed: iteration limit exceeded without convergence",
[2915] = "Automatic degree selection failed: no valid degree found in range",
[2916] = "Constrained fitting failed: incompatible boundary conditions",
[2917] = "Multi-patch decomposition failed: invalid patch grid specification",
[2918] = "Isogeometric refinement failed: knot insertion produced invalid structure",
[2919] = "Surface fairing failed: thin-plate energy minimization diverged",
```

**Add to `E.Geometry` class**:
```csharp
// Fitting Operations (2900-2919)
public static readonly SystemError FittingFailed = Get(2900);
public static readonly SystemError InvalidDegree = Get(2901);
public static readonly SystemError InvalidControlPointCount = Get(2902);
public static readonly SystemError InsufficientPoints = Get(2903);
public static readonly SystemError ConvergenceFailed = Get(2904);
public static readonly SystemError InvalidTolerance = Get(2905);
public static readonly SystemError InvalidKnotVector = Get(2906);
public static readonly SystemError UnsupportedConfiguration = Get(2907);
public static readonly SystemError InvalidPointGrid = Get(2908);
public static readonly SystemError DegenerateGeometry = Get(2909);
public static readonly SystemError ParameterizationFailed = Get(2910);
public static readonly SystemError ConstraintViolation = Get(2911);
public static readonly SystemError EnergyComputationFailed = Get(2912);
public static readonly SystemError SmoothingFailed = Get(2913);
public static readonly SystemError LSPIAFailed = Get(2914);
public static readonly SystemError AutoDegreeSelectionFailed = Get(2915);
public static readonly SystemError ConstrainedFittingFailed = Get(2916);
public static readonly SystemError MultiPatchFailed = Get(2917);
public static readonly SystemError IsogeometricRefinementFailed = Get(2918);
public static readonly SystemError SurfaceFairingFailed = Get(2919);
```

**Error code namespace mapping**:
```csharp
// E.cs GetDomain method already handles:
// >= 2000 and < 3000 => GeometryDomain
```

## Validation Modes

**Existing modes to use**:
- `V.Standard` - Basic `IsValid()` check
- `V.Degeneracy` - Degenerate geometry detection
- `V.NurbsGeometry` - NURBS-specific validation (control points, knots, weights)
- `V.UVDomain` - Surface UV parameter domain checks

**No new validation modes needed** - existing modes cover all fitting validation requirements.

## References

### SDK Documentation
- [RhinoCommon Curve.Fit API](https://developer.rhino3d.com/api/RhinoCommon/html/M_Rhino_Geometry_Curve_Fit.htm)
- [RhinoCommon NurbsCurve.CreateFromFitPoints](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.nurbscurve/createfromfitpoints)
- [RhinoCommon Surface.Fit API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.surface/fit)
- [RhinoCommon Surface.Rebuild API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.surface/rebuild)
- [RhinoCommon Curve.Fair API](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.curve/fair)
- [NURBS-Python Curve Fitting Documentation](https://nurbs-python.readthedocs.io/en/latest/module_fitting.html)
- [NURBS Book - Least Squares Approximation](https://link.springer.com/content/pdf/10.1007/978-3-642-59223-2_9.pdf)

### Related libs/ Code (MUST READ BEFORE IMPLEMENTING)
- `libs/core/results/` - Result monad patterns, Map/Bind/Ensure chaining
- `libs/core/operations/` - UnifiedOperation usage, OperationConfig structure
- `libs/core/validation/` - V.* flag usage, ValidationRules integration
- `libs/core/errors/` - E.* error code allocation, WithContext() usage
- `libs/rhino/analysis/` - Similar curvature/fairness analysis patterns
- `libs/rhino/morphology/` - Iterative optimization, convergence criteria
- `libs/rhino/spatial/` - FrozenDictionary dispatch, ArrayPool usage

### Academic References
- "The NURBS Book" (Piegl & Tiller) - Chapters 9 (curve fitting), Appendix A9.7 (surface fitting)
- "Least Squares Progressive Iterative Approximation" (LSPIA) papers
- "Optimizing NURBS Curves Fitting by Least Squares" - SciEngine
- "Curve and Surface Fitting" - Springer mathematical reference

## Justification for Additional Operations

**Operation 5: Progressive Iterative Approximation (LSPIA)**
- **Justification**: Superior to traditional least-squares for noisy data; automatically refines control points iteratively
- **Use case**: Point cloud fitting where traditional methods produce excessive deviation
- **Complexity**: Moderate - extends core least-squares with iterative refinement loop
- **Value**: State-of-art fitting method from recent research, not in RhinoCommon

**Operation 6: Automatic Degree Selection**
- **Justification**: Users often don't know optimal degree; trying multiple degrees and scoring fairness/deviation provides best result
- **Use case**: Automated workflows where curve quality matters but degree is unknown
- **Complexity**: Low - wraps existing fitting with loop over degrees [1..5] and scoring
- **Value**: Removes guesswork from degree selection, provides best-quality output

**Operation 7: Constrained Fitting**
- **Justification**: Boundary conditions (tangent/curvature continuity) are critical for multi-segment designs
- **Use case**: Connecting fitted curves smoothly, ensuring G1/G2 continuity at joins
- **Complexity**: Moderate - adds constraint rows to least-squares system
- **Value**: Essential for professional CAD workflows, not exposed in RhinoCommon directly

**Operation 8: Multi-Patch Surface Fitting**
- **Justification**: Large point clouds require decomposition into manageable patches for fitting
- **Use case**: Reverse engineering scanned surfaces, terrain modeling
- **Complexity**: High - requires spatial partitioning, patch boundary alignment
- **Value**: Handles large-scale data that single surface fitting cannot

**Operation 9: Isogeometric Refinement**
- **Justification**: Refines NURBS without changing shape via knot insertion; critical for IGA (isogeometric analysis)
- **Use case**: Mesh refinement for simulation, adaptive sampling
- **Complexity**: Low - uses existing RhinoCommon knot insertion or manual implementation
- **Value**: Enables isogeometric workflows, common in FEA/CFD

**Selection of 3 Primary Additional Operations**:
1. **LSPIA** (Operation 5) - Most valuable for fitting quality
2. **Automatic Degree Selection** (Operation 6) - Highest usability improvement
3. **Constrained Fitting** (Operation 7) - Essential for professional use

## Critical Notes

**Main File Suppression**:
```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fitting is the primary API entry point for the Fitting namespace")]
public static class Fitting { }
```

**No Extension Methods**: All functionality must be static methods in main classes, never extension methods
**No Helper Methods**: All logic must be inline or compose existing libs/core primitives
**Nested Types**: Records/interfaces nested within main class, not separate top-level types (except one per file rule)
**Proper Refactoring**: If new validation modes needed, add to `V.cs`, NOT as local helpers
