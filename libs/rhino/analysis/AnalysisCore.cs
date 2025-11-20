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
                    double[] frameParams = new double[AnalysisConfig.CurveFrameSampleCount];
                    for (int i = 0; i < AnalysisConfig.CurveFrameSampleCount; i++) {
                        frameParams[i] = cv.Domain.ParameterAt(AnalysisConfig.CurveFrameSampleCount > 1 ? i / (AnalysisConfig.CurveFrameSampleCount - 1.0) : 0.5);
                    }
                    Plane[] frames = cv.GetPerpendicularFrames(frameParams) is Plane[] pf ? pf : [];
                    return amp is not null
                        ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.CurveData(
                            cv.PointAt(param), derivatives, cv.CurvatureAt(param).Length, frame,
                            frames,
                            cv.TorsionAt(param), disc,
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
    private static readonly Func<Brep, IGeometryContext, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> BrepLogic = (brep, ctx, uv, faceIdx, testPt, order) => {
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
    };

    private static readonly Func<Mesh, IGeometryContext, int?, Result<Analysis.IResult>> MeshLogic = (mesh, _, vertIdx) => {
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
    };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Analysis.IResult>> Execute(
        object geometry,
        IGeometryContext context,
        double? t,
        (double, double)? uv,
        int? index,
        Point3d? testPoint,
        int derivativeOrder) =>
        geometry switch {
            Extrusion e => ((Func<Result<IReadOnlyList<Analysis.IResult>>>)(() => {
                using Brep brep = e.ToBrep();
                return brep is not null
                    ? UnifiedOperation.Apply(
                        brep,
                        (Func<Brep, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                            ResultFactory.Create(value: item)
                                .Validate(args: [context, AnalysisConfig.ValidationModes.GetValueOrDefault(e.GetType(), V.Standard | V.Topology | V.ExtrusionGeometry),])
                                .Bind(valid => BrepLogic(valid, context, uv, index, testPoint, derivativeOrder).Map(result => (IReadOnlyList<Analysis.IResult>)[result,]))),
                        new OperationConfig<Brep, Analysis.IResult> {
                            Context = context,
                            ValidationMode = V.None,
                            OperationName = $"Analysis.{e.GetType().Name}",
                            EnableDiagnostics = false,
                            AccumulateErrors = false,
                            EnableCache = false,
                            SkipInvalid = false,
                        })
                    : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.BrepAnalysisFailed);
            }))(),
            Brep b => UnifiedOperation.Apply(
                b,
                (Func<Brep, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                    ResultFactory.Create(value: item)
                        .Validate(args: [context, AnalysisConfig.ValidationModes.GetValueOrDefault(item.GetType(), V.Standard | V.Topology),])
                        .Bind(valid => BrepLogic(valid, context, uv, index, testPoint, derivativeOrder).Map(result => (IReadOnlyList<Analysis.IResult>)[result,]))),
                new OperationConfig<Brep, Analysis.IResult> {
                    Context = context,
                    ValidationMode = V.None,
                    OperationName = $"Analysis.{b.GetType().Name}",
                    EnableDiagnostics = false,
                    AccumulateErrors = false,
                    EnableCache = false,
                    SkipInvalid = false,
                }),
            Mesh m => UnifiedOperation.Apply(
                m,
                (Func<Mesh, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                    ResultFactory.Create(value: item)
                        .Validate(args: [context, AnalysisConfig.ValidationModes.GetValueOrDefault(item.GetType(), V.Standard | V.MeshSpecific),])
                        .Bind(valid => MeshLogic(valid, context, index).Map(result => (IReadOnlyList<Analysis.IResult>)[result,]))),
                new OperationConfig<Mesh, Analysis.IResult> {
                    Context = context,
                    ValidationMode = V.None,
                    OperationName = $"Analysis.{m.GetType().Name}",
                    EnableDiagnostics = false,
                    AccumulateErrors = false,
                    EnableCache = false,
                    SkipInvalid = false,
                }),
            Curve c => UnifiedOperation.Apply(
                c,
                (Func<Curve, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                    ResultFactory.Create(value: item)
                        .Validate(args: [context, AnalysisConfig.ValidationModes.GetValueOrDefault(item.GetType(), V.Standard | V.Degeneracy),])
                        .Bind(valid => CurveLogic(valid, context, t, derivativeOrder).Map(result => (IReadOnlyList<Analysis.IResult>)[result,]))),
                new OperationConfig<Curve, Analysis.IResult> {
                    Context = context,
                    ValidationMode = V.None,
                    OperationName = $"Analysis.{c.GetType().Name}",
                    EnableDiagnostics = false,
                    AccumulateErrors = false,
                    EnableCache = false,
                    SkipInvalid = false,
                }),
            Surface s => UnifiedOperation.Apply(
                s,
                (Func<Surface, Result<IReadOnlyList<Analysis.IResult>>>)(item =>
                    ResultFactory.Create(value: item)
                        .Validate(args: [context, AnalysisConfig.ValidationModes.GetValueOrDefault(item.GetType(), V.Standard | V.UVDomain),])
                        .Bind(valid => SurfaceLogic(valid, context, uv, derivativeOrder).Map(result => (IReadOnlyList<Analysis.IResult>)[result,]))),
                new OperationConfig<Surface, Analysis.IResult> {
                    Context = context,
                    ValidationMode = V.None,
                    OperationName = $"Analysis.{s.GetType().Name}",
                    EnableDiagnostics = false,
                    AccumulateErrors = false,
                    EnableCache = false,
                    SkipInvalid = false,
                }),
            _ => ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(geometry.GetType().Name)),
        };
}
