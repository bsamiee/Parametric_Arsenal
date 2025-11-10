using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Validation modes and computation constants for differential geometry.</summary>
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

    /// <summary>Surface quality: 100 samples for curvature distribution.</summary>
    internal const int SurfaceQualitySampleCount = 100;

    /// <summary>High curvature threshold 5× median for anomaly detection.</summary>
    internal const double HighCurvatureMultiplier = 5.0;

    /// <summary>Singularity proximity threshold 1% of domain.</summary>
    internal const double SingularityProximityFactor = 0.01;

    /// <summary>Curve fairness: 50 samples for curvature comb analysis.</summary>
    internal const int CurveFairnessSampleCount = 50;

    /// <summary>Inflection sharpness threshold 0.5 for curvature sign change.</summary>
    internal const double InflectionSharpnessThreshold = 0.5;

    /// <summary>Mesh FEA: aspect ratio warning 3.0, critical 10.0.</summary>
    internal const double AspectRatioWarning = 3.0;
    internal const double AspectRatioCritical = 10.0;

    /// <summary>Skewness warning 0.5, critical 0.85.</summary>
    internal const double SkewnessWarning = 0.5;
    internal const double SkewnessCritical = 0.85;

    /// <summary>Jacobian warning 0.3, critical 0.1.</summary>
    internal const double JacobianWarning = 0.3;
    internal const double JacobianCritical = 0.1;
}
