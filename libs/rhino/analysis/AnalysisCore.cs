using System;
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
    /// <summary>Validates geometry and executes computation logic with proper type casting.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Analysis.IResult> ValidateAndCompute<TGeom>(object geometry, IGeometryContext context, V mode, Func<TGeom, Result<Analysis.IResult>> compute) =>
        ResultFactory.Create(value: (TGeom)geometry).Validate(args: [context, mode]).Bind(compute);

    /// <summary>Validates surface curvature for valid differential geometry data.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidCurvature(SurfaceCurvature sc) => !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian);

    private static readonly Func<Curve, IGeometryContext, double?, int, Result<Analysis.IResult>> CurveLogic = (cv, ctx, t, order) => {
        double param = t ?? cv.Domain.Mid;
        double[] buffer = ArrayPool<double>.Shared.Rent(AnalysisConfig.MaxDiscontinuities);
        try {
            (int discCount, double s) = (0, cv.Domain.Min);
            while (discCount < AnalysisConfig.MaxDiscontinuities && cv.GetNextDiscontinuity(Continuity.C1_continuous, s, cv.Domain.Max, out double td)) {
                buffer[discCount++] = td;
                s = td + ctx.AbsoluteTolerance;
            }
            double[] disc = [.. buffer[..discCount]];
            return cv.FrameAt(param, out Plane frame)
                ? ((Func<AreaMassProperties?, Result<Analysis.IResult>>)(amp => amp is not null
                    ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.CurveData(
                        cv.PointAt(param), cv.DerivativeAt(param, order) ?? [], cv.CurvatureAt(param).Length, frame,
                        cv.GetPerpendicularFrames([.. Enumerable.Range(0, AnalysisConfig.CurveFrameSampleCount).Select(i => cv.Domain.ParameterAt(i * 0.25)),]) ?? [],
                        cv.IsClosed ? cv.TorsionAt(param) : 0, disc,
                        [.. disc.Select(dp => cv.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                        cv.GetLength(), amp.Centroid))
                    : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.CurveAnalysisFailed)))(AreaMassProperties.Compute(cv))
                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.CurveAnalysisFailed);
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    };

    private static readonly Func<Surface, IGeometryContext, (double, double)?, int, Result<Analysis.IResult>> SurfaceLogic = (sf, _, uv, order) => {
        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
        return sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) && sf.FrameAt(u, v, out Plane frame)
            ? ((Func<AreaMassProperties?, SurfaceCurvature, Result<Analysis.IResult>>)((amp, sc) =>
                amp is not null && IsValidCurvature(sc)
                    ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.SurfaceData(
                        sf.PointAt(u, v), derivs, sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1),
                        sc.Direction(0), sc.Direction(1), frame, frame.Normal,
                        sf.IsAtSeam(u, v) != 0, sf.IsAtSingularity(u, v, exact: true), amp.Area, amp.Centroid))
                    : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed)))(AreaMassProperties.Compute(sf), sf.CurvatureAt(u, v))
            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed);
    };
    private static readonly FrozenDictionary<Type, V> Modes = AnalysisConfig.ValidationModes;

    private static readonly FrozenDictionary<Type, (V Mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> Compute)> _strategies =
        new Dictionary<Type, (V, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>>)> {
            [typeof(Curve)] = (Modes[typeof(Curve)], (g, ctx, t, _, _, _, order) => ValidateAndCompute<Curve>(g, ctx, Modes[typeof(Curve)], cv => CurveLogic(cv, ctx, t, order))),
            [typeof(NurbsCurve)] = (Modes[typeof(NurbsCurve)], (g, ctx, t, _, _, _, order) => ValidateAndCompute<NurbsCurve>(g, ctx, Modes[typeof(NurbsCurve)], cv => CurveLogic(cv, ctx, t, order))),
            [typeof(Surface)] = (Modes[typeof(Surface)], (g, ctx, _, uv, _, _, order) => ValidateAndCompute<Surface>(g, ctx, Modes[typeof(Surface)], sf => SurfaceLogic(sf, ctx, uv, order))),
            [typeof(NurbsSurface)] = (Modes[typeof(NurbsSurface)], (g, ctx, _, uv, _, _, order) => ValidateAndCompute<NurbsSurface>(g, ctx, Modes[typeof(NurbsSurface)], sf => SurfaceLogic(sf, ctx, uv, order))),
            [typeof(Brep)] = (Modes[typeof(Brep)], (g, ctx, _, uv, faceIdx, testPt, order) => ValidateAndCompute<Brep>(g, ctx, Modes[typeof(Brep)], brep => {
                        int fIdx = Math.Clamp(faceIdx ?? 0, 0, brep.Faces.Count - 1);
                        using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
                        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
                        Point3d testPoint = testPt ?? brep.GetBoundingBox(accurate: false).Center;
                        return sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) && sf.FrameAt(u, v, out Plane frame) &&
                            brep.ClosestPoint(testPoint, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, ctx.AbsoluteTolerance * 100, out Vector3d _)
                            ? ((Func<AreaMassProperties?, VolumeMassProperties?, SurfaceCurvature, Result<Analysis.IResult>>)((amp, vmp, sc) =>
                                amp is not null && vmp is not null && IsValidCurvature(sc)
                                    ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.BrepData(
                                        sf.PointAt(u, v), derivs, sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1),
                                        sc.Direction(0), sc.Direction(1), frame, frame.Normal,
                                        [.. brep.Vertices.Select((vtx, i) => (i, vtx.Location)),],
                                        [.. brep.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))),],
                                        brep.IsManifold, brep.IsSolid, cp, testPoint.DistanceTo(cp),
                                        ci, (uOut, vOut), amp.Area, vmp.Volume, vmp.Centroid))
                                    : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed))(
                                        AreaMassProperties.Compute(brep), VolumeMassProperties.Compute(brep), sf.CurvatureAt(u, v))
                            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed);
                    })),
            [typeof(Mesh)] = (Modes[typeof(Mesh)], (g, ctx, _, _, vertIdx, _, _) => ValidateAndCompute<Mesh>(g, ctx, Modes[typeof(Mesh)], mesh => {
                        int vIdx = Math.Clamp(vertIdx ?? 0, 0, mesh.Vertices.Count - 1);
                        Vector3d normal = mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis;
                        return ((Func<AreaMassProperties?, VolumeMassProperties?, Result<Analysis.IResult>>)((amp, vmp) =>
                            amp is not null && vmp is not null
                                ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.MeshData(
                                    mesh.Vertices[vIdx], new Plane(mesh.Vertices[vIdx], normal), normal,
                                    [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])),],
                                    [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))),],
                                    mesh.IsManifold(topologicalTest: true, out bool _, out bool _), mesh.IsClosed, amp.Area, vmp.Volume))
                                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.MeshAnalysisFailed)))(AreaMassProperties.Compute(mesh), VolumeMassProperties.Compute(mesh));
                    })),
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> Execute(
        object geometry,
        IGeometryContext context,
        double? t,
        (double, double)? uv,
        int? index,
        Point3d? testPoint,
        int derivativeOrder,
        bool enableDiagnostics = false) {
        Type geometryType = geometry.GetType();
        if (!_strategies.TryGetValue(geometryType, out (V mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> compute) strategy)) {
            return ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(geometryType.Name));
        }

        Result<Analysis.IResult> computation = strategy.compute(geometry, context, t, uv, index, testPoint, derivativeOrder);
        return (enableDiagnostics
            ? computation.Capture($"Analysis.{geometryType.Name}", validationApplied: strategy.mode, cacheHit: false)
            : computation).Map(r => (IReadOnlyList<Analysis.IResult>)[r]);
    }
}
