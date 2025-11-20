using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Orchestration layer for differential geometry analysis via UnifiedOperation.</summary>
[Pure]
internal static class AnalysisCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> Execute(
        object geometry,
        IGeometryContext context,
        double? t,
        (double, double)? uv,
        int? index,
        Point3d? testPoint,
        int derivativeOrder) =>
        !AnalysisConfig.StandardAnalysis.TryGetValue(geometry.GetType(), out AnalysisConfig.StandardAnalysisMetadata? metadata)
            ? ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(
                error: E.Geometry.UnsupportedAnalysis.WithContext(geometry.GetType().Name))
            : UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<object, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                    geometry switch {
                        Curve c => AnalyzeCurve(curve: c, context: context, parameter: t, derivativeOrder: derivativeOrder),
                        Surface s => AnalyzeSurface(surface: s, context: context, uvParameter: uv, derivativeOrder: derivativeOrder),
                        Brep b => AnalyzeBrep(brep: b, context: context, uvParameter: uv, faceIndex: index, testPoint: testPoint, derivativeOrder: derivativeOrder),
                        Extrusion e => AnalyzeExtrusion(extrusion: e, context: context, uvParameter: uv, faceIndex: index, testPoint: testPoint, derivativeOrder: derivativeOrder),
                        Mesh m => AnalyzeMesh(mesh: m, vertexIndex: index),
                        _ => ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(
                            error: E.Geometry.UnsupportedAnalysis.WithContext(geometry.GetType().Name)),
                    }),
                config: new OperationConfig<object, Analysis.IResult> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                    EnableDiagnostics = false,
                    AccumulateErrors = false,
                    EnableCache = false,
                    SkipInvalid = false,
                });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] GaussianCurvatures, double[] MeanCurvatures, (double U, double V)[] SingularityLocations, double UniformityScore)> ExecuteSurfaceQuality(
        Surface surface,
        IGeometryContext context) =>
        !AnalysisConfig.QualityAnalysis.TryGetValue(typeof(Surface), out AnalysisConfig.QualityAnalysisMetadata? metadata)
            ? ResultFactory.Create<(double[], double[], (double, double)[], double)>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Surface quality analysis metadata not found"))
            : UnifiedOperation.Apply(
                input: surface,
                operation: (Func<Surface, Result<IReadOnlyList<(double[], double[], (double, double)[], double)>>>)(item =>
                    AnalysisCompute.SurfaceQuality(surface: item, context: context)
                        .Map(result => (IReadOnlyList<(double[], double[], (double, double)[], double)>)[result,])),
                config: new OperationConfig<Surface, (double[], double[], (double, double)[], double)> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double SmoothnessScore, double[] CurvatureValues, (double Parameter, bool IsSharp)[] InflectionPoints, double BendingEnergy)> ExecuteCurveFairness(
        Curve curve,
        IGeometryContext context) =>
        !AnalysisConfig.QualityAnalysis.TryGetValue(typeof(Curve), out AnalysisConfig.QualityAnalysisMetadata? metadata)
            ? ResultFactory.Create<(double, double[], (double, bool)[], double)>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Curve fairness analysis metadata not found"))
            : UnifiedOperation.Apply(
                input: curve,
                operation: (Func<Curve, Result<IReadOnlyList<(double, double[], (double, bool)[], double)>>>)(item =>
                    AnalysisCompute.CurveFairness(curve: item, context: context)
                        .Map(result => (IReadOnlyList<(double, double[], (double, bool)[], double)>)[result,])),
                config: new OperationConfig<Curve, (double, double[], (double, bool)[], double)> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaceIndices, (int WarningCount, int CriticalCount) QualityFlags)> ExecuteMeshFEA(
        Mesh mesh,
        IGeometryContext context) =>
        !AnalysisConfig.QualityAnalysis.TryGetValue(typeof(Mesh), out AnalysisConfig.QualityAnalysisMetadata? metadata)
            ? ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(
                error: E.Geometry.UnsupportedAnalysis.WithContext("Mesh FEA analysis metadata not found"))
            : UnifiedOperation.Apply(
                input: mesh,
                operation: (Func<Mesh, Result<IReadOnlyList<(double[], double[], double[], int[], (int, int))>>>)(item =>
                    AnalysisCompute.MeshForFEA(mesh: item, context: context)
                        .Map(result => (IReadOnlyList<(double[], double[], double[], int[], (int, int))>)[result,])),
                config: new OperationConfig<Mesh, (double[], double[], double[], int[], (int, int))> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                    EnableDiagnostics = false,
                }).Map(static results => results[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Analysis.IResult>> AnalyzeCurve(
        Curve curve,
        IGeometryContext context,
        double? parameter,
        int derivativeOrder) {
        double param = parameter ?? curve.Domain.Mid;
        double[] buffer = ArrayPool<double>.Shared.Rent(AnalysisConfig.MaxDiscontinuities);
        try {
            (int discCount, double s) = (0, curve.Domain.Min);
            while (discCount < AnalysisConfig.MaxDiscontinuities && curve.GetNextDiscontinuity(Continuity.C1_continuous, s, curve.Domain.Max, out double td)) {
                buffer[discCount++] = td;
                s = td + context.AbsoluteTolerance;
            }
            double[] disc = [.. buffer[..discCount]];
            return curve.FrameAt(param, out Plane frame)
                ? ((Func<Result<IReadOnlyList<Analysis.IResult>>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(curve);
                    Vector3d[] derivatives = curve.DerivativeAt(param, derivativeOrder) is Vector3d[] d ? d : [];
                    double[] frameParams = [.. Enumerable.Range(0, AnalysisConfig.CurveFrameSampleCount)
                        .Select(i => curve.Domain.ParameterAt(AnalysisConfig.CurveFrameSampleCount > 1 ? i / (AnalysisConfig.CurveFrameSampleCount - 1.0) : 0.5)),];
                    Plane[] frames = curve.GetPerpendicularFrames(frameParams) is Plane[] pf ? pf : [];
                    return amp is not null
                        ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[new Analysis.CurveData(
                            Location: curve.PointAt(param),
                            Derivatives: derivatives,
                            Curvature: curve.CurvatureAt(param).Length,
                            Frame: frame,
                            PerpendicularFrames: frames,
                            Torsion: curve.TorsionAt(param),
                            DiscontinuityParameters: disc,
                            DiscontinuityTypes: [.. disc.Select(dp => curve.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                            Length: curve.GetLength(),
                            Centroid: amp.Centroid),])
                        : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.CurveAnalysisFailed);
                }))()
                : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.CurveAnalysisFailed);
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Analysis.IResult>> AnalyzeSurface(
        Surface surface,
        IGeometryContext context,
        (double, double)? uvParameter,
        int derivativeOrder) {
        (double u, double v) = uvParameter ?? (surface.Domain(0).Mid, surface.Domain(1).Mid);
        return surface.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) && surface.FrameAt(u, v, out Plane frame)
            ? ((Func<Result<IReadOnlyList<Analysis.IResult>>>)(() => {
                SurfaceCurvature sc = surface.CurvatureAt(u, v);
                using AreaMassProperties? amp = AreaMassProperties.Compute(surface);
                return amp is not null && RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[new Analysis.SurfaceData(
                        Location: surface.PointAt(u, v),
                        Derivatives: derivs,
                        Gaussian: sc.Gaussian,
                        Mean: sc.Mean,
                        K1: sc.Kappa(0),
                        K2: sc.Kappa(1),
                        PrincipalDir1: sc.Direction(0),
                        PrincipalDir2: sc.Direction(1),
                        Frame: frame,
                        Normal: frame.Normal,
                        AtSeam: surface.IsAtSeam(u, v) != 0,
                        AtSingularity: surface.IsAtSingularity(u, v, exact: true),
                        Area: amp.Area,
                        Centroid: amp.Centroid),])
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.SurfaceAnalysisFailed);
            }))()
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.SurfaceAnalysisFailed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Analysis.IResult>> AnalyzeBrep(
        Brep brep,
        IGeometryContext context,
        (double, double)? uvParameter,
        int? faceIndex,
        Point3d? testPoint,
        int derivativeOrder) {
        int fIdx = RhinoMath.Clamp(faceIndex ?? 0, 0, brep.Faces.Count - 1);
        using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
        (double u, double v) = uvParameter ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
        Point3d testPt = testPoint ?? brep.GetBoundingBox(accurate: false).Center;
        return sf.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) && sf.FrameAt(u, v, out Plane frame) &&
            brep.ClosestPoint(testPt, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, context.AbsoluteTolerance * AnalysisConfig.BrepClosestPointToleranceMultiplier, out Vector3d _)
            ? ((Func<Result<IReadOnlyList<Analysis.IResult>>>)(() => {
                SurfaceCurvature sc = sf.CurvatureAt(u, v);
                using AreaMassProperties? amp = AreaMassProperties.Compute(brep);
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                return amp is not null && vmp is not null && RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)
                    ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[new Analysis.BrepData(
                        Location: sf.PointAt(u, v),
                        Derivatives: derivs,
                        Gaussian: sc.Gaussian,
                        Mean: sc.Mean,
                        K1: sc.Kappa(0),
                        K2: sc.Kappa(1),
                        PrincipalDir1: sc.Direction(0),
                        PrincipalDir2: sc.Direction(1),
                        Frame: frame,
                        Normal: frame.Normal,
                        Vertices: [.. brep.Vertices.Select((vtx, i) => (i, vtx.Location)),],
                        Edges: [.. brep.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))),],
                        IsManifold: brep.IsManifold,
                        IsSolid: brep.IsSolid,
                        ClosestPoint: cp,
                        Distance: testPt.DistanceTo(cp),
                        Component: ci,
                        SurfaceUV: (uOut, vOut),
                        Area: amp.Area,
                        Volume: vmp.Volume,
                        Centroid: vmp.Centroid),])
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.BrepAnalysisFailed);
            }))()
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.BrepAnalysisFailed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Analysis.IResult>> AnalyzeExtrusion(
        Extrusion extrusion,
        IGeometryContext context,
        (double, double)? uvParameter,
        int? faceIndex,
        Point3d? testPoint,
        int derivativeOrder) =>
        extrusion.ToBrep() is Brep extrusionBrep
            ? ((Func<Result<IReadOnlyList<Analysis.IResult>>>)(() => {
                using Brep brep = extrusionBrep;
                return AnalyzeBrep(brep: brep, context: context, uvParameter: uvParameter, faceIndex: faceIndex, testPoint: testPoint, derivativeOrder: derivativeOrder);
            }))()
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.BrepAnalysisFailed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Analysis.IResult>> AnalyzeMesh(
        Mesh mesh,
        int? vertexIndex) {
        int vIdx = RhinoMath.Clamp(vertexIndex ?? 0, 0, mesh.Vertices.Count - 1);
        Vector3d normal = mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis;
        return ((Func<Result<IReadOnlyList<Analysis.IResult>>>)(() => {
            using AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
            using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
            return amp is not null && vmp is not null
                ? ResultFactory.Create(value: (IReadOnlyList<Analysis.IResult>)[new Analysis.MeshData(
                    Location: mesh.Vertices[vIdx],
                    Frame: new Plane(mesh.Vertices[vIdx], normal),
                    Normal: normal,
                    TopologyVertices: [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])),],
                    TopologyEdges: [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))),],
                    IsManifold: mesh.IsManifold(topologicalTest: true, out bool _, out bool _),
                    IsClosed: mesh.IsClosed,
                    Area: amp.Area,
                    Volume: vmp.Volume),])
                : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.MeshAnalysisFailed);
        }))();
    }
}
