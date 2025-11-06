using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Diagnostics;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Ultra-dense computation strategies with FrozenDictionary type dispatch and embedded validation.</summary>
internal static class AnalysisCompute {
    private const int MaxDiscontinuities = 20;

    /// <summary>Type-driven strategy lookup mapping geometry types to validation modes and computation functions.</summary>
    private static readonly FrozenDictionary<Type, (ValidationMode Mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> Compute)> _strategies =
        new Dictionary<Type, (ValidationMode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>>)> {
            [typeof(Curve)] = (V.Standard | V.Degeneracy, (g, ctx, t, _, _, _, order) =>
                ResultFactory.Create(value: (Curve)g)
                    .Validate(args: [ctx, V.Standard | V.Degeneracy])
                    .Bind(cv => {
                        double param = t ?? cv.Domain.Mid;
                        ArrayPool<double> pool = ArrayPool<double>.Shared;
                        double[] buffer = pool.Rent(MaxDiscontinuities);
                        try {
                            (int discCount, double s) = (0, cv.Domain.Min);
                            while (discCount < MaxDiscontinuities && cv.GetNextDiscontinuity(Continuity.C1_continuous, s, cv.Domain.Max, out double td)) {
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
                            pool.Return(buffer, clearArray: true);
                        }
                    })),

            [typeof(NurbsCurve)] = (V.Standard | V.Degeneracy, (g, ctx, t, _, _, _, order) =>
                ResultFactory.Create(value: (NurbsCurve)g)
                    .Validate(args: [ctx, V.Standard | V.Degeneracy])
                    .Bind(cv => {
                        double param = t ?? cv.Domain.Mid;
                        ArrayPool<double> pool = ArrayPool<double>.Shared;
                        double[] buffer = pool.Rent(MaxDiscontinuities);
                        try {
                            (int discCount, double s) = (0, cv.Domain.Min);
                            while (discCount < MaxDiscontinuities && cv.GetNextDiscontinuity(Continuity.C1_continuous, s, cv.Domain.Max, out double td)) {
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
                            pool.Return(buffer, clearArray: true);
                        }
                    })),

            [typeof(Surface)] = (V.Standard | V.SurfaceContinuity, (g, ctx, _, uv, _, _, order) =>
                ResultFactory.Create(value: (Surface)g)
                    .Validate(args: [ctx, V.Standard | V.SurfaceContinuity])
                    .Bind(sf => {
                        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
                        AreaMassProperties amp = AreaMassProperties.Compute(sf);
                        if (!sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) || amp is null || !sf.FrameAt(u, v, out Plane frame)) {
                            return ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed);
                        }
                        SurfaceCurvature sc = sf.CurvatureAt(u, v);
                        return !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian)
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
                            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed);
                    })),

            [typeof(NurbsSurface)] = (V.Standard | V.SurfaceContinuity, (g, ctx, _, uv, _, _, order) =>
                ResultFactory.Create(value: (NurbsSurface)g)
                    .Validate(args: [ctx, V.Standard | V.SurfaceContinuity])
                    .Bind(sf => {
                        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
                        AreaMassProperties amp = AreaMassProperties.Compute(sf);
                        if (!sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) || amp is null || !sf.FrameAt(u, v, out Plane frame)) {
                            return ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed);
                        }
                        SurfaceCurvature sc = sf.CurvatureAt(u, v);
                        return !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian)
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
                            : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed);
                    })),

            [typeof(Brep)] = (V.Standard | V.Topology | V.MassProperties, (g, ctx, _, uv, faceIdx, testPt, order) =>
                ResultFactory.Create(value: (Brep)g)
                    .Validate(args: [ctx, V.Standard | V.Topology | V.MassProperties])
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

            [typeof(Mesh)] = (V.MeshSpecific, (g, ctx, _, _, vertIdx, _, _) =>
                ResultFactory.Create(value: (Mesh)g)
                    .Validate(args: [ctx, V.MeshSpecific])
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
        }.ToFrozenDictionary();

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
        _strategies.TryGetValue(geometry.GetType(), out (ValidationMode mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> compute) strategy)
            ? (enableDiagnostics
                ? strategy.compute(geometry, context, t, uv, index, testPoint, derivativeOrder)
                    .Capture($"Analysis.{geometry.GetType().Name}", validationApplied: strategy.mode, cacheHit: false)
                : strategy.compute(geometry, context, t, uv, index, testPoint, derivativeOrder))
                .Map(r => (IReadOnlyList<Analysis.IResult>)[r])
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedGeometry.WithContext(geometry.GetType().Name));
}
