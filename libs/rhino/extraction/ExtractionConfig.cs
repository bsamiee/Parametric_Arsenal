using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Unified metadata, constants, and dispatch tables for extraction operations.</summary>
[Pure]
internal static class ExtractionConfig {
    /// <summary>Point operation validation modes.</summary>
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
            [typeof(Extraction.Discontinuity)] = new(V.Standard | V.Degeneracy, "Extraction.Discontinuity"),
        }.ToFrozenDictionary();

    /// <summary>Curve operation validation modes.</summary>
    internal static readonly FrozenDictionary<Type, ExtractionOperationMetadata> CurveOperations =
        new Dictionary<Type, ExtractionOperationMetadata> {
            [typeof(Extraction.Boundary)] = new(V.Standard | V.UVDomain, "Extraction.Boundary"),
            [typeof(Extraction.Isocurves)] = new(V.Standard | V.UVDomain, "Extraction.Isocurves"),
            [typeof(Extraction.IsocurvesAt)] = new(V.Standard | V.UVDomain, "Extraction.IsocurvesAt"),
            [typeof(Extraction.FeatureEdges)] = new(V.Standard | V.Topology, "Extraction.FeatureEdges"),
        }.ToFrozenDictionary();

    /// <summary>Geometry-specific validation mode refinements.</summary>
    internal static readonly FrozenDictionary<Type, V> GeometryValidation =
        new Dictionary<Type, V> {
            [typeof(Brep)] = V.Topology,
            [typeof(Mesh)] = V.MeshSpecific,
            [typeof(Curve)] = V.Degeneracy,
            [typeof(NurbsCurve)] = V.NurbsGeometry,
            [typeof(Surface)] = V.UVDomain,
            [typeof(NurbsSurface)] = V.NurbsGeometry | V.UVDomain,
            [typeof(PolyCurve)] = V.Degeneracy,
            [typeof(Extrusion)] = V.Topology,
            [typeof(SubD)] = V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Feature extraction metadata.</summary>
    internal static readonly ExtractionOperationMetadata FeatureMetadata = new(
        ValidationMode: V.Standard | V.Topology | V.BrepGranular,
        OperationName: "Extraction.Features");

    /// <summary>Primitive decomposition metadata.</summary>
    internal static readonly ExtractionOperationMetadata PrimitiveMetadata = new(
        ValidationMode: V.Standard | V.BoundingBox,
        OperationName: "Extraction.Primitives");

    /// <summary>Pattern detection metadata.</summary>
    internal static readonly ExtractionOperationMetadata PatternMetadata = new(
        ValidationMode: V.Standard | V.BoundingBox,
        OperationName: "Extraction.Patterns");

    /// <summary>Edge classification thresholds.</summary>
    internal static readonly double SharpEdgeAngleThreshold = RhinoMath.ToRadians(20.0);
    internal static readonly double SmoothEdgeAngleThreshold = RhinoMath.ToRadians(170.0);
    internal static readonly double FeatureEdgeAngleThreshold = RhinoMath.ToRadians(30.0);

    /// <summary>Fillet detection thresholds.</summary>
    internal const double FilletCurvatureVariationThreshold = 0.15;
    internal const int FilletCurvatureSampleCount = 5;

    /// <summary>Hole detection parameters.</summary>
    internal const int MinHolePolySides = 16;

    /// <summary>Primitive decomposition parameters.</summary>
    internal const int PrimitiveResidualSampleCount = 20;
    internal const int CurvatureSampleCount = 16;
    internal const int MinCurvatureSamples = 4;
    internal const double CurvatureVariationThreshold = 0.05;

    /// <summary>Pattern detection parameters.</summary>
    internal const int PatternMinInstances = 3;
    internal const double RadialDistanceVariationThreshold = 0.05;
    internal const double RadialAngleVariationThreshold = 0.05;
    internal const double GridOrthogonalityThreshold = 0.1;
    internal const double GridPointDeviationThreshold = 0.1;
    internal const double ScalingVarianceThreshold = 0.1;

    /// <summary>Extraction limits.</summary>
    internal const int MinIsocurveCount = 2;
    internal const int MaxIsocurveCount = 100;
    internal const int DefaultOsculatingFrameCount = 10;
    internal const int BoundaryIsocurveCount = 5;

    /// <summary>Unified operation metadata for all extraction transforms.</summary>
    internal sealed record ExtractionOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Gets validation mode with geometry-specific refinements.</summary>
    internal static V GetValidationMode(Type _, Type geometryType, V baseMode) =>
        GeometryValidation.TryGetValue(geometryType, out V refinement)
            ? baseMode | refinement
            : baseMode;
}
