using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic differential geometry analysis with unified dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Analysis is the primary API entry point for the Analysis namespace")]
public static class Analysis {
    /// <summary>Base type for analysis requests.</summary>
    public abstract record Request;

    /// <summary>Curve differential geometry analysis at parameter.</summary>
    public sealed record CurveAnalysis(
        double? Parameter = null,
        int DerivativeOrder = 2) : Request;

    /// <summary>Curve fairness analysis via curvature variation.</summary>
    public sealed record CurveFairnessAnalysis : Request;

    /// <summary>Surface differential geometry analysis at UV parameter.</summary>
    public sealed record SurfaceAnalysis(
        (double U, double V)? Parameter = null,
        int DerivativeOrder = 2) : Request;

    /// <summary>Surface quality analysis via curvature sampling.</summary>
    public sealed record SurfaceQualityAnalysis : Request;

    /// <summary>Brep surface, topology, and proximity analysis.</summary>
    public sealed record BrepAnalysis(
        (double U, double V)? Parameter = null,
        int FaceIndex = 0,
        Point3d? TestPoint = null,
        int DerivativeOrder = 2) : Request;

    /// <summary>Mesh topology and manifold property analysis.</summary>
    public sealed record MeshAnalysis(
        int VertexIndex = 0) : Request;

    /// <summary>Mesh quality analysis for finite element analysis.</summary>
    public sealed record MeshFEAAnalysis : Request;

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

    /// <summary>Curve fairness analysis result with smoothness metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record CurveFairnessData(
        Point3d Location,
        double SmoothnessScore,
        double[] CurvatureValues,
        (double Parameter, bool IsSharp)[] InflectionPoints,
        double BendingEnergy) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"CurveFairness | Smoothness={this.SmoothnessScore:F3} | Inflections={this.InflectionPoints.Length} | Energy={this.BendingEnergy:F3}");
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

    /// <summary>Surface quality analysis result with curvature uniformity metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record SurfaceQualityData(
        Point3d Location,
        double[] GaussianCurvatures,
        double[] MeanCurvatures,
        (double U, double V)[] SingularityLocations,
        double UniformityScore) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"SurfaceQuality | Uniformity={this.UniformityScore:F3} | Singularities={this.SingularityLocations.Length}");
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

    /// <summary>Mesh FEA quality analysis result with element quality metrics.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MeshFEAData(
        Point3d Location,
        double[] AspectRatios,
        double[] Skewness,
        double[] Jacobians,
        int[] ProblematicFaceIndices,
        (int WarningCount, int CriticalCount) QualityFlags) : IResult {
        [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"MeshFEA | Warnings={this.QualityFlags.WarningCount} | Critical={this.QualityFlags.CriticalCount} | Problematic={this.ProblematicFaceIndices.Length}");
    }

    /// <summary>Execute analysis request on geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IResult> Execute<T>(T geometry, Request request, IGeometryContext context) where T : GeometryBase =>
        AnalysisCore.Execute(geometry: geometry, request: request, context: context);
}
