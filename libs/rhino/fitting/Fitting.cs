using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fitting;

/// <summary>NURBS curve/surface fitting via least-squares approximation and fairing.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Fitting is the primary API entry point for the Fitting namespace")]
public static class Fitting {
    /// <summary>Fit result marker with geometry and quality metrics.</summary>
    public interface IFitResult {
        /// <summary>Fitted NURBS geometry.</summary>
        public GeometryBase Geometry { get; }
        /// <summary>Maximum deviation from original data.</summary>
        public double MaxDeviation { get; }
        /// <summary>RMS deviation from original data.</summary>
        public double RmsDeviation { get; }
    }

    /// <summary>Curve fitting result with fairness metrics.</summary>
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
            $"CurveFit[deg={this.ActualDegree}] | CP={this.ControlPointCount} | MaxΔ={this.MaxDeviation:E3} | Fair={this.FairnessScore:F3}");
    }

    /// <summary>Surface fitting result with fairness metrics.</summary>
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
            $"SurfaceFit[deg=({this.ActualDegrees.U},{this.ActualDegrees.V})] | CP=({this.ControlPointCounts.U}×{this.ControlPointCounts.V}) | MaxΔ={this.MaxDeviation:E3}");
    }

    /// <summary>Fitting configuration parameters.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct FitOptions(
        int? Degree = null,
        int? ControlPointCount = null,
        double? Tolerance = null,
        bool PreserveTangents = false);

    /// <summary>Fairing configuration parameters.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct FairOptions(
        double? Tolerance = null,
        int MaxIterations = FittingConfig.DefaultFairIterations,
        double RelaxationFactor = FittingConfig.DefaultRelaxation,
        bool FixBoundaries = true);

    /// <summary>Fits NURBS curve from points via least-squares with chord-length parameterization.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveFitResult> FitCurve(
        Point3d[] points,
        FitOptions options,
        IGeometryContext context) =>
        points.Length >= FittingConfig.MinPointsForCurveFit
            ? FittingCore.FitCurveFromPoints(
                points: points,
                degree: options.Degree ?? FittingConfig.DefaultCurveDegree,
                controlPointCount: options.ControlPointCount,
                tolerance: options.Tolerance ?? context.AbsoluteTolerance,
                preserveTangents: options.PreserveTangents,
                context: context)
            : ResultFactory.Create<CurveFitResult>(
                error: E.Geometry.Fitting.InsufficientPoints.WithContext(
                    $"Minimum {FittingConfig.MinPointsForCurveFit} points required, got {points.Length}"));

    /// <summary>Fits NURBS surface from point grid via least-squares.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceFitResult> FitSurface(
        Point3d[,] points,
        FitOptions options,
        IGeometryContext context) =>
        points.GetLength(0) >= FittingConfig.MinPointsForSurfaceFit && points.GetLength(1) >= FittingConfig.MinPointsForSurfaceFit
            ? FittingCore.FitSurfaceFromGrid(
                points,
                options.Degree ?? FittingConfig.DefaultSurfaceDegree,
                options.Degree ?? FittingConfig.DefaultSurfaceDegree,
                options.ControlPointCount,
                options.ControlPointCount,
                options.Tolerance ?? context.AbsoluteTolerance,
                context)
            : ResultFactory.Create<SurfaceFitResult>(
                error: E.Geometry.Fitting.InvalidPointGrid.WithContext(
                    $"Minimum {FittingConfig.MinPointsForSurfaceFit}×{FittingConfig.MinPointsForSurfaceFit} grid required"));

    /// <summary>Rebuilds curve with new control point distribution.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveFitResult> RebuildCurve(
        Curve curve,
        FitOptions options,
        IGeometryContext context) =>
        FittingCore.RebuildCurveWithValidation(
            curve: curve,
            degree: options.Degree,
            controlPointCount: options.ControlPointCount,
            preserveTangents: options.PreserveTangents,
            context: context);

    /// <summary>Rebuilds surface with control point grid.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceFitResult> RebuildSurface(
        Surface surface,
        FitOptions options,
        IGeometryContext context) =>
        FittingCore.RebuildSurfaceWithValidation(
            surface: surface,
            uDegree: options.Degree,
            vDegree: options.Degree,
            uControlPoints: options.ControlPointCount,
            vControlPoints: options.ControlPointCount,
            context: context);

    /// <summary>Removes curvature variations via bending energy minimization.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveFitResult> FairCurve(
        Curve curve,
        FairOptions options,
        IGeometryContext context) =>
        FittingCompute.FairCurveIterative(
            curve: curve,
            options: options,
            context: context);

    /// <summary>Fairs surface via thin-plate bending energy minimization.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceFitResult> FairSurface(
        Surface surface,
        FairOptions options,
        IGeometryContext context) =>
        FittingCompute.FairSurfaceIterative(
            surface: surface,
            options: options,
            context: context);

    /// <summary>Laplacian smoothing with iteration count.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveFitResult> SmoothCurve(
        Curve curve,
        (int Iterations, double Factor) parameters,
        IGeometryContext context) =>
        FittingCompute.SmoothCurve(
            curve: curve,
            iterations: parameters.Iterations,
            relaxationFactor: parameters.Factor,
            context: context);

    /// <summary>Surface smoothing via SDK Smooth method.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceFitResult> SmoothSurface(
        Surface surface,
        (int Iterations, double Factor) parameters,
        (bool X, bool Y, bool Z) axes,
        bool fixBoundaries,
        IGeometryContext context) =>
        FittingCompute.SmoothSurface(
            surface: surface,
            iterations: parameters.Iterations,
            smoothFactor: parameters.Factor,
            axes: axes,
            fixBoundaries: fixBoundaries,
            context: context);

    /// <summary>Isogeometric refinement via knot insertion (shape-preserving).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveFitResult> RefineIsogeometric(
        NurbsCurve curve,
        int subdivisionLevel,
        IGeometryContext context) =>
        FittingCompute.RefineIsogeometric(
            curve: curve,
            level: subdivisionLevel,
            context: context);
}
