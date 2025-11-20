using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic differential geometry analysis with unified dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Analysis is the primary API entry point for the Analysis namespace")]
public static class Analysis {
    public abstract record DifferentialRequest;

    public sealed record CurveAnalysis(
        Curve Geometry,
        double? Parameter = null,
        int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    public sealed record SurfaceAnalysis(
        Surface Geometry,
        (double U, double V)? Parameter = null,
        int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    public sealed record BrepAnalysis(
        Brep Geometry,
        (double U, double V)? Parameter = null,
        int FaceIndex = 0,
        Point3d? TestPoint = null,
        int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    public sealed record ExtrusionAnalysis(
        Extrusion Geometry,
        (double U, double V)? Parameter = null,
        int FaceIndex = 0,
        Point3d? TestPoint = null,
        int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    public sealed record MeshAnalysis(
        Mesh Geometry,
        int VertexIndex = 0) : DifferentialRequest;

    public sealed record BatchAnalysis(
        IReadOnlyList<GeometryBase> Geometries,
        double? Parameter = null,
        (double U, double V)? UvParameter = null,
        int? Index = null,
        Point3d? TestPoint = null,
        int DerivativeOrder = AnalysisConfig.DefaultDerivativeOrder) : DifferentialRequest;

    public sealed record SurfaceQualityAnalysis(
        Surface Geometry) : DifferentialRequest;

    public sealed record CurveFairnessAnalysis(
        Curve Geometry) : DifferentialRequest;

    public sealed record MeshQualityAnalysis(
        Mesh Geometry) : DifferentialRequest;

    /// <summary>Analysis result marker with location property.</summary>
    public interface IResult {
        /// <summary>Evaluation point in world coordinates.</summary>
        public Point3d Location { get; }
    }

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

    /// <summary>Analyzes curve differential geometry at specified parameter.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveData> Analyze(
        CurveAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.Execute(request: request, context: context)
            .Map(results => (CurveData)results[0]);

    /// <summary>Analyzes surface differential geometry at specified UV parameters.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceData> Analyze(
        SurfaceAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.Execute(request: request, context: context)
            .Map(results => (SurfaceData)results[0]);

    /// <summary>Analyzes Brep surface, topology, and proximity to test point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BrepData> Analyze(
        BrepAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.Execute(request: request, context: context)
            .Map(results => (BrepData)results[0]);

    /// <summary>Analyzes extrusion by converting to Brep with inherited parameters.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BrepData> Analyze(
        ExtrusionAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.Execute(request: request, context: context)
            .Map(results => (BrepData)results[0]);

    /// <summary>Analyzes mesh topology and manifold properties at vertex.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshData> Analyze(
        MeshAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.Execute(request: request, context: context)
            .Map(results => (MeshData)results[0]);

    /// <summary>Batch analysis for multiple geometry instances with unified error handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IResult>> Analyze(
        BatchAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.Execute(request: request, context: context);

    /// <summary>Analyzes surface quality via curvature sampling and singularity detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double[] GaussianCurvatures, double[] MeanCurvatures, (double U, double V)[] SingularityLocations, double UniformityScore)> Analyze(
        SurfaceQualityAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.AnalyzeQuality(request: request, context: context);

    /// <summary>Analyzes curve fairness via curvature variation and inflection detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double SmoothnessScore, double[] CurvatureValues, (double Parameter, bool IsSharp)[] InflectionPoints, double BendingEnergy)> Analyze(
        CurveFairnessAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.AnalyzeQuality(request: request, context: context);

    /// <summary>Analyzes mesh quality for FEA via aspect ratio, skewness, and Jacobian metrics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaceIndices, (int WarningCount, int CriticalCount) QualityFlags)> Analyze(
        MeshQualityAnalysis request,
        IGeometryContext context) =>
        AnalysisCore.AnalyzeQuality(request: request, context: context);
}
