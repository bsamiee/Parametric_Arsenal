using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic analysis engine with geometry-specific overloads and unified internal dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Analysis is the primary API entry point for the Analysis namespace")]
public static class Analysis {
    /// <summary>Polymorphic analysis result marker providing location property for spatial queries.</summary>
    public interface IResult {
        /// <summary>Evaluation point location in world coordinates.</summary>
        public Point3d Location { get; }
    }

    /// <summary>Curve differential geometry: derivatives, curvature, torsion, frames, discontinuities.</summary>
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

    /// <summary>Surface differential geometry: Gaussian/mean curvature, principal directions, singularities.</summary>
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
        private string DebuggerDisplay => this.AtSingularity
            ? string.Create(CultureInfo.InvariantCulture, $"Surface @ {this.Location} | K={this.Gaussian:F3} | H={this.Mean:F3} | A={this.Area:F3} [singular]")
            : string.Create(CultureInfo.InvariantCulture, $"Surface @ {this.Location} | K={this.Gaussian:F3} | H={this.Mean:F3} | A={this.Area:F3}");
    }

    /// <summary>Brep topology and geometry: vertices/edges, manifold state, proximity, volume properties.</summary>
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
        private string DebuggerDisplay => this.IsSolid && this.IsManifold
            ? string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [solid] [manifold]")
            : this.IsSolid
                ? string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [solid]")
                : this.IsManifold
                    ? string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [manifold]")
                    : string.Create(CultureInfo.InvariantCulture, $"Brep @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}");
    }

    /// <summary>Mesh topology: vertices/edges, manifold detection, closure state, volume properties.</summary>
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
        private string DebuggerDisplay => this.IsClosed && this.IsManifold
            ? string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [closed] [manifold]")
            : this.IsClosed
                ? string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [closed]")
                : this.IsManifold
                    ? string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3} [manifold]")
                    : string.Create(CultureInfo.InvariantCulture, $"Mesh @ {this.Location} | V={this.Volume:F3} | A={this.Area:F3}");
    }

    /// <summary>Curve curvature extrema: maximum curvature points, parameters, and global extremum location.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record CurvatureExtremaData(
        Point3d Location,
        Point3d[] MaxCurvaturePoints,
        double[] MaxCurvatureParameters,
        double[] MaxCurvatureValues,
        double GlobalMaxCurvature,
        Point3d GlobalMaxLocation) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"CurvatureExtrema: {this.MaxCurvaturePoints.Length} points | κ_max={this.GlobalMaxCurvature:F3} @ {this.GlobalMaxLocation}");
    }

    /// <summary>Multi-face brep analysis: per-face surface data with curvature statistics across all faces.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MultiFaceBrepData(
        Point3d Location,
        IReadOnlyList<SurfaceData> FaceAnalyses,
        IReadOnlyList<int> FaceIndices,
        int TotalFaces,
        (double Min, double Max) GaussianRange,
        (double Min, double Max) MeanRange) : IResult {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"MultiFaceBrep: {this.TotalFaces} faces | K∈[{this.GaussianRange.Min:F3}, {this.GaussianRange.Max:F3}] | H∈[{this.MeanRange.Min:F3}, {this.MeanRange.Max:F3}]");
    }

    /// <summary>Analyzes curve geometry producing comprehensive derivative, curvature, frame, and discontinuity data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveData> Analyze(
        Curve curve,
        IGeometryContext context,
        double? parameter = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCore.Execute(curve, context, t: parameter, uv: null, index: null, testPoint: null, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics)
            .Map(results => (CurveData)results[0]);

    /// <summary>Analyzes surface geometry producing comprehensive derivative, curvature, frame, and singularity data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceData> Analyze(
        Surface surface,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCore.Execute(surface, context, t: null, uv: uvParameter, index: null, testPoint: null, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics)
            .Map(results => (SurfaceData)results[0]);

    /// <summary>Analyzes brep geometry producing comprehensive surface evaluation, topology navigation, and proximity data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BrepData> Analyze(
        Brep brep,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int faceIndex = 0,
        Point3d? testPoint = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCore.Execute(brep, context, t: null, uv: uvParameter, index: faceIndex, testPoint: testPoint, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics)
            .Map(results => (BrepData)results[0]);

    /// <summary>Analyzes mesh geometry producing comprehensive topology navigation and manifold inspection data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshData> Analyze(
        Mesh mesh,
        IGeometryContext context,
        int vertexIndex = 0,
        bool enableDiagnostics = false) =>
        AnalysisCore.Execute(mesh, context, t: null, uv: null, index: vertexIndex, testPoint: null, derivativeOrder: 0, enableDiagnostics: enableDiagnostics)
            .Map(results => (MeshData)results[0]);

    /// <summary>Analyzes collections of geometry producing heterogeneous results via UnifiedOperation batch processing.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IResult>> AnalyzeMultiple<T>(
        IReadOnlyList<T> geometries,
        IGeometryContext context,
        double? parameter = null,
        (double u, double v)? uvParameter = null,
        int? index = null,
        Point3d? testPoint = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) where T : notnull =>
        UnifiedOperation.Apply(
            geometries,
            (Func<object, Result<IReadOnlyList<IResult>>>)(item =>
                AnalysisCore.Execute(item, context, parameter, uvParameter, index, testPoint, derivativeOrder, enableDiagnostics: enableDiagnostics)),
            new OperationConfig<object, IResult> {
                Context = context,
                ValidationMode = Core.Validation.V.None,
                EnableCache = true,
                AccumulateErrors = false,
                OperationName = "Analysis.Multiple",
                EnableDiagnostics = enableDiagnostics,
            });

    /// <summary>Analyzes curve curvature extrema producing maximum curvature locations and values.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurvatureExtremaData> AnalyzeCurvatureExtrema(
        Curve curve,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        AnalysisCore.ExecuteCurvatureExtrema(curve: curve, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Analyzes all faces of brep producing per-face surface data with curvature statistics.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MultiFaceBrepData> AnalyzeAllFaces(
        Brep brep,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCore.ExecuteMultiFaceBrep(brep: brep, context: context, uvParameter: uvParameter, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics);

    /// <summary>Analyzes curve geometry at multiple parameters producing batch derivative, curvature, and frame data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<CurveData>> AnalyzeAt(
        Curve curve,
        IGeometryContext context,
        IReadOnlyList<double> parameters,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCore.ExecuteBatchCurve(curve: curve, context: context, parameters: parameters, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics);

    /// <summary>Analyzes surface geometry at multiple UV parameters producing batch derivative and curvature data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<SurfaceData>> AnalyzeAt(
        Surface surface,
        IGeometryContext context,
        IReadOnlyList<(double u, double v)> uvParameters,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCore.ExecuteBatchSurface(surface: surface, context: context, uvParameters: uvParameters, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics);
}
