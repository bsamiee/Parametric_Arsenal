using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Transform validation modes and algorithmic constants.</summary>
internal static class TransformationConfig {
    /// <summary>Unified transformation operation dispatch table: (GeometryType, OperationType) → metadata.</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, string OperationType), TransformMetadata> Operations =
        new Dictionary<(Type, string), TransformMetadata> {
            [(typeof(GeometryBase), "UniformScale")] = new(V.Standard, "Transformation.UniformScale"),
            [(typeof(GeometryBase), "NonUniformScale")] = new(V.Standard, "Transformation.NonUniformScale"),
            [(typeof(GeometryBase), "Rotation")] = new(V.Standard, "Transformation.Rotation"),
            [(typeof(GeometryBase), "Mirror")] = new(V.Standard, "Transformation.Mirror"),
            [(typeof(GeometryBase), "Translation")] = new(V.None, "Transformation.Translation"),
            [(typeof(GeometryBase), "Shear")] = new(V.Standard, "Transformation.Shear"),
            [(typeof(GeometryBase), "Projection")] = new(V.Standard, "Transformation.Projection"),
            [(typeof(GeometryBase), "ChangeBasis")] = new(V.Standard, "Transformation.ChangeBasis"),
            [(typeof(GeometryBase), "PlaneToPlane")] = new(V.Standard, "Transformation.PlaneToPlane"),
            [(typeof(GeometryBase), "RectangularArray")] = new(V.Standard, "Transformation.RectangularArray"),
            [(typeof(GeometryBase), "PolarArray")] = new(V.Standard, "Transformation.PolarArray"),
            [(typeof(GeometryBase), "LinearArray")] = new(V.Standard, "Transformation.LinearArray"),
            [(typeof(GeometryBase), "PathArray")] = new(V.Standard, "Transformation.PathArray"),
            [(typeof(GeometryBase), "Flow")] = new(V.Standard, "Transformation.Flow"),
            [(typeof(GeometryBase), "Twist")] = new(V.Standard, "Transformation.Twist"),
            [(typeof(GeometryBase), "Bend")] = new(V.Standard, "Transformation.Bend"),
            [(typeof(GeometryBase), "Taper")] = new(V.Standard, "Transformation.Taper"),
            [(typeof(GeometryBase), "Stretch")] = new(V.Standard, "Transformation.Stretch"),
            [(typeof(GeometryBase), "Splop")] = new(V.Standard, "Transformation.Splop"),
            [(typeof(GeometryBase), "Sporph")] = new(V.Standard, "Transformation.Sporph"),
            [(typeof(GeometryBase), "Maelstrom")] = new(V.Standard, "Transformation.Maelstrom"),
        }.ToFrozenDictionary();

    /// <summary>Transformation operation metadata containing validation mode and operation name.</summary>
    internal sealed record TransformMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Geometry type validation mode mapping (backward compatibility).</summary>
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

    /// <summary>Minimum scale factor to prevent singularity.</summary>
    internal const double MinScaleFactor = 1e-6;
    /// <summary>Maximum scale factor to prevent overflow.</summary>
    internal const double MaxScaleFactor = 1e6;

    /// <summary>Maximum array count to prevent memory exhaustion.</summary>
    internal const int MaxArrayCount = 10000;

    /// <summary>Angular tolerance multiplier for vector parallelism checks.</summary>
    internal const double AngleToleranceMultiplier = 10.0;

    /// <summary>Maximum twist angle in radians.</summary>
    internal const double MaxTwistAngle = RhinoMath.TwoPI * 10.0;
    /// <summary>Maximum bend angle in radians.</summary>
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
