using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Configuration constants and validation dispatch for analysis operations.</summary>
internal static class AnalysisConfig {
    internal const int MaxDiscontinuities = 20;

    /// <summary>Validation mode mapping for geometry types in analysis operations.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard | V.SurfaceContinuity,
            [typeof(NurbsSurface)] = V.Standard | V.SurfaceContinuity,
            [typeof(Brep)] = V.Standard | V.Topology | V.MassProperties,
            [typeof(Mesh)] = V.MeshSpecific,
        }.ToFrozenDictionary();
}
