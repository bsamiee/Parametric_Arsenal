using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Orchestration layer for differential geometry analysis via UnifiedOperation.</summary>
[Pure]
internal static class AnalysisCore {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.IResult> Execute<T>(T geometry, Analysis.Request request, IGeometryContext context) where T : GeometryBase {
        Type geometryType = geometry.GetType();
        Type requestType = request.GetType();

        return AnalysisConfig.Operations.TryGetValue((geometryType, requestType), out AnalysisConfig.AnalysisOperationMetadata? exactMeta)
            ? ExecuteWithMetadata(geometry: geometry, request: request, meta: exactMeta, context: context)
            : AnalysisConfig.Operations.FirstOrDefault(kv => kv.Key.Request == requestType && kv.Key.Geometry.IsAssignableFrom(geometryType)) is { } baseMatch
                ? ExecuteWithMetadata(geometry: geometry, request: request, meta: baseMatch.Value, context: context)
                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"{geometryType.Name} + {requestType.Name}"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.IResult> ExecuteWithMetadata<T>(T geometry, Analysis.Request request, AnalysisConfig.AnalysisOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        request switch {
            Analysis.CurveAnalysis curveReq =>
                UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<Analysis.IResult>>>)(g =>
                        AnalysisCompute.CurveDifferential(
                            curve: (Curve)(object)g,
                            parameter: curveReq.Parameter,
                            derivativeOrder: curveReq.DerivativeOrder,
                            context: context)
                        .Map(r => (IReadOnlyList<Analysis.IResult>)[r,])),
                    config: new OperationConfig<T, Analysis.IResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0]),

            Analysis.CurveFairnessAnalysis =>
                UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<Analysis.IResult>>>)(g =>
                        AnalysisCompute.CurveFairness(
                            curve: (Curve)(object)g,
                            context: context)
                        .Map(r => (IReadOnlyList<Analysis.IResult>)[r,])),
                    config: new OperationConfig<T, Analysis.IResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0]),

            Analysis.SurfaceAnalysis surfaceReq =>
                UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<Analysis.IResult>>>)(g =>
                        AnalysisCompute.SurfaceDifferential(
                            surface: (Surface)(object)g,
                            parameter: surfaceReq.Parameter,
                            derivativeOrder: surfaceReq.DerivativeOrder,
                            context: context)
                        .Map(r => (IReadOnlyList<Analysis.IResult>)[r,])),
                    config: new OperationConfig<T, Analysis.IResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0]),

            Analysis.SurfaceQualityAnalysis =>
                UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<Analysis.IResult>>>)(g =>
                        AnalysisCompute.SurfaceQuality(
                            surface: (Surface)(object)g,
                            context: context)
                        .Map(r => (IReadOnlyList<Analysis.IResult>)[r,])),
                    config: new OperationConfig<T, Analysis.IResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0]),

            Analysis.BrepAnalysis brepReq =>
                UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<Analysis.IResult>>>)(g =>
                        AnalysisCompute.BrepTopology(
                            brep: (Brep)(object)g,
                            parameter: brepReq.Parameter,
                            faceIndex: brepReq.FaceIndex,
                            testPoint: brepReq.TestPoint,
                            derivativeOrder: brepReq.DerivativeOrder,
                            context: context)
                        .Map(r => (IReadOnlyList<Analysis.IResult>)[r,])),
                    config: new OperationConfig<T, Analysis.IResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0]),

            Analysis.MeshAnalysis meshReq =>
                UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<Analysis.IResult>>>)(g =>
                        AnalysisCompute.MeshTopology(
                            mesh: (Mesh)(object)g,
                            vertexIndex: meshReq.VertexIndex,
                            context: context)
                        .Map(r => (IReadOnlyList<Analysis.IResult>)[r,])),
                    config: new OperationConfig<T, Analysis.IResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0]),

            Analysis.MeshFEAAnalysis =>
                UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<Analysis.IResult>>>)(g =>
                        AnalysisCompute.MeshForFEA(
                            mesh: (Mesh)(object)g,
                            context: context)
                        .Map(r => (IReadOnlyList<Analysis.IResult>)[r,])),
                    config: new OperationConfig<T, Analysis.IResult> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    }).Map(static r => r[0]),

            _ => ResultFactory.Create<Analysis.IResult>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unknown request: {request.GetType().Name}")),
        };
}
