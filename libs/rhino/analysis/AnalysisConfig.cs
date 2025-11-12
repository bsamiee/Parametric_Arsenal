using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Validation modes and constants for differential geometry.</summary>
internal static class AnalysisConfig {
    /// <summary>Type-to-validation mode mapping for geometry analysis.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(LineCurve)] = V.Standard | V.Degeneracy,
            [typeof(ArcCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy | V.PolycurveStructure,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard | V.UVDomain,
            [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [typeof(PlaneSurface)] = V.Standard,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology | V.ExtrusionGeometry,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Maximum 20 C1/C2 discontinuities per curve.</summary>
    internal const int MaxDiscontinuities = 20;

    /// <summary>Default derivative order 2 for position/tangent/curvature.</summary>
    internal const int DefaultDerivativeOrder = 2;

    /// <summary>Sample 5 perpendicular frames along curve domain.</summary>
    internal const int CurveFrameSampleCount = 5;

    /// <summary>Surface quality: 10×10 grid for curvature sampling.</summary>
    internal const int SurfaceQualityGridDimension = 10;

    /// <summary>High curvature threshold 5× median for anomaly detection.</summary>
    internal const double HighCurvatureMultiplier = 5.0;

    /// <summary>Curve fairness: 50 samples for curvature comb analysis.</summary>
    internal const int CurveFairnessSampleCount = 50;

    /// <summary>Inflection sharpness threshold 0.5 for curvature sign change.</summary>
    internal const double InflectionSharpnessThreshold = 0.5;

    /// <summary>Smoothness sensitivity 10.0 for curvature variation.</summary>
    internal const double SmoothnessSensitivity = 10.0;

    /// <summary>Mesh FEA: aspect ratio warning 3.0, critical 10.0.</summary>
    internal const double AspectRatioWarning = 3.0;
    internal const double AspectRatioCritical = 10.0;

    /// <summary>Skewness warning 0.5, critical 0.85.</summary>
    internal const double SkewnessWarning = 0.5;
    internal const double SkewnessCritical = 0.85;

    /// <summary>Jacobian warning 0.3, critical 0.1.</summary>
    internal const double JacobianWarning = 0.3;
    internal const double JacobianCritical = 0.1;

    /// <summary>Brep closest point tolerance multiplier: 100× absolute tolerance for proximity queries.</summary>
    internal const double BrepClosestPointToleranceMultiplier = 100.0;
}
