using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Configuration constants and validation mode dispatch tables for orientation operations.</summary>
internal static class OrientConfig {
    /// <summary>Validation mode mappings for geometry types in orientation operations.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy,
            [typeof(LineCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(ArcCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard,
            [typeof(NurbsSurface)] = V.Standard,
            [typeof(PlaneSurface)] = V.Standard,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point)] = V.None,
            [typeof(Point3d)] = V.None,
            [typeof(PointCloud)] = V.None,
        }.ToFrozenDictionary();

    /// <summary>Tolerance defaults for degenerate checks in orientation operations.</summary>
    internal static class ToleranceDefaults {
        /// <summary>Minimum plane size to avoid degenerate plane construction.</summary>
        internal const double MinPlaneSize = 1e-6;

        /// <summary>Minimum vector length to avoid zero-length vector operations.</summary>
        internal const double MinVectorLength = 1e-8;

        /// <summary>Minimum rotation angle to avoid identity transform generation.</summary>
        internal const double MinRotationAngle = 1e-10;

        /// <summary>Minimum determinant value to validate transform invertibility.</summary>
        internal const double MinDeterminant = 1e-12;

        /// <summary>Cosine threshold for parallel vector detection (cos(0.01°) ≈ 0.99999985).</summary>
        internal const double ParallelCosineThreshold = 0.99999985;

        /// <summary>Cosine threshold for antiparallel vector detection (cos(179.99°) ≈ -0.99999985).</summary>
        internal const double AntiparallelCosineThreshold = -0.99999985;
    }
}
