using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Differential geometry analysis validation modes and computation constants.</summary>
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

    /// <summary>Discontinuity buffer limit: 20 C1/C2 breaks per curve analysis.</summary>
    internal const int MaxDiscontinuities = 20;

    /// <summary>Standard derivative order: 2 for position, tangent, curvature computation.</summary>
    internal const int DefaultDerivativeOrder = 2;

    /// <summary>Frame sampling: 5 perpendicular planes at 0%, 25%, 50%, 75%, 100% curve domain.</summary>
    internal const int CurveFrameSampleCount = 5;
}
