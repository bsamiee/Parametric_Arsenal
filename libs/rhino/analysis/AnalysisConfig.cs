using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Validation modes, metadata, and constants for differential geometry.</summary>
internal static class AnalysisConfig {
    internal static readonly FrozenDictionary<Type, DifferentialGeometryMetadata> DifferentialGeometry =
        new Dictionary<Type, DifferentialGeometryMetadata> {
            [typeof(Curve)] = new(V.Standard | V.Degeneracy, "Analysis.Curve"),
            [typeof(NurbsCurve)] = new(V.Standard | V.Degeneracy | V.NurbsGeometry, "Analysis.Curve"),
            [typeof(LineCurve)] = new(V.Standard | V.Degeneracy, "Analysis.Curve"),
            [typeof(ArcCurve)] = new(V.Standard | V.Degeneracy, "Analysis.Curve"),
            [typeof(PolyCurve)] = new(V.Standard | V.Degeneracy | V.PolycurveStructure, "Analysis.Curve"),
            [typeof(PolylineCurve)] = new(V.Standard | V.Degeneracy, "Analysis.Curve"),
            [typeof(Surface)] = new(V.Standard | V.UVDomain, "Analysis.Surface"),
            [typeof(NurbsSurface)] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Analysis.Surface"),
            [typeof(PlaneSurface)] = new(V.Standard, "Analysis.Surface"),
            [typeof(Brep)] = new(V.Standard | V.Topology, "Analysis.Brep"),
            [typeof(Extrusion)] = new(V.Standard | V.Topology | V.ExtrusionGeometry, "Analysis.Brep"),
            [typeof(Mesh)] = new(V.Standard | V.MeshSpecific, "Analysis.Mesh"),
        }.ToFrozenDictionary();

    internal static readonly MetricOperationMetadata SurfaceQualityMetadata = new(
        ValidationMode: V.Standard | V.BoundingBox | V.UVDomain,
        OperationName: "Analysis.SurfaceQuality");

    internal static readonly MetricOperationMetadata CurveFairnessMetadata = new(
        ValidationMode: V.Standard | V.Degeneracy | V.SurfaceContinuity,
        OperationName: "Analysis.CurveFairness");

    internal static readonly MetricOperationMetadata MeshElementQualityMetadata = new(
        ValidationMode: V.Standard | V.MeshSpecific,
        OperationName: "Analysis.MeshForFEA");

    internal const string MultipleOperationName = "Analysis.Multiple";
    internal const int MaxDiscontinuities = 20;
    internal const int DefaultDerivativeOrder = 2;
    internal const int CurveFrameSampleCount = 5;
    internal const int CurveFairnessSampleCount = 50;
    internal const int SurfaceQualityGridDimension = 10;
    internal const int SurfaceQualitySampleCount = SurfaceQualityGridDimension * SurfaceQualityGridDimension;
    internal const double SingularityBoundaryFraction = 0.1;
    internal const double SingularityProximityFactor = 0.01;
    internal const double HighCurvatureMultiplier = 5.0;
    internal const double InflectionSharpnessThreshold = 0.5;
    internal const double SmoothnessSensitivity = 10.0;
    internal const double BrepClosestPointToleranceMultiplier = 100.0;
    internal const double AspectRatioWarning = 3.0;
    internal const double AspectRatioCritical = 10.0;
    internal const double SkewnessWarning = 0.5;
    internal const double SkewnessCritical = 0.85;
    internal const double JacobianWarning = 0.3;
    internal const double JacobianCritical = 0.1;
    internal const double TriangleIdealAngleDegrees = 60.0;
    internal static readonly double QuadIdealAngleDegrees = RhinoMath.ToDegrees(RhinoMath.HalfPI);

    internal sealed record DifferentialGeometryMetadata(
        V ValidationMode,
        string OperationName);

    internal sealed record MetricOperationMetadata(
        V ValidationMode,
        string OperationName);
}
