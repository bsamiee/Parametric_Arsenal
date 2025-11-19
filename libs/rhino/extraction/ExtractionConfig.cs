using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration for extraction operations: validation metadata, thresholds, and algorithm parameters.</summary>
internal static class ExtractionConfig {
    private static readonly IComparer<Type> SpecificityComparer = Comparer<Type>.Create(static (a, b) =>
        a == b ? 0 : a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0);

    private static readonly (Extraction.PointOperationKind Operation, Type Geometry, V Mode)[] PointValidationEntries = [
        (Extraction.PointOperationKind.Analytical, typeof(Brep), V.Standard | V.MassProperties),
        (Extraction.PointOperationKind.Analytical, typeof(Curve), V.Standard | V.AreaCentroid),
        (Extraction.PointOperationKind.Analytical, typeof(Surface), V.Standard | V.AreaCentroid),
        (Extraction.PointOperationKind.Analytical, typeof(Mesh), V.Standard | V.MassProperties),
        (Extraction.PointOperationKind.Analytical, typeof(Extrusion), V.Standard | V.MassProperties),
        (Extraction.PointOperationKind.Analytical, typeof(SubD), V.Standard | V.Topology),
        (Extraction.PointOperationKind.Extremal, typeof(GeometryBase), V.BoundingBox),
        (Extraction.PointOperationKind.Greville, typeof(GeometryBase), V.Standard),
        (Extraction.PointOperationKind.Inflection, typeof(Curve), V.Standard | V.Degeneracy),
        (Extraction.PointOperationKind.Quadrant, typeof(Curve), V.Tolerance),
        (Extraction.PointOperationKind.EdgeMidpoints, typeof(Brep), V.Standard | V.Topology),
        (Extraction.PointOperationKind.EdgeMidpoints, typeof(Mesh), V.Standard | V.MeshSpecific),
        (Extraction.PointOperationKind.EdgeMidpoints, typeof(Curve), V.Standard | V.Degeneracy),
        (Extraction.PointOperationKind.FaceCentroids, typeof(Brep), V.Standard | V.Topology),
        (Extraction.PointOperationKind.FaceCentroids, typeof(Mesh), V.Standard | V.MeshSpecific),
        (Extraction.PointOperationKind.OsculatingFrames, typeof(Curve), V.Standard | V.Degeneracy),
        (Extraction.PointOperationKind.DivideByCount, typeof(Curve), V.Standard | V.Degeneracy),
        (Extraction.PointOperationKind.DivideByCount, typeof(Surface), V.Standard),
        (Extraction.PointOperationKind.DivideByCount, typeof(Brep), V.Standard | V.Topology),
        (Extraction.PointOperationKind.DivideByCount, typeof(Extrusion), V.Standard | V.Topology),
        (Extraction.PointOperationKind.DivideByCount, typeof(SubD), V.Standard | V.Topology),
        (Extraction.PointOperationKind.DivideByLength, typeof(Curve), V.Standard | V.Degeneracy),
        (Extraction.PointOperationKind.DivideByLength, typeof(Surface), V.Standard | V.AreaCentroid),
        (Extraction.PointOperationKind.DivideByLength, typeof(Brep), V.Standard | V.Topology),
        (Extraction.PointOperationKind.DirectionalExtrema, typeof(Curve), V.Standard | V.Degeneracy),
        (Extraction.PointOperationKind.DirectionalExtrema, typeof(Surface), V.Standard | V.AreaCentroid),
        (Extraction.PointOperationKind.DirectionalExtrema, typeof(Brep), V.Standard | V.Topology),
        (Extraction.PointOperationKind.Discontinuities, typeof(Curve), V.Standard | V.Degeneracy),
        (Extraction.PointOperationKind.Discontinuities, typeof(PolyCurve), V.Standard | V.Degeneracy),
    ];

    private static readonly (Extraction.CurveOperationKind Operation, Type Geometry, V Mode)[] CurveValidationEntries = [
        (Extraction.CurveOperationKind.Boundary, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.Boundary, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.Boundary, typeof(Brep), V.Standard | V.Topology),
        (Extraction.CurveOperationKind.IsocurveU, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.IsocurveU, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.IsocurveV, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.IsocurveV, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.IsocurveUV, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.IsocurveUV, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.FeatureEdges, typeof(Brep), V.Standard | V.Topology),
        (Extraction.CurveOperationKind.UniformIsocurves, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.UniformIsocurves, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.DirectionalIsocurves, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.DirectionalIsocurves, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.ParameterIsocurves, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.ParameterIsocurves, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.ParameterDirectionalIsocurves, typeof(Surface), V.Standard | V.UVDomain),
        (Extraction.CurveOperationKind.ParameterDirectionalIsocurves, typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain),
        (Extraction.CurveOperationKind.CustomFeatureEdges, typeof(Brep), V.Standard | V.Topology),
    ];

    internal static readonly FrozenDictionary<Extraction.PointOperationKind, OperationMetadata> PointOperations = BuildPointMetadata();
    internal static readonly FrozenDictionary<Extraction.CurveOperationKind, OperationMetadata> CurveOperations = BuildCurveMetadata();

    internal static V ResolvePointValidation(Extraction.PointOperationKind kind, Type geometryType) =>
        PointOperations.TryGetValue(kind, out OperationMetadata? metadata) ? ResolveValidation(metadata.ValidationModes, geometryType) : V.Standard;

    internal static string ResolvePointOperationName(Extraction.PointOperationKind kind) =>
        PointOperations.TryGetValue(kind, out OperationMetadata? metadata) ? metadata.OperationName : "Extraction.Points";

    internal static V ResolveCurveValidation(Extraction.CurveOperationKind kind, Type geometryType) =>
        CurveOperations.TryGetValue(kind, out OperationMetadata? metadata) ? ResolveValidation(metadata.ValidationModes, geometryType) : V.Standard;

    internal static string ResolveCurveOperationName(Extraction.CurveOperationKind kind) =>
        CurveOperations.TryGetValue(kind, out OperationMetadata? metadata) ? metadata.OperationName : "Extraction.Curves";

    private static FrozenDictionary<Extraction.PointOperationKind, OperationMetadata> BuildPointMetadata() {
        Dictionary<Extraction.PointOperationKind, OperationMetadata> map = new() {
            [Extraction.PointOperationKind.Analytical] = new("Extraction.Points.Analytical", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.Analytical)),
            [Extraction.PointOperationKind.Extremal] = new("Extraction.Points.Extremal", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.Extremal)),
            [Extraction.PointOperationKind.Greville] = new("Extraction.Points.Greville", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.Greville)),
            [Extraction.PointOperationKind.Inflection] = new("Extraction.Points.Inflection", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.Inflection)),
            [Extraction.PointOperationKind.Quadrant] = new("Extraction.Points.Quadrant", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.Quadrant)),
            [Extraction.PointOperationKind.EdgeMidpoints] = new("Extraction.Points.EdgeMidpoints", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.EdgeMidpoints)),
            [Extraction.PointOperationKind.FaceCentroids] = new("Extraction.Points.FaceCentroids", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.FaceCentroids)),
            [Extraction.PointOperationKind.OsculatingFrames] = new("Extraction.Points.OsculatingFrames", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.OsculatingFrames)),
            [Extraction.PointOperationKind.DivideByCount] = new("Extraction.Points.DivideByCount", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.DivideByCount)),
            [Extraction.PointOperationKind.DivideByLength] = new("Extraction.Points.DivideByLength", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.DivideByLength)),
            [Extraction.PointOperationKind.DirectionalExtrema] = new("Extraction.Points.DirectionalExtrema", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.DirectionalExtrema)),
            [Extraction.PointOperationKind.Discontinuities] = new("Extraction.Points.Discontinuities", BuildValidationMap(PointValidationEntries, Extraction.PointOperationKind.Discontinuities)),
        };
        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<Extraction.CurveOperationKind, OperationMetadata> BuildCurveMetadata() {
        Dictionary<Extraction.CurveOperationKind, OperationMetadata> map = new() {
            [Extraction.CurveOperationKind.Boundary] = new("Extraction.Curves.Boundary", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.Boundary)),
            [Extraction.CurveOperationKind.IsocurveU] = new("Extraction.Curves.IsocurveU", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.IsocurveU)),
            [Extraction.CurveOperationKind.IsocurveV] = new("Extraction.Curves.IsocurveV", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.IsocurveV)),
            [Extraction.CurveOperationKind.IsocurveUV] = new("Extraction.Curves.IsocurveUV", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.IsocurveUV)),
            [Extraction.CurveOperationKind.FeatureEdges] = new("Extraction.Curves.FeatureEdges", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.FeatureEdges)),
            [Extraction.CurveOperationKind.UniformIsocurves] = new("Extraction.Curves.UniformIsocurves", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.UniformIsocurves)),
            [Extraction.CurveOperationKind.DirectionalIsocurves] = new("Extraction.Curves.DirectionalIsocurves", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.DirectionalIsocurves)),
            [Extraction.CurveOperationKind.ParameterIsocurves] = new("Extraction.Curves.ParameterIsocurves", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.ParameterIsocurves)),
            [Extraction.CurveOperationKind.ParameterDirectionalIsocurves] = new("Extraction.Curves.ParameterDirectionalIsocurves", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.ParameterDirectionalIsocurves)),
            [Extraction.CurveOperationKind.CustomFeatureEdges] = new("Extraction.Curves.CustomFeatureEdges", BuildValidationMap(CurveValidationEntries, Extraction.CurveOperationKind.CustomFeatureEdges)),
        };
        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<Type, V> BuildValidationMap<T>(
        (T Operation, Type Geometry, V Mode)[] entries,
        T operation) where T : notnull =>
        entries
            .Where(entry => EqualityComparer<T>.Default.Equals(entry.Operation, operation))
            .GroupBy(entry => entry.Geometry)
            .ToDictionary(static group => group.Key, static group => group.Last().Mode)
            .ToFrozenDictionary();

    private static V ResolveValidation(FrozenDictionary<Type, V> validationTable, Type geometryType) =>
        validationTable.TryGetValue(geometryType, out V exact)
            ? exact
            : validationTable
                .Where(entry => entry.Key.IsAssignableFrom(geometryType))
                .OrderByDescending(entry => entry.Key, SpecificityComparer)
                .Select(entry => entry.Value)
                .DefaultIfEmpty(V.Standard)
                .First();

    internal const double FilletCurvatureVariationThreshold = 0.15;
    internal const int FilletCurvatureSampleCount = 5;

    internal static readonly double SharpEdgeAngleThreshold = RhinoMath.ToRadians(20.0);
    internal static readonly double SmoothEdgeAngleThreshold = RhinoMath.ToRadians(170.0);
    internal static readonly double FeatureEdgeAngleThreshold = RhinoMath.ToRadians(30.0);

    internal const int MinHolePolySides = 16;

    internal const int PrimitiveResidualSampleCount = 20;
    internal const int CurvatureSampleCount = 16;
    internal const int MinCurvatureSamples = 4;
    internal const double CurvatureVariationThreshold = 0.05;

    internal const int PatternMinInstances = 3;
    internal const double RadialDistanceVariationThreshold = 0.05;
    internal const double RadialAngleVariationThreshold = 0.05;
    internal const double GridOrthogonalityThreshold = 0.1;
    internal const double GridPointDeviationThreshold = 0.1;
    internal const double ScalingVarianceThreshold = 0.1;

    internal const int MinIsocurveCount = 2;
    internal const int MaxIsocurveCount = 100;
    internal const int DefaultOsculatingFrameCount = 10;
    internal const int BoundaryIsocurveCount = 5;
}

internal sealed record OperationMetadata(string OperationName, FrozenDictionary<Type, V> ValidationModes);
