using System;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Unified metadata and constants for analysis operations.</summary>
[Pure]
internal static class AnalysisConfig {
    internal static readonly FrozenDictionary<Type, DifferentialMetadata> DifferentialOperations =
        new Dictionary<Type, DifferentialMetadata> {
            [typeof(Curve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.Curve",
                FrameSampleCount: 5,
                MaxDiscontinuities: 20,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(NurbsCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy | V.NurbsGeometry,
                OperationName: "Analysis.NurbsCurve",
                FrameSampleCount: 5,
                MaxDiscontinuities: 20,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(LineCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.LineCurve",
                FrameSampleCount: 5,
                MaxDiscontinuities: 20,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(ArcCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.ArcCurve",
                FrameSampleCount: 5,
                MaxDiscontinuities: 20,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(PolyCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy | V.PolycurveStructure,
                OperationName: "Analysis.PolyCurve",
                FrameSampleCount: 5,
                MaxDiscontinuities: 20,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(PolylineCurve)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.PolylineCurve",
                FrameSampleCount: 5,
                MaxDiscontinuities: 20,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(Surface)] = new(
                ValidationMode: V.Standard | V.UVDomain,
                OperationName: "Analysis.Surface",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(NurbsSurface)] = new(
                ValidationMode: V.Standard | V.NurbsGeometry | V.UVDomain,
                OperationName: "Analysis.NurbsSurface",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(PlaneSurface)] = new(
                ValidationMode: V.Standard,
                OperationName: "Analysis.PlaneSurface",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(Brep)] = new(
                ValidationMode: V.Standard | V.Topology,
                OperationName: "Analysis.Brep",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 100.0),
            [typeof(Extrusion)] = new(
                ValidationMode: V.Standard | V.Topology | V.ExtrusionGeometry,
                OperationName: "Analysis.Extrusion",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 100.0),
            [typeof(Mesh)] = new(
                ValidationMode: V.Standard | V.MeshSpecific,
                OperationName: "Analysis.Mesh",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 1.0),
        }.ToFrozenDictionary();

    internal static readonly SurfaceQualityMetadata SurfaceQuality = new(
        ValidationMode: V.Standard | V.BoundingBox | V.UVDomain,
        OperationName: "Analysis.SurfaceQuality",
        GridDimension: 10,
        SingularityBoundaryFraction: 0.1,
        SingularityProximityFactor: 0.01,
        HighCurvatureMultiplier: 5.0);

    internal static readonly CurveFairnessMetadata CurveFairness = new(
        ValidationMode: V.Standard | V.Degeneracy | V.SurfaceContinuity,
        OperationName: "Analysis.CurveFairness",
        SampleCount: 50,
        InflectionSharpnessThreshold: 0.5,
        SmoothnessSensitivity: 10.0);

    internal static readonly MeshQualityMetadata MeshQuality = new(
        ValidationMode: V.Standard | V.MeshSpecific,
        OperationName: "Analysis.MeshQuality",
        AspectRatioWarning: 3.0,
        AspectRatioCritical: 10.0,
        SkewnessWarning: 0.5,
        SkewnessCritical: 0.85,
        JacobianWarning: 0.3,
        JacobianCritical: 0.1,
        TriangleIdealAngleDegrees: 60.0,
        QuadIdealAngleDegrees: RhinoMath.ToDegrees(RhinoMath.HalfPI));

    internal static readonly BatchMetadata Batch = new(
        ValidationMode: V.None,
        OperationName: "Analysis.Batch");

    internal const int DefaultDerivativeOrder = 2;

    internal sealed record DifferentialMetadata(
        V ValidationMode,
        string OperationName,
        int FrameSampleCount,
        int MaxDiscontinuities,
        double ClosestPointToleranceMultiplier);

    internal sealed record SurfaceQualityMetadata(
        V ValidationMode,
        string OperationName,
        int GridDimension,
        double SingularityBoundaryFraction,
        double SingularityProximityFactor,
        double HighCurvatureMultiplier);

    internal sealed record CurveFairnessMetadata(
        V ValidationMode,
        string OperationName,
        int SampleCount,
        double InflectionSharpnessThreshold,
        double SmoothnessSensitivity);

    internal sealed record MeshQualityMetadata(
        V ValidationMode,
        string OperationName,
        double AspectRatioWarning,
        double AspectRatioCritical,
        double SkewnessWarning,
        double SkewnessCritical,
        double JacobianWarning,
        double JacobianCritical,
        double TriangleIdealAngleDegrees,
        double QuadIdealAngleDegrees);

    internal sealed record BatchMetadata(
        V ValidationMode,
        string OperationName);
}
