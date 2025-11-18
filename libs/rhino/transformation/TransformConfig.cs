using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Transform validation modes and algorithmic constants.</summary>
internal static class TransformConfig {
    /// <summary>Geometry type validation mode mapping.</summary>
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
            [typeof(PlaneSurface)] = V.Standard | V.UVDomain,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.Topology | V.ExtrusionGeometry,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Point3d)] = V.None,
            [typeof(Point3d[])] = V.None,
            [typeof(PointCloud)] = V.Standard,
        }.ToFrozenDictionary();

    /// <summary>Minimum scale factor to prevent singularity (1e-6).</summary>
    internal const double MinScaleFactor = 1e-6;
    /// <summary>Maximum scale factor to prevent overflow (1e6).</summary>
    internal const double MaxScaleFactor = 1e6;

    /// <summary>Maximum array count to prevent memory exhaustion.</summary>
    internal const int MaxArrayCount = 10000;

    /// <summary>Angular tolerance multiplier for vector parallelism checks.</summary>
    internal const double AngleToleranceMultiplier = 10.0;

    /// <summary>Maximum twist angle in radians (10 full rotations).</summary>
    internal const double MaxTwistAngle = RhinoMath.TwoPI * 10.0;
    /// <summary>Maximum bend angle in radians (full circle).</summary>
    internal const double MaxBendAngle = RhinoMath.TwoPI;

    /// <summary>Default tolerance for morph operations.</summary>
    internal const double DefaultMorphTolerance = 0.001;

    /// <summary>Get validation mode for geometry type with inheritance fallback.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static V GetValidationMode(Type geometryType) =>
        ValidationModes.TryGetValue(geometryType, out V mode)
            ? mode
            : ValidationModes
                .Where(kv => kv.Key.IsAssignableFrom(geometryType))
                .Aggregate(
                    ((V?)null, (Type?)null),
                    (best, kv) => best.Item2?.IsAssignableFrom(kv.Key) != false
                        ? (kv.Value, kv.Key)
                        : best)
                .Item1 ?? V.Standard;
}
