using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense geometric quality analysis algorithms.</summary>
[Pure]
internal static class AnalysisCompute {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurveData> CurveDifferential(Curve curve, double? parameter, int derivativeOrder, IGeometryContext context) {
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
                ? ((Func<Result<Analysis.CurveData>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(curve);
                    Vector3d[] derivatives = curve.DerivativeAt(param, derivativeOrder) is Vector3d[] d ? d : [];
                    double[] frameParams = new double[AnalysisConfig.CurveFrameSampleCount];
                    for (int i = 0; i < AnalysisConfig.CurveFrameSampleCount; i++) {
                        frameParams[i] = curve.Domain.ParameterAt(AnalysisConfig.CurveFrameSampleCount > 1 ? i / (AnalysisConfig.CurveFrameSampleCount - 1.0) : 0.5);
                    }
                    Plane[] frames = curve.GetPerpendicularFrames(frameParams) is Plane[] pf ? pf : [];
                    return amp is not null
                        ? ResultFactory.Create(value: new Analysis.CurveData(
                            curve.PointAt(param), derivatives, curve.CurvatureAt(param).Length, frame,
                            frames,
                            curve.TorsionAt(param), disc,
                            [.. disc.Select(dp => curve.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                            curve.GetLength(), amp.Centroid))
                        : ResultFactory.Create<Analysis.CurveData>(error: E.Geometry.CurveAnalysisFailed);
                }))()
                : ResultFactory.Create<Analysis.CurveData>(error: E.Geometry.CurveAnalysisFailed);
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurveFairnessData> CurveFairness(Curve curve, IGeometryContext context) {
        const int maxSamples = AnalysisConfig.CurveFairnessSampleCount;
        (double Parameter, Vector3d Curvature)[] samples = new (double, Vector3d)[maxSamples];
        double[] curvatures = new double[maxSamples];
        int validCount = 0;
        const double sampleDivisor = maxSamples - 1.0;

        for (int i = 0; i < maxSamples; i++) {
            double t = curve.Domain.ParameterAt(i / sampleDivisor);
            Vector3d curvature = curve.CurvatureAt(t);
            if (curvature.IsValid) {
                samples[validCount] = (t, curvature);
                curvatures[validCount] = curvature.Length;
                validCount++;
            }
        }
        (double Parameter, Vector3d Curvature)[] validSamples = samples.AsSpan(0, validCount).ToArray();
        double[] validCurvatures = curvatures.AsSpan(0, validCount).ToArray();
        return validSamples.Length > 2
            && Enumerable.Range(1, validCurvatures.Length - 1).Sum(i => Math.Abs(validCurvatures[i] - validCurvatures[i - 1])) / (validCurvatures.Length - 1) is double avgDiff
            && curve.GetLength() is double curveLength
            ? ResultFactory.Create(value: new Analysis.CurveFairnessData(
                Location: curve.PointAt(curve.Domain.Mid),
                SmoothnessScore: RhinoMath.Clamp(1.0 / (1.0 + (avgDiff * AnalysisConfig.SmoothnessSensitivity)), 0.0, 1.0),
                CurvatureValues: validCurvatures,
                InflectionPoints: Enumerable.Range(1, validCurvatures.Length - 2)
                    .Where(i => Math.Abs((validCurvatures[i] - validCurvatures[i - 1]) - (validCurvatures[i + 1] - validCurvatures[i])) > AnalysisConfig.InflectionSharpnessThreshold || ((validCurvatures[i] - validCurvatures[i - 1]) * (validCurvatures[i + 1] - validCurvatures[i])) < 0)
                    .Select(i => (validSamples[i].Parameter, Math.Abs(validCurvatures[i] - validCurvatures[i - 1]) > AnalysisConfig.InflectionSharpnessThreshold))
                    .ToArray(),
                BendingEnergy: validCurvatures.Max() is double maxCurv && maxCurv > context.AbsoluteTolerance
                    ? (validCurvatures.Sum(k => k * k) * (curveLength / (AnalysisConfig.CurveFairnessSampleCount - 1))) / (maxCurv * curveLength)
                    : 0.0))
            : ResultFactory.Create<Analysis.CurveFairnessData>(error: E.Geometry.CurveAnalysisFailed.WithContext("Insufficient valid curvature samples"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.SurfaceData> SurfaceDifferential(Surface surface, (double, double)? parameter, int derivativeOrder, IGeometryContext context) {
        (double u, double v) = parameter ?? (surface.Domain(0).Mid, surface.Domain(1).Mid);
        return surface.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) && surface.FrameAt(u, v, out Plane frame)
            ? ((Func<Result<Analysis.SurfaceData>>)(() => {
                SurfaceCurvature sc = surface.CurvatureAt(u, v);
                using AreaMassProperties? amp = AreaMassProperties.Compute(surface);
                return amp is not null && RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)
                    ? ResultFactory.Create(value: new Analysis.SurfaceData(
                        surface.PointAt(u, v), derivs, sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1),
                        sc.Direction(0), sc.Direction(1), frame, frame.Normal,
                        surface.IsAtSeam(u, v) != 0, surface.IsAtSingularity(u, v, exact: true), amp.Area, amp.Centroid))
                    : ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed);
            }))()
            : ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.SurfaceQualityData> SurfaceQuality(Surface surface, IGeometryContext context) {
        int gridSize = Math.Max(2, (int)Math.Sqrt(AnalysisConfig.SurfaceQualitySampleCount));
        int totalSamples = gridSize * gridSize;
        (double u, double v)[] uvGrid = new (double, double)[totalSamples];
        SurfaceCurvature[] curvatures = new SurfaceCurvature[totalSamples];
        int validCount = 0;
        int uvIndex = 0;
        double gridDivisor = gridSize - 1.0;

        for (int i = 0; i < gridSize; i++) {
            double u = surface.Domain(0).ParameterAt(i / gridDivisor);
            for (int j = 0; j < gridSize; j++) {
                double v = surface.Domain(1).ParameterAt(j / gridDivisor);
                uvGrid[uvIndex++] = (u, v);
                SurfaceCurvature sc = surface.CurvatureAt(u: u, v: v);
                if (RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)) {
                    curvatures[validCount++] = sc;
                }
            }
        }
        SurfaceCurvature[] validCurvatures = curvatures.AsSpan(0, validCount).ToArray();
        Interval uDomain = surface.Domain(0);
        Interval vDomain = surface.Domain(1);
        double uSpan = uDomain.Length;
        double vSpan = vDomain.Length;
        double singularityThresholdU = RhinoMath.Clamp(
            uSpan * AnalysisConfig.SingularityProximityFactor,
            RhinoMath.SqrtEpsilon,
            uSpan * AnalysisConfig.SingularityBoundaryFraction);
        double singularityThresholdV = RhinoMath.Clamp(
            vSpan * AnalysisConfig.SingularityProximityFactor,
            RhinoMath.SqrtEpsilon,
            vSpan * AnalysisConfig.SingularityBoundaryFraction);
        return validCurvatures.Length > 0
            && validCurvatures.Select(sc => Math.Abs(sc.Gaussian)).Order().ToArray() is double[] gaussianSorted
            && (gaussianSorted.Length % 2 is 0 ? (gaussianSorted[(gaussianSorted.Length / 2) - 1] + gaussianSorted[gaussianSorted.Length / 2]) / 2.0 : gaussianSorted[gaussianSorted.Length / 2]) is double medianGaussian
            && validCurvatures.Average(sc => Math.Abs(sc.Gaussian)) is double avgGaussian
            && Math.Sqrt(validCurvatures.Sum(sc => Math.Pow(Math.Abs(sc.Gaussian) - avgGaussian, 2)) / validCurvatures.Length) is double stdDevGaussian
            ? ResultFactory.Create(value: new Analysis.SurfaceQualityData(
                Location: surface.PointAt(surface.Domain(0).Mid, surface.Domain(1).Mid),
                GaussianCurvatures: validCurvatures.Select(sc => sc.Gaussian).ToArray(),
                MeanCurvatures: validCurvatures.Select(sc => sc.Mean).ToArray(),
                SingularityLocations: uvGrid.Where(uv =>
                    surface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)
                    || Math.Min(Math.Abs(uv.u - uDomain.Min), Math.Abs(uDomain.Max - uv.u)) <= singularityThresholdU
                    || Math.Min(Math.Abs(uv.v - vDomain.Min), Math.Abs(vDomain.Max - uv.v)) <= singularityThresholdV).ToArray(),
                UniformityScore: RhinoMath.Clamp(medianGaussian > context.AbsoluteTolerance ? (1.0 - (stdDevGaussian / (medianGaussian * AnalysisConfig.HighCurvatureMultiplier))) : gaussianSorted[^1] < context.AbsoluteTolerance ? 1.0 : 0.0, 0.0, 1.0)))
            : ResultFactory.Create<Analysis.SurfaceQualityData>(error: E.Geometry.SurfaceAnalysisFailed.WithContext("No valid curvature samples"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.BrepData> BrepTopology(Brep brep, (double, double)? parameter, int faceIndex, Point3d? testPoint, int derivativeOrder, IGeometryContext context) {
        int fIdx = RhinoMath.Clamp(faceIndex, 0, brep.Faces.Count - 1);
        using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
        (double u, double v) = parameter ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
        Point3d testPt = testPoint ?? brep.GetBoundingBox(accurate: false).Center;
        return sf.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) && sf.FrameAt(u, v, out Plane frame) &&
            brep.ClosestPoint(testPt, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, context.AbsoluteTolerance * AnalysisConfig.BrepClosestPointToleranceMultiplier, out Vector3d _)
            ? ((Func<Result<Analysis.BrepData>>)(() => {
                SurfaceCurvature sc = sf.CurvatureAt(u, v);
                using AreaMassProperties? amp = AreaMassProperties.Compute(brep);
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                return amp is not null && vmp is not null && RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)
                    ? ResultFactory.Create(value: new Analysis.BrepData(
                        sf.PointAt(u, v), derivs, sc.Gaussian, sc.Mean, sc.Kappa(0), sc.Kappa(1),
                        sc.Direction(0), sc.Direction(1), frame, frame.Normal,
                        [.. brep.Vertices.Select((vtx, i) => (i, vtx.Location)),],
                        [.. brep.Edges.Select((e, i) => (i, new Line(e.PointAtStart, e.PointAtEnd))),],
                        brep.IsManifold, brep.IsSolid, cp, testPt.DistanceTo(cp),
                        ci, (uOut, vOut), amp.Area, vmp.Volume, vmp.Centroid))
                    : ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed);
            }))()
            : ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.MeshData> MeshTopology(Mesh mesh, int vertexIndex, IGeometryContext context) {
        int vIdx = RhinoMath.Clamp(vertexIndex, 0, mesh.Vertices.Count - 1);
        Vector3d normal = mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis;
        return ((Func<Result<Analysis.MeshData>>)(() => {
            using AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
            using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
            return amp is not null && vmp is not null
                ? ResultFactory.Create(value: new Analysis.MeshData(
                    mesh.Vertices[vIdx], new Plane(mesh.Vertices[vIdx], normal), normal,
                    [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])),],
                    [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))),],
                    mesh.IsManifold(topologicalTest: true, out bool _, out bool _), mesh.IsClosed, amp.Area, vmp.Volume))
                : ResultFactory.Create<Analysis.MeshData>(error: E.Geometry.MeshAnalysisFailed);
        }))();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.MeshFEAData> MeshForFEA(Mesh mesh, IGeometryContext context) {
        Point3d[] vertices = ArrayPool<Point3d>.Shared.Rent(4);
        double[] edgeLengths = ArrayPool<double>.Shared.Rent(4);
        try {
            (double AspectRatio, double Skewness, double Jacobian)[] metrics = [.. Enumerable.Range(0, mesh.Faces.Count).Select(i => {
                Point3d center = mesh.Faces.GetFaceCenter(i);
                MeshFace face = mesh.Faces[i];
                bool isQuad = face.IsQuad;
                bool validIndices = face.A >= 0 && face.A < mesh.Vertices.Count
                    && face.B >= 0 && face.B < mesh.Vertices.Count
                    && face.C >= 0 && face.C < mesh.Vertices.Count
                    && (!isQuad || (face.D >= 0 && face.D < mesh.Vertices.Count));

                vertices[0] = validIndices ? (Point3d)mesh.Vertices[face.A] : center;
                vertices[1] = validIndices ? (Point3d)mesh.Vertices[face.B] : center;
                vertices[2] = validIndices ? (Point3d)mesh.Vertices[face.C] : center;
                vertices[3] = validIndices && isQuad ? (Point3d)mesh.Vertices[face.D] : vertices[0];

                int vertCount = isQuad ? 4 : 3;

                double minEdge = double.MaxValue;
                double maxEdge = double.MinValue;
                for (int j = 0; j < vertCount; j++) {
                    double length = vertices[j].DistanceTo(vertices[(j + 1) % vertCount]);
                    edgeLengths[j] = length;
                    minEdge = length < minEdge ? length : minEdge;
                    maxEdge = length > maxEdge ? length : maxEdge;
                }
                double aspectRatio = maxEdge / (minEdge + context.AbsoluteTolerance);

                double skewness = isQuad
                    ? ((double[])[
                        Vector3d.VectorAngle(vertices[1] - vertices[0], vertices[3] - vertices[0]),
                        Vector3d.VectorAngle(vertices[2] - vertices[1], vertices[0] - vertices[1]),
                        Vector3d.VectorAngle(vertices[3] - vertices[2], vertices[1] - vertices[2]),
                        Vector3d.VectorAngle(vertices[0] - vertices[3], vertices[2] - vertices[3]),
                    ]).Max(angle => Math.Abs(RhinoMath.ToDegrees(angle) - AnalysisConfig.QuadIdealAngleDegrees)) / AnalysisConfig.QuadIdealAngleDegrees
                    : (vertices[1] - vertices[0], vertices[2] - vertices[0], vertices[2] - vertices[1]) is (Vector3d ab, Vector3d ac, Vector3d bc)
                        ? (
                            RhinoMath.ToDegrees(Vector3d.VectorAngle(ab, ac)),
                            RhinoMath.ToDegrees(Vector3d.VectorAngle(bc, -ab)),
                            RhinoMath.ToDegrees(Vector3d.VectorAngle(-ac, -bc))
                        ) is (double angleA, double angleB, double angleC)
                            ? Math.Max(Math.Abs(angleA - AnalysisConfig.TriangleIdealAngleDegrees), Math.Max(Math.Abs(angleB - AnalysisConfig.TriangleIdealAngleDegrees), Math.Abs(angleC - AnalysisConfig.TriangleIdealAngleDegrees))) / AnalysisConfig.TriangleIdealAngleDegrees
                            : 1.0
                        : 1.0;

                double jacobian = isQuad
                    ? edgeLengths.Take(4).Average() is double avgLen && avgLen > context.AbsoluteTolerance
                        ? ((double[])[
                            Vector3d.CrossProduct(vertices[1] - vertices[0], vertices[3] - vertices[0]).Length,
                            Vector3d.CrossProduct(vertices[2] - vertices[1], vertices[0] - vertices[1]).Length,
                            Vector3d.CrossProduct(vertices[3] - vertices[2], vertices[1] - vertices[2]).Length,
                            Vector3d.CrossProduct(vertices[0] - vertices[3], vertices[2] - vertices[3]).Length,
                        ]).Min() / ((avgLen * avgLen) + context.AbsoluteTolerance)
                        : 0.0
                    : edgeLengths.Take(3).Average() is double triAvgLen && triAvgLen > context.AbsoluteTolerance
                        ? Vector3d.CrossProduct(vertices[1] - vertices[0], vertices[2] - vertices[0]).Length / ((2.0 * triAvgLen * triAvgLen) + context.AbsoluteTolerance)
                        : 0.0;

                return (AspectRatio: aspectRatio, Skewness: skewness, Jacobian: jacobian);
            }),
            ];
            return metrics.Length > 0
                ? ResultFactory.Create(value: new Analysis.MeshFEAData(
                    Location: mesh.GetBoundingBox(accurate: false).Center,
                    AspectRatios: [.. metrics.Select(m => m.AspectRatio),],
                    Skewness: [.. metrics.Select(m => m.Skewness),],
                    Jacobians: [.. metrics.Select(m => m.Jacobian),],
                    ProblematicFaceIndices: [.. metrics.Select((m, i) => (m, i)).Where(pair => pair.m.AspectRatio > AnalysisConfig.AspectRatioCritical || pair.m.Skewness > AnalysisConfig.SkewnessCritical || pair.m.Jacobian < AnalysisConfig.JacobianCritical).Select(pair => pair.i),],
                    QualityFlags: (metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioWarning || m.Skewness > AnalysisConfig.SkewnessWarning || m.Jacobian < AnalysisConfig.JacobianWarning), metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioCritical || m.Skewness > AnalysisConfig.SkewnessCritical || m.Jacobian < AnalysisConfig.JacobianCritical))))
                : ResultFactory.Create<Analysis.MeshFEAData>(error: E.Geometry.MeshAnalysisFailed);
        } finally {
            ArrayPool<Point3d>.Shared.Return(vertices, clearArray: true);
            ArrayPool<double>.Shared.Return(edgeLengths, clearArray: true);
        }
    }
}
