using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic analysis engine with geometry-specific overloads and unified internal dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Analysis is the primary API entry point for the Analysis namespace")]
public static class Analysis {
    /// <summary>Analysis result marker interface for polymorphic return discrimination.</summary>
    public interface IResult {
        public Point3d Location { get; }
    }

    /// <summary>Curve analysis result containing derivatives, curvature, frame data, discontinuities, and metrics.</summary>
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
        Point3d Centroid) : IResult;

    /// <summary>Surface analysis result containing derivatives, principal curvatures, frame data, singularity detection, and metrics.</summary>
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
        Point3d Centroid) : IResult;

    /// <summary>Brep analysis result containing surface evaluation, topology navigation, proximity data, and solid metrics.</summary>
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
        Point3d Centroid) : IResult;

    /// <summary>Mesh analysis result containing topology navigation, manifold state, and volume metrics.</summary>
    public sealed record MeshData(
        Point3d Location,
        Plane Frame,
        Vector3d Normal,
        (int Index, Point3d Point)[] TopologyVertices,
        (int Index, Line Geometry)[] TopologyEdges,
        bool IsManifold,
        bool IsClosed,
        double Area,
        double Volume) : IResult;

    /// <summary>Analyzes curve geometry producing comprehensive derivative, curvature, frame, and discontinuity data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<CurveData> Analyze(
        Curve curve,
        IGeometryContext context,
        double? parameter = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCompute.Execute(curve, context, t: parameter, uv: null, index: null, testPoint: null, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics)
            .Map(results => (CurveData)results[0]);

    /// <summary>Analyzes surface geometry producing comprehensive derivative, curvature, frame, and singularity data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<SurfaceData> Analyze(
        Surface surface,
        IGeometryContext context,
        (double u, double v)? uvParameter = null,
        int derivativeOrder = 2,
        bool enableDiagnostics = false) =>
        AnalysisCompute.Execute(surface, context, t: null, uv: uvParameter, index: null, testPoint: null, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics)
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
        AnalysisCompute.Execute(brep, context, t: null, uv: uvParameter, index: faceIndex, testPoint: testPoint, derivativeOrder: derivativeOrder, enableDiagnostics: enableDiagnostics)
            .Map(results => (BrepData)results[0]);

    /// <summary>Analyzes mesh geometry producing comprehensive topology navigation and manifold inspection data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MeshData> Analyze(
        Mesh mesh,
        IGeometryContext context,
        int vertexIndex = 0,
        bool enableDiagnostics = false) =>
        AnalysisCompute.Execute(mesh, context, t: null, uv: null, index: vertexIndex, testPoint: null, derivativeOrder: 0, enableDiagnostics: enableDiagnostics)
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
                AnalysisCompute.Execute(item, context, parameter, uvParameter, index, testPoint, derivativeOrder, enableDiagnostics: enableDiagnostics)),
            new OperationConfig<object, IResult> {
                Context = context,
                ValidationMode = Core.Validation.V.None,
                EnableCache = true,
                AccumulateErrors = false,
                OperationName = "Analysis.Multiple",
                EnableDiagnostics = enableDiagnostics,
            });
}
