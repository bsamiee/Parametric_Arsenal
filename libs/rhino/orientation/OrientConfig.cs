using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Configuration constants and validation mode mappings for orientation operations.</summary>
internal static class OrientConfig {
    /// <summary>Type-based validation mode dispatch for geometry-specific checks.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy,
            [typeof(LineCurve)] = V.Standard,
            [typeof(PolylineCurve)] = V.Standard,
            [typeof(ArcCurve)] = V.Standard,
            [typeof(Surface)] = V.Standard,
            [typeof(NurbsSurface)] = V.Standard,
            [typeof(PlaneSurface)] = V.Standard,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point)] = V.None,
            [typeof(PointCloud)] = V.None,
            [typeof(Extrusion)] = V.Standard,
            [typeof(SubD)] = V.Standard,
        }.ToFrozenDictionary();

    /// <summary>Tolerance thresholds for degenerate geometry detection.</summary>
    internal static class ToleranceDefaults {
        /// <summary>Minimum plane size for valid plane extraction.</summary>
        internal const double MinPlaneSize = 1e-6;

        /// <summary>Minimum vector length for valid direction extraction.</summary>
        internal const double MinVectorLength = 1e-8;

        /// <summary>Minimum rotation angle for transform validity.</summary>
        internal const double MinRotationAngle = 1e-10;

        /// <summary>Minimum determinant value for valid transform.</summary>
        internal const double MinDeterminant = 1e-12;

        /// <summary>Parallel vector angle threshold in radians.</summary>
        internal const double ParallelAngleThreshold = 1e-6;
    }
}
