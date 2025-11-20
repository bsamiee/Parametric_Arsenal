using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Orchestration layer for differential and quality analysis via UnifiedOperation.</summary>
[Pure]
internal static class AnalysisCore {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Execute<T>(Analysis.DifferentialRequest request, IGeometryContext context) =>
        !AnalysisConfig.DifferentialOperations.TryGetValue(request.GetType(), out AnalysisConfig.DifferentialMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unknown request: {request.GetType().Name}"))
            : request switch {
                Analysis.CurveAnalysis r => (Result<T>)(object)ExecuteCurve(request: r, meta: meta, context: context),
                Analysis.SurfaceAnalysis r => (Result<T>)(object)ExecuteSurface(request: r, meta: meta, context: context),
                Analysis.BrepAnalysis r => (Result<T>)(object)ExecuteBrep(request: r, meta: meta, context: context),
                Analysis.ExtrusionAnalysis r => (Result<T>)(object)ExecuteExtrusion(request: r, meta: meta, context: context),
                Analysis.MeshAnalysis r => (Result<T>)(object)ExecuteMesh(request: r, meta: meta, context: context),
                _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unhandled request: {request.GetType().Name}")),
            };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteQuality<T>(Analysis.QualityRequest request, IGeometryContext context) =>
        !AnalysisConfig.QualityOperations.TryGetValue(request.GetType(), out AnalysisConfig.QualityMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unknown quality request type: {request.GetType().Name}. Supported types: SurfaceQualityAnalysis, CurveFairnessAnalysis, MeshQualityAnalysis"))
            : request switch {
                Analysis.SurfaceQualityAnalysis r => (Result<T>)(object)ExecuteSurfaceQuality(surface: r.Surface, meta: meta, context: context),
                Analysis.CurveFairnessAnalysis r => (Result<T>)(object)ExecuteCurveFairness(curve: r.Curve, meta: meta, context: context),
                Analysis.MeshQualityAnalysis r => (Result<T>)(object)ExecuteMeshQuality(mesh: r.Mesh, meta: meta, context: context),
                _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedAnalysis.WithContext($"Unhandled quality request: {request.GetType().Name}")),
            };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> ExecuteBatch<T>(
        IReadOnlyList<T> geometries,
        IGeometryContext context,
        double? parameter,
        (double, double)? uv,
        int? index,
        Point3d? testPoint,
        int derivativeOrder) where T : GeometryBase {
        Analysis.IResult?[] results = [.. geometries.Select(item => item switch {
            Curve c => parameter is double p
                ? Execute<Analysis.CurveData>(request: new Analysis.CurveAnalysis(Curve: c, Parameter: p, DerivativeOrder: derivativeOrder), context: context)
                    .Match(onSuccess: r => (Analysis.IResult?)r, onFailure: _ => null)
                : Execute<Analysis.CurveData>(request: new Analysis.CurveAnalysis(curve: c, derivativeOrder: derivativeOrder), context: context)
                    .Match(onSuccess: r => (Analysis.IResult?)r, onFailure: _ => null),
            Surface s => uv is (double u, double v)
                ? Execute<Analysis.SurfaceData>(request: new Analysis.SurfaceAnalysis(Surface: s, U: u, V: v, DerivativeOrder: derivativeOrder), context: context)
                    .Match(onSuccess: r => (Analysis.IResult?)r, onFailure: _ => null)
                : Execute<Analysis.SurfaceData>(request: new Analysis.SurfaceAnalysis(surface: s, derivativeOrder: derivativeOrder), context: context)
                    .Match(onSuccess: r => (Analysis.IResult?)r, onFailure: _ => null),
            Brep b => ((Func<Analysis.IResult?>)(() => {
                int fIdx = index ?? 0;
                double u = uv?.Item1 ?? (b.Faces.Count > fIdx ? b.Faces[fIdx].Domain(0).Mid : 0.5);
                double v = uv?.Item2 ?? (b.Faces.Count > fIdx ? b.Faces[fIdx].Domain(1).Mid : 0.5);
                Point3d tp = testPoint ?? b.GetBoundingBox(accurate: false).Center;
                return Execute<Analysis.BrepData>(request: new Analysis.BrepAnalysis(Brep: b, FaceIndex: fIdx, U: u, V: v, TestPoint: tp, DerivativeOrder: derivativeOrder), context: context)
                    .Match(onSuccess: r => (Analysis.IResult?)r, onFailure: _ => null);
            }))(),
            Mesh m => index is int idx
                ? Execute<Analysis.MeshData>(request: new Analysis.MeshAnalysis(Mesh: m, VertexIndex: idx), context: context)
                    .Match(onSuccess: r => (Analysis.IResult?)r, onFailure: _ => null)
                : Execute<Analysis.MeshData>(request: new Analysis.MeshAnalysis(mesh: m), context: context)
                    .Match(onSuccess: r => (Analysis.IResult?)r, onFailure: _ => null),
            _ => null,
        }),];
        return results.Any(r => r is null)
            ? ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext("One or more geometries failed analysis"))
            : ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[.. results.Where(r => r is not null).Select(r => r!),]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.CurveData> ExecuteCurve(
        Analysis.CurveAnalysis request,
        AnalysisConfig.DifferentialMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Curve,
            operation: (Func<Curve, Result<IReadOnlyList<Analysis.CurveData>>>)(curve =>
                AnalysisCompute.ComputeCurve(
                    curve: curve,
                    parameter: request.Parameter,
                    derivativeOrder: request.DerivativeOrder,
                    frameSampleCount: meta.FrameSampleCount,
                    maxDiscontinuities: meta.MaxDiscontinuities,
                    context: context).Map(r => (IReadOnlyList<Analysis.CurveData>)[r,])),
            config: new OperationConfig<Curve, Analysis.CurveData> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.SurfaceData> ExecuteSurface(
        Analysis.SurfaceAnalysis request,
        AnalysisConfig.DifferentialMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Surface,
            operation: (Func<Surface, Result<IReadOnlyList<Analysis.SurfaceData>>>)(surface =>
                AnalysisCompute.ComputeSurface(
                    surface: surface,
                    u: request.U,
                    v: request.V,
                    derivativeOrder: request.DerivativeOrder).Map(r => (IReadOnlyList<Analysis.SurfaceData>)[r,])),
            config: new OperationConfig<Surface, Analysis.SurfaceData> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.BrepData> ExecuteBrep(
        Analysis.BrepAnalysis request,
        AnalysisConfig.DifferentialMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Brep,
            operation: (Func<Brep, Result<IReadOnlyList<Analysis.BrepData>>>)(brep =>
                AnalysisCompute.ComputeBrep(
                    brep: brep,
                    faceIndex: request.FaceIndex,
                    u: request.U,
                    v: request.V,
                    testPoint: request.TestPoint,
                    derivativeOrder: request.DerivativeOrder,
                    closestPointToleranceMultiplier: meta.ClosestPointToleranceMultiplier,
                    context: context).Map(r => (IReadOnlyList<Analysis.BrepData>)[r,])),
            config: new OperationConfig<Brep, Analysis.BrepData> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.BrepData> ExecuteExtrusion(
        Analysis.ExtrusionAnalysis request,
        AnalysisConfig.DifferentialMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Extrusion,
            operation: (Func<Extrusion, Result<IReadOnlyList<Analysis.BrepData>>>)(extrusion =>
                AnalysisCompute.ComputeExtrusion(
                    extrusion: extrusion,
                    faceIndex: request.FaceIndex,
                    u: request.U,
                    v: request.V,
                    testPoint: request.TestPoint,
                    derivativeOrder: request.DerivativeOrder,
                    closestPointToleranceMultiplier: meta.ClosestPointToleranceMultiplier,
                    context: context).Map(r => (IReadOnlyList<Analysis.BrepData>)[r,])),
            config: new OperationConfig<Extrusion, Analysis.BrepData> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.MeshData> ExecuteMesh(
        Analysis.MeshAnalysis request,
        AnalysisConfig.DifferentialMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: request.Mesh,
            operation: (Func<Mesh, Result<IReadOnlyList<Analysis.MeshData>>>)(mesh =>
                AnalysisCompute.ComputeMesh(
                    mesh: mesh,
                    vertexIndex: request.VertexIndex).Map(r => (IReadOnlyList<Analysis.MeshData>)[r,])),
            config: new OperationConfig<Mesh, Analysis.MeshData> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.SurfaceQualityResult> ExecuteSurfaceQuality(
        Surface surface,
        AnalysisConfig.QualityMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: surface,
            operation: (Func<Surface, Result<IReadOnlyList<Analysis.SurfaceQualityResult>>>)(s =>
                AnalysisCompute.ComputeSurfaceQuality(
                    surface: s,
                    gridDimension: meta.GridDimension,
                    boundaryFraction: meta.BoundaryFraction,
                    proximityFactor: meta.ProximityFactor,
                    curvatureMultiplier: meta.CurvatureMultiplier,
                    context: context).Map(r => (IReadOnlyList<Analysis.SurfaceQualityResult>)[r,])),
            config: new OperationConfig<Surface, Analysis.SurfaceQualityResult> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.CurveFairnessResult> ExecuteCurveFairness(
        Curve curve,
        AnalysisConfig.QualityMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: curve,
            operation: (Func<Curve, Result<IReadOnlyList<Analysis.CurveFairnessResult>>>)(c =>
                AnalysisCompute.ComputeCurveFairness(
                    curve: c,
                    sampleCount: meta.SampleCount,
                    inflectionThreshold: meta.InflectionThreshold,
                    smoothnessSensitivity: meta.SmoothnessSensitivity,
                    context: context).Map(r => (IReadOnlyList<Analysis.CurveFairnessResult>)[r,])),
            config: new OperationConfig<Curve, Analysis.CurveFairnessResult> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.MeshQualityResult> ExecuteMeshQuality(
        Mesh mesh,
        AnalysisConfig.QualityMetadata meta,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: mesh,
            operation: (Func<Mesh, Result<IReadOnlyList<Analysis.MeshQualityResult>>>)(m =>
                AnalysisCompute.ComputeMeshQuality(
                    mesh: m,
                    context: context).Map(r => (IReadOnlyList<Analysis.MeshQualityResult>)[r,])),
            config: new OperationConfig<Mesh, Analysis.MeshQualityResult> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);
}
