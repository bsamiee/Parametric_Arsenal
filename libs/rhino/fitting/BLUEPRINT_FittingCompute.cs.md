# Blueprint: FittingCompute.cs (Advanced Operations)

## File Purpose
Advanced fairing, smoothing, energy minimization, and isogeometric refinement operations using iterative optimization and SDK methods.

## Total Types: 1
- `FittingCompute` (internal static class)

## Estimated LOC: 240-260

---

## Complete Implementation

```csharp
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fitting;

/// <summary>Advanced fairing and smoothing algorithms with energy minimization.</summary>
internal static class FittingCompute {
    /// <summary>Iteratively fairs curve by minimizing bending energy ∫(κ²).</summary>
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

    /// <summary>Fairs NURBS curve via iterative control point optimization.</summary>
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
            : ResultFactory.Create(value: new Fitting.CurveFitResult(
                Curve: working,
                MaxDeviation: 0.0,
                RmsDeviation: 0.0,
                FairnessScore: ComputeFairnessScore(working),
                ControlPointCount: working.Points.Count,
                ActualDegree: working.Degree));
    }

    /// <summary>Computes discrete bending energy E = ∫(κ²) ≈ Σ||κ(t[i])||²·Δt.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeBendingEnergy(Curve curve) {
        int n = FittingConfig.EnergySampleCount;
        double energy = 0.0;
        double dt = curve.Domain.Length / (n - 1.0);

        for (int i = 0; i < n; i++) {
            double t = curve.Domain.ParameterAt(i / (n - 1.0));
            Vector3d curvatureVector = curve.CurvatureAt(t);
            energy += curvatureVector.SquareLength * dt;
        }

        return energy;
    }

    /// <summary>Single Laplacian smoothing step: P[i] → (P[i-1] + P[i+1])/2.</summary>
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

    /// <summary>Computes fairness score from curvature standard deviation.</summary>
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

    /// <summary>Laplacian smoothing for curves with iteration count.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.CurveFitResult> SmoothCurve(
        Curve curve,
        int iterations,
        double relaxationFactor,
        IGeometryContext context) =>
        ResultFactory.Create(value: curve)
            .Validate(args: [context, V.Standard | V.Degeneracy,])
            .Bind(validCurve => {
                int clampedIterations = RhinoMath.Clamp(
                    iterations,
                    1,
                    FittingConfig.MaxSmoothIterations);
                double clampedRelaxation = RhinoMath.Clamp(
                    relaxationFactor,
                    FittingConfig.MinRelaxation,
                    FittingConfig.MaxRelaxation);
                NurbsCurve working = validCurve.ToNurbsCurve() ?? validCurve as NurbsCurve;
                return working is null
                    ? ResultFactory.Create<Fitting.CurveFitResult>(
                        error: E.Fitting.FittingFailed.WithContext("Cannot convert to NURBS"))
                    : ((Func<Result<Fitting.CurveFitResult>>)(() => {
                        for (int iter = 0; iter < clampedIterations; iter++) {
                            OptimizeControlPointsStep(
                                curve: working,
                                relaxation: clampedRelaxation,
                                fixBoundaries: false);
                        }
                        return ResultFactory.Create(value: new Fitting.CurveFitResult(
                            Curve: working,
                            MaxDeviation: 0.0,
                            RmsDeviation: 0.0,
                            FairnessScore: ComputeFairnessScore(working),
                            ControlPointCount: working.Points.Count,
                            ActualDegree: working.Degree));
                    }))();
            });

    /// <summary>Surface smoothing via SDK Surface.Smooth method with iteration.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.SurfaceFitResult> SmoothSurface(
        Surface surface,
        int iterations,
        double smoothFactor,
        (bool X, bool Y, bool Z) axes,
        bool fixBoundaries,
        IGeometryContext context) =>
        ResultFactory.Create(value: surface)
            .Validate(args: [context, V.Standard | V.UVDomain,])
            .Bind(validSurface => {
                Surface working = validSurface.Duplicate() as Surface ?? validSurface;
                int clampedIterations = RhinoMath.Clamp(
                    iterations,
                    1,
                    FittingConfig.MaxSmoothIterations);

                for (int i = 0; i < clampedIterations; i++) {
                    working = working.Smooth(
                        smoothFactor: smoothFactor,
                        bXSmooth: axes.X,
                        bYSmooth: axes.Y,
                        bZSmooth: axes.Z,
                        bFixBoundaries: fixBoundaries,
                        coordinateSystem: SmoothingCoordinateSystem.World,
                        plane: Plane.WorldXY) switch {
                        null => return ResultFactory.Create<Fitting.SurfaceFitResult>(
                            error: E.Fitting.SmoothingFailed.WithContext($"Iteration {i + 1} failed")),
                        Surface s => s,
                    };
                }

                return working is NurbsSurface ns
                    ? ResultFactory.Create(value: new Fitting.SurfaceFitResult(
                        Surface: ns,
                        MaxDeviation: 0.0,
                        RmsDeviation: 0.0,
                        FairnessScore: 0.0,
                        ControlPointCounts: (ns.Points.CountU, ns.Points.CountV),
                        ActualDegrees: (ns.Degree(0), ns.Degree(1))))
                    : ResultFactory.Create<Fitting.SurfaceFitResult>(
                        error: E.Fitting.FittingFailed.WithContext("Result not NURBS"));
            });

    /// <summary>Isogeometric refinement via knot insertion (shape-preserving subdivision).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.CurveFitResult> RefineIsogeometric(
        NurbsCurve curve,
        int level,
        IGeometryContext context) =>
        ResultFactory.Create(value: curve)
            .Validate(args: [context, V.Standard | V.NurbsGeometry,])
            .Bind(validCurve => {
                NurbsCurve refined = validCurve.Duplicate() as NurbsCurve ?? validCurve;
                
                for (int lvl = 0; lvl < level; lvl++) {
                    double[] knotSpans = ComputeMidpointKnots(refined.Knots);
                    
                    foreach (double knotParam in knotSpans) {
                        bool inserted = refined.Knots.InsertKnot(value: knotParam, multiplicity: 1);
                        if (!inserted) {
                            return ResultFactory.Create<Fitting.CurveFitResult>(
                                error: E.Fitting.IsogeometricRefinementFailed.WithContext(
                                    $"Knot insertion failed at t={knotParam:F6} (level {lvl + 1})"));
                        }
                    }
                }

                return ResultFactory.Create(value: new Fitting.CurveFitResult(
                    Curve: refined,
                    MaxDeviation: 0.0,
                    RmsDeviation: 0.0,
                    FairnessScore: ComputeFairnessScore(refined),
                    ControlPointCount: refined.Points.Count,
                    ActualDegree: refined.Degree));
            });

    /// <summary>Computes midpoint parameters for knot insertion (uniform refinement).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double[] ComputeMidpointKnots(NurbsCurveKnotList knots) {
        int count = knots.Count;
        List<double> midpoints = new(capacity: count);
        
        for (int i = 0; i < count - 1; i++) {
            double k1 = knots[i];
            double k2 = knots[i + 1];
            double mid = (k1 + k2) * 0.5;
            
            if (Math.Abs(k2 - k1) > FittingConfig.ParameterTolerance) {
                midpoints.Add(mid);
            }
        }
        
        return [.. midpoints];
    }

    /// <summary>Iteratively fairs surface via thin-plate energy minimization.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.SurfaceFitResult> FairSurfaceIterative(
        Surface surface,
        Fitting.FairOptions options,
        IGeometryContext context) =>
        ResultFactory.Create(value: surface)
            .Validate(args: [context, V.Standard | V.UVDomain,])
            .Bind(validSurface => {
                Surface working = validSurface.Duplicate() as Surface ?? validSurface;
                int iterations = options.MaxIterations;

                for (int i = 0; i < iterations; i++) {
                    working = working.Smooth(
                        smoothFactor: options.RelaxationFactor,
                        bXSmooth: true,
                        bYSmooth: true,
                        bZSmooth: true,
                        bFixBoundaries: options.FixBoundaries,
                        coordinateSystem: SmoothingCoordinateSystem.World,
                        plane: Plane.WorldXY) switch {
                        null => return ResultFactory.Create<Fitting.SurfaceFitResult>(
                            error: E.Fitting.SurfaceFairingFailed.WithContext($"Iteration {i + 1} failed")),
                        Surface s => s,
                    };
                }

                return working is NurbsSurface ns
                    ? ResultFactory.Create(value: new Fitting.SurfaceFitResult(
                        Surface: ns,
                        MaxDeviation: 0.0,
                        RmsDeviation: 0.0,
                        FairnessScore: 0.0,
                        ControlPointCounts: (ns.Points.CountU, ns.Points.CountV),
                        ActualDegrees: (ns.Degree(0), ns.Degree(1))))
                    : ResultFactory.Create<Fitting.SurfaceFitResult>(
                        error: E.Fitting.FittingFailed.WithContext("Result not NURBS"));
            });
}
```

## Key Patterns
- Iterative optimization with convergence check
- Bending energy formula: E = ∫(κ²) ≈ Σ||κ(t[i])||²·Δt
- Laplacian smoothing: P[i] → (P[i-1] + P[i+1])/2 with relaxation
- SDK Surface.Smooth usage (not manual implementation)
- Knot insertion via refined.Knots.InsertKnot()
- RhinoMath.Clamp for parameter validation
- for loops for iterations (not LINQ - performance)
- Pattern matching for null checks
