using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;
using RhinoTransform = Rhino.Geometry.Transform;

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

    /// <summary>Tolerance and threshold constants for orientation analysis.</summary>
    internal const double BestFitResidualThreshold = 1e-3;
    internal const double LowProfileAspectRatio = 0.5;
    internal const double PatternAnomalyThreshold = 0.5;

    /// <summary>Canonical positioning score weights for optimization.</summary>
    internal const double OrientationScoreWeight1 = 0.4;
    internal const double OrientationScoreWeight2 = 0.4;
    internal const double OrientationScoreWeight3 = 0.2;

    /// <summary>Count and size thresholds for orientation operations.</summary>
    internal const int BestFitMinPoints = 3;
    internal const int PatternMinInstances = 3;
    internal const int RotationSymmetrySampleCount = 36;
    internal const int MaxDegeneracyDimensions = 3;
}
