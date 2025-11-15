using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Fitting;

/// <summary>Fitting configuration constants and unified dispatch registry.</summary>
[Pure]
internal static class FittingConfig {
    /// <summary>Operation types for FrozenDictionary dispatch.</summary>
    internal enum FitOperation : byte {
        CurveFromPoints = 1,
        SurfaceFromGrid = 2,
        RebuildCurve = 3,
        RebuildSurface = 4,
        FairCurve = 5,
        FairSurface = 6,
    }

    /// <summary>Unified dispatch table: (Type, Operation) → (ValidationMode, OperationName).</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, FitOperation Operation), (V ValidationMode, string OperationName)> OperationRegistry =
        new Dictionary<(Type, FitOperation), (V, string)> {
            [(typeof(Point3d[]), FitOperation.CurveFromPoints)] = (V.None, "Fitting.CurveFromPoints"),
            [(typeof(Point3d[,]), FitOperation.SurfaceFromGrid)] = (V.None, "Fitting.SurfaceFromGrid"),
            [(typeof(Curve), FitOperation.RebuildCurve)] = (V.Standard | V.Degeneracy, "Fitting.RebuildCurve"),
            [(typeof(NurbsCurve), FitOperation.RebuildCurve)] = (V.Standard | V.NurbsGeometry, "Fitting.RebuildNurbsCurve"),
            [(typeof(Surface), FitOperation.RebuildSurface)] = (V.Standard | V.UVDomain, "Fitting.RebuildSurface"),
            [(typeof(NurbsSurface), FitOperation.RebuildSurface)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Fitting.RebuildNurbsSurface"),
            [(typeof(Curve), FitOperation.FairCurve)] = (V.Standard | V.Degeneracy, "Fitting.FairCurve"),
            [(typeof(NurbsCurve), FitOperation.FairCurve)] = (V.Standard | V.NurbsGeometry, "Fitting.FairNurbsCurve"),
            [(typeof(Surface), FitOperation.FairSurface)] = (V.Standard | V.UVDomain, "Fitting.FairSurface"),
            [(typeof(NurbsSurface), FitOperation.FairSurface)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Fitting.FairNurbsSurface"),
        }.ToFrozenDictionary();

    /// <summary>Degree constraints for NURBS geometry.</summary>
    internal const int MinDegree = 1;
    internal const int MaxDegree = 11;
    internal const int DefaultCurveDegree = 3;
    internal const int DefaultSurfaceDegree = 3;

    /// <summary>Control point constraints per degree.</summary>
    internal const int MinControlPointsPerDegree = 2;
    [Pure, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static int MinControlPoints(int degree) => degree + 1 + MinControlPointsPerDegree;

    /// <summary>Fitting point count requirements.</summary>
    internal const int MinPointsForCurveFit = 3;
    internal const int MinPointsForSurfaceFit = 3;

    /// <summary>Parameterization constants for chord-length method.</summary>
    internal const double ChordPowerStandard = 1.0;
    internal const double ChordPowerCentripetal = 0.5;

    /// <summary>Iteration limits for optimization algorithms.</summary>
    internal const int DefaultFairIterations = 500;
    internal const int MaxFairIterations = 1000;
    internal const int DefaultSmoothIterations = 10;
    internal const int MaxSmoothIterations = 100;

    /// <summary>Convergence thresholds using RhinoMath constants.</summary>
    internal static readonly double EnergyConvergence = RhinoMath.SqrtEpsilon;
    internal static readonly double RmsConvergence = RhinoMath.SqrtEpsilon;
    internal static readonly double ParameterTolerance = RhinoMath.ZeroTolerance;

    /// <summary>Energy computation parameters.</summary>
    internal const int EnergySampleCount = 100;
    internal const double CurvatureWeight = 1.0;
    internal const double SecondDerivativeWeight = 0.1;

    /// <summary>Fairing and smoothing defaults.</summary>
    internal const double DefaultRelaxation = 0.5;
    internal const double MinRelaxation = 0.1;
    internal const double MaxRelaxation = 1.0;

    /// <summary>Gaussian elimination pivot tolerance for least-squares solver.</summary>
    internal static readonly double PivotTolerance = RhinoMath.SqrtEpsilon;

    /// <summary>Maximum matrix size for direct solver (use iterative beyond this).</summary>
    internal const int MaxDirectSolverSize = 1000;
}
