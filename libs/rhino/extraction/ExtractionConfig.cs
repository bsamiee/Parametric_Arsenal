using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration for semantic extraction: type IDs, thresholds, validation modes.</summary>
internal static class ExtractionConfig {
    /// <summary>Epsilon tolerance for zero comparisons and near-zero checks.</summary>
    internal const double Epsilon = 1e-10;

    /// <summary>Feature type IDs for edge/loop classification.</summary>
    internal const byte FeatureTypeFillet = 0;
    internal const byte FeatureTypeChamfer = 1;
    internal const byte FeatureTypeHole = 2;
    internal const byte FeatureTypeGenericEdge = 3;
    internal const byte FeatureTypeVariableRadiusFillet = 4;

    /// <summary>Primitive type IDs for surface classification.</summary>
    internal const byte PrimitiveTypePlane = 0;
    internal const byte PrimitiveTypeCylinder = 1;
    internal const byte PrimitiveTypeSphere = 2;
    internal const byte PrimitiveTypeUnknown = 3;
    internal const byte PrimitiveTypeCone = 4;
    internal const byte PrimitiveTypeTorus = 5;
    internal const byte PrimitiveTypeExtrusion = 6;

    /// <summary>Pattern type IDs: 0=Linear, 1=Radial, 2=Grid, 3=Scaling.</summary>
    internal const byte PatternTypeLinear = 0;
    internal const byte PatternTypeRadial = 1;
    internal const byte PatternTypeGrid = 2;
    internal const byte PatternTypeScaling = 3;

    /// <summary>Fillet curvature variation threshold 0.15 for constant detection.</summary>
    internal const double FilletCurvatureVariationThreshold = 0.15;
    /// <summary>Fillet curvature sample count 5 for edge analysis.</summary>
    internal const int FilletCurvatureSampleCount = 5;
    /// <summary>G2 continuity angle tolerance 0.01 radians for smooth edge detection.</summary>
    internal const double G2ContinuityTolerance = 0.01;
    /// <summary>Chamfer dihedral angle range: sharp edge below 0.349 radians (20°).</summary>
    internal const double SharpEdgeAngleThreshold = 0.349;
    /// <summary>Chamfer dihedral angle range: smooth edge above 2.967 radians (170°).</summary>
    internal const double SmoothEdgeAngleThreshold = 2.967;
    /// <summary>Minimum hole polyline sides 16 for circular approximation.</summary>
    internal const int MinHolePolySides = 16;
    /// <summary>Primitive fit tolerance 0.001 for TryGet* methods.</summary>
    internal const double PrimitiveFitTolerance = 0.001;
    /// <summary>Primitive residual sample count 20 for RMS distance calculation.</summary>
    internal const int PrimitiveResidualSampleCount = 20;
    /// <summary>Pattern minimum instances 3 for detection.</summary>
    internal const int PatternMinInstances = 3;
    /// <summary>Radial pattern distance variation 0.05 relative to mean.</summary>
    internal const double RadialDistanceVariationThreshold = 0.05;
    /// <summary>Radial pattern angle variation 0.05 radians for uniform spacing.</summary>
    internal const double RadialAngleVariationThreshold = 0.05;
    /// <summary>Grid orthogonality threshold 0.1 for dot product in basis detection.</summary>
    internal const double GridOrthogonalityThreshold = 0.1;
    /// <summary>Grid point deviation tolerance 0.1 for integer coordinate validation.</summary>
    internal const double GridPointDeviationThreshold = 0.1;
    /// <summary>Scaling pattern variance threshold 0.1 for ratio consistency.</summary>
    internal const double ScalingVarianceThreshold = 0.1;
    /// <summary>(Kind, Type) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(Brep))] = V.Standard | V.MassProperties,
            [(1, typeof(Curve))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Mesh))] = V.Standard | V.MassProperties,
            [(1, typeof(Extrusion))] = V.Standard | V.MassProperties,
            [(1, typeof(SubD))] = V.Standard | V.Topology,
            [(2, typeof(GeometryBase))] = V.BoundingBox,
            [(3, typeof(GeometryBase))] = V.Standard,
            [(4, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(5, typeof(Curve))] = V.Tolerance,
            [(6, typeof(Brep))] = V.Standard | V.Topology,
            [(6, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(6, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(7, typeof(Brep))] = V.Standard | V.Topology,
            [(7, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(10, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(10, typeof(Surface))] = V.Standard,
            [(10, typeof(Brep))] = V.Standard | V.Topology,
            [(10, typeof(Extrusion))] = V.Standard | V.Topology,
            [(10, typeof(SubD))] = V.Standard | V.Topology,
            [(11, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(11, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(11, typeof(Brep))] = V.Standard | V.Topology,
            [(12, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(12, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(12, typeof(Brep))] = V.Standard | V.Topology,
            [(13, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(13, typeof(PolyCurve))] = V.Standard | V.Degeneracy,
        }.ToFrozenDictionary();

    /// <summary>Gets validation mode with fallback for (kind, type) pair.</summary>
    internal static V GetValidationMode(byte kind, Type geometryType) =>
        ValidationModes.TryGetValue((kind, geometryType), out V exact) ? exact : ValidationModes.Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType)).OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0)).Select(kv => kv.Value).DefaultIfEmpty(V.Standard).First();
}
