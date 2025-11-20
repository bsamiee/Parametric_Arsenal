using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Differential geometry orchestration and metadata-driven dispatch.</summary>
internal static class AnalysisCore {
    private static readonly FrozenDictionary<Type, Func<Analysis.DifferentialRequest, IGeometryContext, Result<Analysis.IResult>>> DifferentialExecutors =
        new Dictionary<Type, Func<Analysis.DifferentialRequest, IGeometryContext, Result<Analysis.IResult>>> {
            [typeof(Analysis.CurveAnalysis)] = (request, context) => AnalyzeCurve((Analysis.CurveAnalysis)request, context),
            [typeof(Analysis.SurfaceAnalysis)] = (request, context) => AnalyzeSurface((Analysis.SurfaceAnalysis)request, context),
            [typeof(Analysis.BrepAnalysis)] = (request, context) => AnalyzeBrep((Analysis.BrepAnalysis)request, context),
            [typeof(Analysis.ExtrusionAnalysis)] = (request, context) => AnalyzeExtrusion((Analysis.ExtrusionAnalysis)request, context),
            [typeof(Analysis.MeshAnalysis)] = (request, context) => AnalyzeMesh((Analysis.MeshAnalysis)request, context),
        }.ToFrozenDictionary();

    [Pure]
    internal static Result<Analysis.IResult> Analyze(Analysis.DifferentialRequest request, IGeometryContext context) =>
        DifferentialExecutors.TryGetValue(request.GetType(), out Func<Analysis.DifferentialRequest, IGeometryContext, Result<Analysis.IResult>> executor)
            ? executor(request, context)
            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.GetType().Name));

    [Pure]
    internal static Result<IReadOnlyList<Analysis.IResult>> AnalyzeMany(IReadOnlyList<Analysis.DifferentialRequest> requests, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: requests,
            operation: (Func<Analysis.DifferentialRequest, Result<IReadOnlyList<Analysis.IResult>>>)(request =>
                Analyze(request: request, context: context).Map(result => (IReadOnlyList<Analysis.IResult>)[result])),
            config: new OperationConfig<Analysis.DifferentialRequest, Analysis.IResult> {
                Context = context,
                ValidationMode = V.None,
                OperationName = AnalysisConfig.MultipleOperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = true,
                SkipInvalid = false,
            });

    [Pure]
    internal static Result<Analysis.SurfaceQualityResult> AnalyzeSurfaceQuality(Analysis.SurfaceQualityRequest request, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Surface, Result<IReadOnlyList<Analysis.SurfaceQualityResult>>>)(surface =>
                AnalysisCompute.TrySurfaceQuality(surface, context, out Analysis.SurfaceQualityResult result, out string? reason)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.SurfaceQualityResult>)[result])
                    : ResultFactory.Create<IReadOnlyList<Analysis.SurfaceQualityResult>>(error: E.Geometry.SurfaceAnalysisFailed.WithContext(reason ?? AnalysisConfig.SurfaceQualityMetadata.OperationName))),
            config: new OperationConfig<Surface, Analysis.SurfaceQualityResult> {
                Context = context,
                ValidationMode = AnalysisConfig.SurfaceQualityMetadata.ValidationMode,
                OperationName = AnalysisConfig.SurfaceQualityMetadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<Analysis.CurveFairnessResult> AnalyzeCurveFairness(Analysis.CurveFairnessRequest request, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Curve, Result<IReadOnlyList<Analysis.CurveFairnessResult>>>)(curve =>
                AnalysisCompute.TryCurveFairness(curve, context, out Analysis.CurveFairnessResult result, out string? reason)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.CurveFairnessResult>)[result])
                    : ResultFactory.Create<IReadOnlyList<Analysis.CurveFairnessResult>>(error: E.Geometry.CurveAnalysisFailed.WithContext(reason ?? AnalysisConfig.CurveFairnessMetadata.OperationName))),
            config: new OperationConfig<Curve, Analysis.CurveFairnessResult> {
                Context = context,
                ValidationMode = AnalysisConfig.CurveFairnessMetadata.ValidationMode,
                OperationName = AnalysisConfig.CurveFairnessMetadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);

    [Pure]
    internal static Result<Analysis.MeshElementQualityResult> AnalyzeMeshForFEA(Analysis.MeshElementQualityRequest request, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Mesh, Result<IReadOnlyList<Analysis.MeshElementQualityResult>>>)(mesh =>
                AnalysisCompute.TryMeshForFEA(mesh, context, out Analysis.MeshElementQualityResult result)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.MeshElementQualityResult>)[result])
                    : ResultFactory.Create<IReadOnlyList<Analysis.MeshElementQualityResult>>(error: E.Geometry.MeshAnalysisFailed)),
            config: new OperationConfig<Mesh, Analysis.MeshElementQualityResult> {
                Context = context,
                ValidationMode = AnalysisConfig.MeshElementQualityMetadata.ValidationMode,
                OperationName = AnalysisConfig.MeshElementQualityMetadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);

    private static Result<Analysis.IResult> AnalyzeCurve(Analysis.CurveAnalysis request, IGeometryContext context) {
        DifferentialGeometryMetadata metadata = AnalysisConfig.DifferentialGeometry.TryGetValue(request.Geometry.GetType(), out DifferentialGeometryMetadata found)
            ? found
            : AnalysisConfig.DifferentialGeometry[typeof(Curve)];

        return UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Curve, Result<IReadOnlyList<Analysis.IResult>>>)(curve =>
                AnalysisCompute.TryCurveData(curve, context, request.Parameter, request.DerivativeOrder, out Analysis.CurveData data)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[data])
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.CurveAnalysisFailed)),
            config: new OperationConfig<Curve, Analysis.IResult> {
                Context = context,
                ValidationMode = metadata.ValidationMode,
                OperationName = metadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);
    }

    private static Result<Analysis.IResult> AnalyzeSurface(Analysis.SurfaceAnalysis request, IGeometryContext context) {
        DifferentialGeometryMetadata metadata = AnalysisConfig.DifferentialGeometry.TryGetValue(request.Geometry.GetType(), out DifferentialGeometryMetadata found)
            ? found
            : AnalysisConfig.DifferentialGeometry[typeof(Surface)];

        return UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Surface, Result<IReadOnlyList<Analysis.IResult>>>)(surface =>
                AnalysisCompute.TrySurfaceData(surface, context, request.Parameter, request.DerivativeOrder, out Analysis.SurfaceData data)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[data])
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.SurfaceAnalysisFailed)),
            config: new OperationConfig<Surface, Analysis.IResult> {
                Context = context,
                ValidationMode = metadata.ValidationMode,
                OperationName = metadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);
    }

    private static Result<Analysis.IResult> AnalyzeBrep(Analysis.BrepAnalysis request, IGeometryContext context) {
        DifferentialGeometryMetadata metadata = AnalysisConfig.DifferentialGeometry.TryGetValue(request.Geometry.GetType(), out DifferentialGeometryMetadata found)
            ? found
            : AnalysisConfig.DifferentialGeometry[typeof(Brep)];

        return UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Brep, Result<IReadOnlyList<Analysis.IResult>>>)(brep =>
                AnalysisCompute.TryBrepData(brep, context, request.Parameter, request.FaceIndex, request.TestPoint, request.DerivativeOrder, out Analysis.BrepData data)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[data])
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.BrepAnalysisFailed)),
            config: new OperationConfig<Brep, Analysis.IResult> {
                Context = context,
                ValidationMode = metadata.ValidationMode,
                OperationName = metadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);
    }

    private static Result<Analysis.IResult> AnalyzeExtrusion(Analysis.ExtrusionAnalysis request, IGeometryContext context) {
        DifferentialGeometryMetadata metadata = AnalysisConfig.DifferentialGeometry.TryGetValue(request.Geometry.GetType(), out DifferentialGeometryMetadata found)
            ? found
            : AnalysisConfig.DifferentialGeometry[typeof(Extrusion)];

        return UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Extrusion, Result<IReadOnlyList<Analysis.IResult>>>)(extrusion =>
                AnalysisCompute.TryExtrusionData(extrusion, context, request.Parameter, request.FaceIndex, request.TestPoint, request.DerivativeOrder, out Analysis.BrepData data)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[data])
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.BrepAnalysisFailed)),
            config: new OperationConfig<Extrusion, Analysis.IResult> {
                Context = context,
                ValidationMode = metadata.ValidationMode,
                OperationName = metadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);
    }

    private static Result<Analysis.IResult> AnalyzeMesh(Analysis.MeshAnalysis request, IGeometryContext context) {
        DifferentialGeometryMetadata metadata = AnalysisConfig.DifferentialGeometry.TryGetValue(request.Geometry.GetType(), out DifferentialGeometryMetadata found)
            ? found
            : AnalysisConfig.DifferentialGeometry[typeof(Mesh)];

        return UnifiedOperation.Apply(
            input: request.Geometry,
            operation: (Func<Mesh, Result<IReadOnlyList<Analysis.IResult>>>)(mesh =>
                AnalysisCompute.TryMeshData(mesh, context, request.VertexIndex, out Analysis.MeshData data)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[data])
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.MeshAnalysisFailed)),
            config: new OperationConfig<Mesh, Analysis.IResult> {
                Context = context,
                ValidationMode = metadata.ValidationMode,
                OperationName = metadata.OperationName,
                AccumulateErrors = false,
                EnableDiagnostics = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);
    }
}
