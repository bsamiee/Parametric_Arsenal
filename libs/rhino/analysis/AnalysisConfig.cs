using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Configuration constants for analysis operations.</summary>
internal static class AnalysisConfig {
    /// <summary>Maximum number of discontinuities to detect per curve.</summary>
    internal const int MaxDiscontinuities = 20;

    /// <summary>Default derivative order for analysis operations.</summary>
    internal const int DefaultDerivativeOrder = 2;

    /// <summary>Validation mode configuration mapping geometry types to validation strategies.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy,
            [typeof(LineCurve)] = V.Standard | V.Degeneracy,
            [typeof(ArcCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard | V.SurfaceContinuity,
            [typeof(NurbsSurface)] = V.Standard | V.SurfaceContinuity,
            [typeof(PlaneSurface)] = V.Standard | V.SurfaceContinuity,
            [typeof(Brep)] = V.Standard | V.Topology | V.MassProperties,
            [typeof(Mesh)] = V.MeshSpecific,
        }.ToFrozenDictionary();
}
