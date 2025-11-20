using System;
using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Validation modes and constants for differential geometry.</summary>
internal static class AnalysisConfig {
    internal sealed record DifferentialMetadata(V ValidationMode, string OperationName);

    internal sealed record QualityMetadata(V ValidationMode, string OperationName);

    internal static readonly FrozenDictionary<Type, DifferentialMetadata> DifferentialModes =
        new Dictionary<Type, DifferentialMetadata> {
            [typeof(Curve)] = new(V.Standard | V.Degeneracy, "Analysis.Curve"),
            [typeof(NurbsCurve)] = new(V.Standard | V.Degeneracy | V.NurbsGeometry, "Analysis.NurbsCurve"),
            [typeof(LineCurve)] = new(V.Standard | V.Degeneracy, "Analysis.LineCurve"),
            [typeof(ArcCurve)] = new(V.Standard | V.Degeneracy, "Analysis.ArcCurve"),
            [typeof(PolyCurve)] = new(V.Standard | V.Degeneracy | V.PolycurveStructure, "Analysis.PolyCurve"),
            [typeof(PolylineCurve)] = new(V.Standard | V.Degeneracy, "Analysis.PolylineCurve"),
            [typeof(Surface)] = new(V.Standard | V.UVDomain, "Analysis.Surface"),
            [typeof(NurbsSurface)] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Analysis.NurbsSurface"),
            [typeof(PlaneSurface)] = new(V.Standard, "Analysis.PlaneSurface"),
            [typeof(Brep)] = new(V.Standard | V.Topology, "Analysis.Brep"),
            [typeof(Extrusion)] = new(V.Standard | V.Topology | V.ExtrusionGeometry, "Analysis.Extrusion"),
            [typeof(Mesh)] = new(V.Standard | V.MeshSpecific, "Analysis.Mesh"),
        }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<Type, QualityMetadata> QualityModes =
        new Dictionary<Type, QualityMetadata> {
            [typeof(Analysis.SurfaceQualityRequest)] = new(V.Standard | V.BoundingBox | V.UVDomain, "Analysis.SurfaceQuality"),
            [typeof(Analysis.CurveFairnessRequest)] = new(V.Standard | V.Degeneracy | V.SurfaceContinuity, "Analysis.CurveFairness"),
            [typeof(Analysis.MeshFeaRequest)] = new(V.Standard | V.MeshSpecific, "Analysis.MeshForFEA"),
        }.ToFrozenDictionary();

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
}
