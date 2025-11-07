using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Configuration constants and validation mode mappings for orientation operations.</summary>
internal static class OrientConfig {
    /// <summary>Validation mode dispatch table mapping geometry types to appropriate validation flags.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy,
            [typeof(LineCurve)] = V.Standard,
            [typeof(ArcCurve)] = V.Standard,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard,
            [typeof(NurbsSurface)] = V.Standard,
            [typeof(PlaneSurface)] = V.Standard,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point)] = V.None,
            [typeof(Point3d)] = V.None,
            [typeof(PointCloud)] = V.None,
        }.ToFrozenDictionary();

    /// <summary>Tolerance threshold constants for degenerate geometry detection in orientation operations.</summary>
    internal static class ToleranceDefaults {
        internal const double MinPlaneSize = 1e-6;
        internal const double MinVectorLength = 1e-8;
        internal const double MinRotationAngle = 1e-10;
        internal const double MinDeterminant = 1e-12;
        internal const double ParallelVectorThreshold = 0.999999;
        internal const double PerpendicularVectorThreshold = 0.9;
    }
}
