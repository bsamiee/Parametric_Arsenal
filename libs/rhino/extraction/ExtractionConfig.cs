using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Unified extraction configuration with consolidated FrozenDictionary dispatch and thresholds.</summary>
[Pure]
internal static class ExtractionConfig {
    /// <summary>Operation type discriminators for unified dispatch table.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "Non-zero values align with semantic operation discriminants")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte storage matches Semantic struct pattern and reduces memory")]
    internal enum OpType : byte {
        Analytical = 1, Extremal = 2, Greville = 3, Inflection = 4, Quadrant = 5,
        EdgeMidpoints = 6, FaceCentroids = 7, OsculatingFrames = 8,
        DivideByCount = 10, DivideByLength = 11, DirectionalExtrema = 12, Discontinuities = 13,
        Boundary = 20, IsocurveU = 21, IsocurveV = 22, IsocurveUV = 23, FeatureEdges = 24,
        UniformIsocurves = 30, DirectionalIsocurves = 31, ParameterIsocurves = 32,
        DirectionalParameterIsocurves = 33, CustomFeatureEdges = 34,
    }

    /// <summary>Unified operation metadata: (Type, OpType) → (ValidationMode, OperationName).</summary>
    internal static readonly FrozenDictionary<(Type GeometryType, OpType Operation), (V ValidationMode, string OpName)> OperationMeta =
        new Dictionary<(Type, OpType), (V, string)> {
            [(typeof(Brep), OpType.Analytical)] = (V.Standard | V.MassProperties, "Extract.Points.Analytical.Brep"),
            [(typeof(Curve), OpType.Analytical)] = (V.Standard | V.AreaCentroid, "Extract.Points.Analytical.Curve"),
            [(typeof(Surface), OpType.Analytical)] = (V.Standard | V.AreaCentroid, "Extract.Points.Analytical.Surface"),
            [(typeof(Mesh), OpType.Analytical)] = (V.Standard | V.MassProperties, "Extract.Points.Analytical.Mesh"),
            [(typeof(Extrusion), OpType.Analytical)] = (V.Standard | V.MassProperties, "Extract.Points.Analytical.Extrusion"),
            [(typeof(SubD), OpType.Analytical)] = (V.Standard | V.Topology, "Extract.Points.Analytical.SubD"),
            [(typeof(GeometryBase), OpType.Extremal)] = (V.BoundingBox, "Extract.Points.Extremal"),
            [(typeof(GeometryBase), OpType.Greville)] = (V.Standard, "Extract.Points.Greville"),
            [(typeof(Curve), OpType.Inflection)] = (V.Standard | V.Degeneracy, "Extract.Points.Inflection"),
            [(typeof(Curve), OpType.Quadrant)] = (V.Tolerance, "Extract.Points.Quadrant"),
            [(typeof(Brep), OpType.EdgeMidpoints)] = (V.Standard | V.Topology, "Extract.Points.EdgeMidpoints.Brep"),
            [(typeof(Mesh), OpType.EdgeMidpoints)] = (V.Standard | V.MeshSpecific, "Extract.Points.EdgeMidpoints.Mesh"),
            [(typeof(Curve), OpType.EdgeMidpoints)] = (V.Standard | V.Degeneracy, "Extract.Points.EdgeMidpoints.Curve"),
            [(typeof(Brep), OpType.FaceCentroids)] = (V.Standard | V.Topology, "Extract.Points.FaceCentroids.Brep"),
            [(typeof(Mesh), OpType.FaceCentroids)] = (V.Standard | V.MeshSpecific, "Extract.Points.FaceCentroids.Mesh"),
            [(typeof(Curve), OpType.OsculatingFrames)] = (V.Standard | V.Degeneracy, "Extract.Points.OsculatingFrames"),
            [(typeof(Curve), OpType.DivideByCount)] = (V.Standard | V.Degeneracy, "Extract.Points.DivideByCount.Curve"),
            [(typeof(Surface), OpType.DivideByCount)] = (V.Standard, "Extract.Points.DivideByCount.Surface"),
            [(typeof(Brep), OpType.DivideByCount)] = (V.Standard | V.Topology, "Extract.Points.DivideByCount.Brep"),
            [(typeof(Extrusion), OpType.DivideByCount)] = (V.Standard | V.Topology, "Extract.Points.DivideByCount.Extrusion"),
            [(typeof(SubD), OpType.DivideByCount)] = (V.Standard | V.Topology, "Extract.Points.DivideByCount.SubD"),
            [(typeof(Curve), OpType.DivideByLength)] = (V.Standard | V.Degeneracy, "Extract.Points.DivideByLength.Curve"),
            [(typeof(Surface), OpType.DivideByLength)] = (V.Standard | V.AreaCentroid, "Extract.Points.DivideByLength.Surface"),
            [(typeof(Brep), OpType.DivideByLength)] = (V.Standard | V.Topology, "Extract.Points.DivideByLength.Brep"),
            [(typeof(Curve), OpType.DirectionalExtrema)] = (V.Standard | V.Degeneracy, "Extract.Points.DirectionalExtrema.Curve"),
            [(typeof(Surface), OpType.DirectionalExtrema)] = (V.Standard | V.AreaCentroid, "Extract.Points.DirectionalExtrema.Surface"),
            [(typeof(Brep), OpType.DirectionalExtrema)] = (V.Standard | V.Topology, "Extract.Points.DirectionalExtrema.Brep"),
            [(typeof(Curve), OpType.Discontinuities)] = (V.Standard | V.Degeneracy, "Extract.Points.Discontinuities.Curve"),
            [(typeof(PolyCurve), OpType.Discontinuities)] = (V.Standard | V.Degeneracy, "Extract.Points.Discontinuities.PolyCurve"),
            [(typeof(Surface), OpType.Boundary)] = (V.Standard | V.UVDomain, "Extract.Curves.Boundary.Surface"),
            [(typeof(NurbsSurface), OpType.Boundary)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.Boundary.NurbsSurface"),
            [(typeof(Brep), OpType.Boundary)] = (V.Standard | V.Topology, "Extract.Curves.Boundary.Brep"),
            [(typeof(Surface), OpType.IsocurveU)] = (V.Standard | V.UVDomain, "Extract.Curves.IsocurveU.Surface"),
            [(typeof(NurbsSurface), OpType.IsocurveU)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.IsocurveU.NurbsSurface"),
            [(typeof(Surface), OpType.IsocurveV)] = (V.Standard | V.UVDomain, "Extract.Curves.IsocurveV.Surface"),
            [(typeof(NurbsSurface), OpType.IsocurveV)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.IsocurveV.NurbsSurface"),
            [(typeof(Surface), OpType.IsocurveUV)] = (V.Standard | V.UVDomain, "Extract.Curves.IsocurveUV.Surface"),
            [(typeof(NurbsSurface), OpType.IsocurveUV)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.IsocurveUV.NurbsSurface"),
            [(typeof(Brep), OpType.FeatureEdges)] = (V.Standard | V.Topology, "Extract.Curves.FeatureEdges.Brep"),
            [(typeof(Surface), OpType.UniformIsocurves)] = (V.Standard | V.UVDomain, "Extract.Curves.UniformIsocurves.Surface"),
            [(typeof(NurbsSurface), OpType.UniformIsocurves)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.UniformIsocurves.NurbsSurface"),
            [(typeof(Surface), OpType.DirectionalIsocurves)] = (V.Standard | V.UVDomain, "Extract.Curves.DirectionalIsocurves.Surface"),
            [(typeof(NurbsSurface), OpType.DirectionalIsocurves)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.DirectionalIsocurves.NurbsSurface"),
            [(typeof(Surface), OpType.ParameterIsocurves)] = (V.Standard | V.UVDomain, "Extract.Curves.ParameterIsocurves.Surface"),
            [(typeof(NurbsSurface), OpType.ParameterIsocurves)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.ParameterIsocurves.NurbsSurface"),
            [(typeof(Surface), OpType.DirectionalParameterIsocurves)] = (V.Standard | V.UVDomain, "Extract.Curves.DirectionalParameterIsocurves.Surface"),
            [(typeof(NurbsSurface), OpType.DirectionalParameterIsocurves)] = (V.Standard | V.NurbsGeometry | V.UVDomain, "Extract.Curves.DirectionalParameterIsocurves.NurbsSurface"),
            [(typeof(Brep), OpType.CustomFeatureEdges)] = (V.Standard | V.Topology, "Extract.Curves.CustomFeatureEdges.Brep"),
        }.ToFrozenDictionary();

    /// <summary>Resolve operation metadata with fallback to assignable types.</summary>
    internal static (V ValidationMode, string OpName) GetOperationMeta(OpType operation, Type geometryType) =>
        OperationMeta.TryGetValue((geometryType, operation), out (V, string) exact)
            ? exact
            : OperationMeta
                .Where(kv => kv.Key.Operation == operation && kv.Key.GeometryType.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty((V.Standard, $"Extract.{operation}"))
                .First();

    /// <summary>Feature type identifiers.</summary>
    internal const byte FeatureTypeFillet = 0;
    internal const byte FeatureTypeChamfer = 1;
    internal const byte FeatureTypeHole = 2;
    internal const byte FeatureTypeGenericEdge = 3;
    internal const byte FeatureTypeVariableRadiusFillet = 4;

    /// <summary>Primitive type identifiers.</summary>
    internal const byte PrimitiveTypePlane = 0;
    internal const byte PrimitiveTypeCylinder = 1;
    internal const byte PrimitiveTypeSphere = 2;
    internal const byte PrimitiveTypeUnknown = 3;
    internal const byte PrimitiveTypeCone = 4;
    internal const byte PrimitiveTypeTorus = 5;
    internal const byte PrimitiveTypeExtrusion = 6;

    /// <summary>Pattern type identifiers.</summary>
    internal const byte PatternTypeLinear = 0;
    internal const byte PatternTypeRadial = 1;
    internal const byte PatternTypeGrid = 2;
    internal const byte PatternTypeScaling = 3;

    /// <summary>Fillet detection thresholds.</summary>
    internal const double FilletCurvatureVariationThreshold = 0.15;
    internal const int FilletCurvatureSampleCount = 5;

    /// <summary>Edge classification thresholds.</summary>
    internal static readonly double SharpEdgeAngleThreshold = RhinoMath.ToRadians(20.0);
    internal static readonly double SmoothEdgeAngleThreshold = RhinoMath.ToRadians(170.0);
    internal static readonly double FeatureEdgeAngleThreshold = RhinoMath.ToRadians(30.0);

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
}
