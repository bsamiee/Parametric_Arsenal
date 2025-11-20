using System.Buffers;
using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense geometric quality analysis algorithms.</summary>
internal static class AnalysisCompute {
    [Pure]
    internal static bool TryCurveData(Curve curve, IGeometryContext context, double? parameter, int derivativeOrder, out Analysis.CurveData result) {
        double param = parameter ?? curve.Domain.Mid;
        double[] buffer = ArrayPool<double>.Shared.Rent(AnalysisConfig.MaxDiscontinuities);
        try {
            int discCount = 0;
            double s = curve.Domain.Min;
            while (discCount < AnalysisConfig.MaxDiscontinuities && curve.GetNextDiscontinuity(Continuity.C1_continuous, s, curve.Domain.Max, out double discontinuity)) {
                buffer[discCount] = discontinuity;
                discCount++;
                s = discontinuity + context.AbsoluteTolerance;
            }

            if (!curve.FrameAt(param, out Plane frame)) {
                result = new Analysis.CurveData(Point3d.Unset, [], 0.0, Plane.Unset, [], 0.0, [], [], 0.0, Point3d.Unset);
                return false;
            }

            AreaMassProperties? amp = AreaMassProperties.Compute(curve);
            Vector3d[] derivatives = curve.DerivativeAt(param, derivativeOrder) is Vector3d[] d ? d : [];
            double[] frameParams = new double[AnalysisConfig.CurveFrameSampleCount];
            for (int i = 0; i < AnalysisConfig.CurveFrameSampleCount; i++) {
                frameParams[i] = curve.Domain.ParameterAt(AnalysisConfig.CurveFrameSampleCount > 1 ? i / (AnalysisConfig.CurveFrameSampleCount - 1.0) : 0.5);
            }
            Plane[] frames = curve.GetPerpendicularFrames(frameParams) is Plane[] pf ? pf : [];
            double[] discontinuities = buffer.AsSpan(0, discCount).ToArray();
            Continuity[] discTypes = [.. discontinuities.Select(dp => curve.IsContinuous(Continuity.C2_continuous, dp) ? Continuity.C1_continuous : Continuity.C0_continuous),];

            result = amp is not null
                ? new Analysis.CurveData(
                    curve.PointAt(param), derivatives, curve.CurvatureAt(param).Length, frame,
                    frames,
                    curve.TorsionAt(param), discontinuities,
                    discTypes,
                    curve.GetLength(), amp.Centroid)
                : new Analysis.CurveData(Point3d.Unset, [], 0.0, Plane.Unset, [], 0.0, [], [], 0.0, Point3d.Unset);
            return amp is not null;
        } finally {
            ArrayPool<double>.Shared.Return(buffer, clearArray: true);
        }
    }

    [Pure]
    internal static bool TrySurfaceData(Surface surface, IGeometryContext _, (double, double)? parameter, int derivativeOrder, out Analysis.SurfaceData result) {
        (double u, double v) = parameter ?? (surface.Domain(0).Mid, surface.Domain(1).Mid);
        bool evaluated = surface.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivatives) && surface.FrameAt(u, v, out Plane frame);
        SurfaceCurvature curvature = surface.CurvatureAt(u, v);
        AreaMassProperties? amp = evaluated ? AreaMassProperties.Compute(surface) : null;
        bool valid = evaluated && amp is not null && RhinoMath.IsValidDouble(curvature.Gaussian) && RhinoMath.IsValidDouble(curvature.Mean);
        result = valid
            ? new Analysis.SurfaceData(
                surface.PointAt(u, v), derivatives, curvature.Gaussian, curvature.Mean, curvature.Kappa(0), curvature.Kappa(1),
                curvature.Direction(0), curvature.Direction(1), frame, frame.Normal,
                surface.IsAtSeam(u, v) != 0, surface.IsAtSingularity(u, v, exact: true), amp!.Area, amp.Centroid)
            : new Analysis.SurfaceData(Point3d.Unset, [], 0.0, 0.0, 0.0, 0.0, Vector3d.Unset, Vector3d.Unset, Plane.Unset, Vector3d.Unset, false, false, 0.0, Point3d.Unset);
        return valid;
    }

    [Pure]
    internal static bool TryBrepData(Brep brep, IGeometryContext context, (double, double)? parameter, int faceIndex, Point3d? testPoint, int derivativeOrder, out Analysis.BrepData result) {
        int fIdx = RhinoMath.Clamp(faceIndex, 0, brep.Faces.Count - 1);
        using Surface sf = brep.Faces[fIdx].UnderlyingSurface();
        (double u, double v) = parameter ?? (sf.Domain(0).Mid, sf.Domain(1).Mid);
        Point3d point = testPoint ?? brep.GetBoundingBox(accurate: false).Center;
        bool eval = sf.Evaluate(u, v, derivativeOrder, out Point3d _, out Vector3d[] derivs) && sf.FrameAt(u, v, out Plane frame);
        bool hasClosest = brep.ClosestPoint(point, out Point3d cp, out ComponentIndex ci, out double uOut, out double vOut, context.AbsoluteTolerance * AnalysisConfig.BrepClosestPointToleranceMultiplier, out Vector3d _);
        SurfaceCurvature curvature = sf.CurvatureAt(u, v);
        using AreaMassProperties? amp = eval ? AreaMassProperties.Compute(brep) : null;
        using VolumeMassProperties? vmp = eval ? VolumeMassProperties.Compute(brep) : null;
        bool valid = eval && hasClosest && amp is not null && vmp is not null && RhinoMath.IsValidDouble(curvature.Gaussian) && RhinoMath.IsValidDouble(curvature.Mean);
        result = valid
            ? new Analysis.BrepData(
                sf.PointAt(u, v), derivs, curvature.Gaussian, curvature.Mean, curvature.Kappa(0), curvature.Kappa(1),
                curvature.Direction(0), curvature.Direction(1), frame, frame.Normal,
                [.. brep.Vertices.Select((vertex, i) => (i, vertex.Location)),],
                [.. brep.Edges.Select((edge, i) => (i, new Line(edge.PointAtStart, edge.PointAtEnd))),],
                brep.IsManifold, brep.IsSolid, cp, point.DistanceTo(cp),
                ci, (uOut, vOut), amp!.Area, vmp!.Volume, vmp.Centroid)
            : new Analysis.BrepData(Point3d.Unset, [], 0.0, 0.0, 0.0, 0.0, Vector3d.Unset, Vector3d.Unset, Plane.Unset, Vector3d.Unset, [], [], false, false, Point3d.Unset, 0.0, new ComponentIndex(), (0.0, 0.0), 0.0, 0.0, Point3d.Unset);
        return valid;
    }

    [Pure]
    internal static bool TryExtrusionData(Extrusion extrusion, IGeometryContext context, (double, double)? parameter, int faceIndex, Point3d? testPoint, int derivativeOrder, out Analysis.BrepData result) {
        Brep? brep = extrusion.ToBrep();
        if (brep is null) {
            result = new Analysis.BrepData(Point3d.Unset, [], 0.0, 0.0, 0.0, 0.0, Vector3d.Unset, Vector3d.Unset, Plane.Unset, Vector3d.Unset, [], [], false, false, Point3d.Unset, 0.0, new ComponentIndex(), (0.0, 0.0), 0.0, 0.0, Point3d.Unset);
            return false;
        }

        using (brep) {
            return TryBrepData(brep, context, parameter, faceIndex, testPoint, derivativeOrder, out result);
        }
    }

    [Pure]
    internal static bool TryMeshData(Mesh mesh, IGeometryContext context, int vertexIndex, out Analysis.MeshData result) {
        int vIdx = RhinoMath.Clamp(vertexIndex, 0, mesh.Vertices.Count - 1);
        Vector3d normal = mesh.Normals.Count > vIdx ? mesh.Normals[vIdx] : Vector3d.ZAxis;
        using AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
        using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
        bool valid = amp is not null && vmp is not null;
        result = valid
            ? new Analysis.MeshData(
                mesh.Vertices[vIdx], new Plane(mesh.Vertices[vIdx], normal), normal,
                [.. Enumerable.Range(0, mesh.TopologyVertices.Count).Select(i => (i, (Point3d)mesh.TopologyVertices[i])),],
                [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => (i, mesh.TopologyEdges.EdgeLine(i))),],
                mesh.IsManifold(topologicalTest: true, out bool _, out bool _), mesh.IsClosed, amp!.Area, vmp!.Volume)
            : new Analysis.MeshData(Point3d.Unset, Plane.Unset, Vector3d.Unset, [], [], false, false, 0.0, 0.0);
        return valid;
    }

    [Pure]
    internal static bool TrySurfaceQuality(Surface surface, IGeometryContext context, out Analysis.SurfaceQualityResult result, out string? failureReason) {
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
                uvGrid[uvIndex] = (u, v);
                uvIndex++;
                SurfaceCurvature curvature = surface.CurvatureAt(u: u, v: v);
                bool valid = RhinoMath.IsValidDouble(curvature.Gaussian) && RhinoMath.IsValidDouble(curvature.Mean);
                curvatures[validCount] = valid ? curvature : default;
                validCount += valid ? 1 : 0;
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

        double[] gaussianSorted = validCurvatures.Select(curvature => Math.Abs(curvature.Gaussian)).Order().ToArray();
        double medianGaussian = gaussianSorted.Length switch {
            0 => 0.0,
            1 => gaussianSorted[0],
            _ => gaussianSorted.Length % 2 is 0
                ? (gaussianSorted[(gaussianSorted.Length / 2) - 1] + gaussianSorted[gaussianSorted.Length / 2]) / 2.0
                : gaussianSorted[gaussianSorted.Length / 2],
        };

        double averageGaussian = validCurvatures.Length > 0
            ? validCurvatures.Average(curvature => Math.Abs(curvature.Gaussian))
            : 0.0;
        double stdDevGaussian = validCurvatures.Length > 0
            ? Math.Sqrt(validCurvatures.Sum(curvature => Math.Pow(Math.Abs(curvature.Gaussian) - averageGaussian, 2)) / validCurvatures.Length)
            : 0.0;

        bool hasSamples = validCurvatures.Length > 0;
        result = hasSamples
            ? new Analysis.SurfaceQualityResult(
                validCurvatures.Select(curvature => curvature.Gaussian).ToArray(),
                validCurvatures.Select(curvature => curvature.Mean).ToArray(),
                uvGrid.Where(uv =>
                    surface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)
                    || Math.Min(Math.Abs(uv.u - uDomain.Min), Math.Abs(uDomain.Max - uv.u)) <= singularityThresholdU
                    || Math.Min(Math.Abs(uv.v - vDomain.Min), Math.Abs(vDomain.Max - uv.v)) <= singularityThresholdV).ToArray(),
                RhinoMath.Clamp(medianGaussian > context.AbsoluteTolerance ? (1.0 - (stdDevGaussian / (medianGaussian * AnalysisConfig.HighCurvatureMultiplier))) : gaussianSorted[^1] < context.AbsoluteTolerance ? 1.0 : 0.0, 0.0, 1.0))
            : new Analysis.SurfaceQualityResult([], [], [], 0.0);
        failureReason = hasSamples ? null : "No valid curvature samples";
        return hasSamples;
    }

    [Pure]
    internal static bool TryCurveFairness(Curve curve, IGeometryContext context, out Analysis.CurveFairnessResult result, out string? failureReason) {
        const int maxSamples = AnalysisConfig.CurveFairnessSampleCount;
        (double Parameter, Vector3d Curvature)[] samples = new (double, Vector3d)[maxSamples];
        double[] curvatures = new double[maxSamples];
        int validCount = 0;
        const double sampleDivisor = maxSamples - 1.0;

        for (int i = 0; i < maxSamples; i++) {
            double t = curve.Domain.ParameterAt(i / sampleDivisor);
            Vector3d curvature = curve.CurvatureAt(t);
            bool valid = curvature.IsValid;
            samples[validCount] = valid ? (t, curvature) : samples[validCount];
            curvatures[validCount] = valid ? curvature.Length : curvatures[validCount];
            validCount += valid ? 1 : 0;
        }

        (double Parameter, Vector3d Curvature)[] validSamples = samples.AsSpan(0, validCount).ToArray();
        double[] validCurvatures = curvatures.AsSpan(0, validCount).ToArray();
        double curveLength = curve.GetLength();
        double averageDifference = validCurvatures.Length > 1
            ? Enumerable.Range(1, validCurvatures.Length - 1).Sum(i => Math.Abs(validCurvatures[i] - validCurvatures[i - 1])) / (validCurvatures.Length - 1)
            : 0.0;
        double maxCurvature = validCurvatures.Length > 0 ? validCurvatures.Max() : 0.0;
        double energyMetric = maxCurvature > context.AbsoluteTolerance && validCurvatures.Length > 0
            ? (validCurvatures.Sum(k => k * k) * (curveLength / (AnalysisConfig.CurveFairnessSampleCount - 1))) / (maxCurvature * curveLength)
            : 0.0;

        Analysis.CurveFairnessResult computed = validSamples.Length > 2
            ? new Analysis.CurveFairnessResult(
                RhinoMath.Clamp(1.0 / (1.0 + (averageDifference * AnalysisConfig.SmoothnessSensitivity)), 0.0, 1.0),
                validCurvatures,
                Enumerable.Range(1, validCurvatures.Length - 2)
                    .Where(i => Math.Abs((validCurvatures[i] - validCurvatures[i - 1]) - (validCurvatures[i + 1] - validCurvatures[i])) > AnalysisConfig.InflectionSharpnessThreshold || ((validCurvatures[i] - validCurvatures[i - 1]) * (validCurvatures[i + 1] - validCurvatures[i])) < 0)
                    .Select(i => (validSamples[i].Parameter, Math.Abs(validCurvatures[i] - validCurvatures[i - 1]) > AnalysisConfig.InflectionSharpnessThreshold))
                    .ToArray(),
                energyMetric)
            : new Analysis.CurveFairnessResult([], [], [], 0.0);
        result = computed;
        failureReason = validSamples.Length > 2 ? null : "Insufficient valid curvature samples";
        return validSamples.Length > 2;
    }

    [Pure]
    internal static bool TryMeshForFEA(Mesh mesh, IGeometryContext context, out Analysis.MeshElementQualityResult result) {
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
            bool hasMetrics = metrics.Length > 0;
            result = hasMetrics
                ? new Analysis.MeshElementQualityResult(
                    [.. metrics.Select(m => m.AspectRatio),],
                    [.. metrics.Select(m => m.Skewness),],
                    [.. metrics.Select(m => m.Jacobian),],
                    [.. metrics.Select((metric, i) => (metric, i)).Where(pair => pair.metric.AspectRatio > AnalysisConfig.AspectRatioCritical || pair.metric.Skewness > AnalysisConfig.SkewnessCritical || pair.metric.Jacobian < AnalysisConfig.JacobianCritical).Select(pair => pair.i),],
                    (
                        metrics.Count(metric => metric.AspectRatio > AnalysisConfig.AspectRatioWarning || metric.Skewness > AnalysisConfig.SkewnessWarning || metric.Jacobian < AnalysisConfig.JacobianWarning),
                        metrics.Count(metric => metric.AspectRatio > AnalysisConfig.AspectRatioCritical || metric.Skewness > AnalysisConfig.SkewnessCritical || metric.Jacobian < AnalysisConfig.JacobianCritical)
                    ))
                : new Analysis.MeshElementQualityResult([], [], [], [], (0, 0));
            return hasMetrics;
        } finally {
            ArrayPool<Point3d>.Shared.Return(vertices, clearArray: true);
            ArrayPool<double>.Shared.Return(edgeLengths, clearArray: true);
        }
    }
}
