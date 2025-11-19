using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration for extraction operations: metadata records, dispatch tables, thresholds.</summary>
internal static class ExtractionConfig {
    // ═══════════════════════════════════════════════════════════════════════════════
    // Metadata Records
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Unified operation metadata for extraction operations.</summary>
    internal sealed record ExtractionOperationMetadata(
        V ValidationMode,
        string OperationName);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Point Operation Metadata Tables
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base validation modes per point operation type (geometry-independent).</summary>
    internal static readonly FrozenDictionary<Type, ExtractionOperationMetadata> PointOperations =
        new Dictionary<Type, ExtractionOperationMetadata> {
            [typeof(Extraction.Analytical)] = new(V.Standard | V.MassProperties, "Extraction.Analytical"),
            [typeof(Extraction.Extremal)] = new(V.BoundingBox, "Extraction.Extremal"),
            [typeof(Extraction.Greville)] = new(V.Standard, "Extraction.Greville"),
            [typeof(Extraction.Inflection)] = new(V.Standard | V.Degeneracy, "Extraction.Inflection"),
            [typeof(Extraction.Quadrant)] = new(V.Tolerance, "Extraction.Quadrant"),
            [typeof(Extraction.EdgeMidpoints)] = new(V.Standard | V.Topology, "Extraction.EdgeMidpoints"),
            [typeof(Extraction.FaceCentroids)] = new(V.Standard | V.Topology, "Extraction.FaceCentroids"),
            [typeof(Extraction.OsculatingFrames)] = new(V.Standard | V.Degeneracy, "Extraction.OsculatingFrames"),
            [typeof(Extraction.ByCount)] = new(V.Standard | V.Degeneracy, "Extraction.ByCount"),
            [typeof(Extraction.ByLength)] = new(V.Standard | V.Degeneracy, "Extraction.ByLength"),
            [typeof(Extraction.ByDirection)] = new(V.Standard | V.Degeneracy, "Extraction.ByDirection"),
            [typeof(Extraction.ByContinuity)] = new(V.Standard | V.Degeneracy, "Extraction.ByContinuity"),
        }.ToFrozenDictionary();

    /// <summary>Geometry-specific validation mode overrides for point operations.</summary>
    internal static readonly FrozenDictionary<(Type Operation, Type Geometry), V> PointValidationOverrides =
        new Dictionary<(Type, Type), V> {
            [(typeof(Extraction.Analytical), typeof(Brep))] = V.Standard | V.MassProperties,
            [(typeof(Extraction.Analytical), typeof(Curve))] = V.Standard | V.AreaCentroid,
            [(typeof(Extraction.Analytical), typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(typeof(Extraction.Analytical), typeof(Mesh))] = V.Standard | V.MassProperties,
            [(typeof(Extraction.Analytical), typeof(Extrusion))] = V.Standard | V.MassProperties,
            [(typeof(Extraction.Analytical), typeof(SubD))] = V.Standard | V.Topology,
            // EdgeMidpoints
            [(typeof(Extraction.EdgeMidpoints), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Extraction.EdgeMidpoints), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(typeof(Extraction.EdgeMidpoints), typeof(Curve))] = V.Standard | V.Degeneracy,
            // FaceCentroids
            [(typeof(Extraction.FaceCentroids), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Extraction.FaceCentroids), typeof(Mesh))] = V.Standard | V.MeshSpecific,
            // ByCount
            [(typeof(Extraction.ByCount), typeof(Curve))] = V.Standard | V.Degeneracy,
            [(typeof(Extraction.ByCount), typeof(Surface))] = V.Standard,
            [(typeof(Extraction.ByCount), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Extraction.ByCount), typeof(Extrusion))] = V.Standard | V.Topology,
            [(typeof(Extraction.ByCount), typeof(SubD))] = V.Standard | V.Topology,
            // ByLength
            [(typeof(Extraction.ByLength), typeof(Curve))] = V.Standard | V.Degeneracy,
            [(typeof(Extraction.ByLength), typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(typeof(Extraction.ByLength), typeof(Brep))] = V.Standard | V.Topology,
            // ByDirection
            [(typeof(Extraction.ByDirection), typeof(Curve))] = V.Standard | V.Degeneracy,
            [(typeof(Extraction.ByDirection), typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(typeof(Extraction.ByDirection), typeof(Brep))] = V.Standard | V.Topology,
        }.ToFrozenDictionary();

    // ═══════════════════════════════════════════════════════════════════════════════
    // Curve Operation Metadata Tables
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base validation modes per curve operation type.</summary>
    internal static readonly FrozenDictionary<Type, ExtractionOperationMetadata> CurveOperations =
        new Dictionary<Type, ExtractionOperationMetadata> {
            [typeof(Extraction.Boundary)] = new(V.Standard | V.UVDomain, "Extraction.Boundary"),
            [typeof(Extraction.FeatureEdges)] = new(V.Standard | V.Topology, "Extraction.FeatureEdges"),
            [typeof(Extraction.IsocurveCount)] = new(V.Standard | V.UVDomain, "Extraction.IsocurveCount"),
            [typeof(Extraction.IsocurveParams)] = new(V.Standard | V.UVDomain, "Extraction.IsocurveParams"),
        }.ToFrozenDictionary();

    /// <summary>Geometry-specific validation mode overrides for curve operations.</summary>
    internal static readonly FrozenDictionary<(Type Operation, Type Geometry), V> CurveValidationOverrides =
        new Dictionary<(Type, Type), V> {
            // Boundary
            [(typeof(Extraction.Boundary), typeof(Surface))] = V.Standard | V.UVDomain,
            [(typeof(Extraction.Boundary), typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(typeof(Extraction.Boundary), typeof(Brep))] = V.Standard | V.Topology,
            // IsocurveCount
            [(typeof(Extraction.IsocurveCount), typeof(Surface))] = V.Standard | V.UVDomain,
            [(typeof(Extraction.IsocurveCount), typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            // IsocurveParams
            [(typeof(Extraction.IsocurveParams), typeof(Surface))] = V.Standard | V.UVDomain,
            [(typeof(Extraction.IsocurveParams), typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
        }.ToFrozenDictionary();

    // ═══════════════════════════════════════════════════════════════════════════════
    // Analysis Operation Metadata
    // ═══════════════════════════════════════════════════════════════════════════════

    internal static readonly ExtractionOperationMetadata FeatureExtractionMetadata = new(
        ValidationMode: V.Standard | V.Topology | V.BrepGranular,
        OperationName: "Extraction.DesignFeatures");

    internal static readonly ExtractionOperationMetadata PrimitiveDecompositionMetadata = new(
        ValidationMode: V.Standard | V.BoundingBox,
        OperationName: "Extraction.PrimitiveDecomposition");

    internal static readonly ExtractionOperationMetadata PatternExtractionMetadata = new(
        ValidationMode: V.Standard | V.BoundingBox,
        OperationName: "Extraction.PatternExtraction");

    // ═══════════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Gets the validation mode for a point operation and geometry type combination.</summary>
    internal static V GetPointValidationMode(Type operationType, Type geometryType) =>
        PointValidationOverrides.TryGetValue((operationType, geometryType), out V mode)
            ? mode
            : PointOperations.TryGetValue(operationType, out ExtractionOperationMetadata? meta)
                ? meta.ValidationMode
                : V.Standard;

    /// <summary>Gets the validation mode for a curve operation and geometry type combination.</summary>
    internal static V GetCurveValidationMode(Type operationType, Type geometryType) =>
        CurveValidationOverrides.TryGetValue((operationType, geometryType), out V mode)
            ? mode
            : CurveOperations.TryGetValue(operationType, out ExtractionOperationMetadata? meta)
                ? meta.ValidationMode
                : V.Standard;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Feature Detection Thresholds
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Coefficient of variation threshold for fillet detection.</summary>
    internal const double FilletCurvatureVariationThreshold = 0.15;

    /// <summary>Number of samples for curvature analysis along edges.</summary>
    internal const int FilletCurvatureSampleCount = 5;

    /// <summary>Angle threshold for sharp edge classification (radians).</summary>
    internal static readonly double SharpEdgeAngleThreshold = RhinoMath.ToRadians(20.0);

    /// <summary>Angle threshold for smooth edge classification (radians).</summary>
    internal static readonly double SmoothEdgeAngleThreshold = RhinoMath.ToRadians(170.0);

    /// <summary>Default angle threshold for feature edge extraction (radians).</summary>
    internal static readonly double FeatureEdgeAngleThreshold = RhinoMath.ToRadians(30.0);

    /// <summary>Minimum polygon sides to classify as hole.</summary>
    internal const int MinHolePolySides = 16;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Primitive Decomposition Parameters
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Number of samples for residual computation.</summary>
    internal const int PrimitiveResidualSampleCount = 20;

    /// <summary>Number of samples for curvature analysis.</summary>
    internal const int CurvatureSampleCount = 16;

    /// <summary>Minimum valid curvature samples required.</summary>
    internal const int MinCurvatureSamples = 4;

    /// <summary>Threshold for curvature constancy detection.</summary>
    internal const double CurvatureVariationThreshold = 0.05;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Pattern Detection Parameters
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Minimum instances required for pattern detection.</summary>
    internal const int PatternMinInstances = 3;

    /// <summary>Threshold for radial distance variation.</summary>
    internal const double RadialDistanceVariationThreshold = 0.05;

    /// <summary>Threshold for radial angle variation.</summary>
    internal const double RadialAngleVariationThreshold = 0.05;

    /// <summary>Threshold for grid orthogonality.</summary>
    internal const double GridOrthogonalityThreshold = 0.1;

    /// <summary>Threshold for grid point deviation.</summary>
    internal const double GridPointDeviationThreshold = 0.1;

    /// <summary>Threshold for scaling pattern variance.</summary>
    internal const double ScalingVarianceThreshold = 0.1;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Extraction Limits
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Minimum isocurve count.</summary>
    internal const int MinIsocurveCount = 2;

    /// <summary>Maximum isocurve count.</summary>
    internal const int MaxIsocurveCount = 100;

    /// <summary>Default osculating frame count.</summary>
    internal const int DefaultOsculatingFrameCount = 10;

    /// <summary>Boundary isocurve count for semantic extraction.</summary>
    internal const int BoundaryIsocurveCount = 5;
}
