using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Unified metadata, constants, and dispatch tables for differential geometry analysis.</summary>
internal static class AnalysisConfig {
    /// <summary>Unified operation metadata for all analysis operations.</summary>
    internal sealed record AnalysisOperationMetadata(
        V ValidationMode,
        string OperationName,
        int DefaultDerivativeOrder = 2,
        int DefaultSampleCount = 50);

    /// <summary>Singular unified operations dispatch table: (geometry type, request type) → metadata.</summary>
    internal static readonly FrozenDictionary<(Type Geometry, Type Request), AnalysisOperationMetadata> Operations =
        new Dictionary<(Type, Type), AnalysisOperationMetadata> {
            [(typeof(Curve), typeof(Analysis.CurveAnalysis))] = new(V.Standard | V.Degeneracy, "Analysis.Curve", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFrameSampleCount),
            [(typeof(NurbsCurve), typeof(Analysis.CurveAnalysis))] = new(V.Standard | V.Degeneracy | V.NurbsGeometry, "Analysis.NurbsCurve", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFrameSampleCount),
            [(typeof(LineCurve), typeof(Analysis.CurveAnalysis))] = new(V.Standard | V.Degeneracy, "Analysis.LineCurve", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFrameSampleCount),
            [(typeof(ArcCurve), typeof(Analysis.CurveAnalysis))] = new(V.Standard | V.Degeneracy, "Analysis.ArcCurve", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFrameSampleCount),
            [(typeof(PolyCurve), typeof(Analysis.CurveAnalysis))] = new(V.Standard | V.Degeneracy | V.PolycurveStructure, "Analysis.PolyCurve", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFrameSampleCount),
            [(typeof(PolylineCurve), typeof(Analysis.CurveAnalysis))] = new(V.Standard | V.Degeneracy, "Analysis.PolylineCurve", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFrameSampleCount),
            [(typeof(Curve), typeof(Analysis.CurveFairnessAnalysis))] = new(V.Standard | V.Degeneracy | V.SurfaceContinuity, "Analysis.CurveFairness", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFairnessSampleCount),
            [(typeof(NurbsCurve), typeof(Analysis.CurveFairnessAnalysis))] = new(V.Standard | V.Degeneracy | V.SurfaceContinuity | V.NurbsGeometry, "Analysis.NurbsCurveFairness", DefaultDerivativeOrder: 2, DefaultSampleCount: CurveFairnessSampleCount),
            [(typeof(Surface), typeof(Analysis.SurfaceAnalysis))] = new(V.Standard | V.UVDomain, "Analysis.Surface", DefaultDerivativeOrder: 2, DefaultSampleCount: 1),
            [(typeof(NurbsSurface), typeof(Analysis.SurfaceAnalysis))] = new(V.Standard | V.NurbsGeometry | V.UVDomain, "Analysis.NurbsSurface", DefaultDerivativeOrder: 2, DefaultSampleCount: 1),
            [(typeof(PlaneSurface), typeof(Analysis.SurfaceAnalysis))] = new(V.Standard, "Analysis.PlaneSurface", DefaultDerivativeOrder: 2, DefaultSampleCount: 1),
            [(typeof(Surface), typeof(Analysis.SurfaceQualityAnalysis))] = new(V.Standard | V.BoundingBox | V.UVDomain, "Analysis.SurfaceQuality", DefaultDerivativeOrder: 0, DefaultSampleCount: SurfaceQualitySampleCount),
            [(typeof(NurbsSurface), typeof(Analysis.SurfaceQualityAnalysis))] = new(V.Standard | V.BoundingBox | V.UVDomain | V.NurbsGeometry, "Analysis.NurbsSurfaceQuality", DefaultDerivativeOrder: 0, DefaultSampleCount: SurfaceQualitySampleCount),
            [(typeof(Brep), typeof(Analysis.BrepAnalysis))] = new(V.Standard | V.Topology, "Analysis.Brep", DefaultDerivativeOrder: 2, DefaultSampleCount: 1),
            [(typeof(Extrusion), typeof(Analysis.BrepAnalysis))] = new(V.Standard | V.Topology | V.ExtrusionGeometry, "Analysis.Extrusion", DefaultDerivativeOrder: 2, DefaultSampleCount: 1),
            [(typeof(Mesh), typeof(Analysis.MeshAnalysis))] = new(V.Standard | V.MeshSpecific, "Analysis.Mesh", DefaultDerivativeOrder: 0, DefaultSampleCount: 1),
            [(typeof(Mesh), typeof(Analysis.MeshFEAAnalysis))] = new(V.Standard | V.MeshSpecific, "Analysis.MeshFEA", DefaultDerivativeOrder: 0, DefaultSampleCount: 1),
        }.ToFrozenDictionary();

    /// <summary>Maximum discontinuity count per curve for C1/C2 detection.</summary>
    internal const int MaxDiscontinuities = 20;

    /// <summary>Default derivative order for position, tangent, and curvature computation.</summary>
    internal const int DefaultDerivativeOrder = 2;

    /// <summary>Number of perpendicular frames sampled along curve domain.</summary>
    internal const int CurveFrameSampleCount = 5;

    /// <summary>Sample count for curve fairness curvature comb analysis.</summary>
    internal const int CurveFairnessSampleCount = 50;

    /// <summary>Grid dimension for surface quality UV sampling (10×10).</summary>
    internal const int SurfaceQualityGridDimension = 10;

    /// <summary>Total surface quality sample count derived from grid dimensions.</summary>
    internal const int SurfaceQualitySampleCount = SurfaceQualityGridDimension * SurfaceQualityGridDimension;

    /// <summary>Domain fraction defining boundary proximity for singularity detection.</summary>
    internal const double SingularityBoundaryFraction = 0.1;

    /// <summary>Singularity proximity threshold as domain fraction.</summary>
    internal const double SingularityProximityFactor = 0.01;

    /// <summary>High curvature threshold as multiplier of median for anomaly detection.</summary>
    internal const double HighCurvatureMultiplier = 5.0;

    /// <summary>Threshold for detecting sharp inflection points via curvature change.</summary>
    internal const double InflectionSharpnessThreshold = 0.5;

    /// <summary>Sensitivity factor for smoothness scoring via curvature variation.</summary>
    internal const double SmoothnessSensitivity = 10.0;

    /// <summary>Brep closest point tolerance multiplier relative to context.</summary>
    internal const double BrepClosestPointToleranceMultiplier = 100.0;

    /// <summary>Mesh FEA quality thresholds.</summary>
    internal const double AspectRatioWarning = 3.0;
    internal const double AspectRatioCritical = 10.0;
    internal const double SkewnessWarning = 0.5;
    internal const double SkewnessCritical = 0.85;
    internal const double JacobianWarning = 0.3;
    internal const double JacobianCritical = 0.1;

    /// <summary>Ideal interior angles for element types.</summary>
    internal const double TriangleIdealAngleDegrees = 60.0;
    internal static readonly double QuadIdealAngleDegrees = RhinoMath.ToDegrees(RhinoMath.HalfPI);
}
