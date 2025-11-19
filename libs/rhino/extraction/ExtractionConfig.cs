using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Unified metadata, constants, and dispatch tables for extraction operations.</summary>
[Pure]
internal static class ExtractionConfig {
    /// <summary>Unified operation metadata for all extraction operations.</summary>
    internal sealed record ExtractionOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Point extraction mode metadata dispatch table.</summary>
    internal static readonly FrozenDictionary<(Type Mode, Type Geometry), ExtractionOperationMetadata> PointModeMetadata =
        new Dictionary<(Type, Type), ExtractionOperationMetadata> {
            [(typeof(Extraction.Analytical), typeof(Brep))] = new(V.Standard | V.MassProperties, "Extraction.Points.Analytical"),
            [(typeof(Extraction.Analytical), typeof(Curve))] = new(V.Standard | V.AreaCentroid, "Extraction.Points.Analytical"),
            [(typeof(Extraction.Analytical), typeof(Surface))] = new(V.Standard | V.AreaCentroid, "Extraction.Points.Analytical"),
            [(typeof(Extraction.Analytical), typeof(Mesh))] = new(V.Standard | V.MassProperties, "Extraction.Points.Analytical"),
            [(typeof(Extraction.Analytical), typeof(Extrusion))] = new(V.Standard | V.MassProperties, "Extraction.Points.Analytical"),
            [(typeof(Extraction.Analytical), typeof(SubD))] = new(V.Standard | V.Topology, "Extraction.Points.Analytical"),
            [(typeof(Extraction.Analytical), typeof(PointCloud))] = new(V.Standard, "Extraction.Points.Analytical"),
            [(typeof(Extraction.Extremal), typeof(GeometryBase))] = new(V.BoundingBox, "Extraction.Points.Extremal"),
            [(typeof(Extraction.Extremal), typeof(Curve))] = new(V.Standard, "Extraction.Points.Extremal"),
            [(typeof(Extraction.Extremal), typeof(Surface))] = new(V.Standard | V.UVDomain, "Extraction.Points.Extremal"),
            [(typeof(Extraction.Greville), typeof(NurbsCurve))] = new(V.Standard | V.NurbsGeometry, "Extraction.Points.Greville"),
            [(typeof(Extraction.Greville), typeof(NurbsSurface))] = new(V.Standard | V.NurbsGeometry, "Extraction.Points.Greville"),
            [(typeof(Extraction.Greville), typeof(Curve))] = new(V.Standard, "Extraction.Points.Greville"),
            [(typeof(Extraction.Greville), typeof(Surface))] = new(V.Standard, "Extraction.Points.Greville"),
            [(typeof(Extraction.Inflection), typeof(NurbsCurve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.Inflection"),
            [(typeof(Extraction.Inflection), typeof(Curve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.Inflection"),
            [(typeof(Extraction.Quadrant), typeof(Curve))] = new(V.Tolerance, "Extraction.Points.Quadrant"),
            [(typeof(Extraction.EdgeMidpoints), typeof(Brep))] = new(V.Standard | V.Topology, "Extraction.Points.EdgeMidpoints"),
            [(typeof(Extraction.EdgeMidpoints), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "Extraction.Points.EdgeMidpoints"),
            [(typeof(Extraction.EdgeMidpoints), typeof(Curve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.EdgeMidpoints"),
            [(typeof(Extraction.FaceCentroids), typeof(Brep))] = new(V.Standard | V.Topology, "Extraction.Points.FaceCentroids"),
            [(typeof(Extraction.FaceCentroids), typeof(Mesh))] = new(V.Standard | V.MeshSpecific, "Extraction.Points.FaceCentroids"),
            [(typeof(Extraction.OsculatingFrames), typeof(Curve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.OsculatingFrames"),
            [(typeof(Extraction.DivideByCount), typeof(Curve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.DivideByCount"),
            [(typeof(Extraction.DivideByCount), typeof(Surface))] = new(V.Standard | V.UVDomain, "Extraction.Points.DivideByCount"),
            [(typeof(Extraction.DivideByCount), typeof(Brep))] = new(V.Standard | V.Topology, "Extraction.Points.DivideByCount"),
            [(typeof(Extraction.DivideByCount), typeof(Extrusion))] = new(V.Standard | V.Topology, "Extraction.Points.DivideByCount"),
            [(typeof(Extraction.DivideByCount), typeof(SubD))] = new(V.Standard | V.Topology, "Extraction.Points.DivideByCount"),
            [(typeof(Extraction.DivideByLength), typeof(Curve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.DivideByLength"),
            [(typeof(Extraction.DivideByLength), typeof(Surface))] = new(V.Standard | V.AreaCentroid, "Extraction.Points.DivideByLength"),
            [(typeof(Extraction.DivideByLength), typeof(Brep))] = new(V.Standard | V.Topology, "Extraction.Points.DivideByLength"),
            [(typeof(Extraction.DirectionalExtreme), typeof(Curve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.DirectionalExtreme"),
            [(typeof(Extraction.DirectionalExtreme), typeof(Surface))] = new(V.Standard | V.AreaCentroid, "Extraction.Points.DirectionalExtreme"),
            [(typeof(Extraction.DirectionalExtreme), typeof(Brep))] = new(V.Standard | V.Topology, "Extraction.Points.DirectionalExtreme"),
            [(typeof(Extraction.DiscontinuityPoints), typeof(Curve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.DiscontinuityPoints"),
            [(typeof(Extraction.DiscontinuityPoints), typeof(PolyCurve))] = new(V.Standard | V.Degeneracy, "Extraction.Points.DiscontinuityPoints"),
        }.ToFrozenDictionary();

    /// <summary>Curve extraction mode metadata dispatch table.</summary>
    internal static readonly FrozenDictionary<(Type Mode, Type Geometry), ExtractionOperationMetadata> CurveModeMetadata =
        new Dictionary<(Type, Type), ExtractionOperationMetadata> {
            [(typeof(Extraction.Boundary), typeof(Surface))] = new(V.Standard | V.UVDomain, "Extraction.Curves.Boundary"),
            [(typeof(Extraction.Boundary), typeof(NurbsSurface))] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Extraction.Curves.Boundary"),
            [(typeof(Extraction.Boundary), typeof(Brep))] = new(V.Standard | V.Topology, "Extraction.Curves.Boundary"),
            [(typeof(Extraction.IsocurveUniform), typeof(Surface))] = new(V.Standard | V.UVDomain, "Extraction.Curves.IsocurveUniform"),
            [(typeof(Extraction.IsocurveUniform), typeof(NurbsSurface))] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Extraction.Curves.IsocurveUniform"),
            [(typeof(Extraction.IsocurveCount), typeof(Surface))] = new(V.Standard | V.UVDomain, "Extraction.Curves.IsocurveCount"),
            [(typeof(Extraction.IsocurveCount), typeof(NurbsSurface))] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Extraction.Curves.IsocurveCount"),
            [(typeof(Extraction.IsocurveParameters), typeof(Surface))] = new(V.Standard | V.UVDomain, "Extraction.Curves.IsocurveParameters"),
            [(typeof(Extraction.IsocurveParameters), typeof(NurbsSurface))] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Extraction.Curves.IsocurveParameters"),
            [(typeof(Extraction.FeatureEdges), typeof(Brep))] = new(V.Standard | V.Topology, "Extraction.Curves.FeatureEdges"),
        }.ToFrozenDictionary();

    /// <summary>Feature extraction metadata.</summary>
    internal static readonly ExtractionOperationMetadata FeatureExtractionMetadata = new(
        ValidationMode: V.Standard | V.Topology | V.BrepGranular,
        OperationName: "Extraction.DesignFeatures");

    /// <summary>Primitive decomposition metadata.</summary>
    internal static readonly ExtractionOperationMetadata PrimitiveDecompositionMetadata = new(
        ValidationMode: V.Standard | V.BoundingBox,
        OperationName: "Extraction.DecomposeToPrimitives");

    /// <summary>Pattern extraction metadata.</summary>
    internal static readonly ExtractionOperationMetadata PatternExtractionMetadata = new(
        ValidationMode: V.Standard | V.BoundingBox,
        OperationName: "Extraction.ExtractPatterns");

    /// <summary>Default fallback validation mode for point extraction.</summary>
    internal static readonly ExtractionOperationMetadata DefaultPointMetadata = new(V.Standard, "Extraction.Points");

    /// <summary>Default fallback validation mode for curve extraction.</summary>
    internal static readonly ExtractionOperationMetadata DefaultCurveMetadata = new(V.Standard, "Extraction.Curves");

    /// <summary>Fillet detection thresholds.</summary>
    internal const double FilletCurvatureVariationThreshold = 0.15;
    internal const int FilletCurvatureSampleCount = 5;

    /// <summary>Edge classification thresholds.</summary>
    internal static readonly double SharpEdgeAngleThreshold = RhinoMath.ToRadians(20.0);
    internal static readonly double SmoothEdgeAngleThreshold = RhinoMath.ToRadians(170.0);
    internal static readonly double DefaultFeatureEdgeAngleThreshold = RhinoMath.ToRadians(30.0);

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

    /// <summary>Get metadata for point extraction mode with geometry type fallback.</summary>
    internal static ExtractionOperationMetadata GetPointMetadata(Type modeType, Type geometryType) =>
        PointModeMetadata.TryGetValue((modeType, geometryType), out ExtractionOperationMetadata? exact) ? exact
            : PointModeMetadata.Where(kv => kv.Key.Mode == modeType && kv.Key.Geometry.IsAssignableFrom(geometryType))
                .OrderByDescending(static kv => kv.Key.Geometry, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(static kv => kv.Value)
                .DefaultIfEmpty(DefaultPointMetadata)
                .First();

    /// <summary>Get metadata for curve extraction mode with geometry type fallback.</summary>
    internal static ExtractionOperationMetadata GetCurveMetadata(Type modeType, Type geometryType) =>
        CurveModeMetadata.TryGetValue((modeType, geometryType), out ExtractionOperationMetadata? exact) ? exact
            : CurveModeMetadata.Where(kv => kv.Key.Mode == modeType && kv.Key.Geometry.IsAssignableFrom(geometryType))
                .OrderByDescending(static kv => kv.Key.Geometry, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(static kv => kv.Value)
                .DefaultIfEmpty(DefaultCurveMetadata)
                .First();
}
