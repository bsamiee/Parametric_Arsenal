using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Validation modes and thresholds for orientation.</summary>
internal static class OrientConfig {
    /// <summary>Type-to-validation mode mapping for orientation operations.</summary>
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
            [typeof(Point3d)] = V.None,
            [typeof(PointCloud)] = V.None,
        }.ToFrozenDictionary();

    /// <summary>Minimum vector magnitude 1e-8 for non-degenerate direction.</summary>
    internal const double MinVectorLength = 1e-8;
    /// <summary>Parallel detection tolerance 1e-6 for vector alignment.</summary>
    internal const double ParallelThreshold = 1e-6;
    /// <summary>Minimum 3 points for best-fit plane.</summary>
    internal const int BestFitMinPoints = 3;
    /// <summary>Maximum 1e-3 deviation for best-fit plane residuals.</summary>
    internal const double BestFitResidualThreshold = 1e-3;
    /// <summary>Minimum 3 instances for pattern detection.</summary>
    internal const int PatternMinInstances = 3;
    /// <summary>Optimization iteration limit for orientation search.</summary>
    internal const int OptimizationMaxIterations = 24;
    /// <summary>Symmetry test tolerance for relative orientation.</summary>
    internal const double SymmetryTestTolerance = 1e-3;
    /// <summary>Primary score weight 0.4 for orientation criteria 4.</summary>
    internal const double OrientationScoreWeight1 = 0.4;
    /// <summary>Secondary score weight 0.4 for orientation criteria 4.</summary>
    internal const double OrientationScoreWeight2 = 0.4;
    /// <summary>Tertiary score weight 0.2 for orientation criteria 4.</summary>
    internal const double OrientationScoreWeight3 = 0.2;
    /// <summary>Aspect ratio threshold 0.5 for low-profile detection.</summary>
    internal const double LowProfileAspectRatio = 0.5;
    /// <summary>Pattern anomaly threshold 0.5 for deviation detection.</summary>
    internal const double PatternAnomalyThreshold = 0.5;
    /// <summary>Minimum optimization score 0.1 for accepting transforms.</summary>
    internal const double MinimumOptimizationScore = 0.1;
    /// <summary>Rotation symmetry sample count 36 for 10-degree increments.</summary>
    internal const int RotationSymmetrySampleCount = 36;
    /// <summary>Symmetry angle tolerance 0.01 radians for orientation matching.</summary>
    internal const double SymmetryAngleToleranceRadians = 0.01;
}
