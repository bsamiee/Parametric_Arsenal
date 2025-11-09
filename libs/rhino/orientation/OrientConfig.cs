using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Orientation transformation validation modes and vector/alignment thresholds.</summary>
internal static class OrientConfig {
    /// <summary>Validation mode mapping for orientation geometry types.</summary>
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

    /// <summary>Minimum vector magnitude: 1e-8 for non-degenerate direction vectors.</summary>
    internal const double MinVectorLength = 1e-8;
    /// <summary>Parallel detection: 1e-6 tolerance for vector alignment detection.</summary>
    internal const double ParallelThreshold = 1e-6;
    /// <summary>Minimum point count for best-fit plane: 3 points required.</summary>
    internal const int BestFitMinPoints = 3;
    /// <summary>Best-fit residual threshold: 1e-3 maximum allowed deviation.</summary>
    internal const double BestFitResidualThreshold = 1e-3;
}
