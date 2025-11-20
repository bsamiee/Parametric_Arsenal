using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense geometric quality and differential analysis algorithms.</summary>
internal static class AnalysisCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurveData> ComputeCurve(
        Curve curve,
        double parameter,
        int derivativeOrder,
        int frameSampleCount,
        int maxDiscontinuities,
        IGeometryContext context) {
        double[] buffer = ArrayPool<double>.Shared.Rent(maxDiscontinuities);
        try {
            int discCount = 0;
            double s = curve.Domain.Min;
            while (discCount < maxDiscontinuities && curve.GetNextDiscontinuity(Continuity.C1_continuous, s, curve.Domain.Max, out double td)) {
                buffer[discCount++] = td;
                s = td + context.AbsoluteTolerance;
            }
            double[] disc = [.. buffer[..discCount]];
            return !curve.FrameAt(parameter, out Plane frame)
                ? ResultFactory.Create<Analysis.CurveData>(error: E.Geometry.CurveAnalysisFailed)
                : ((Func<Result<Analysis.CurveData>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(curve);
                    return amp is null
                        ? ResultFactory.Create<Analysis.CurveData>(error: E.Geometry.CurveAnalysisFailed)
                        : ((Func<Result<Analysis.CurveData>>)(() => {
                            Vector3d[] derivatives = curve.DerivativeAt(parameter, derivativeOrder) is Vector3d[] d ? d : [];
                            double[] frameParams = new double[frameSampleCount];
                            for (int i = 0; i < frameSampleCount; i++) {
                                frameParams[i] = curve.Domain.ParameterAt(frameSampleCount > 1 ? i / (frameSampleCount - 1.0) : 0.5);
                            }
                            Plane[] frames = curve.GetPerpendicularFrames(frameParams) is Plane[] pf ? pf : [];
                            return ResultFactory.Create(value: new Analysis.CurveData(
                                Location: curve.PointAt(parameter),
                                Derivatives: derivatives,
                                Curvature: curve.CurvatureAt(parameter).Length,
                                Frame: frame,
                                PerpendicularFrames: frames,
                                Torsion: curve.TorsionAt(parameter),
                                DiscontinuityParameters: disc,
                                DiscontinuityTypes: [.. disc.Select(dp => curve.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                                Length: curve.GetLength(),
                                Centroid: amp.Centroid));
                        }))();
                }))();
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.SurfaceData> ComputeSurface(
        Surface surface,
        double u,
        double v,
        int derivativeOrder) =>
        !surface.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) || !surface.FrameAt(u, v, out Plane frame)
            ? ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed)
            : ((Func<Result<Analysis.SurfaceData>>)(() => {
                SurfaceCurvature sc = surface.CurvatureAt(u, v);
                using AreaMassProperties? amp = AreaMassProperties.Compute(surface);
                return amp is null || !RhinoMath.IsValidDouble(sc.Gaussian) || !RhinoMath.IsValidDouble(sc.Mean)
                    ? ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed)
                    : ResultFactory.Create(value: new Analysis.SurfaceData(
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
                        Centroid: amp.Centroid));
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.BrepData> ComputeBrep(
        Brep brep,
        int faceIndex,
        double u,
        double v,
        Point3d testPoint,
        int derivativeOrder,
        double closestPointToleranceMultiplier,
        IGeometryContext context) {
        int fIdx = RhinoMath.Clamp(faceIndex, 0, brep.Faces.Count - 1);
        using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
        return !sf.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs)
            || !sf.FrameAt(u, v, out Plane frame)
            || !brep.ClosestPoint(testPoint, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, context.AbsoluteTolerance * closestPointToleranceMultiplier, out Vector3d _)
            ? ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed)
            : ((Func<Result<Analysis.BrepData>>)(() => {
                SurfaceCurvature sc = sf.CurvatureAt(u, v);
                using AreaMassProperties? amp = AreaMassProperties.Compute(brep);
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                return amp is null || vmp is null || !RhinoMath.IsValidDouble(sc.Gaussian) || !RhinoMath.IsValidDouble(sc.Mean)
                    ? ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed)
                    : ResultFactory.Create(value: new Analysis.BrepData(
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
                        Distance: testPoint.DistanceTo(cp),
                        Component: ci,
                        SurfaceUV: (uOut, vOut),
                        Area: amp.Area,
                        Volume: vmp.Volume,
                        Centroid: vmp.Centroid));
            }))();
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.BrepData> ComputeExtrusion(
        Extrusion extrusion,
        int faceIndex,
        double u,
        double v,
        Point3d testPoint,
        int derivativeOrder,
        double closestPointToleranceMultiplier,
        IGeometryContext context) {
        using Brep? brep = extrusion.ToBrep();
        return brep is null
            ? ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed)
            : ComputeBrep(
                brep: brep,
                faceIndex: faceIndex,
                u: u,
                v: v,
                testPoint: testPoint,
                derivativeOrder: derivativeOrder,
                closestPointToleranceMultiplier: closestPointToleranceMultiplier,
                context: context);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.MeshData> ComputeMesh(
        Mesh mesh,
        int vertexIndex) {
        int vIdx = RhinoMath.Clamp(vertexIndex, 0, mesh.Vertices.Count - 1);
        Vector3d normal = mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis;
        using AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
        using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
        return amp is null || vmp is null
            ? ResultFactory.Create<Analysis.MeshData>(error: E.Geometry.MeshAnalysisFailed)
            : ResultFactory.Create(value: new Analysis.MeshData(
                Location: mesh.Vertices[vIdx],
                Frame: new Plane(mesh.Vertices[vIdx], normal),
                Normal: normal,
                TopologyVertices: [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])),],
                TopologyEdges: [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))),],
                IsManifold: mesh.IsManifold(topologicalTest: true, out bool _, out bool _),
                IsClosed: mesh.IsClosed,
                Area: amp.Area,
                Volume: vmp.Volume));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.SurfaceQualityResult> ComputeSurfaceQuality(
        Surface surface,
        int gridDimension,
        double boundaryFraction,
        double proximityFactor,
        double curvatureMultiplier,
        IGeometryContext context) {
        int gridSize = Math.Max(2, gridDimension);
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
            uSpan * proximityFactor,
            RhinoMath.SqrtEpsilon,
            uSpan * boundaryFraction);
        double singularityThresholdV = RhinoMath.Clamp(
            vSpan * proximityFactor,
            RhinoMath.SqrtEpsilon,
            vSpan * boundaryFraction);
        return validCurvatures.Length is 0
            ? ResultFactory.Create<Analysis.SurfaceQualityResult>(error: E.Geometry.SurfaceAnalysisFailed.WithContext("No valid curvature samples"))
            : ((Func<Result<Analysis.SurfaceQualityResult>>)(() => {
                double[] gaussianSorted = [.. validCurvatures.Select(sc => Math.Abs(sc.Gaussian)).Order(),];
                double medianGaussian = gaussianSorted.Length % 2 is 0
                    ? (gaussianSorted[(gaussianSorted.Length / 2) - 1] + gaussianSorted[gaussianSorted.Length / 2]) / 2.0
                    : gaussianSorted[gaussianSorted.Length / 2];
                double avgGaussian = validCurvatures.Average(sc => Math.Abs(sc.Gaussian));
                double stdDevGaussian = Math.Sqrt(validCurvatures.Sum(sc => Math.Pow(Math.Abs(sc.Gaussian) - avgGaussian, 2)) / validCurvatures.Length);
                return ResultFactory.Create(value: new Analysis.SurfaceQualityResult(
                    GaussianCurvatures: [.. validCurvatures.Select(sc => sc.Gaussian),],
                    MeanCurvatures: [.. validCurvatures.Select(sc => sc.Mean),],
                    SingularityLocations: [.. uvGrid.Where(uv =>
                        surface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)
                        || Math.Min(Math.Abs(uv.u - uDomain.Min), Math.Abs(uDomain.Max - uv.u)) <= singularityThresholdU
                        || Math.Min(Math.Abs(uv.v - vDomain.Min), Math.Abs(vDomain.Max - uv.v)) <= singularityThresholdV),],
                    UniformityScore: RhinoMath.Clamp(
                        medianGaussian > context.AbsoluteTolerance
                            ? (1.0 - (stdDevGaussian / (medianGaussian * curvatureMultiplier)))
                            : gaussianSorted[^1] < context.AbsoluteTolerance ? 1.0 : 0.0,
                        0.0,
                        1.0)));
            }))();
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurveFairnessResult> ComputeCurveFairness(
        Curve curve,
        int sampleCount,
        double inflectionThreshold,
        double smoothnessSensitivity,
        IGeometryContext context) {
        int maxSamples = Math.Max(2, sampleCount);
        (double Parameter, Vector3d Curvature)[] samples = new (double, Vector3d)[maxSamples];
        double[] curvatures = new double[maxSamples];
        int validCount = 0;
        double sampleDivisor = maxSamples - 1.0;
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
        return validSamples.Length <= 2
            ? ResultFactory.Create<Analysis.CurveFairnessResult>(error: E.Geometry.CurveAnalysisFailed.WithContext("Insufficient valid curvature samples"))
            : ((Func<Result<Analysis.CurveFairnessResult>>)(() => {
                double avgDiff = Enumerable.Range(1, validCurvatures.Length - 1).Sum(i => Math.Abs(validCurvatures[i] - validCurvatures[i - 1])) / (validCurvatures.Length - 1);
                double curveLength = curve.GetLength();
                return ResultFactory.Create(value: new Analysis.CurveFairnessResult(
                    SmoothnessScore: RhinoMath.Clamp(1.0 / (1.0 + (avgDiff * smoothnessSensitivity)), 0.0, 1.0),
                    CurvatureValues: validCurvatures,
                    InflectionPoints: [.. Enumerable.Range(1, validCurvatures.Length - 2)
                        .Where(i => Math.Abs((validCurvatures[i] - validCurvatures[i - 1]) - (validCurvatures[i + 1] - validCurvatures[i])) > inflectionThreshold || ((validCurvatures[i] - validCurvatures[i - 1]) * (validCurvatures[i + 1] - validCurvatures[i])) < 0)
                        .Select(i => (validSamples[i].Parameter, Math.Abs(validCurvatures[i] - validCurvatures[i - 1]) > inflectionThreshold)),],
                    BendingEnergy: validCurvatures.Max() is double maxCurv && maxCurv > context.AbsoluteTolerance
                        ? (validCurvatures.Sum(k => k * k) * (curveLength / (sampleCount - 1))) / (maxCurv * curveLength)
                        : 0.0));
            }))();
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.MeshQualityResult> ComputeMeshQuality(
        Mesh mesh,
        IGeometryContext context) {
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
            return metrics.Length is 0
                ? ResultFactory.Create<Analysis.MeshQualityResult>(error: E.Geometry.MeshAnalysisFailed)
                : ResultFactory.Create(value: new Analysis.MeshQualityResult(
                    AspectRatios: [.. metrics.Select(m => m.AspectRatio),],
                    Skewness: [.. metrics.Select(m => m.Skewness),],
                    Jacobians: [.. metrics.Select(m => m.Jacobian),],
                    ProblematicFaceIndices: [.. metrics.Select((m, i) => (m, i)).Where(pair => pair.m.AspectRatio > AnalysisConfig.AspectRatioCritical || pair.m.Skewness > AnalysisConfig.SkewnessCritical || pair.m.Jacobian < AnalysisConfig.JacobianCritical).Select(pair => pair.i),],
                    QualityFlags: (
                        Warning: metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioWarning || m.Skewness > AnalysisConfig.SkewnessWarning || m.Jacobian < AnalysisConfig.JacobianWarning),
                        Critical: metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioCritical || m.Skewness > AnalysisConfig.SkewnessCritical || m.Jacobian < AnalysisConfig.JacobianCritical))));
        } finally {
            ArrayPool<Point3d>.Shared.Return(vertices, clearArray: true);
            ArrayPool<double>.Shared.Return(edgeLengths, clearArray: true);
        }
    }
}
