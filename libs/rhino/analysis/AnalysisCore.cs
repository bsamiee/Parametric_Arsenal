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

/// <summary>Ultra-dense computation strategies with FrozenDictionary type dispatch and polymorphic computation.</summary>
internal static class AnalysisCore {
    /// <summary>Computes curve analysis with discontinuity tracking, curvature, and mass properties.</summary>
    [Pure]
    private static Result<Analysis.IResult> ComputeCurve(Curve cv, IGeometryContext ctx, double? t, int order) {
        double param = t ?? cv.Domain.Mid;
        ArrayPool<double> pool = ArrayPool<double>.Shared;
        double[] buffer = pool.Rent(AnalysisConfig.MaxDiscontinuities);
        try {
            (int discCount, double s) = (0, cv.Domain.Min);
            while (discCount < AnalysisConfig.MaxDiscontinuities && cv.GetNextDiscontinuity(Continuity.C1_continuous, s, cv.Domain.Max, out double td)) {
                buffer[discCount++] = td;
                s = td + ctx.AbsoluteTolerance;
            }
            AreaMassProperties? amp = AreaMassProperties.Compute(cv);
            return amp is not null && cv.FrameAt(param, out Plane frame)
                ? ResultFactory.Create(value: (Analysis.IResult)new Analysis.CurveData(
                    cv.PointAt(param),
                    cv.DerivativeAt(param, order) ?? [],
                    cv.CurvatureAt(param).Length,
                    frame,
                    cv.GetPerpendicularFrames([.. Enumerable.Range(0, AnalysisConfig.CurveFrameSampleCount).Select(i => cv.Domain.ParameterAt(i * (1.0 / (AnalysisConfig.CurveFrameSampleCount - 1)))),]) ?? [],
                    cv.IsClosed ? cv.TorsionAt(param) : 0,
                    [.. buffer[..discCount]],
                    [.. buffer[..discCount].Select(dp => cv.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                    cv.GetLength(),
                    amp.Centroid))
                : ResultFactory.Create<Analysis.IResult>(error: E.Geometry.CurveAnalysisFailed);
        } finally {
            pool.Return(buffer, clearArray: true);
        }
    }

    /// <summary>Computes surface analysis with curvature evaluation and singularity detection.</summary>
    [Pure]
    private static Result<Analysis.IResult> ComputeSurface(Surface sf, (double, double)? uv, int order) {
        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
        AreaMassProperties? amp = AreaMassProperties.Compute(sf);
        SurfaceCurvature sc = sf.CurvatureAt(u, v);
        return !sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) || amp is null || !sf.FrameAt(u, v, out Plane frame) || double.IsNaN(sc.Gaussian) || double.IsInfinity(sc.Gaussian)
            ? ResultFactory.Create<Analysis.IResult>(error: E.Geometry.SurfaceAnalysisFailed)
            : ResultFactory.Create(value: (Analysis.IResult)new Analysis.SurfaceData(
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
                amp.Centroid));
    }

    /// <summary>Computes brep analysis with topology navigation, proximity queries, and volume metrics.</summary>
    [Pure]
    private static Result<Analysis.IResult> ComputeBrep(Brep brep, IGeometryContext ctx, (double, double)? uv, int? faceIdx, Point3d? testPt, int order) {
        using Surface sf = brep.Faces[Math.Clamp(faceIdx ?? 0, 0, brep.Faces.Count - 1)].UnderlyingSurface();
        (double u, double v) = uv ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
        Point3d testPoint = testPt ?? brep.GetBoundingBox(accurate: false).Center;
        (AreaMassProperties? amp, VolumeMassProperties? vmp, SurfaceCurvature sc) = (AreaMassProperties.Compute(brep), VolumeMassProperties.Compute(brep), sf.CurvatureAt(u, v));
        return amp is null || vmp is null || !sf.Evaluate(u, v, order, out Point3d _, out Vector3d[] derivs) || !sf.FrameAt(u, v, out Plane frame) || !brep.ClosestPoint(testPoint, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, ctx.AbsoluteTolerance * 100, out Vector3d _) || double.IsNaN(sc.Gaussian) || double.IsInfinity(sc.Gaussian)
            ? ResultFactory.Create<Analysis.IResult>(error: E.Geometry.BrepAnalysisFailed)
            : ResultFactory.Create(value: (Analysis.IResult)new Analysis.BrepData(
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
                vmp.Centroid));
    }

    /// <summary>Computes mesh analysis with topology enumeration and manifold inspection.</summary>
    [Pure]
    private static Result<Analysis.IResult> ComputeMesh(Mesh mesh, int? vertIdx) {
        int vIdx = Math.Clamp(vertIdx ?? 0, 0, mesh.Vertices.Count - 1);
        (Vector3d normal, AreaMassProperties? amp, VolumeMassProperties? vmp) = (mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis, AreaMassProperties.Compute(mesh), VolumeMassProperties.Compute(mesh));
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
    }

    /// <summary>Type-driven strategy lookup mapping geometry types to validation modes and computation delegates.</summary>
    private static readonly FrozenDictionary<Type, (V Mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> Compute)> _strategies =
        new Dictionary<Type, (V, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>>)> {
            [typeof(Curve)] = (V.Standard | V.Degeneracy, (g, ctx, t, _, _, _, order) => ComputeCurve((Curve)g, ctx, t, order)),
            [typeof(NurbsCurve)] = (V.Standard | V.Degeneracy, (g, ctx, t, _, _, _, order) => ComputeCurve((Curve)g, ctx, t, order)),
            [typeof(Surface)] = (V.Standard | V.SurfaceContinuity, (g, _, _, uv, _, _, order) => ComputeSurface((Surface)g, uv, order)),
            [typeof(NurbsSurface)] = (V.Standard | V.SurfaceContinuity, (g, _, _, uv, _, _, order) => ComputeSurface((Surface)g, uv, order)),
            [typeof(Brep)] = (V.Standard | V.Topology | V.MassProperties, (g, ctx, _, uv, faceIdx, testPt, order) => ComputeBrep((Brep)g, ctx, uv, faceIdx, testPt, order)),
            [typeof(Mesh)] = (V.MeshSpecific, (g, _, _, _, vertIdx, _, _) => ComputeMesh((Mesh)g, vertIdx)),
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
        _strategies.TryGetValue(geometry.GetType(), out (V mode, Func<object, IGeometryContext, double?, (double, double)?, int?, Point3d?, int, Result<Analysis.IResult>> compute) strategy)
            ? ResultFactory.Create(value: geometry)
                .Validate(args: [context, strategy.mode])
                .Bind(g => enableDiagnostics
                    ? strategy.compute(g, context, t, uv, index, testPoint, derivativeOrder)
                        .Capture($"Analysis.{geometry.GetType().Name}", validationApplied: strategy.mode, cacheHit: false)
                    : strategy.compute(g, context, t, uv, index, testPoint, derivativeOrder))
                .Map(r => (IReadOnlyList<Analysis.IResult>)[r])
            : ResultFactory.Create<IReadOnlyList<Analysis.IResult>>(error: E.Geometry.UnsupportedAnalysis.WithContext(geometry.GetType().Name));
}
