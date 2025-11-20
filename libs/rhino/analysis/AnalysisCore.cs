using System;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Differential geometry computation with pooled buffers and dispatch.</summary>
internal static class AnalysisCore {
    private static readonly FrozenDictionary<Type, AnalysisConfig.DifferentialMetadata> DifferentialModes = AnalysisConfig.DifferentialModes;

    private static readonly FrozenDictionary<Type, AnalysisConfig.QualityMetadata> QualityModes = AnalysisConfig.QualityModes;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurveData> AnalyzeCurve(Analysis.CurveAnalysisRequest request, IGeometryContext context) =>
        DifferentialModes.TryGetValue(request.Curve.GetType(), out AnalysisConfig.DifferentialMetadata metadata)
            ? ExecuteSingle(
                geometry: request.Curve,
                context: context,
                metadata: metadata,
                compute: curve => AnalysisCompute.ComputeCurveData(curve: curve, context: context, parameter: request.Parameter, derivativeOrder: request.DerivativeOrder),
                error: E.Geometry.CurveAnalysisFailed)
            : ResultFactory.Create<Analysis.CurveData>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.Curve.GetType().Name));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.SurfaceData> AnalyzeSurface(Analysis.SurfaceAnalysisRequest request, IGeometryContext context) =>
        DifferentialModes.TryGetValue(request.Surface.GetType(), out AnalysisConfig.DifferentialMetadata metadata)
            ? ExecuteSingle(
                geometry: request.Surface,
                context: context,
                metadata: metadata,
                compute: surface => AnalysisCompute.ComputeSurfaceData(surface: surface, context: context, parameter: request.Parameter, derivativeOrder: request.DerivativeOrder),
                error: E.Geometry.SurfaceAnalysisFailed)
            : ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.Surface.GetType().Name));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.BrepData> AnalyzeBrep(Analysis.BrepAnalysisRequest request, IGeometryContext context) =>
        DifferentialModes.TryGetValue(request.Brep.GetType(), out AnalysisConfig.DifferentialMetadata metadata)
            ? ExecuteSingle(
                geometry: request.Brep,
                context: context,
                metadata: metadata,
                compute: brep => AnalysisCompute.ComputeBrepData(brep: brep, context: context, parameter: request.Parameter, faceIndex: request.FaceIndex, testPoint: request.TestPoint, derivativeOrder: request.DerivativeOrder),
                error: E.Geometry.BrepAnalysisFailed)
            : ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.Brep.GetType().Name));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.BrepData> AnalyzeExtrusion(Analysis.ExtrusionAnalysisRequest request, IGeometryContext context) =>
        DifferentialModes.TryGetValue(request.Extrusion.GetType(), out AnalysisConfig.DifferentialMetadata metadata)
            ? ExecuteSingle(
                geometry: request.Extrusion,
                context: context,
                metadata: metadata,
                compute: extrusion => AnalysisCompute.ComputeExtrusionData(extrusion: extrusion, context: context, parameter: request.Parameter, faceIndex: request.FaceIndex, testPoint: request.TestPoint, derivativeOrder: request.DerivativeOrder),
                error: E.Geometry.BrepAnalysisFailed)
            : ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.Extrusion.GetType().Name));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.MeshData> AnalyzeMesh(Analysis.MeshAnalysisRequest request, IGeometryContext context) =>
        DifferentialModes.TryGetValue(request.Mesh.GetType(), out AnalysisConfig.DifferentialMetadata metadata)
            ? ExecuteSingle(
                geometry: request.Mesh,
                context: context,
                metadata: metadata,
                compute: mesh => AnalysisCompute.ComputeMeshData(mesh: mesh, context: context, vertexIndex: request.VertexIndex),
                error: E.Geometry.MeshAnalysisFailed)
            : ResultFactory.Create<Analysis.MeshData>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.Mesh.GetType().Name));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<object>> AnalyzeMultiple(IReadOnlyList<Analysis.DifferentialRequest> requests, IGeometryContext context) =>
        UnifiedOperation.Apply(
            requests,
            (Func<Analysis.DifferentialRequest, Result<IReadOnlyList<object>>>)(request => request switch {
                Analysis.CurveAnalysisRequest r => AnalyzeCurve(request: r, context: context).Map(value => (IReadOnlyList<object>)[value]),
                Analysis.SurfaceAnalysisRequest r => AnalyzeSurface(request: r, context: context).Map(value => (IReadOnlyList<object>)[value]),
                Analysis.BrepAnalysisRequest r => AnalyzeBrep(request: r, context: context).Map(value => (IReadOnlyList<object>)[value]),
                Analysis.ExtrusionAnalysisRequest r => AnalyzeExtrusion(request: r, context: context).Map(value => (IReadOnlyList<object>)[value]),
                Analysis.MeshAnalysisRequest r => AnalyzeMesh(request: r, context: context).Map(value => (IReadOnlyList<object>)[value]),
                _ => ResultFactory.Create<IReadOnlyList<object>>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.GetType().Name)),
            }),
            new OperationConfig<Analysis.DifferentialRequest, object> {
                Context = context,
                ValidationMode = V.None,
                OperationName = AnalysisConfig.MultipleOperationName,
                EnableDiagnostics = false,
                AccumulateErrors = false,
                EnableCache = false,
                SkipInvalid = false,
            });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.SurfaceQualityResult> AnalyzeSurfaceQuality(Analysis.SurfaceQualityRequest request, IGeometryContext context) =>
        ExecuteQuality(
            geometry: request.Surface,
            context: context,
            metadata: QualityModes[typeof(Analysis.SurfaceQualityRequest)],
            compute: surface => AnalysisCompute.ComputeSurfaceQuality(surface: surface, context: context),
            error: E.Geometry.SurfaceAnalysisFailed);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurveFairnessResult> AnalyzeCurveFairness(Analysis.CurveFairnessRequest request, IGeometryContext context) =>
        ExecuteQuality(
            geometry: request.Curve,
            context: context,
            metadata: QualityModes[typeof(Analysis.CurveFairnessRequest)],
            compute: curve => AnalysisCompute.ComputeCurveFairness(curve: curve, context: context),
            error: E.Geometry.CurveAnalysisFailed);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.MeshFeaResult> AnalyzeMeshForFea(Analysis.MeshFeaRequest request, IGeometryContext context) =>
        ExecuteQuality(
            geometry: request.Mesh,
            context: context,
            metadata: QualityModes[typeof(Analysis.MeshFeaRequest)],
            compute: mesh => AnalysisCompute.ComputeMeshFea(mesh: mesh, context: context),
            error: E.Geometry.MeshAnalysisFailed);

    private static Result<TResult> ExecuteSingle<TGeom, TResult>(
        TGeom geometry,
        IGeometryContext context,
        AnalysisConfig.DifferentialMetadata metadata,
        Func<TGeom, TResult?> compute,
        SystemError error) where TGeom : GeometryBase where TResult : class =>
        UnifiedOperation.Apply(
            geometry,
            (Func<TGeom, Result<IReadOnlyList<TResult>>>)(input => compute(input) is TResult result
                ? ResultFactory.Create(value: (IReadOnlyList<TResult>)[result])
                : ResultFactory.Create<IReadOnlyList<TResult>>(error: error)),
            new OperationConfig<TGeom, TResult> {
                Context = context,
                ValidationMode = metadata.ValidationMode,
                OperationName = metadata.OperationName,
                EnableDiagnostics = false,
                AccumulateErrors = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);

    private static Result<TResult> ExecuteQuality<TGeom, TResult>(
        TGeom geometry,
        IGeometryContext context,
        AnalysisConfig.QualityMetadata metadata,
        Func<TGeom, TResult?> compute,
        SystemError error) where TGeom : GeometryBase where TResult : class =>
        UnifiedOperation.Apply(
            geometry,
            (Func<TGeom, Result<IReadOnlyList<TResult>>>)(input => compute(input) is TResult result
                ? ResultFactory.Create(value: (IReadOnlyList<TResult>)[result])
                : ResultFactory.Create<IReadOnlyList<TResult>>(error: error)),
            new OperationConfig<TGeom, TResult> {
                Context = context,
                ValidationMode = metadata.ValidationMode,
                OperationName = metadata.OperationName,
                EnableDiagnostics = false,
                AccumulateErrors = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);
}
