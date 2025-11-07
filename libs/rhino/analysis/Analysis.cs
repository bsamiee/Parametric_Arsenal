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
    public static Result<IReadOnlyList<IAnalysisResult>> AnalyzeMultiple<T>(
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
            (Func<object, Result<IReadOnlyList<IAnalysisResult>>>)(item =>
                AnalysisCore.Execute(item, context, parameter, uvParameter, index, testPoint, derivativeOrder, enableDiagnostics: enableDiagnostics)),
            new OperationConfig<object, IAnalysisResult> {
                Context = context,
                ValidationMode = Core.Validation.V.None,
                EnableCache = true,
                AccumulateErrors = false,
                OperationName = "Analysis.Multiple",
                EnableDiagnostics = enableDiagnostics,
            });
}
