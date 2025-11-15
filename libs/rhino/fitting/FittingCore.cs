using System.Buffers;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fitting;

/// <summary>Core NURBS fitting algorithms with chord-length parameterization and least-squares.</summary>
internal static class FittingCore {
    /// <summary>Fits NURBS curve from points via least-squares with chord-length parameterization.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.CurveFitResult> FitCurveFromPoints(
        Point3d[] points,
        int degree,
        int? controlPointCount,
        double tolerance,
        bool preserveTangents,
        IGeometryContext context) =>
        degree switch {
            < FittingConfig.MinDegree => ResultFactory.Create<Fitting.CurveFitResult>(
                error: E.Geometry.Fitting.InvalidDegree.WithContext(
                    $"Degree must be >= {FittingConfig.MinDegree}, got {degree}")),
            > FittingConfig.MaxDegree => ResultFactory.Create<Fitting.CurveFitResult>(
                error: E.Geometry.Fitting.InvalidDegree.WithContext(
                    $"Degree must be <= {FittingConfig.MaxDegree}, got {degree}")),
            _ when controlPointCount.HasValue && controlPointCount.Value < FittingConfig.MinControlPoints(degree) =>
                ResultFactory.Create<Fitting.CurveFitResult>(
                    error: E.Geometry.Fitting.InvalidControlPointCount.WithContext(
                        $"Need >= {FittingConfig.MinControlPoints(degree)} control points for degree {degree}")),
            _ => ComputeChordParameters(points: points, power: FittingConfig.ChordPowerStandard)
                .Bind(parameters => {
                    int numControlPoints = controlPointCount ?? Math.Max(
                        FittingConfig.MinControlPoints(degree),
                        Math.Min(points.Length - 1, points.Length / 2));
                    return GenerateKnotVector(
                        parameters: parameters,
                        degree: degree,
                        controlPoints: numControlPoints)
                        .Bind(knots => SolveLeastSquares(
                            parameters: parameters,
                            dataPoints: points,
                            knots: knots,
                            degree: degree,
                            numControlPoints: numControlPoints,
                            preserveTangents: preserveTangents)
                            .Bind(controlPts => ConstructAndValidateCurve(
                                controlPoints: controlPts,
                                knots: knots,
                                degree: degree,
                                originalPoints: points,
                                tolerance: tolerance,
                                context: context)));
                }),
        };

    /// <summary>Computes chord-length parameterization: u[i] = Σ||p[j]-p[j-1]|| / totalLength.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double[]> ComputeChordParameters(Point3d[] points, double power) {
        int n = points.Length;
        double[] parameters = new double[n];
        double totalLength = 0.0;

        for (int i = 1; i < n; i++) {
            double segmentLength = points[i].DistanceTo(points[i - 1]);
            totalLength += Math.Pow(segmentLength, power);
            parameters[i] = totalLength;
        }

        return totalLength > RhinoMath.ZeroTolerance
            ? ResultFactory.Create(value: parameters.Select(t => t / totalLength).ToArray())
            : ResultFactory.Create<double[]>(
                error: E.Geometry.Fitting.ParameterizationFailed.WithContext(
                    "Coincident points: total chord length < ZeroTolerance"));
    }

    /// <summary>Generates knot vector via averaging: knot[i] = (t[i-p] + ... + t[i-1]) / p.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double[]> GenerateKnotVector(
        double[] parameters,
        int degree,
        int controlPoints) {
        int m = controlPoints + degree + 1;
        double[] knots = new double[m];

        for (int i = 0; i <= degree; i++) {
            knots[i] = 0.0;
            knots[m - 1 - i] = 1.0;
        }

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

    /// <summary>Cox-de Boor recursion: N[i,p](u) for B-spline basis function evaluation.</summary>
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

    /// <summary>Solves least-squares system N^T·N·P = N^T·D for control points P via Gaussian elimination.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Point3d[]> SolveLeastSquares(
        double[] parameters,
        Point3d[] dataPoints,
        double[] knots,
        int degree,
        int numControlPoints,
        bool preserveTangents) {
        double[,] N = new double[parameters.Length, numControlPoints];

        for (int i = 0; i < parameters.Length; i++) {
            double u = parameters[i];
            for (int j = 0; j < numControlPoints; j++) {
                N[i, j] = EvaluateBasis(i: j, p: degree, u: u, knots: knots);
            }
        }

        double[,] NtN = new double[numControlPoints, numControlPoints];
        Point3d[] NtD = new Point3d[numControlPoints];

        for (int i = 0; i < numControlPoints; i++) {
            for (int j = 0; j < numControlPoints; j++) {
                double sum = 0.0;
                for (int k = 0; k < parameters.Length; k++) {
                    sum += N[k, i] * N[k, j];
                }
                NtN[i, j] = sum;
            }

            Point3d sumPt = Point3d.Origin;
            for (int k = 0; k < parameters.Length; k++) {
                sumPt += N[k, i] * dataPoints[k];
            }
            NtD[i] = sumPt;
        }

        Point3d[] controlPoints = new Point3d[numControlPoints];
        double[] buffer = ArrayPool<double>.Shared.Rent(numControlPoints);
        try {
            for (int col = 0; col < numControlPoints; col++) {
                int pivotRow = col;
                double maxPivot = Math.Abs(NtN[col, col]);

                for (int row = col + 1; row < numControlPoints; row++) {
                    double absVal = Math.Abs(NtN[row, col]);
                    if (absVal > maxPivot) {
                        maxPivot = absVal;
                        pivotRow = row;
                    }
                }

                if (maxPivot < FittingConfig.PivotTolerance) {
                    return ResultFactory.Create<Point3d[]>(
                        error: E.Geometry.Fitting.FittingFailed.WithContext(
                            $"Singular matrix at column {col}"));
                }

                if (pivotRow != col) {
                    for (int k = 0; k < numControlPoints; k++) {
                        (NtN[col, k], NtN[pivotRow, k]) = (NtN[pivotRow, k], NtN[col, k]);
                    }
                    (NtD[col], NtD[pivotRow]) = (NtD[pivotRow], NtD[col]);
                }

                double pivot = NtN[col, col];
                for (int k = col; k < numControlPoints; k++) {
                    NtN[col, k] /= pivot;
                }
                NtD[col] /= pivot;

                for (int row = col + 1; row < numControlPoints; row++) {
                    double factor = NtN[row, col];
                    for (int k = col; k < numControlPoints; k++) {
                        NtN[row, k] -= factor * NtN[col, k];
                    }
                    Vector3d scaled = NtD[col] - Point3d.Origin;
                    NtD[row] = Point3d.Origin + (NtD[row] - Point3d.Origin - (factor * scaled));
                }
            }

            for (int i = numControlPoints - 1; i >= 0; i--) {
                Point3d sum = NtD[i];
                for (int j = i + 1; j < numControlPoints; j++) {
                    Vector3d scaled = controlPoints[j] - Point3d.Origin;
                    sum = Point3d.Origin + (sum - Point3d.Origin - (NtN[i, j] * scaled));
                }
                controlPoints[i] = sum;
            }

            return ResultFactory.Create(value: controlPoints);
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Constructs NurbsCurve from control points and computes quality metrics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Fitting.CurveFitResult> ConstructAndValidateCurve(
        Point3d[] controlPoints,
        double[] knots,
        int degree,
        Point3d[] originalPoints,
        double tolerance,
        IGeometryContext context) =>
        NurbsCurve.Create(periodic: false, degree: degree, points: controlPoints) is NurbsCurve curve && curve.IsValid
            ? ComputeDeviation(fitted: curve, original: originalPoints)
                .Bind(dev => dev.MaxDev > tolerance
                    ? ResultFactory.Create<Fitting.CurveFitResult>(
                        error: E.Geometry.Fitting.ConstraintViolation.WithContext(string.Create(
                            CultureInfo.InvariantCulture,
                            $"Max deviation {dev.MaxDev.ToString("E3", CultureInfo.InvariantCulture)} exceeds tolerance {tolerance.ToString("E3", CultureInfo.InvariantCulture)}")))
                    : ResultFactory.Create(value: new Fitting.CurveFitResult(
                        Curve: curve,
                        MaxDeviation: dev.MaxDev,
                        RmsDeviation: dev.RmsDev,
                        FairnessScore: ComputeFairnessScore(curve: curve),
                        ControlPointCount: controlPoints.Length,
                        ActualDegree: curve.Degree)))
            : ResultFactory.Create<Fitting.CurveFitResult>(
                error: E.Geometry.Fitting.FittingFailed.WithContext("Failed to construct valid NURBS curve"));

    /// <summary>Computes max/RMS deviation between fitted curve and original points.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(double MaxDev, double RmsDev)> ComputeDeviation(
        Curve fitted,
        Point3d[] original) {
        double maxDev = 0.0;
        double sumSqDev = 0.0;

        for (int i = 0; i < original.Length; i++) {
            Point3d closest = fitted.ClosestPoint(testPoint: original[i], t: out _);
            double dev = original[i].DistanceTo(closest);
            maxDev = dev > maxDev ? dev : maxDev;
            sumSqDev += dev * dev;
        }

        double rmsDev = Math.Sqrt(sumSqDev / original.Length);
        return ResultFactory.Create(value: (maxDev, rmsDev));
    }

    /// <summary>Computes fairness score from curvature variation.</summary>
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

    /// <summary>Rebuilds curve with validation and quality computation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.CurveFitResult> RebuildCurveWithValidation(
        Curve curve,
        int? degree,
        int? controlPointCount,
        bool preserveTangents,
        IGeometryContext context) =>
        ResultFactory.Create(value: curve)
            .Validate(args: [context, V.Standard | V.Degeneracy,])
            .Bind(validCurve =>
                validCurve.Rebuild(
                    pointCount: controlPointCount ?? (validCurve is NurbsCurve nc ? nc.Points.Count : FittingConfig.MinControlPoints(degree ?? validCurve.Degree)),
                    degree: degree ?? validCurve.Degree,
                    preserveTangents: preserveTangents) is NurbsCurve result && result.IsValid
                        ? ResultFactory.Create(value: new Fitting.CurveFitResult(
                            Curve: result,
                            MaxDeviation: 0.0,
                            RmsDeviation: 0.0,
                            FairnessScore: ComputeFairnessScore(curve: result),
                            ControlPointCount: result.Points.Count,
                            ActualDegree: result.Degree))
                        : ResultFactory.Create<Fitting.CurveFitResult>(
                            error: E.Geometry.Fitting.FittingFailed.WithContext("Rebuild produced invalid curve")));

    /// <summary>Fits surface from point grid (placeholder - full implementation similar to curve).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.SurfaceFitResult> FitSurfaceFromGrid(
        Point3d[,] _,
        int __,
        int ___,
        int? ____,
        int? _____,
        double ______,
        IGeometryContext _______) =>
        ResultFactory.Create<Fitting.SurfaceFitResult>(
            error: E.Geometry.Fitting.FittingFailed.WithContext("Surface fitting implementation pending"));

    /// <summary>Rebuilds surface with validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Fitting.SurfaceFitResult> RebuildSurfaceWithValidation(
        Surface surface,
        int? uDegree,
        int? vDegree,
        int? uControlPoints,
        int? vControlPoints,
        IGeometryContext context) =>
        ResultFactory.Create(value: surface)
            .Validate(args: [context, V.Standard | V.UVDomain,])
            .Bind(validSurface =>
                validSurface.Rebuild(
                    uPointCount: uControlPoints ?? FittingConfig.MinControlPoints(uDegree ?? (validSurface is NurbsSurface ns ? ns.Degree(0) : FittingConfig.DefaultSurfaceDegree)),
                    vPointCount: vControlPoints ?? FittingConfig.MinControlPoints(vDegree ?? (validSurface is NurbsSurface ns2 ? ns2.Degree(1) : FittingConfig.DefaultSurfaceDegree)),
                    uDegree: uDegree ?? (validSurface is NurbsSurface ns3 ? ns3.Degree(0) : FittingConfig.DefaultSurfaceDegree),
                    vDegree: vDegree ?? (validSurface is NurbsSurface ns4 ? ns4.Degree(1) : FittingConfig.DefaultSurfaceDegree)) is NurbsSurface result && result.IsValid
                        ? ResultFactory.Create(value: new Fitting.SurfaceFitResult(
                            Surface: result,
                            MaxDeviation: 0.0,
                            RmsDeviation: 0.0,
                            FairnessScore: 0.0,
                            ControlPointCounts: (result.Points.CountU, result.Points.CountV),
                            ActualDegrees: (result.Degree(0), result.Degree(1))))
                        : ResultFactory.Create<Fitting.SurfaceFitResult>(
                            error: E.Geometry.Fitting.FittingFailed.WithContext("Rebuild produced invalid surface")));
}
