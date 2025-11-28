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
    internal static Result<Analysis.SurfaceData> ComputeSurface(
        Surface surface,
        double u,
        double v,
        int derivativeOrder) =>
        (surface.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs), surface.FrameAt(u, v, out Plane frame), surface.CurvatureAt(u, v), AreaMassProperties.Compute(surface)) switch {
            (false, _, _, _) => ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed),
            (_, false, _, _) => ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed),
            (_, _, SurfaceCurvature sc, null) => ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed),
            (_, _, SurfaceCurvature sc, _) when !RhinoMath.IsValidDouble(sc.Gaussian) || !RhinoMath.IsValidDouble(sc.Mean) => ResultFactory.Create<Analysis.SurfaceData>(error: E.Geometry.SurfaceAnalysisFailed),
            (true, true, SurfaceCurvature sc, AreaMassProperties amp) => ((Func<Result<Analysis.SurfaceData>>)(() => {
                using (amp) {
                    return ResultFactory.Create(value: new Analysis.SurfaceData(
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
                }
            }))(),
        };

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
            (samples[validCount], curvatures[validCount], validCount) = curvature.IsValid
                ? ((t, curvature), curvature.Length, validCount + 1)
                : (samples[validCount], curvatures[validCount], validCount);
        }
        (double Parameter, Vector3d Curvature)[] validSamples = samples.AsSpan(0, validCount).ToArray();
        double[] validCurvatures = curvatures.AsSpan(0, validCount).ToArray();
        double avgDiff = validCurvatures.Length > 1
            ? Enumerable.Range(1, validCurvatures.Length - 1).Sum(i => Math.Abs(validCurvatures[i] - validCurvatures[i - 1])) / (validCurvatures.Length - 1)
            : 0.0;
        double curveLength = curve.GetLength();
        return validSamples.Length <= 2
            ? ResultFactory.Create<Analysis.CurveFairnessResult>(error: E.Geometry.CurveAnalysisFailed.WithContext("Insufficient valid curvature samples"))
            : ResultFactory.Create(value: new Analysis.CurveFairnessResult(
                SmoothnessScore: RhinoMath.Clamp(1.0 / (1.0 + (avgDiff * smoothnessSensitivity)), 0.0, 1.0),
                CurvatureValues: validCurvatures,
                InflectionPoints: [.. Enumerable.Range(1, validCurvatures.Length - 2)
                    .Where(i => Math.Abs((validCurvatures[i] - validCurvatures[i - 1]) - (validCurvatures[i + 1] - validCurvatures[i])) > inflectionThreshold || ((validCurvatures[i] - validCurvatures[i - 1]) * (validCurvatures[i + 1] - validCurvatures[i])) < 0)
                    .Select(i => (validSamples[i].Parameter, Math.Abs(validCurvatures[i] - validCurvatures[i - 1]) > inflectionThreshold)),
                ],
                BendingEnergy: validCurvatures.Max() is double maxCurv && maxCurv > context.AbsoluteTolerance
                    ? (validCurvatures.Sum(k => k * k) * (curveLength / (validCount - 1))) / (maxCurv * curveLength)
                    : 0.0));
    }

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
            return (curve.FrameAt(parameter, out Plane frame), AreaMassProperties.Compute(curve)) switch {
                (false, _) => ResultFactory.Create<Analysis.CurveData>(error: E.Geometry.CurveAnalysisFailed),
                (_, null) => ResultFactory.Create<Analysis.CurveData>(error: E.Geometry.CurveAnalysisFailed),
                (true, AreaMassProperties amp) => ((Func<Result<Analysis.CurveData>>)(() => {
                    using (amp) {
                        double[] frameParams = [.. Enumerable.Range(0, frameSampleCount).Select(i => curve.Domain.ParameterAt(frameSampleCount > 1 ? i / (frameSampleCount - 1.0) : 0.5)),];
                        return ResultFactory.Create(value: new Analysis.CurveData(
                            Location: curve.PointAt(parameter),
                            Derivatives: curve.DerivativeAt(parameter, derivativeOrder) ?? [],
                            Curvature: curve.CurvatureAt(parameter).Length,
                            Frame: frame,
                            PerpendicularFrames: curve.GetPerpendicularFrames(frameParams) ?? [],
                            Torsion: curve.TorsionAt(parameter),
                            DiscontinuityParameters: disc,
                            DiscontinuityTypes: [.. disc.Select(dp => curve.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),],
                            Length: curve.GetLength(),
                            Centroid: amp.Centroid));
                    }
                }))(),
            };
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    }

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
        return (sf.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs),
            sf.FrameAt(u, v, out Plane frame),
            brep.ClosestPoint(testPoint, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, context.AbsoluteTolerance * closestPointToleranceMultiplier, out Vector3d _),
            sf.CurvatureAt(u, v), AreaMassProperties.Compute(brep), VolumeMassProperties.Compute(brep)) switch {
                (false, _, _, _, _, _) => ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed),
                (_, false, _, _, _, _) => ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed),
                (_, _, false, _, _, _) => ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed),
                (_, _, _, SurfaceCurvature sc, _, _) when !RhinoMath.IsValidDouble(sc.Gaussian) || !RhinoMath.IsValidDouble(sc.Mean) => ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed),
                (_, _, _, _, null, _) => ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed),
                (_, _, _, _, _, null) => ResultFactory.Create<Analysis.BrepData>(error: E.Geometry.BrepAnalysisFailed),
                (true, true, true, SurfaceCurvature sc, AreaMassProperties amp, VolumeMassProperties vmp) => ((Func<Result<Analysis.BrepData>>)(() => {
                    using (amp)
                    using (vmp) {
                        return ResultFactory.Create(value: new Analysis.BrepData(
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
                    }
                }))(),
            };
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
        double gridDivisor = gridSize - 1.0;
        (double u, double v, SurfaceCurvature curvature)[] samples = [..
            from i in Enumerable.Range(0, gridSize)
            from j in Enumerable.Range(0, gridSize)
            let u = surface.Domain(0).ParameterAt(i / gridDivisor)
            let v = surface.Domain(1).ParameterAt(j / gridDivisor)
            let sc = surface.CurvatureAt(u, v)
            where RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean)
            select (u, v, sc),
        ];
        return samples.Length is 0
            ? ResultFactory.Create<Analysis.SurfaceQualityResult>(error: E.Geometry.SurfaceAnalysisFailed.WithContext("No valid curvature samples"))
            : ((Func<Result<Analysis.SurfaceQualityResult>>)(() => {
                (Interval uDomain, Interval vDomain) = (surface.Domain(0), surface.Domain(1));
                (double uSpan, double vSpan) = (uDomain.Length, vDomain.Length);
                (double singularityThresholdU, double singularityThresholdV) = (
                    RhinoMath.Clamp(uSpan * proximityFactor, RhinoMath.SqrtEpsilon, uSpan * boundaryFraction),
                    RhinoMath.Clamp(vSpan * proximityFactor, RhinoMath.SqrtEpsilon, vSpan * boundaryFraction));
                double[] gaussianAbs = new double[samples.Length];
                double gaussianSum = 0.0;
                for (int i = 0; i < samples.Length; i++) {
                    gaussianAbs[i] = Math.Abs(samples[i].curvature.Gaussian);
                    gaussianSum += gaussianAbs[i];
                }
                Array.Sort(gaussianAbs);
                double medianGaussian = gaussianAbs.Length % 2 is 0
                    ? (gaussianAbs[(gaussianAbs.Length / 2) - 1] + gaussianAbs[gaussianAbs.Length / 2]) / 2.0
                    : gaussianAbs[gaussianAbs.Length / 2];
                double avgGaussian = gaussianSum / samples.Length;
                double sumSquaredDiff = 0.0;
                for (int i = 0; i < gaussianAbs.Length; i++) {
                    double diff = gaussianAbs[i] - avgGaussian;
                    sumSquaredDiff += diff * diff;
                }
                return ResultFactory.Create(value: new Analysis.SurfaceQualityResult(
                    GaussianCurvatures: [.. samples.Select(s => s.curvature.Gaussian),],
                    MeanCurvatures: [.. samples.Select(s => s.curvature.Mean),],
                    SingularityLocations: [.. samples.Select(s => (s.u, s.v)).Where(uv =>
                        surface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)
                        || Math.Min(Math.Abs(uv.u - uDomain.Min), Math.Abs(uDomain.Max - uv.u)) <= singularityThresholdU
                        || Math.Min(Math.Abs(uv.v - vDomain.Min), Math.Abs(vDomain.Max - uv.v)) <= singularityThresholdV),
                    ],
                    UniformityScore: RhinoMath.Clamp(
                        medianGaussian > context.AbsoluteTolerance
                            ? (1.0 - (Math.Sqrt(sumSquaredDiff / samples.Length) / (medianGaussian * curvatureMultiplier)))
                            : gaussianAbs[^1] < context.AbsoluteTolerance ? 1.0 : 0.0,
                        0.0,
                        1.0)));
            }))();
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.MeshQualityResult> ComputeMeshQuality(
        Mesh mesh,
        IGeometryContext context) =>
        mesh.Faces.Count is 0
            ? ResultFactory.Create<Analysis.MeshQualityResult>(error: E.Geometry.MeshAnalysisFailed)
            : ((Func<Result<Analysis.MeshQualityResult>>)(() => {
                int faceCount = mesh.Faces.Count;
                Point3d[] vertices = ArrayPool<Point3d>.Shared.Rent(4);
                double[] aspectRatios = new double[faceCount];
                double[] skewness = new double[faceCount];
                double[] jacobians = new double[faceCount];
                int warningCount = 0;
                int criticalCount = 0;
                try {
                    for (int i = 0; i < faceCount; i++) {
                        MeshFace face = mesh.Faces[i];
                        bool isQuad = face.IsQuad;
                        Point3d center = mesh.Faces.GetFaceCenter(i);
                        bool validIndices = face.A >= 0 && face.A < mesh.Vertices.Count
                            && face.B >= 0 && face.B < mesh.Vertices.Count
                            && face.C >= 0 && face.C < mesh.Vertices.Count
                            && (!isQuad || (face.D >= 0 && face.D < mesh.Vertices.Count));
                        vertices[0] = validIndices ? (Point3d)mesh.Vertices[face.A] : center;
                        vertices[1] = validIndices ? (Point3d)mesh.Vertices[face.B] : center;
                        vertices[2] = validIndices ? (Point3d)mesh.Vertices[face.C] : center;
                        vertices[3] = validIndices && isQuad ? (Point3d)mesh.Vertices[face.D] : vertices[0];
                        double edge0 = vertices[0].DistanceTo(vertices[1]);
                        double edge1 = vertices[1].DistanceTo(vertices[2]);
                        double edge2 = vertices[2].DistanceTo(isQuad ? vertices[3] : vertices[0]);
                        double edge3 = isQuad ? vertices[3].DistanceTo(vertices[0]) : edge0;
                        double minEdge = Math.Min(Math.Min(edge0, edge1), Math.Min(edge2, edge3));
                        double maxEdge = Math.Max(Math.Max(edge0, edge1), Math.Max(edge2, edge3));
                        aspectRatios[i] = maxEdge / (minEdge + context.AbsoluteTolerance);
                        skewness[i] = isQuad
                            ? ((Func<double>)(() => {
                                double angle0 = Math.Abs(RhinoMath.ToDegrees(Vector3d.VectorAngle(vertices[1] - vertices[0], vertices[3] - vertices[0])) - AnalysisConfig.QuadIdealAngleDegrees);
                                double angle1 = Math.Abs(RhinoMath.ToDegrees(Vector3d.VectorAngle(vertices[2] - vertices[1], vertices[0] - vertices[1])) - AnalysisConfig.QuadIdealAngleDegrees);
                                double angle2 = Math.Abs(RhinoMath.ToDegrees(Vector3d.VectorAngle(vertices[3] - vertices[2], vertices[1] - vertices[2])) - AnalysisConfig.QuadIdealAngleDegrees);
                                double angle3 = Math.Abs(RhinoMath.ToDegrees(Vector3d.VectorAngle(vertices[0] - vertices[3], vertices[2] - vertices[3])) - AnalysisConfig.QuadIdealAngleDegrees);
                                return Math.Max(Math.Max(angle0, angle1), Math.Max(angle2, angle3)) / AnalysisConfig.QuadIdealAngleDegrees;
                            }))()
                            : ((Func<double>)(() => {
                                double angleA = Math.Abs(RhinoMath.ToDegrees(Vector3d.VectorAngle(vertices[1] - vertices[0], vertices[2] - vertices[0])) - AnalysisConfig.TriangleIdealAngleDegrees);
                                double angleB = Math.Abs(RhinoMath.ToDegrees(Vector3d.VectorAngle(vertices[2] - vertices[1], vertices[0] - vertices[1])) - AnalysisConfig.TriangleIdealAngleDegrees);
                                double angleC = Math.Abs(RhinoMath.ToDegrees(Vector3d.VectorAngle(vertices[0] - vertices[2], vertices[1] - vertices[2])) - AnalysisConfig.TriangleIdealAngleDegrees);
                                return Math.Max(Math.Max(angleA, angleB), angleC) / AnalysisConfig.TriangleIdealAngleDegrees;
                            }))();
                        double avgEdge = isQuad ? (edge0 + edge1 + edge2 + edge3) / 4.0 : (edge0 + edge1 + edge2) / 3.0;
                        jacobians[i] = avgEdge > context.AbsoluteTolerance
                            ? isQuad
                                ? Math.Min(
                                    Math.Min(
                                        Vector3d.CrossProduct(vertices[1] - vertices[0], vertices[3] - vertices[0]).Length,
                                        Vector3d.CrossProduct(vertices[2] - vertices[1], vertices[0] - vertices[1]).Length),
                                    Math.Min(
                                        Vector3d.CrossProduct(vertices[3] - vertices[2], vertices[1] - vertices[2]).Length,
                                        Vector3d.CrossProduct(vertices[0] - vertices[3], vertices[2] - vertices[3]).Length)
                                ) / ((avgEdge * avgEdge) + context.AbsoluteTolerance)
                                : Vector3d.CrossProduct(vertices[1] - vertices[0], vertices[2] - vertices[0]).Length / ((2.0 * avgEdge * avgEdge) + context.AbsoluteTolerance)
                            : 0.0;
                        bool isCritical = aspectRatios[i] > AnalysisConfig.AspectRatioCritical || skewness[i] > AnalysisConfig.SkewnessCritical || jacobians[i] < AnalysisConfig.JacobianCritical;
                        bool isWarning = aspectRatios[i] > AnalysisConfig.AspectRatioWarning || skewness[i] > AnalysisConfig.SkewnessWarning || jacobians[i] < AnalysisConfig.JacobianWarning;
                        criticalCount += isCritical ? 1 : 0;
                        warningCount += isWarning ? 1 : 0;
                    }
                    int[] problematicFaceIndices = [.. Enumerable.Range(0, faceCount)
                        .Where(i => aspectRatios[i] > AnalysisConfig.AspectRatioCritical
                            || skewness[i] > AnalysisConfig.SkewnessCritical
                            || jacobians[i] < AnalysisConfig.JacobianCritical),
                    ];
                    return ResultFactory.Create(value: new Analysis.MeshQualityResult(
                        AspectRatios: aspectRatios,
                        Skewness: skewness,
                        Jacobians: jacobians,
                        ProblematicFaceIndices: problematicFaceIndices,
                        QualityFlags: (Warning: warningCount, Critical: criticalCount)));
                } finally {
                    ArrayPool<Point3d>.Shared.Return(vertices, clearArray: true);
                }
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurvatureProfileResult> ComputeCurvatureProfile(
        Curve curve,
        int sampleCount,
        bool includeTorsion,
        IGeometryContext __) {
        int count = Math.Max(2, sampleCount);
        double[] parameters = new double[count];
        double[] curvatures = new double[count];
        double[]? torsions = includeTorsion ? new double[count] : null;
        double divisor = count - 1.0;
        double sum = 0.0;
        double min = double.MaxValue;
        double max = double.MinValue;
        int minIdx = 0;
        int maxIdx = 0;
        for (int i = 0; i < count; i++) {
            double t = curve.Domain.ParameterAt(i / divisor);
            parameters[i] = t;
            Vector3d curvatureVec = curve.CurvatureAt(t);
            double k = curvatureVec.IsValid ? curvatureVec.Length : 0.0;
            curvatures[i] = k;
            sum += k;
            (min, minIdx) = k < min ? (k, i) : (min, minIdx);
            (max, maxIdx) = k > max ? (k, i) : (max, maxIdx);
            torsions = torsions is double[] arr ? (arr[i] = curve.TorsionAt(t), arr).arr : null;
        }
        double mean = sum / count;
        double varianceSum = 0.0;
        for (int i = 0; i < count; i++) {
            double diff = curvatures[i] - mean;
            varianceSum += diff * diff;
        }
        return ResultFactory.Create(value: new Analysis.CurvatureProfileResult(
            Parameters: parameters,
            CurvatureValues: curvatures,
            TorsionValues: torsions,
            ExtremaLocations: [
                (parameters[minIdx], min),
                (parameters[maxIdx], max),
            ],
            MinCurvature: min,
            MaxCurvature: max,
            MeanCurvature: mean,
            Variance: varianceSum / count));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.SurfaceCurvatureProfileResult> ComputeSurfaceCurvatureProfile(
        Surface surface,
        int sampleCountU,
        int sampleCountV,
        Analysis.CurvatureProfileDirection direction,
        IGeometryContext context) {
        int countU = Math.Max(2, sampleCountU);
        int countV = Math.Max(2, sampleCountV);
        (int effectiveU, int effectiveV) = direction switch {
            Analysis.UDirection => (countU, 1),
            Analysis.VDirection => (1, countV),
            _ => (countU, countV),
        };
        int totalSamples = effectiveU * effectiveV;
        (double U, double V)[] locations = new (double, double)[totalSamples];
        double[] gaussianValues = new double[totalSamples];
        double[] meanValues = new double[totalSamples];
        double divisorU = effectiveU > 1 ? effectiveU - 1.0 : 1.0;
        double divisorV = effectiveV > 1 ? effectiveV - 1.0 : 1.0;
        double gaussianMin = double.MaxValue;
        double gaussianMax = double.MinValue;
        double meanMin = double.MaxValue;
        double meanMax = double.MinValue;
        int gaussianMinIdx = 0;
        int gaussianMaxIdx = 0;
        int meanMinIdx = 0;
        int meanMaxIdx = 0;
        double gaussianSum = 0.0;
        int validCount = 0;
        for (int i = 0; i < effectiveU; i++) {
            for (int j = 0; j < effectiveV; j++) {
                int idx = (i * effectiveV) + j;
                double u = surface.Domain(0).ParameterAt(i / divisorU);
                double v = surface.Domain(1).ParameterAt(j / divisorV);
                locations[idx] = (u, v);
                SurfaceCurvature sc = surface.CurvatureAt(u, v);
                bool valid = RhinoMath.IsValidDouble(sc.Gaussian) && RhinoMath.IsValidDouble(sc.Mean);
                gaussianValues[idx] = valid ? sc.Gaussian : 0.0;
                meanValues[idx] = valid ? sc.Mean : 0.0;
                validCount += valid ? 1 : 0;
                gaussianSum += valid ? Math.Abs(sc.Gaussian) : 0.0;
                (gaussianMin, gaussianMinIdx) = valid && sc.Gaussian < gaussianMin ? (sc.Gaussian, idx) : (gaussianMin, gaussianMinIdx);
                (gaussianMax, gaussianMaxIdx) = valid && sc.Gaussian > gaussianMax ? (sc.Gaussian, idx) : (gaussianMax, gaussianMaxIdx);
                (meanMin, meanMinIdx) = valid && sc.Mean < meanMin ? (sc.Mean, idx) : (meanMin, meanMinIdx);
                (meanMax, meanMaxIdx) = valid && sc.Mean > meanMax ? (sc.Mean, idx) : (meanMax, meanMaxIdx);
            }
        }
        return validCount is 0
            ? ResultFactory.Create<Analysis.SurfaceCurvatureProfileResult>(error: E.Geometry.SurfaceAnalysisFailed.WithContext("No valid curvature samples"))
            : ResultFactory.Create(value: new Analysis.SurfaceCurvatureProfileResult(
                SampleLocations: locations,
                GaussianValues: gaussianValues,
                MeanValues: meanValues,
                GaussianExtrema: [
                    (locations[gaussianMinIdx].U, locations[gaussianMinIdx].V, gaussianMin),
                    (locations[gaussianMaxIdx].U, locations[gaussianMaxIdx].V, gaussianMax),
                ],
                MeanExtrema: [
                    (locations[meanMinIdx].U, locations[meanMinIdx].V, meanMin),
                    (locations[meanMaxIdx].U, locations[meanMaxIdx].V, meanMax),
                ],
                GaussianRange: gaussianMax - gaussianMin,
                MeanRange: meanMax - meanMin,
                UniformityScore: RhinoMath.Clamp(
                    (gaussianSum / validCount) > context.AbsoluteTolerance
                        ? 1.0 / (1.0 + ((gaussianMax - gaussianMin) / (gaussianSum / validCount)))
                        : (gaussianMax - gaussianMin) < context.AbsoluteTolerance ? 1.0 : 0.0,
                    0.0,
                    1.0)));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.ShapeConformanceResult> ComputeShapeConformance(
        Surface surface,
        Analysis.ShapeTarget target,
        int sampleCount,
        IGeometryContext context) =>
        TryFitPrimitive(surface: surface, target: target, sampleCount: sampleCount, _: context) switch {
            null => ResultFactory.Create<Analysis.ShapeConformanceResult>(
                error: E.Geometry.SurfaceAnalysisFailed.WithContext("No conforming primitive detected")),
            (Analysis.ShapeTarget detected, object primitive, double[] deviations, Point3d maxLoc) result =>
                ResultFactory.Create(value: new Analysis.ShapeConformanceResult(
                    DetectedShape: result.detected,
                    IdealPrimitive: result.primitive,
                    MaxDeviation: result.deviations.Max(),
                    MinDeviation: result.deviations.Min(),
                    MeanDeviation: result.deviations.Average(),
                    RmsDeviation: Math.Sqrt(result.deviations.Sum(d => d * d) / result.deviations.Length),
                    MaxDeviationLocation: result.maxLoc,
                    ConformanceScore: ComputeConformanceScore(deviations: result.deviations, tolerance: context.AbsoluteTolerance),
                    WithinTolerance: result.deviations.Max() <= context.AbsoluteTolerance)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Analysis.CurveConformanceResult> ComputeCurveConformance(
        Curve curve,
        Analysis.CurveShapeTarget target,
        int sampleCount,
        IGeometryContext context) =>
        TryFitCurvePrimitive(curve: curve, target: target, sampleCount: sampleCount) switch {
            null => ResultFactory.Create<Analysis.CurveConformanceResult>(
                error: E.Geometry.CurveAnalysisFailed.WithContext("No conforming curve primitive detected")),
            (Analysis.CurveShapeTarget detected, object primitive, double[] deviations) result =>
                ResultFactory.Create(value: new Analysis.CurveConformanceResult(
                    DetectedShape: result.detected,
                    IdealPrimitive: result.primitive,
                    MaxDeviation: result.deviations.Max(),
                    MeanDeviation: result.deviations.Average(),
                    RmsDeviation: Math.Sqrt(result.deviations.Sum(d => d * d) / result.deviations.Length),
                    ConformanceScore: ComputeConformanceScore(deviations: result.deviations, tolerance: context.AbsoluteTolerance))),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Analysis.ShapeTarget detected, object primitive, double[] deviations, Point3d maxLoc)? TryFitPrimitive(
        Surface surface,
        Analysis.ShapeTarget target,
        int sampleCount,
        IGeometryContext _) =>
        target switch {
            Analysis.PlanarTarget => surface.TryGetPlane(out Plane plane)
                ? ComputeSurfaceDeviations(surface: surface, primitive: plane, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                    ? (new Analysis.PlanarTarget(), plane, devs, maxPt)
                    : null
                : null,
            Analysis.CylindricalTarget => surface.TryGetCylinder(out Cylinder cyl)
                ? ComputeSurfaceDeviations(surface: surface, primitive: cyl, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                    ? (new Analysis.CylindricalTarget(), cyl, devs, maxPt)
                    : null
                : null,
            Analysis.SphericalTarget => surface.TryGetSphere(out Sphere sph)
                ? ComputeSurfaceDeviations(surface: surface, primitive: sph, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                    ? (new Analysis.SphericalTarget(), sph, devs, maxPt)
                    : null
                : null,
            Analysis.ConicalTarget => surface.TryGetCone(out Cone cone)
                ? ComputeSurfaceDeviations(surface: surface, primitive: cone, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                    ? (new Analysis.ConicalTarget(), cone, devs, maxPt)
                    : null
                : null,
            Analysis.ToroidalTarget => surface.TryGetTorus(out Torus torus)
                ? ComputeSurfaceDeviations(surface: surface, primitive: torus, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                    ? (new Analysis.ToroidalTarget(), torus, devs, maxPt)
                    : null
                : null,
            Analysis.AnyTarget => TryFitBestPrimitive(surface: surface, sampleCount: sampleCount),
            _ => null,
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Analysis.ShapeTarget, object, double[], Point3d)? TryFitBestPrimitive(
        Surface surface,
        int sampleCount) =>
        (surface.TryGetPlane(out Plane plane),
         surface.TryGetCylinder(out Cylinder cyl),
         surface.TryGetSphere(out Sphere sph),
         surface.TryGetCone(out Cone cone),
         surface.TryGetTorus(out Torus torus)) switch {
            (true, _, _, _, _) => ComputeSurfaceDeviations(surface: surface, primitive: plane, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                ? (new Analysis.PlanarTarget(), plane, devs, maxPt)
                : null,
            (_, true, _, _, _) => ComputeSurfaceDeviations(surface: surface, primitive: cyl, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                ? (new Analysis.CylindricalTarget(), cyl, devs, maxPt)
                : null,
            (_, _, true, _, _) => ComputeSurfaceDeviations(surface: surface, primitive: sph, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                ? (new Analysis.SphericalTarget(), sph, devs, maxPt)
                : null,
            (_, _, _, true, _) => ComputeSurfaceDeviations(surface: surface, primitive: cone, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                ? (new Analysis.ConicalTarget(), cone, devs, maxPt)
                : null,
            (_, _, _, _, true) => ComputeSurfaceDeviations(surface: surface, primitive: torus, sampleCount: sampleCount) is (double[] devs, Point3d maxPt)
                ? (new Analysis.ToroidalTarget(), torus, devs, maxPt)
                : null,
            _ => null,
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double[] deviations, Point3d maxDeviationPoint)? ComputeSurfaceDeviations(
        Surface surface,
        object primitive,
        int sampleCount) {
        int gridSize = Math.Max(3, (int)Math.Sqrt(sampleCount));
        double divisor = gridSize - 1.0;
        double[] deviations = new double[gridSize * gridSize];
        double maxDeviation = 0.0;
        Point3d maxPoint = Point3d.Origin;
        for (int i = 0; i < gridSize; i++) {
            for (int j = 0; j < gridSize; j++) {
                int idx = (i * gridSize) + j;
                double u = surface.Domain(0).ParameterAt(i / divisor);
                double v = surface.Domain(1).ParameterAt(j / divisor);
                Point3d pt = surface.PointAt(u, v);
                double deviation = primitive switch {
                    Plane p => Math.Abs(p.DistanceTo(pt)),
                    Cylinder c => ComputeCylinderDeviation(point: pt, cylinder: c),
                    Sphere s => Math.Abs(pt.DistanceTo(s.ClosestPoint(pt))),
                    Cone cn => ComputeConeDeviation(point: pt, cone: cn),
                    Torus t => ComputeTorusDeviation(point: pt, torus: t),
                    _ => 0.0,
                };
                deviations[idx] = deviation;
                (maxDeviation, maxPoint) = deviation > maxDeviation ? (deviation, pt) : (maxDeviation, maxPoint);
            }
        }
        return (deviations, maxPoint);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeCylinderDeviation(Point3d point, Cylinder cylinder) {
        // Project point onto cylinder axis and compute radial distance
        Circle baseCircle = cylinder.CircleAt(0.0);
        Line axisLine = new(baseCircle.Center, baseCircle.Center + (cylinder.Axis * cylinder.TotalHeight));
        Point3d closestOnAxis = axisLine.ClosestPoint(point, limitToFiniteSegment: false);
        double radialDistance = point.DistanceTo(closestOnAxis);
        return Math.Abs(radialDistance - baseCircle.Radius);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeConeDeviation(Point3d point, Cone cone) {
        // Compute distance to cone surface using axis projection
        Vector3d toPoint = point - cone.ApexPoint;
        Vector3d axis = cone.Axis;
        bool unitized = axis.Unitize();
        double axialDistance = unitized ? toPoint * axis : 0.0;
        double expectedRadius = axialDistance > 0 ? axialDistance * Math.Tan(cone.AngleInRadians()) : 0.0;
        Point3d projectedOnAxis = cone.ApexPoint + (axis * axialDistance);
        double radialDistance = point.DistanceTo(projectedOnAxis);
        return Math.Abs(radialDistance - expectedRadius);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeTorusDeviation(Point3d point, Torus torus) {
        // Project point to torus plane, compute distance to major radius, then minor radius
        Point3d projectedOnPlane = torus.Plane.ClosestPoint(point);
        Vector3d toProjected = projectedOnPlane - torus.Plane.Origin;
        double majorDist = toProjected.Length;
        Point3d closestOnMajorCircle = majorDist > RhinoMath.ZeroTolerance
            ? torus.Plane.Origin + (toProjected * (torus.MajorRadius / majorDist))
            : torus.Plane.Origin + (torus.Plane.XAxis * torus.MajorRadius);
        double distanceToMinorCenter = point.DistanceTo(closestOnMajorCircle);
        return Math.Abs(distanceToMinorCenter - torus.MinorRadius);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Analysis.CurveShapeTarget detected, object primitive, double[] deviations)? TryFitCurvePrimitive(
        Curve curve,
        Analysis.CurveShapeTarget target,
        int sampleCount) =>
        target switch {
            Analysis.LinearTarget => curve.IsLinear(RhinoMath.ZeroTolerance) && curve.TryGetPolyline(out Polyline polyline) && polyline.Count >= 2
                ? ((Func<(Analysis.CurveShapeTarget, object, double[])?>)(() => {
                    Line line = new(polyline[0], polyline[^1]);
                    return (new Analysis.LinearTarget(), line, ComputeCurveDeviations(curve: curve, primitive: line, sampleCount: sampleCount));
                }))()
                : null,
            Analysis.CircularTarget => curve.TryGetArc(out Arc arc)
                ? (new Analysis.CircularTarget(), arc, ComputeCurveDeviations(curve: curve, primitive: arc, sampleCount: sampleCount))
                : null,
            Analysis.AnyCurveTarget => TryFitBestCurvePrimitive(curve: curve, sampleCount: sampleCount),
            _ => null,
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Analysis.CurveShapeTarget, object, double[])? TryFitBestCurvePrimitive(
        Curve curve,
        int sampleCount) {
        Polyline? polyline = null;
        bool isLinear = curve.IsLinear(RhinoMath.ZeroTolerance) && curve.TryGetPolyline(out polyline) && polyline.Count >= 2;
        bool isArc = curve.TryGetArc(out Arc arc);
        return (isLinear, isArc, polyline) switch {
            (true, _, Polyline pl) when pl.Count >= 2 => ((Func<(Analysis.CurveShapeTarget, object, double[])?>)(() => {
                Line line = new(pl[0], pl[^1]);
                return (new Analysis.LinearTarget(), line, ComputeCurveDeviations(curve: curve, primitive: line, sampleCount: sampleCount));
            }))(),
            (_, true, _) => (new Analysis.CircularTarget(), arc, ComputeCurveDeviations(curve: curve, primitive: arc, sampleCount: sampleCount)),
            _ => null,
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double[] ComputeCurveDeviations(
        Curve curve,
        object primitive,
        int sampleCount) {
        int count = Math.Max(3, sampleCount);
        double[] deviations = new double[count];
        double divisor = count - 1.0;
        for (int i = 0; i < count; i++) {
            double t = curve.Domain.ParameterAt(i / divisor);
            Point3d pt = curve.PointAt(t);
            deviations[i] = primitive switch {
                Line ln => pt.DistanceTo(ln.ClosestPoint(pt, limitToFiniteSegment: false)),
                Arc a => pt.DistanceTo(a.ClosestPoint(pt)),
                _ => 0.0,
            };
        }
        return deviations;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeConformanceScore(
        double[] deviations,
        double tolerance) {
        double maxDev = deviations.Max();
        return maxDev <= tolerance
            ? 1.0
            : RhinoMath.Clamp(1.0 / (1.0 + (maxDev / tolerance)), 0.0, 1.0);
    }
}
