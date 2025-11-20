using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Unified metadata, constants, and dispatch tables for differential and quality analysis.</summary>
[Pure]
internal static class AnalysisConfig {
    /// <summary>Unified metadata for differential geometry operations.</summary>
    internal sealed record DifferentialMetadata(
        V ValidationMode,
        string OperationName,
        int FrameSampleCount,
        int MaxDiscontinuities,
        double ClosestPointToleranceMultiplier);

    /// <summary>Unified metadata for quality analysis operations.</summary>
    internal sealed record QualityMetadata(
        V ValidationMode,
        string OperationName,
        int SampleCount,
        int GridDimension,
        double BoundaryFraction,
        double ProximityFactor,
        double CurvatureMultiplier,
        double InflectionThreshold,
        double SmoothnessSensitivity);

    /// <summary>Differential geometry dispatch table: request type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, DifferentialMetadata> DifferentialOperations =
        new Dictionary<Type, DifferentialMetadata> {
            [typeof(Analysis.CurveAnalysis)] = new(
                ValidationMode: V.Standard | V.Degeneracy,
                OperationName: "Analysis.Curve",
                FrameSampleCount: 5,
                MaxDiscontinuities: 20,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(Analysis.SurfaceAnalysis)] = new(
                ValidationMode: V.Standard | V.UVDomain,
                OperationName: "Analysis.Surface",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 1.0),
            [typeof(Analysis.BrepAnalysis)] = new(
                ValidationMode: V.Standard | V.Topology,
                OperationName: "Analysis.Brep",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 100.0),
            [typeof(Analysis.ExtrusionAnalysis)] = new(
                ValidationMode: V.Standard | V.Topology | V.ExtrusionGeometry,
                OperationName: "Analysis.Extrusion",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 100.0),
            [typeof(Analysis.MeshAnalysis)] = new(
                ValidationMode: V.Standard | V.MeshSpecific,
                OperationName: "Analysis.Mesh",
                FrameSampleCount: 0,
                MaxDiscontinuities: 0,
                ClosestPointToleranceMultiplier: 1.0),
        }.ToFrozenDictionary();

    /// <summary>Quality analysis dispatch table: request type → metadata.</summary>
    internal static readonly FrozenDictionary<Type, QualityMetadata> QualityOperations =
        new Dictionary<Type, QualityMetadata> {
            [typeof(Analysis.SurfaceQualityAnalysis)] = new(
                ValidationMode: V.Standard | V.BoundingBox | V.UVDomain,
                OperationName: "Analysis.SurfaceQuality",
                SampleCount: 100,
                GridDimension: 10,
                BoundaryFraction: 0.1,
                ProximityFactor: 0.01,
                CurvatureMultiplier: 5.0,
                InflectionThreshold: 0.0,
                SmoothnessSensitivity: 0.0),
            [typeof(Analysis.CurveFairnessAnalysis)] = new(
                ValidationMode: V.Standard | V.Degeneracy | V.SurfaceContinuity,
                OperationName: "Analysis.CurveFairness",
                SampleCount: 50,
                GridDimension: 0,
                BoundaryFraction: 0.0,
                ProximityFactor: 0.0,
                CurvatureMultiplier: 0.0,
                InflectionThreshold: 0.5,
                SmoothnessSensitivity: 10.0),
            [typeof(Analysis.MeshQualityAnalysis)] = new(
                ValidationMode: V.Standard | V.MeshSpecific,
                OperationName: "Analysis.MeshQuality",
                SampleCount: 0,
                GridDimension: 0,
                BoundaryFraction: 0.0,
                ProximityFactor: 0.0,
                CurvatureMultiplier: 0.0,
                InflectionThreshold: 0.0,
                SmoothnessSensitivity: 0.0),
        }.ToFrozenDictionary();

    /// <summary>Default derivative order for position, tangent, and curvature computation.</summary>
    internal const int DefaultDerivativeOrder = 2;

    /// <summary>Mesh FEA quality thresholds: aspect ratio.</summary>
    internal const double AspectRatioWarning = 3.0;
    internal const double AspectRatioCritical = 10.0;

    /// <summary>Mesh FEA quality thresholds: skewness.</summary>
    internal const double SkewnessWarning = 0.5;
    internal const double SkewnessCritical = 0.85;

    /// <summary>Mesh FEA quality thresholds: Jacobian.</summary>
    internal const double JacobianWarning = 0.3;
    internal const double JacobianCritical = 0.1;

    /// <summary>Ideal interior angles for mesh element types.</summary>
    internal const double TriangleIdealAngleDegrees = 60.0;
    internal static readonly double QuadIdealAngleDegrees = RhinoMath.ToDegrees(RhinoMath.HalfPI);
}
