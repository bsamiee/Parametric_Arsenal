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

/// <summary>Differential geometry computation with unified dispatch.</summary>
[Pure]
internal static class AnalysisCore {
    private static readonly FrozenDictionary<Type, DifferentialExecutor> DifferentialDispatch =
        new Dictionary<Type, DifferentialExecutor> {
            [typeof(Curve)] = CreateCurveExecutor(typeof(Curve)),
            [typeof(NurbsCurve)] = CreateCurveExecutor(typeof(NurbsCurve)),
            [typeof(LineCurve)] = CreateCurveExecutor(typeof(LineCurve)),
            [typeof(ArcCurve)] = CreateCurveExecutor(typeof(ArcCurve)),
            [typeof(PolyCurve)] = CreateCurveExecutor(typeof(PolyCurve)),
            [typeof(PolylineCurve)] = CreateCurveExecutor(typeof(PolylineCurve)),
            [typeof(Surface)] = CreateSurfaceExecutor(typeof(Surface)),
            [typeof(NurbsSurface)] = CreateSurfaceExecutor(typeof(NurbsSurface)),
            [typeof(PlaneSurface)] = CreateSurfaceExecutor(typeof(PlaneSurface)),
            [typeof(Brep)] = CreateBrepExecutor(),
            [typeof(Extrusion)] = CreateExtrusionExecutor(),
            [typeof(Mesh)] = CreateMeshExecutor(),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> Execute(
        Analysis.DifferentialRequest request,
        IGeometryContext context) =>
        request switch {
            Analysis.CurveAnalysis r => ExecuteSingle(
                geometry: r.Geometry,
                parameter: r.Parameter,
                uv: null,
                index: null,
                testPoint: null,
                derivativeOrder: r.DerivativeOrder,
                key: typeof(Curve),
                context: context),
            Analysis.SurfaceAnalysis r => ExecuteSingle(
                geometry: r.Geometry,
                parameter: null,
                uv: r.Parameter,
                index: null,
                testPoint: null,
                derivativeOrder: r.DerivativeOrder,
                key: typeof(Surface),
                context: context),
            Analysis.BrepAnalysis r => ExecuteSingle(
                geometry: r.Geometry,
                parameter: null,
                uv: r.Parameter,
                index: r.FaceIndex,
                testPoint: r.TestPoint,
                derivativeOrder: r.DerivativeOrder,
                key: typeof(Brep),
                context: context),
            Analysis.ExtrusionAnalysis r => ExecuteSingle(
                geometry: r.Geometry,
                parameter: null,
                uv: r.Parameter,
                index: r.FaceIndex,
                testPoint: r.TestPoint,
                derivativeOrder: r.DerivativeOrder,
                key: typeof(Extrusion),
                context: context),
            Analysis.MeshAnalysis r => ExecuteSingle(
                geometry: r.Geometry,
                parameter: null,
                uv: null,
                index: r.VertexIndex,
                testPoint: null,
                derivativeOrder: 0,
                key: typeof(Mesh),
                context: context),
            Analysis.BatchAnalysis r => ExecuteBatch(request: r, context: context),
            _ => ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(request.GetType().Name)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] GaussianCurvatures, double[] MeanCurvatures, (double U, double V)[] SingularityLocations, double UniformityScore)> AnalyzeQuality(
        Analysis.SurfaceQualityAnalysis request,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            request.Geometry,
            (Func<Surface, Result<IReadOnlyList<(double[], double[], (double, double)[], double)>>>)(surface =>
                AnalysisCompute.SurfaceQuality(surface: surface, context: context, metadata: AnalysisConfig.SurfaceQuality)
                    .Map(result => (IReadOnlyList<(double[], double[], (double, double)[], double)>)[result])),
            new OperationConfig<Surface, (double[], double[], (double, double)[], double)> {
                Context = context,
                ValidationMode = AnalysisConfig.SurfaceQuality.ValidationMode,
                OperationName = AnalysisConfig.SurfaceQuality.OperationName,
                EnableDiagnostics = false,
                AccumulateErrors = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double SmoothnessScore, double[] CurvatureSamples, (double Parameter, bool IsSharp)[] InflectionPoints, double EnergyMetric)> AnalyzeQuality(
        Analysis.CurveFairnessAnalysis request,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            request.Geometry,
            (Func<Curve, Result<IReadOnlyList<(double, double[], (double, bool)[], double)>>>)(curve =>
                AnalysisCompute.CurveFairness(curve: curve, context: context, metadata: AnalysisConfig.CurveFairness)
                    .Map(result => (IReadOnlyList<(double, double[], (double, bool)[], double)>)[result])),
            new OperationConfig<Curve, (double, double[], (double, bool)[], double)> {
                Context = context,
                ValidationMode = AnalysisConfig.CurveFairness.ValidationMode,
                OperationName = AnalysisConfig.CurveFairness.OperationName,
                EnableDiagnostics = false,
                AccumulateErrors = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaces, (int WarningCount, int CriticalCount) QualityFlags)> AnalyzeQuality(
        Analysis.MeshQualityAnalysis request,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            request.Geometry,
            (Func<Mesh, Result<IReadOnlyList<(double[], double[], double[], int[], (int, int))>>>)(mesh =>
                AnalysisCompute.MeshForFEA(mesh: mesh, context: context, metadata: AnalysisConfig.MeshQuality)
                    .Map(result => (IReadOnlyList<(double[], double[], double[], int[], (int, int))>)[result])),
            new OperationConfig<Mesh, (double[], double[], double[], int[], (int, int))> {
                Context = context,
                ValidationMode = AnalysisConfig.MeshQuality.ValidationMode,
                OperationName = AnalysisConfig.MeshQuality.OperationName,
                EnableDiagnostics = false,
                AccumulateErrors = false,
                EnableCache = false,
                SkipInvalid = false,
            })
            .Map(results => results[0]);

    private static Result<IReadOnlyList<Analysis.IResult>> ExecuteBatch(
        Analysis.BatchAnalysis request,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            request.Geometries,
            (Func<object, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                item is GeometryBase geometry
                    ? ExecuteSingle(
                        geometry: geometry,
                        parameter: request.Parameter,
                        uv: request.UvParameter,
                        index: request.Index,
                        testPoint: request.TestPoint,
                        derivativeOrder: request.DerivativeOrder,
                        key: geometry.GetType(),
                        context: context)
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(item.GetType().Name)))),
            new OperationConfig<object, Analysis.IResult> {
                Context = context,
                ValidationMode = AnalysisConfig.Batch.ValidationMode,
                OperationName = AnalysisConfig.Batch.OperationName,
                EnableDiagnostics = false,
                AccumulateErrors = false,
                EnableCache = true,
                SkipInvalid = false,
            });

    private static Result<IReadOnlyList<Analysis.IResult>> ExecuteSingle(
        GeometryBase geometry,
        double? parameter,
        (double, double)? uv,
        int? index,
        Point3d? testPoint,
        int derivativeOrder,
        Type key,
        IGeometryContext context) =>
        DifferentialDispatch.TryGetValue(key, out DifferentialExecutor? executor)
            ? UnifiedOperation.Apply(
                geometry,
                (Func<GeometryBase, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                    executor.Executor(item, context, parameter, uv, index, testPoint, derivativeOrder)
                        .Map(result => (IReadOnlyList<Analysis.IResult>)[result])),
                new OperationConfig<GeometryBase, Analysis.IResult> {
                    Context = context,
                    ValidationMode = executor.Metadata.ValidationMode,
                    OperationName = executor.Metadata.OperationName,
                    EnableDiagnostics = false,
                    AccumulateErrors = false,
                    EnableCache = false,
                    SkipInvalid = false,
                })
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(key.Name));

    private static DifferentialExecutor CreateCurveExecutor(Type type) {
        AnalysisConfig.DifferentialMetadata metadata = AnalysisConfig.DifferentialOperations[type];
        return new DifferentialExecutor(
            Metadata: metadata,
            Executor: (geometry, context, parameter, _, _, _, derivativeOrder) => AnalysisCompute.CurveDifferential(
                curve: (Curve)geometry,
                context: context,
                parameter: parameter ?? ((Curve)geometry).Domain.Mid,
                derivativeOrder: derivativeOrder,
                metadata: metadata)
                .Map(result => (Analysis.IResult)result));
    }

    private static DifferentialExecutor CreateSurfaceExecutor(Type type) {
        AnalysisConfig.DifferentialMetadata metadata = AnalysisConfig.DifferentialOperations[type];
        return new DifferentialExecutor(
            Metadata: metadata,
            Executor: (geometry, _, _, uv, _, _, derivativeOrder) => {
                Surface surface = (Surface)geometry;
                (double u, double v) = uv ?? (surface.Domain(0).Mid, surface.Domain(1).Mid);
                return AnalysisCompute.SurfaceDifferential(
                    surface: surface,
                    u: u,
                    v: v,
                    derivativeOrder: derivativeOrder)
                    .Map(result => (Analysis.IResult)result);
            });
    }

    private static DifferentialExecutor CreateBrepExecutor() {
        AnalysisConfig.DifferentialMetadata metadata = AnalysisConfig.DifferentialOperations[typeof(Brep)];
        return new DifferentialExecutor(
            Metadata: metadata,
            Executor: (geometry, context, _, uv, index, testPoint, derivativeOrder) => {
                Brep brep = (Brep)geometry;
                (double u, double v) = uv ?? (brep.Faces.Count > 0 ? brep.Faces[0].Domain(0).Mid : 0.5, brep.Faces.Count > 0 ? brep.Faces[0].Domain(1).Mid : 0.5);
                Point3d probe = testPoint ?? brep.GetBoundingBox(accurate: false).Center;
                return AnalysisCompute.BrepDifferential(
                    brep: brep,
                    context: context,
                    uv: (u, v),
                    faceIndex: index ?? 0,
                    testPoint: probe,
                    derivativeOrder: derivativeOrder,
                    toleranceMultiplier: metadata.ClosestPointToleranceMultiplier)
                    .Map(result => (Analysis.IResult)result);
            });
    }

    private static DifferentialExecutor CreateExtrusionExecutor() {
        AnalysisConfig.DifferentialMetadata metadata = AnalysisConfig.DifferentialOperations[typeof(Extrusion)];
        return new DifferentialExecutor(
            Metadata: metadata,
            Executor: (geometry, context, _, uv, index, testPoint, derivativeOrder) =>
                ((Extrusion)geometry).ToBrep() is Brep brep
                    ? AnalysisCompute.BrepDifferential(
                        brep: brep,
                        context: context,
                        uv: uv ?? (brep.Faces.Count > 0 ? brep.Faces[0].Domain(0).Mid : 0.5, brep.Faces.Count > 0 ? brep.Faces[0].Domain(1).Mid : 0.5),
                        faceIndex: index ?? 0,
                        testPoint: testPoint ?? brep.GetBoundingBox(accurate: false).Center,
                        derivativeOrder: derivativeOrder,
                        toleranceMultiplier: metadata.ClosestPointToleranceMultiplier)
                        .Map(result => (Analysis.IResult)result)
                    : ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed)
                        .Map(result => (Analysis.IResult)result));
    }

    private static DifferentialExecutor CreateMeshExecutor() {
        AnalysisConfig.DifferentialMetadata metadata = AnalysisConfig.DifferentialOperations[typeof(Mesh)];
        return new DifferentialExecutor(
            Metadata: metadata,
            Executor: (geometry, _, _, _, index, _, _) => AnalysisCompute.MeshDifferential(
                mesh: (Mesh)geometry,
                vertexIndex: index ?? 0)
                .Map(result => (Analysis.IResult)result));
    }

    private sealed record DifferentialExecutor(
        AnalysisConfig.DifferentialMetadata Metadata,
        Func<GeometryBase, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> Executor);
}
