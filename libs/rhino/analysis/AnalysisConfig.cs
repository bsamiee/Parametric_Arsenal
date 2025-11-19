using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Unified metadata, constants, and dispatch tables for differential geometry analysis.</summary>
[Pure]
internal static class AnalysisConfig {
    /// <summary>Standard differential geometry analysis metadata by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, StandardAnalysisMetadata> StandardAnalysis =
        new Dictionary<Type, StandardAnalysisMetadata> {
            [typeof(Curve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.Curve",
                DefaultDerivativeOrder: 2),
            [typeof(NurbsCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy | V.NurbsGeometry,
                OperationName: "Analysis.NurbsCurve",
                DefaultDerivativeOrder: 2),
            [typeof(LineCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.LineCurve",
                DefaultDerivativeOrder: 2),
            [typeof(ArcCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.ArcCurve",
                DefaultDerivativeOrder: 2),
            [typeof(PolyCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy | V.PolycurveStructure,
                OperationName: "Analysis.PolyCurve",
                DefaultDerivativeOrder: 2),
            [typeof(PolylineCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.PolylineCurve",
                DefaultDerivativeOrder: 2),
            [typeof(Surface)] = new(
                ValidationMode: V.Standard | V.UVDomain,
                OperationName: "Analysis.Surface",
                DefaultDerivativeOrder: 2),
            [typeof(NurbsSurface)] = new(
                ValidationMode: V.Standard | V.NurbsGeometry | V.UVDomain,
                OperationName: "Analysis.NurbsSurface",
                DefaultDerivativeOrder: 2),
            [typeof(PlaneSurface)] = new(
                ValidationMode: V.Standard,
                OperationName: "Analysis.PlaneSurface",
                DefaultDerivativeOrder: 2),
            [typeof(Brep)] = new(
                ValidationMode: V.Standard | V.Topology,
                OperationName: "Analysis.Brep",
                DefaultDerivativeOrder: 2),
            [typeof(Extrusion)] = new(
                ValidationMode: V.Standard | V.Topology | V.ExtrusionGeometry,
                OperationName: "Analysis.Extrusion",
                DefaultDerivativeOrder: 2),
            [typeof(Mesh)] = new(
                ValidationMode: V.Standard | V.MeshSpecific,
                OperationName: "Analysis.Mesh",
                DefaultDerivativeOrder: 0),
        }.ToFrozenDictionary();

    /// <summary>Quality analysis operations metadata.</summary>
    internal static readonly FrozenDictionary<Type, QualityAnalysisMetadata> QualityAnalysis =
        new Dictionary<Type, QualityAnalysisMetadata> {
            [typeof(Surface)] = new(
                ValidationMode: V.Standard | V.BoundingBox | V.UVDomain,
                OperationName: "Analysis.SurfaceQuality",
                SampleCount: SurfaceQualitySampleCount,
                GridDimension: SurfaceQualityGridDimension),
            [typeof(Curve)] = new(
                ValidationMode: V.Standard | V.Degeneracy | V.SurfaceContinuity,
                OperationName: "Analysis.CurveFairness",
                SampleCount: CurveFairnessSampleCount,
                GridDimension: 1),
            [typeof(Mesh)] = new(
                ValidationMode: V.Standard | V.MeshSpecific,
                OperationName: "Analysis.MeshFEA",
                SampleCount: 0,
                GridDimension: 0),
        }.ToFrozenDictionary();

    /// <summary>Standard differential geometry analysis metadata.</summary>
    internal sealed record StandardAnalysisMetadata(
        V ValidationMode,
        string OperationName,
        int DefaultDerivativeOrder);

    /// <summary>Quality analysis metadata with sampling configuration.</summary>
    internal sealed record QualityAnalysisMetadata(
        V ValidationMode,
        string OperationName,
        int SampleCount,
        int GridDimension);

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
