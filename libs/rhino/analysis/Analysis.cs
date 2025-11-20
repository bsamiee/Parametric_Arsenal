using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic differential geometry analysis with unified dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Analysis is the primary API entry point for the Analysis namespace")]
public static class Analysis {
    /// <summary>Analysis result marker with location property.</summary>
    public interface IResult {
        /// <summary>Evaluation point in world coordinates.</summary>
        public Point3d Location { get; }
    }

    /// <summary>Base request for differential geometry evaluation.</summary>
    public abstract record DifferentialRequest;

    /// <summary>Curve differential geometry request.</summary>
    public sealed record CurveAnalysis(Curve Geometry, double? Parameter = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Surface differential geometry request.</summary>
    public sealed record SurfaceAnalysis(Surface Geometry, (double U, double V)? Parameter = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Brep differential geometry request.</summary>
    public sealed record BrepAnalysis(Brep Geometry, (double U, double V)? Parameter = null, int FaceIndex = 0, Point3d? TestPoint = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Extrusion differential geometry request.</summary>
    public sealed record ExtrusionAnalysis(Extrusion Geometry, (double U, double V)? Parameter = null, int FaceIndex = 0, Point3d? TestPoint = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Mesh topology and manifold request.</summary>
    public sealed record MeshAnalysis(Mesh Geometry, int VertexIndex = 0) : DifferentialRequest;

    /// <summary>Base metric request for quality analysis.</summary>
    public abstract record MetricRequest;

    /// <summary>Surface quality sampling request.</summary>
    public sealed record SurfaceQualityRequest(Surface Geometry) : MetricRequest;

    /// <summary>Curve fairness evaluation request.</summary>
    public sealed record CurveFairnessRequest(Curve Geometry) : MetricRequest;

    /// <summary>Mesh element quality request.</summary>
    public sealed record MeshElementQualityRequest(Mesh Geometry) : MetricRequest;

    /// <summary>Curve differential geometry: derivatives, curvature, frames, discontinuities, length, centroid.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record CurveData(
        Point3d Location,
        Vector3d[] Derivatives,
        double Curvature,
        Plane Frame,
        Plane[] PerpendicularFrames,
        double Torsion,
        double[] DiscontinuityParameters,
        Continuity[] DiscontinuityTypes,
        double Length,
        Point3d Centroid) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"Curve @ {this.Location} | κ={this.Curvature:F3} | L={this.Length:F3} | Disc={this.DiscontinuityParameters?.Length.ToString(CultureInfo.InvariantCulture) ?? "0"}");
    }

    /// <summary>Surface differential geometry: Gaussian/mean curvature, principal directions, singularities, area, centroid.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record SurfaceData(
        Point3d Location,
        Vector3d[] Derivatives,
        double Gaussian,
        double Mean,
        double K1,
        double K2,
        Vector3d PrincipalDir1,
        Vector3d PrincipalDir2,
        Plane Frame,
        Vector3d Normal,
        bool AtSeam,
        bool AtSingularity,
        double Area,
        Point3d Centroid) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
            $"Surface @ {this.Location} | K={this.Gaussian:F3} | H={this.Mean:F3} | A={this.Area:F3}{(this.AtSingularity ? " [singular]" : "")}");
    }

    /// <summary>Brep topology: vertices, edges, manifold state, closest point, volume, area, centroid.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record BrepData(
        Point3d Location,
        Vector3d[] Derivatives,
        double Gaussian,
        double Mean,
        double K1,
        double K2,
        Vector3d PrincipalDir1,
        Vector3d PrincipalDir2,
        Plane Frame,
        Vector3d Normal,
        (int Index, Point3d Point)[] Vertices,
        (int Index, Line Geometry)[] Edges,
        bool IsManifold,
        bool IsSolid,
        Point3d ClosestPoint,
        double Distance,
        ComponentIndex Component,
        (double U, double V) SurfaceUV,
        double Area,
        double Volume,
        Point3d Centroid) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
            $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}{(this.IsSolid ? " [solid]" : "")}{(this.IsManifold ? " [manifold]" : "")}");
    }

    /// <summary>Mesh topology: vertices, edges, manifold state, closure, area, volume.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MeshData(
        Point3d Location,
        Plane Frame,
        Vector3d Normal,
        (int Index, Point3d Point)[] TopologyVertices,
        (int Index, Line Geometry)[] TopologyEdges,
        bool IsManifold,
        bool IsClosed,
        double Area,
        double Volume) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
            $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}{(this.IsClosed ? " [closed]" : "")}{(this.IsManifold ? " [manifold]" : "")}");
    }

    /// <summary>Surface curvature sampling result.</summary>
    public sealed record SurfaceQualityResult(
        double[] GaussianCurvatures,
        double[] MeanCurvatures,
        (double U, double V)[] SingularityLocations,
        double UniformityScore);

    /// <summary>Curve fairness evaluation result.</summary>
    public sealed record CurveFairnessResult(
        double SmoothnessScore,
        double[] CurvatureValues,
        (double Parameter, bool IsSharp)[] InflectionPoints,
        double BendingEnergy);

    /// <summary>Mesh quality metrics for FEA.</summary>
    public sealed record MeshElementQualityResult(
        double[] AspectRatios,
        double[] Skewness,
        double[] Jacobians,
        int[] ProblematicFaceIndices,
        (int WarningCount, int CriticalCount) QualityFlags);

    /// <summary>Analyzes a single differential request.</summary>
    [Pure]
    public static Result<IResult> Analyze(DifferentialRequest request, IGeometryContext context) =>
        AnalysisCore.Analyze(request: request, context: context);

    /// <summary>Batch analysis for multiple differential requests.</summary>
    [Pure]
    public static Result<IReadOnlyList<IResult>> AnalyzeMany(IReadOnlyList<DifferentialRequest> requests, IGeometryContext context) =>
        AnalysisCore.AnalyzeMany(requests: requests, context: context);

    /// <summary>Analyzes surface quality via curvature sampling and singularity detection.</summary>
    [Pure]
    public static Result<SurfaceQualityResult> AnalyzeSurfaceQuality(SurfaceQualityRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeSurfaceQuality(request: request, context: context);

    /// <summary>Analyzes curve fairness via curvature variation and inflection detection.</summary>
    [Pure]
    public static Result<CurveFairnessResult> AnalyzeCurveFairness(CurveFairnessRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeCurveFairness(request: request, context: context);

    /// <summary>Analyzes mesh quality for FEA via aspect ratio, skewness, and Jacobian metrics.</summary>
    [Pure]
    public static Result<MeshElementQualityResult> AnalyzeMeshForFEA(MeshElementQualityRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeMeshForFEA(request: request, context: context);
}
