using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration constants for semantic extraction: type identifiers, detection thresholds, and validation mode mappings.</summary>
internal static class ExtractionConfig {
    /// <summary>Classification type enumeration for unified dispatch.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte is required for compatibility with existing API")]
    internal enum ClassificationType : byte {
        FeatureFillet = 0,
        FeatureChamfer = 1,
        FeatureHole = 2,
        FeatureGenericEdge = 3,
        FeatureVariableRadiusFillet = 4,
        PrimitivePlane = 10,
        PrimitiveCylinder = 11,
        PrimitiveSphere = 12,
        PrimitiveUnknown = 13,
        PrimitiveCone = 14,
        PrimitiveTorus = 15,
        PrimitiveExtrusion = 16,
        PatternLinear = 20,
        PatternRadial = 21,
        PatternGrid = 22,
        PatternScaling = 23,
    }

    /// <summary>Epsilon tolerance for zero comparisons and near-zero checks.</summary>
    internal const double Epsilon = 1e-10;

    /// <summary>Edge classification thresholds (curvature variation, sample count, angle thresholds).</summary>
    internal static readonly FrozenDictionary<string, double> EdgeThresholds =
        new Dictionary<string, double>(StringComparer.Ordinal) {
            ["FilletCurvatureVariation"] = 0.15,
            ["FilletCurvatureSampleCount"] = 5,
            ["G2ContinuityTolerance"] = 0.01,
            ["SharpEdgeAngle"] = 0.349,
            ["SmoothEdgeAngle"] = 2.967,
            ["MinHolePolySides"] = 16,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Primitive fitting and analysis thresholds.</summary>
    internal static readonly FrozenDictionary<string, double> PrimitiveThresholds =
        new Dictionary<string, double>(StringComparer.Ordinal) {
            ["FitTolerance"] = 0.001,
            ["ResidualSampleCount"] = 20,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Pattern detection thresholds for all pattern types.</summary>
    internal static readonly FrozenDictionary<string, double> PatternThresholds =
        new Dictionary<string, double>(StringComparer.Ordinal) {
            ["MinInstances"] = 3,
            ["RadialDistanceVariation"] = 0.05,
            ["RadialAngleVariation"] = 0.05,
            ["GridOrthogonality"] = 0.1,
            ["GridPointDeviation"] = 0.1,
            ["ScalingVariance"] = 0.1,
        }.ToFrozenDictionary(StringComparer.Ordinal);
    /// <summary>(Kind, Type) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(Brep))] = V.Standard | V.MassProperties,
            [(1, typeof(Curve))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Mesh))] = V.Standard | V.MassProperties,
            [(2, typeof(GeometryBase))] = V.BoundingBox,
            [(3, typeof(GeometryBase))] = V.Standard,
            [(4, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(5, typeof(Curve))] = V.Tolerance,
            [(6, typeof(Brep))] = V.Standard | V.Topology,
            [(6, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(7, typeof(Brep))] = V.Standard | V.Topology,
            [(7, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(10, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(10, typeof(Surface))] = V.Standard,
            [(11, typeof(Curve))] = V.Standard | V.Degeneracy,
        }.ToFrozenDictionary();

    /// <summary>Gets validation mode with inheritance fallback for (kind, type) pair.</summary>
    internal static V GetValidationMode(byte kind, Type geometryType) =>
        ValidationModes.TryGetValue((kind, geometryType), out V exact) ? exact : ValidationModes.Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType)).OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0)).Select(kv => kv.Value).DefaultIfEmpty(V.Standard).First();
}
