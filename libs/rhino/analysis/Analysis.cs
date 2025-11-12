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
    /// <summary>Analysis result marker with location property.</summary>
    public interface IResult {
        /// <summary>Evaluation point in world coordinates.</summary>
        public Point3d Location { get; }
    }

    /// <summary>Curve differential geometry: derivatives, curvature, frames.</summary>
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

    /// <summary>Surface differential geometry: Gaussian/mean curvature, singularities.</summary>
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

    /// <summary>Brep topology: vertices/edges, manifold state, volume.</summary>
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

    /// <summary>Mesh topology: manifold detection, closure, volume.</summary>
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

    /// <summary>Curve analysis: derivatives, curvature, frames, discontinuities.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveData> Analyze(
        Curve curve,
        IGeometryContext context,
        double? parameter = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) =>
        AnalysisCore.Execute(curve, context, t: parameter, uv: null, index: null, testPoint: null, derivativeOrder: derivativeOrder)
            .Map(results => (CurveData)results[0]);

    /// <summary>Surface analysis: derivatives, curvature, frames, singularities.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceData> Analyze(
        Surface surface,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) =>
        AnalysisCore.Execute(surface, context, t: null, uv: uvParameter, index: null, testPoint: null, derivativeOrder: derivativeOrder)
            .Map(results => (SurfaceData)results[0]);

    /// <summary>Brep analysis: surface evaluation, topology, proximity.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<BrepData> Analyze(
        Brep brep,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int faceIndex = 0,
        Point3d? testPoint = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) =>
        AnalysisCore.Execute(brep, context, t: null, uv: uvParameter, index: faceIndex, testPoint: testPoint, derivativeOrder: derivativeOrder)
            .Map(results => (BrepData)results[0]);

    /// <summary>Mesh analysis: topology navigation, manifold inspection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshData> Analyze(
        Mesh mesh,
        IGeometryContext context,
        int vertexIndex = 0) =>
        AnalysisCore.Execute(mesh, context, t: null, uv: null, index: vertexIndex, testPoint: null, derivativeOrder: 0)
            .Map(results => (MeshData)results[0]);

    /// <summary>Batch analysis for heterogeneous geometry types.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IResult>> AnalyzeMultiple<T>(
        IReadOnlyList<T> geometries,
        IGeometryContext context,
        double? parameter = null,
        (double u, double v)? uvParameter = null,
        int? index = null,
        Point3d? testPoint = null,
        int derivativeOrder = AnalysisConfig.DefaultDerivativeOrder) where T : GeometryBase =>
        UnifiedOperation.Apply(
            geometries,
            (Func<object, Result<IReadOnlyList<IResult>>>)(item =>
                AnalysisCore.Execute(item, context, parameter, uvParameter, index, testPoint, derivativeOrder)),
            new OperationConfig<object, IResult> {
                Context = context,
                ValidationMode = Core.Validation.V.None,
                EnableCache = true,
                AccumulateErrors = false,
                OperationName = "Analysis.Multiple",
                EnableDiagnostics = false,
            });

    /// <summary>Surface quality: curvature distribution, singularities, uniformity score.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double[] GaussianCurvatures, double[] MeanCurvatures, (double U, double V)[] SingularityLocations, double UniformityScore)> AnalyzeSurfaceQuality(
        Surface surface,
        IGeometryContext context) =>
        AnalysisCompute.SurfaceQuality(surface: surface, context: context);

    /// <summary>Curve fairness: smoothness score, curvature samples, inflection points, energy.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double SmoothnessScore, double[] CurvatureValues, (double Parameter, bool IsSharp)[] InflectionPoints, double BendingEnergy)> AnalyzeCurveFairness(
        Curve curve,
        IGeometryContext context) =>
        AnalysisCompute.CurveFairness(curve: curve, context: context);

    /// <summary>Mesh FEA quality: aspect ratios, skewness, Jacobians, problematic elements.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaceIndices, (int WarningCount, int CriticalCount) QualityFlags)> AnalyzeMeshForFEA(
        Mesh mesh,
        IGeometryContext context) =>
        AnalysisCompute.MeshForFEA(mesh: mesh, context: context);
}
