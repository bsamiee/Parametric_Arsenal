using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Configuration constants and validation dispatch for analysis operations.</summary>
internal static class AnalysisConfig {
    /// <summary>Validation mode mapping for analysis geometry types.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy,
            [typeof(LineCurve)] = V.Standard | V.Degeneracy,
            [typeof(ArcCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard,
            [typeof(NurbsSurface)] = V.Standard,
            [typeof(PlaneSurface)] = V.Standard,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Maximum number of discontinuities to track in curve analysis.</summary>
    internal const int MaxDiscontinuities = 20;

    /// <summary>Default derivative order for analysis operations.</summary>
    internal const int DefaultDerivativeOrder = 2;

    /// <summary>Curve frame sample count for perpendicular frame extraction.</summary>
    internal const int CurveFrameSampleCount = 5;
}
