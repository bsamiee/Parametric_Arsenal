using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic differential and quality analysis with unified dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Analysis is the primary API entry point for the Analysis namespace")]
public static class Analysis {
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
        Point3d Centroid) {
        [Pure]
        private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"Curve @ {this.Location} | κ={this.Curvature:F3} | L={this.Length:F3} | Disc={this.DiscontinuityParameters.Length.ToString(CultureInfo.InvariantCulture)}");
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
        Point3d Centroid) {
        [Pure]
        private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
            $"Surface @ {this.Location} | K={this.Gaussian:F3} | H={this.Mean:F3} | A={this.Area:F3}{(this.AtSingularity ? " [singular]" : string.Empty)}");
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
        Point3d Centroid) {
        [Pure]
        private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
            $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}{(this.IsSolid ? " [solid]" : string.Empty)}{(this.IsManifold ? " [manifold]" : string.Empty)}");
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
        double Volume) {
        [Pure]
        private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
            $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}{(this.IsClosed ? " [closed]" : string.Empty)}{(this.IsManifold ? " [manifold]" : string.Empty)}");
    }

    /// <summary>Surface quality metrics.</summary>
    public sealed record SurfaceQualityResult(
        double[] GaussianCurvatures,
        double[] MeanCurvatures,
        (double U, double V)[] SingularityLocations,
        double UniformityScore);

    /// <summary>Curve fairness metrics.</summary>
    public sealed record CurveFairnessResult(
        double SmoothnessScore,
        double[] CurvatureValues,
        (double Parameter, bool IsSharp)[] InflectionPoints,
        double BendingEnergy);

    /// <summary>Mesh quality metrics for finite-element analysis.</summary>
    public sealed record MeshFeaResult(
        double[] AspectRatios,
        double[] Skewness,
        double[] Jacobians,
        int[] ProblematicFaceIndices,
        (int WarningCount, int CriticalCount) QualityFlags);

    /// <summary>Base type for differential analysis requests.</summary>
    public abstract record DifferentialRequest;

    /// <summary>Curve differential analysis request.</summary>
    public sealed record CurveAnalysisRequest(Curve Curve, double? Parameter = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Surface differential analysis request.</summary>
    public sealed record SurfaceAnalysisRequest(Surface Surface, (double U, double V)? Parameter = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Brep differential analysis request.</summary>
    public sealed record BrepAnalysisRequest(Brep Brep, (double U, double V)? Parameter = null, int FaceIndex = 0, Point3d? TestPoint = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Extrusion differential analysis request.</summary>
    public sealed record ExtrusionAnalysisRequest(Extrusion Extrusion, (double U, double V)? Parameter = null, int FaceIndex = 0, Point3d? TestPoint = null, int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    /// <summary>Mesh differential analysis request.</summary>
    public sealed record MeshAnalysisRequest(Mesh Mesh, int VertexIndex = 0) : DifferentialRequest;

    /// <summary>Surface quality sampling request.</summary>
    public sealed record SurfaceQualityRequest(Surface Surface);

    /// <summary>Curve fairness evaluation request.</summary>
    public sealed record CurveFairnessRequest(Curve Curve);

    /// <summary>Mesh finite-element readiness request.</summary>
    public sealed record MeshFeaRequest(Mesh Mesh);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveData> Analyze(CurveAnalysisRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeCurve(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceData> Analyze(SurfaceAnalysisRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeSurface(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BrepData> Analyze(BrepAnalysisRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeBrep(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BrepData> Analyze(ExtrusionAnalysisRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeExtrusion(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshData> Analyze(MeshAnalysisRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeMesh(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<object>> AnalyzeMultiple(
        IReadOnlyList<DifferentialRequest> requests,
        IGeometryContext context) =>
        AnalysisCore.AnalyzeMultiple(requests: requests, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceQualityResult> AnalyzeSurfaceQuality(SurfaceQualityRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeSurfaceQuality(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveFairnessResult> AnalyzeCurveFairness(CurveFairnessRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeCurveFairness(request: request, context: context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshFeaResult> AnalyzeMeshForFea(MeshFeaRequest request, IGeometryContext context) =>
        AnalysisCore.AnalyzeMeshForFea(request: request, context: context);
}
