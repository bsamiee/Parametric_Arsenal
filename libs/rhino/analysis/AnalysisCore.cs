using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Differential geometry computation with pooled buffers and dispatch.</summary>
internal static class AnalysisCore {
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
                ? ((Func<Result<Analysis.IResult>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(cv);
                    Vector3d[] derivatives = cv.DerivativeAt(param, order) is Vector3d[] d ? d : [];
                    Plane[] frames = cv.GetPerpendicularFrames([.. Enumerable.Range(0, AnalysisConfig.CurveFrameSampleCount).Select(i => cv.Domain.ParameterAt(AnalysisConfig.CurveFrameSampleCount > 1 ? i / (AnalysisConfig.CurveFrameSampleCount - 1.0) : 0.5)),]) is Plane[] pf ? pf : [];
                    return amp is not null
                        ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.CurveData(
                            cv.PointAt(param), derivatives, cv.CurvatureAt(param).Length, frame,
                            frames,
                            cv.IsClosed ? cv.TorsionAt(param) : 0, disc,
                            [.. disc.Select(dp => cv.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                            cv.GetLength(), amp.Centroid))
                        : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.CurveAnalysisFailed);
                }))()
                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.CurveAnalysisFailed);
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    };

    private static readonly Func<Surface, IGeometryContext, (double, double)?, int, Result<Analysis.IResult>> SurfaceLogic = (sf, _, uv, order) => {
        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
        return sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) && sf.FrameAt(u, v, out Plane frame)
            ? ((Func<Result<Analysis.IResult>>)(() => {
                SurfaceCurvature sc = sf.CurvatureAt(u, v);
                using AreaMassProperties? amp = AreaMassProperties.Compute(sf);
                return amp is not null && RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)
                    ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.SurfaceData(
                        sf.PointAt(u, v), derivs, sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1),
                        sc.Direction(0), sc.Direction(1), frame, frame.Normal,
                        sf.IsAtSeam(u, v) != 0, sf.IsAtSingularity(u, v, exact: true), amp.Area, amp.Centroid))
                    : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed);
            }))()
            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed);
    };
    private static readonly FrozenDictionary<Type, V> Modes = AnalysisConfig.ValidationModes;

    private static readonly FrozenDictionary<Type, (V Mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> Compute)> _strategies =
        ((Func<FrozenDictionary<Type, (V, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>>)>>)(() => {
            Dictionary<Type, (V, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>>)> map = new() {
                [typeof(Curve)] = (Modes[typeof(Curve)], (g, ctx, t, _, _, _, order) => CurveLogic((Curve)g, ctx, t, order)),
                [typeof(NurbsCurve)] = (Modes[typeof(NurbsCurve)], (g, ctx, t, _, _, _, order) => CurveLogic((NurbsCurve)g, ctx, t, order)),
                [typeof(LineCurve)] = (Modes[typeof(LineCurve)], (g, ctx, t, _, _, _, order) => CurveLogic((LineCurve)g, ctx, t, order)),
                [typeof(ArcCurve)] = (Modes[typeof(ArcCurve)], (g, ctx, t, _, _, _, order) => CurveLogic((ArcCurve)g, ctx, t, order)),
                [typeof(PolyCurve)] = (Modes[typeof(PolyCurve)], (g, ctx, t, _, _, _, order) => CurveLogic((PolyCurve)g, ctx, t, order)),
                [typeof(PolylineCurve)] = (Modes[typeof(PolylineCurve)], (g, ctx, t, _, _, _, order) => CurveLogic((PolylineCurve)g, ctx, t, order)),
                [typeof(Surface)] = (Modes[typeof(Surface)], (g, ctx, _, uv, _, _, order) => SurfaceLogic((Surface)g, ctx, uv, order)),
                [typeof(NurbsSurface)] = (Modes[typeof(NurbsSurface)], (g, ctx, _, uv, _, _, order) => SurfaceLogic((NurbsSurface)g, ctx, uv, order)),
                [typeof(PlaneSurface)] = (Modes[typeof(PlaneSurface)], (g, ctx, _, uv, _, _, order) => SurfaceLogic((PlaneSurface)g, ctx, uv, order)),
                [typeof(Brep)] = (Modes[typeof(Brep)], (g, ctx, _, uv, faceIdx, testPt, order) => {
                    Brep brep = (Brep)g;
                    int fIdx = RhinoMath.Clamp(faceIdx ?? 0, 0, brep.Faces.Count - 1);
                    using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
                    (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
                    Point3d testPoint = testPt ?? brep.GetBoundingBox(accurate: false).Center;
                    return sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) && sf.FrameAt(u, v, out Plane frame) &&
                        brep.ClosestPoint(testPoint, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, ctx.AbsoluteTolerance * AnalysisConfig.BrepClosestPointToleranceMultiplier, out Vector3d _)
                        ? ((Func<Result<Analysis.IResult>>)(() => {
                            SurfaceCurvature sc = sf.CurvatureAt(u, v);
                            using AreaMassProperties? amp = AreaMassProperties.Compute(brep);
                            using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                            return amp is not null && vmp is not null && RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)
                                ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.BrepData(
                                    sf.PointAt(u, v), derivs, sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1),
                                    sc.Direction(0), sc.Direction(1), frame, frame.Normal,
                                    [.. brep.Vertices.Select((vtx, i) => (i, vtx.Location)),],
                                    [.. brep.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))),],
                                    brep.IsManifold, brep.IsSolid, cp, testPoint.DistanceTo(cp),
                                    ci, (uOut, vOut), amp.Area, vmp.Volume, vmp.Centroid))
                                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed);
                        }))()
                        : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed);
                }
                ),
                [typeof(Mesh)] = (Modes[typeof(Mesh)], (g, _, _, _, vertIdx, _, _) => {
                    Mesh mesh = (Mesh)g;
                    int vIdx = RhinoMath.Clamp(vertIdx ?? 0, 0, mesh.Vertices.Count - 1);
                    Vector3d normal = mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis;
                    return ((Func<Result<Analysis.IResult>>)(() => {
                        using AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
                        using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
                        return amp is not null && vmp is not null
                            ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.MeshData(
                                mesh.Vertices[vIdx], new Plane(mesh.Vertices[vIdx], normal), normal,
                                [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])),],
                                [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))),],
                                mesh.IsManifold(topologicalTest: true, out bool _, out bool _), mesh.IsClosed, amp.Area, vmp.Volume))
                            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.MeshAnalysisFailed);
                    }))();
                }
                ),
            };

            map[typeof(Extrusion)] = (Modes[typeof(Extrusion)], (g, ctx, _, uv, faceIdx, testPt, order) => ((Extrusion)g).ToBrep() is Brep extrusionBrep
                ? ((Func<Result<Analysis.IResult>>)(() => {
                    using Brep brep = extrusionBrep;
                    return map[typeof(Brep)].Item2(brep, ctx, null, uv, faceIdx, testPt, order);
                }))()
                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed));

            return map.ToFrozenDictionary();
        }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> Execute(
        object geometry,
        IGeometryContext context,
        double? t,
        (double, double)? uv,
        int? index,
        Point3d? testPoint,
        int derivativeOrder) =>
        _strategies.TryGetValue(geometry.GetType(), out (V mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> compute) strategy)
            ? UnifiedOperation.Apply(
                geometry,
                (Func<object, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                    ResultFactory.Create(value: item)
                        .Validate(args: [context, strategy.mode,])
                        .Bind(valid =>
                            strategy.compute(valid, context, t, uv, index, testPoint, derivativeOrder)
                                .Map(result => (IReadOnlyList<Analysis.IResult>)[result])
                        )),
                new OperationConfig<object, Analysis.IResult> {
                    Context = context,
                    ValidationMode = V.None,
                    OperationName = $"Analysis.{geometry.GetType().Name}",
                    EnableDiagnostics = false,
                    AccumulateErrors = false,
                    EnableCache = false,
                    SkipInvalid = false,
                })
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(geometry.GetType().Name));
}
