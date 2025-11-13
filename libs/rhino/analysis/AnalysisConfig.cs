using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
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

    /// <summary>Maximum discontinuity count per curve for C1/C2 detection.</summary>
    internal const int MaxDiscontinuities = 20;
    /// <summary>Default derivative order for position, tangent, and curvature computation.</summary>
    internal const int DefaultDerivativeOrder = 2;
    /// <summary>Number of perpendicular frames sampled along curve domain.</summary>
    internal const int CurveFrameSampleCount = 5;
    /// <summary>Sample count for curve fairness curvature comb analysis.</summary>
    internal const int CurveFairnessSampleCount = 50;

    /// <summary>Grid dimension for surface quality UV sampling (10×10).</summary>
    internal const int SurfaceQualityGridDimension = 10;
    /// <summary>Total surface quality sample count derived from grid dimensions.</summary>
    internal const int SurfaceQualitySampleCount = SurfaceQualityGridDimension * SurfaceQualityGridDimension;

    /// <summary>Domain fraction defining boundary proximity for singularity detection.</summary>
    internal const double SingularityBoundaryFraction = 0.1;
    /// <summary>Singularity proximity threshold as domain fraction.</summary>
    internal const double SingularityProximityFactor = 0.01;
    /// <summary>High curvature threshold as multiplier of median for anomaly detection.</summary>
    internal const double HighCurvatureMultiplier = 5.0;
    /// <summary>Threshold for detecting sharp inflection points via curvature change.</summary>
    internal const double InflectionSharpnessThreshold = 0.5;
    /// <summary>Sensitivity factor for smoothness scoring via curvature variation.</summary>
    internal const double SmoothnessSensitivity = 10.0;
    /// <summary>Brep closest point tolerance multiplier relative to context.</summary>
    internal const double BrepClosestPointToleranceMultiplier = 100.0;

    /// <summary>Mesh FEA quality thresholds.</summary>
    internal const double AspectRatioWarning = 3.0;
    internal const double AspectRatioCritical = 10.0;
    internal const double SkewnessWarning = 0.5;
    internal const double SkewnessCritical = 0.85;
    internal const double JacobianWarning = 0.3;
    internal const double JacobianCritical = 0.1;

    /// <summary>Ideal interior angles for element types.</summary>
    internal const double TriangleIdealAngleDegrees = 60.0;
    internal static readonly double QuadIdealAngleDegrees = RhinoMath.ToDegrees(RhinoMath.HalfPI);
}
