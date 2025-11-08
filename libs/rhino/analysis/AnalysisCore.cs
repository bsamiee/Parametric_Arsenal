using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Diagnostics;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Differential geometry computation with ArrayPool buffers and FrozenDictionary dispatch.</summary>
internal static class AnalysisCore {
    /// <summary>Type-driven strategy lookup mapping geometry types to validation modes and computation functions.</summary>
    private static readonly FrozenDictionary<Type, (V Mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> Compute)> _strategies =
        ((Func<Curve, IGeometryContext, double?, int, Result<Analysis.IResult>>)((cv, ctx, t, order) => {
            double param = t ?? cv.Domain.Mid;
            double[] buffer = ArrayPool<double>.Shared.Rent(AnalysisConfig.MaxDiscontinuities);
            try {
                (int discCount, double s) = (0, cv.Domain.Min);
                while (discCount < AnalysisConfig.MaxDiscontinuities && cv.GetNextDiscontinuity(Continuity.C1_continuous, s, cv.Domain.Max, out double td)) {
                    buffer[discCount++] = td;
                    s = td + ctx.AbsoluteTolerance;
                }
                AreaMassProperties amp = AreaMassProperties.Compute(cv);
                return amp is not null && cv.FrameAt(param, out Plane frame)
                    ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.CurveData(
                        cv.PointAt(param),
                        cv.DerivativeAt(param, order) ?? [],
                        cv.CurvatureAt(param).Length,
                        frame,
                        cv.GetPerpendicularFrames([.. Enumerable.Range(0, 5).Select(i => cv.Domain.ParameterAt(i * 0.25)),]) ?? [],
                        cv.IsClosed ? cv.TorsionAt(param) : 0,
                        [.. buffer[..discCount]],
                        [.. buffer[..discCount].Select(dp => cv.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                        cv.GetLength(),
                        amp.Centroid))
                    : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.CurveAnalysisFailed);
            } finally {
                ArrayPool<double>.Shared.Return(buffer, clearArray: true);
            }
        }), (Func<Surface, IGeometryContext, (double, double)?, int, Result<Analysis.IResult>>)((sf, _, uv, order) => {
            (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
            AreaMassProperties amp = AreaMassProperties.Compute(sf);
            return !sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) || amp is null || !sf.FrameAt(u, v, out Plane frame)
                ? ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed)
                : ((Func<SurfaceCurvature, Result<Analysis.IResult>>)(sc =>
                    !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian)
                        ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.SurfaceData(
                            sf.PointAt(u, v),
                            derivs,
                            sc.Gaussian,
                            sc.Mean,
                            sc.Kappa(0),
                            sc.Kappa(1),
                            sc.Direction(0),
                            sc.Direction(1),
                            frame,
                            frame.Normal,
                            sf.IsAtSeam(u, v) != 0,
                            sf.IsAtSingularity(u, v, exact: true),
                            amp.Area,
                            amp.Centroid))
                        : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed)))(sf.CurvatureAt(u, v));
        })) switch {
            (Func<Curve, IGeometryContext, double?, int, Result<Analysis.IResult>> curveLogic, Func<Surface, IGeometryContext, (double, double)?, int, Result<Analysis.IResult>> surfaceLogic) => new Dictionary<Type, (V, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>>)> {
                [typeof(Curve)] = (AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Curve), V.Standard | V.Degeneracy), (g, ctx, t, _, _, _, order) =>
                    ResultFactory.Create(value: (Curve)g)
                        .Validate(args: [ctx, AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Curve), V.Standard | V.Degeneracy)])
                        .Bind(cv => curveLogic(cv, ctx, t, order))),

                [typeof(NurbsCurve)] = (AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(NurbsCurve), V.Standard | V.Degeneracy), (g, ctx, t, _, _, _, order) =>
                    ResultFactory.Create(value: (NurbsCurve)g)
                        .Validate(args: [ctx, AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(NurbsCurve), V.Standard | V.Degeneracy)])
                        .Bind(cv => curveLogic(cv, ctx, t, order))),

                [typeof(Surface)] = (AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Surface), V.Standard), (g, ctx, _, uv, _, _, order) =>
                    ResultFactory.Create(value: (Surface)g)
                        .Validate(args: [ctx, AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Surface), V.Standard)])
                        .Bind(sf => surfaceLogic(sf, ctx, uv, order))),

                [typeof(NurbsSurface)] = (AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(NurbsSurface), V.Standard), (g, ctx, _, uv, _, _, order) =>
                    ResultFactory.Create(value: (NurbsSurface)g)
                        .Validate(args: [ctx, AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(NurbsSurface), V.Standard)])
                        .Bind(sf => surfaceLogic(sf, ctx, uv, order))),

                [typeof(Brep)] = (AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Brep), V.Standard | V.Topology), (g, ctx, _, uv, faceIdx, testPt, order) =>
                ResultFactory.Create(value: (Brep)g)
                    .Validate(args: [ctx, AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Brep), V.Standard | V.Topology)])
                    .Bind(brep => {
                        int fIdx = Math.Clamp(faceIdx ?? 0, 0, brep.Faces.Count - 1);
                        using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
                        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
                        Point3d testPoint = testPt ?? brep.GetBoundingBox(accurate: false).Center;
                        AreaMassProperties? amp = AreaMassProperties.Compute(brep);
                        VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                        if (amp is null || vmp is null || !sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) || !sf.FrameAt(u, v, out Plane frame) || !brep.ClosestPoint(testPoint, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, ctx.AbsoluteTolerance * 100, out Vector3d _)) {
                            return ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed);
                        }
                        SurfaceCurvature sc = sf.CurvatureAt(u, v);
                        return !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian)
                            ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.BrepData(
                                sf.PointAt(u, v),
                                derivs,
                                sc.Gaussian,
                                sc.Mean,
                                sc.Kappa(0),
                                sc.Kappa(1),
                                sc.Direction(0),
                                sc.Direction(1),
                                frame,
                                frame.Normal,
                                [.. brep.Vertices.Select((vtx, i) => (i, vtx.Location)),],
                                [.. brep.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))),],
                                brep.IsManifold,
                                brep.IsSolid,
                                cp,
                                testPoint.DistanceTo(cp),
                                ci,
                                (uOut, vOut),
                                amp.Area,
                                vmp.Volume,
                                vmp.Centroid))
                            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed);
                    })),

                [typeof(Mesh)] = (AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Mesh), V.MeshSpecific), (g, ctx, _, _, vertIdx, _, _) =>
                    ResultFactory.Create(value: (Mesh)g)
                        .Validate(args: [ctx, AnalysisConfig.ValidationModes.GetValueOrDefault(typeof(Mesh), V.MeshSpecific)])
                        .Bind(mesh => {
                            int vIdx = Math.Clamp(vertIdx ?? 0, 0, mesh.Vertices.Count - 1);
                            Vector3d normal = mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis;
                            AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
                            VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
                            return amp is null || vmp is null
                                ? ResultFactory.Create<Analysis.IResult>(error: E.Geometry.MeshAnalysisFailed)
                                : ResultFactory.Create(value: (Analysis.IResult)new Analysis.MeshData(
                                    mesh.Vertices[vIdx],
                                    new Plane(mesh.Vertices[vIdx], normal),
                                    normal,
                                    [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])),],
                                    [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))),],
                                    mesh.IsManifold(topologicalTest: true, out bool _, out bool _),
                                    mesh.IsClosed,
                                    amp.Area,
                                    vmp.Volume));
                        })),
            }.ToFrozenDictionary(),
        };

    /// <summary>Executes type-driven dispatch with automatic validation and geometry-specific computation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> Execute(
        object geometry,
        IGeometryContext context,
        double? t,
        (double, double)? uv,
        int? index,
        Point3d? testPoint,
        int derivativeOrder,
        bool enableDiagnostics = false) =>
        _strategies.TryGetValue(geometry.GetType(), out (V mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> compute) strategy)
            ? (enableDiagnostics
                ? strategy.compute(geometry, context, t, uv, index, testPoint, derivativeOrder)
                    .Capture($"Analysis.{geometry.GetType().Name}", validationApplied: strategy.mode, cacheHit: false)
                : strategy.compute(geometry, context, t, uv, index, testPoint, derivativeOrder))
                .Map(r => (IReadOnlyList<Analysis.IResult>)[r])
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(geometry.GetType().Name));

    /// <summary>Computes curvature extrema using Curve.MaxCurvaturePoints SDK method.</summary>
    [Pure]
    internal static Result<Analysis.CurvatureExtremaData> ExecuteCurvatureExtrema(
        Curve curve,
        IGeometryContext context,
        bool _) =>
        ResultFactory.Create(value: curve)
            .Validate(args: [context, V.Standard | V.Degeneracy])
            .Bind(cv => {
                Point3d[] points = cv.MaxCurvaturePoints(out double[] parameters);
                if (points is null || parameters is null || points.Length == 0) {
                    return ResultFactory.Create<Analysis.CurvatureExtremaData>(error: E.Geometry.CurvatureExtremaFailed);
                }
                double[] curvatures = [.. points.Select((_, i) => cv.CurvatureAt(parameters[i]).Length),];
                int maxIdx = curvatures.Select((k, i) => (k, i)).MaxBy(x => x.k).i;
                return ResultFactory.Create(value: new Analysis.CurvatureExtremaData(
                    Location: points[0],
                    MaxCurvaturePoints: points,
                    MaxCurvatureParameters: parameters,
                    MaxCurvatureValues: curvatures,
                    GlobalMaxCurvature: curvatures[maxIdx],
                    GlobalMaxLocation: points[maxIdx]));
            });

    /// <summary>Analyzes all faces of brep producing per-face surface data with curvature statistics.</summary>
    [Pure]
    internal static Result<Analysis.MultiFaceBrepData> ExecuteMultiFaceBrep(
        Brep brep,
        IGeometryContext context,
        (double u, double v)? uvParameter,
        int derivativeOrder,
        bool _) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, V.Standard | V.Topology])
            .Bind(b => {
                Analysis.SurfaceData[] faceData = ArrayPool<Analysis.SurfaceData>.Shared.Rent(b.Faces.Count);
                try {
                    int successCount = 0;
                    for (int i = 0; i < b.Faces.Count; i++) {
                        using Surface sf = b.Faces[i].UnderlyingSurface();
                        (double u, double v) = uvParameter ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
                        AreaMassProperties? amp = AreaMassProperties.Compute(sf);
                        if (amp is null || !sf.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) || !sf.FrameAt(u, v, out Plane frame)) {
                            continue;
                        }
                        SurfaceCurvature sc = sf.CurvatureAt(u, v);
                        if (double.IsNaN(sc.Gaussian) || double.IsInfinity(sc.Gaussian)) {
                            continue;
                        }
                        faceData[successCount++] = new Analysis.SurfaceData(
                            sf.PointAt(u, v),
                            derivs,
                            sc.Gaussian,
                            sc.Mean,
                            sc.Kappa(0),
                            sc.Kappa(1),
                            sc.Direction(0),
                            sc.Direction(1),
                            frame,
                            frame.Normal,
                            sf.IsAtSeam(u, v) != 0,
                            sf.IsAtSingularity(u, v, exact: true),
                            amp.Area,
                            amp.Centroid);
                    }
                    if (successCount == 0) {
                        return ResultFactory.Create<Analysis.MultiFaceBrepData>(error: E.Geometry.MultiFaceBrepFailed);
                    }
                    Analysis.SurfaceData[] faces = [.. faceData[..successCount]];
                    return ResultFactory.Create(value: new Analysis.MultiFaceBrepData(
                        Location: b.GetBoundingBox(accurate: false).Center,
                        FaceAnalyses: faces,
                        FaceIndices: [.. Enumerable.Range(0, faces.Length),],
                        TotalFaces: faces.Length,
                        GaussianRange: (faces.Min(f => f.Gaussian), faces.Max(f => f.Gaussian)),
                        MeanRange: (faces.Min(f => f.Mean), faces.Max(f => f.Mean))));
                } finally {
                    ArrayPool<Analysis.SurfaceData>.Shared.Return(faceData, clearArray: true);
                }
            });

    /// <summary>Analyzes curve at multiple parameters producing batch results with single validation.</summary>
    [Pure]
    internal static Result<IReadOnlyList<Analysis.CurveData>> ExecuteBatchCurve(
        Curve curve,
        IGeometryContext context,
        IReadOnlyList<double> parameters,
        int derivativeOrder,
        bool _) =>
        parameters.Count == 0
            ? ResultFactory.Create<IReadOnlyList<Analysis.CurveData>>(error: E.Geometry.EmptyParameterList)
            : ResultFactory.Create(value: curve)
                .Validate(args: [context, V.Standard | V.Degeneracy])
                .Bind(cv => {
                    double[] buffer = ArrayPool<double>.Shared.Rent(AnalysisConfig.MaxDiscontinuities);
                    Analysis.CurveData[] results = ArrayPool<Analysis.CurveData>.Shared.Rent(parameters.Count);
                    try {
                        (int discCount, double s) = (0, cv.Domain.Min);
                        while (discCount < AnalysisConfig.MaxDiscontinuities && cv.GetNextDiscontinuity(Continuity.C1_continuous, s, cv.Domain.Max, out double td)) {
                            buffer[discCount++] = td;
                            s = td + context.AbsoluteTolerance;
                        }
                        AreaMassProperties amp = AreaMassProperties.Compute(cv);
                        if (amp is null) {
                            return ResultFactory.Create<IReadOnlyList<Analysis.CurveData>>(error: E.Geometry.CurveAnalysisFailed);
                        }
                        for (int i = 0; i < parameters.Count; i++) {
                            double param = parameters[i];
                            if (!cv.FrameAt(param, out Plane frame)) {
                                return ResultFactory.Create<IReadOnlyList<Analysis.CurveData>>(error: E.Geometry.CurveAnalysisFailed);
                            }
                            results[i] = new Analysis.CurveData(
                                cv.PointAt(param),
                                cv.DerivativeAt(param, derivativeOrder) ?? [],
                                cv.CurvatureAt(param).Length,
                                frame,
                                cv.GetPerpendicularFrames([.. Enumerable.Range(0, 5).Select(j => cv.Domain.ParameterAt(j * 0.25)),]) ?? [],
                                cv.IsClosed ? cv.TorsionAt(param) : 0,
                                [.. buffer[..discCount]],
                                [.. buffer[..discCount].Select(dp => cv.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                                cv.GetLength(),
                                amp.Centroid);
                        }
                        return ResultFactory.Create(value: (IReadOnlyList<Analysis.CurveData>)[.. results[..parameters.Count]]);
                    } finally {
                        ArrayPool<double>.Shared.Return(buffer, clearArray: true);
                        ArrayPool<Analysis.CurveData>.Shared.Return(results, clearArray: true);
                    }
                });

    /// <summary>Analyzes surface at multiple UV parameters producing batch results with single validation.</summary>
    [Pure]
    internal static Result<IReadOnlyList<Analysis.SurfaceData>> ExecuteBatchSurface(
        Surface surface,
        IGeometryContext context,
        IReadOnlyList<(double u, double v)> uvParameters,
        int derivativeOrder,
        bool _) =>
        uvParameters.Count == 0
            ? ResultFactory.Create<IReadOnlyList<Analysis.SurfaceData>>(error: E.Geometry.EmptyParameterList)
            : ResultFactory.Create(value: surface)
                .Validate(args: [context, V.Standard])
                .Bind(sf => {
                    Analysis.SurfaceData[] results = ArrayPool<Analysis.SurfaceData>.Shared.Rent(uvParameters.Count);
                    try {
                        AreaMassProperties? amp = AreaMassProperties.Compute(sf);
                        if (amp is null) {
                            return ResultFactory.Create<IReadOnlyList<Analysis.SurfaceData>>(error: E.Geometry.SurfaceAnalysisFailed);
                        }
                        for (int i = 0; i < uvParameters.Count; i++) {
                            (double u, double v) = uvParameters[i];
                            if (!sf.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) || !sf.FrameAt(u, v, out Plane frame)) {
                                return ResultFactory.Create<IReadOnlyList<Analysis.SurfaceData>>(error: E.Geometry.SurfaceAnalysisFailed);
                            }
                            SurfaceCurvature sc = sf.CurvatureAt(u, v);
                            if (double.IsNaN(sc.Gaussian) || double.IsInfinity(sc.Gaussian)) {
                                return ResultFactory.Create<IReadOnlyList<Analysis.SurfaceData>>(error: E.Geometry.SurfaceAnalysisFailed);
                            }
                            results[i] = new Analysis.SurfaceData(
                                sf.PointAt(u, v),
                                derivs,
                                sc.Gaussian,
                                sc.Mean,
                                sc.Kappa(0),
                                sc.Kappa(1),
                                sc.Direction(0),
                                sc.Direction(1),
                                frame,
                                frame.Normal,
                                sf.IsAtSeam(u, v) != 0,
                                sf.IsAtSingularity(u, v, exact: true),
                                amp.Area,
                                amp.Centroid);
                        }
                        return ResultFactory.Create(value: (IReadOnlyList<Analysis.SurfaceData>)[.. results[..uvParameters.Count]]);
                    } finally {
                        ArrayPool<Analysis.SurfaceData>.Shared.Return(results, clearArray: true);
                    }
                });
}
