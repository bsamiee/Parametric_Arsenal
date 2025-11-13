using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Validation modes, thresholds, and configuration for orientation operations.</summary>
[Pure]
internal static class OrientConfig {
    /// <summary>Type-specific validation mode dispatch for orientation operations.</summary>
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

    /// <summary>Minimum vector length 1e-8 for non-degenerate direction checks.</summary>
    internal const double MinVectorLength = 1e-8;
    /// <summary>Parallel threshold 1e-6 for vector alignment detection.</summary>
    internal const double ParallelThreshold = 1e-6;
    /// <summary>Minimum 3 points required for best-fit plane computation.</summary>
    internal const int BestFitMinPoints = 3;
    /// <summary>Maximum 1e-3 RMS deviation for acceptable best-fit plane quality.</summary>
    internal const double BestFitResidualThreshold = 1e-3;
    /// <summary>Minimum 3 instances required for pattern detection algorithms.</summary>
    internal const int PatternMinInstances = 3;
    /// <summary>Symmetry tolerance 1e-3 for relative orientation analysis.</summary>
    internal const double SymmetryTestTolerance = 1e-3;
    /// <summary>Weight 0.4 for ground contact score in canonical positioning.</summary>
    internal const double OrientationScoreWeight1 = 0.4;
    /// <summary>Weight 0.4 for origin alignment score in canonical positioning.</summary>
    internal const double OrientationScoreWeight2 = 0.4;
    /// <summary>Weight 0.2 for low-profile score in canonical positioning.</summary>
    internal const double OrientationScoreWeight3 = 0.2;
    /// <summary>Aspect ratio 0.5 threshold for low-profile geometry detection.</summary>
    internal const double LowProfileAspectRatio = 0.5;
    /// <summary>Anomaly threshold 0.5 for pattern deviation classification.</summary>
    internal const double PatternAnomalyThreshold = 0.5;
    /// <summary>Sample count 36 for 10-degree rotation symmetry testing.</summary>
    internal const int RotationSymmetrySampleCount = 36;
    /// <summary>Maximum 3 degenerate dimensions for flatness scoring.</summary>
    internal const int MaxDegeneracyDimensions = 3;
}
