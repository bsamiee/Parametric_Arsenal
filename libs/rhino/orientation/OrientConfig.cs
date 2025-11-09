using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Validation modes and thresholds for orientation transformations.</summary>
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

    /// <summary>Minimum vector magnitude 1e-8 for non-degenerate directions.</summary>
    internal const double MinVectorLength = 1e-8;
    /// <summary>Parallel detection tolerance 1e-6 for vector alignment.</summary>
    internal const double ParallelThreshold = 1e-6;
    /// <summary>Minimum 3 points required for best-fit plane.</summary>
    internal const int BestFitMinPoints = 3;
    /// <summary>Maximum 1e-3 deviation for best-fit plane residuals.</summary>
    internal const double BestFitResidualThreshold = 1e-3;
    /// <summary>Minimum 3 instances required for pattern detection.</summary>
    internal const int PatternMinInstances = 3;
    /// <summary>Optimization iteration limit for orientation search.</summary>
    internal const int OptimizationMaxIterations = 24;
    /// <summary>Symmetry test tolerance for relative orientation.</summary>
    internal const double SymmetryTestTolerance = 1e-3;
}
