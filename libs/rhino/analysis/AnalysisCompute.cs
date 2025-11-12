using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense geometric quality analysis algorithms.</summary>
internal static class AnalysisCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] GaussianSamples, double[] MeanSamples, (double U, double V)[] Singularities, double UniformityScore)> SurfaceQuality(Surface surface, IGeometryContext context) =>
        ResultFactory.Create(value: surface)
            .Validate(args: [context, V.Standard | V.BoundingBox | V.UVDomain,])
            .Bind(validSurface => {
                int gridSize = Math.Max(2, (int)Math.Sqrt(AnalysisConfig.SurfaceQualitySampleCount));
                Interval uDomain = validSurface.Domain(0);
                Interval vDomain = validSurface.Domain(1);
                double uThreshold = Math.Abs(uDomain.Length) * AnalysisConfig.SingularityProximityFactor;
                double vThreshold = Math.Abs(vDomain.Length) * AnalysisConfig.SingularityProximityFactor;
                Func<(double u, double v), bool> singularityDetector = uv =>
                    validSurface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)
                        || Math.Min(Math.Abs(uv.u - uDomain.Min), Math.Abs(uDomain.Max - uv.u)) <= uThreshold
                        || Math.Min(Math.Abs(uv.v - vDomain.Min), Math.Abs(vDomain.Max - uv.v)) <= vThreshold;
                (double u, double v)[] uvGrid = [.. Enumerable.Range(0, gridSize)
                    .SelectMany(i => Enumerable.Range(0, gridSize).Select(j => (u: uDomain.ParameterAt(i / (gridSize - 1.0)), v: vDomain.ParameterAt(j / (gridSize - 1.0))))),
                ];
                SurfaceCurvature[] curvatures = [.. uvGrid
                    .Select(uv => validSurface.CurvatureAt(u: uv.u, v: uv.v))
                    .Where(sc => !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian) && !double.IsNaN(sc.Mean) && !double.IsInfinity(sc.Mean)),
                ];
                Interval uDomain = validSurface.Domain(0);
                Interval vDomain = validSurface.Domain(1);
                double uSpan = Math.Abs(uDomain.Length);
                double vSpan = Math.Abs(vDomain.Length);
                double singularityThresholdU = uSpan > context.AbsoluteTolerance
                    ? uSpan * AnalysisConfig.SingularityProximityFactor
                    : context.AbsoluteTolerance;
                double singularityThresholdV = vSpan > context.AbsoluteTolerance
                    ? vSpan * AnalysisConfig.SingularityProximityFactor
                    : context.AbsoluteTolerance;
                return curvatures.Length > 0
                    && curvatures.Select(sc => Math.Abs(sc.Gaussian)).Order().ToArray() is double[] gaussianSorted
                    && (gaussianSorted.Length % 2 is 0 ? (gaussianSorted[(gaussianSorted.Length / 2) - 1] + gaussianSorted[gaussianSorted.Length / 2]) / 2.0 : gaussianSorted[gaussianSorted.Length / 2]) is double medianGaussian
                    && curvatures.Average(sc => Math.Abs(sc.Gaussian)) is double avgGaussian
                    && Math.Sqrt(curvatures.Sum(sc => Math.Pow(Math.Abs(sc.Gaussian) - avgGaussian, 2)) / curvatures.Length) is double stdDevGaussian
                    ? ResultFactory.Create(value: (
                        GaussianSamples: curvatures.Select(sc => sc.Gaussian).ToArray(),
                        MeanSamples: curvatures.Select(sc => sc.Mean).ToArray(),
                        Singularities: uvGrid.Where(singularityDetector.Invoke).ToArray(),
                        UniformityScore: Math.Clamp(medianGaussian > context.AbsoluteTolerance ? Math.Max(0.0, 1.0 - (stdDevGaussian / (medianGaussian * AnalysisConfig.HighCurvatureMultiplier))) : gaussianSorted[^1] < context.AbsoluteTolerance ? 1.0 : 0.0, 0.0, 1.0)))
                    : ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Geometry.SurfaceAnalysisFailed.WithContext("No valid curvature samples"));
            });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double SmoothnessScore, double[] CurvatureSamples, (double Parameter, bool IsSharp)[] InflectionPoints, double EnergyMetric)> CurveFairness(Curve curve, IGeometryContext context) =>
        ResultFactory.Create(value: curve)
            .Validate(args: [context, V.Standard | V.Degeneracy | V.SurfaceContinuity,])
            .Bind(validCurve => {
                (double Parameter, Vector3d Curvature)[] samples = [.. Enumerable.Range(0, AnalysisConfig.CurveFairnessSampleCount)
                    .Select(i => validCurve.Domain.ParameterAt(i / (AnalysisConfig.CurveFairnessSampleCount - 1.0)))
                    .Select(t => (Parameter: t, Curvature: validCurve.CurvatureAt(t)))
                    .Where(pair => pair.Curvature.IsValid),
                ];
                double[] curvatures = [.. samples.Select(s => s.Curvature.Length)];
                return samples.Length > 2
                    && Enumerable.Range(1, curvatures.Length - 1).Sum(i => Math.Abs(curvatures[i] - curvatures[i - 1])) / (curvatures.Length - 1) is double avgDiff
                    && validCurve.GetLength() is double curveLength
                    ? ResultFactory.Create(value: (
                        SmoothnessScore: Math.Clamp(1.0 / (1.0 + (avgDiff * AnalysisConfig.SmoothnessSensitivity)), 0.0, 1.0),
                        CurvatureSamples: curvatures,
                        InflectionPoints: Enumerable.Range(1, curvatures.Length - 2)
                            .Where(i => Math.Abs((curvatures[i] - curvatures[i - 1]) - (curvatures[i + 1] - curvatures[i])) > AnalysisConfig.InflectionSharpnessThreshold || ((curvatures[i] - curvatures[i - 1]) * (curvatures[i + 1] - curvatures[i])) < 0)
                            .Select(i => (samples[i].Parameter, Math.Abs(curvatures[i] - curvatures[i - 1]) > AnalysisConfig.InflectionSharpnessThreshold))
                            .ToArray(),
                        EnergyMetric: curvatures.Max() is double maxCurv && maxCurv > context.AbsoluteTolerance
                            ? (curvatures.Sum(k => k * k) * (curveLength / (AnalysisConfig.CurveFairnessSampleCount - 1))) / (maxCurv * curveLength)
                            : 0.0))
                    : ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed.WithContext("Insufficient valid curvature samples"));
            });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaces, (int Warning, int Critical) Counts)> MeshForFEA(Mesh mesh, IGeometryContext context) =>
        ResultFactory.Create(value: mesh)
            .Validate(args: [context, V.Standard | V.MeshSpecific,])
            .Bind(validMesh => {
                Point3d[] vertices = ArrayPool<Point3d>.Shared.Rent(4);
                double[] edgeLengths = ArrayPool<double>.Shared.Rent(4);
                try {
                    (double AspectRatio, double Skewness, double Jacobian)[] metrics = [.. Enumerable.Range(0, validMesh.Faces.Count).Select(i => {
                        Point3d center = validMesh.Faces.GetFaceCenter(i);
                        MeshFace face = validMesh.Faces[i];
                        bool isQuad = face.IsQuad;
                        bool validIndices = face.A >= 0 && face.A < validMesh.Vertices.Count
                            && face.B >= 0 && face.B < validMesh.Vertices.Count
                            && face.C >= 0 && face.C < validMesh.Vertices.Count
                            && (!isQuad || (face.D >= 0 && face.D < validMesh.Vertices.Count));

                        vertices[0] = validIndices ? (Point3d)validMesh.Vertices[face.A] : center;
                        vertices[1] = validIndices ? (Point3d)validMesh.Vertices[face.B] : center;
                        vertices[2] = validIndices ? (Point3d)validMesh.Vertices[face.C] : center;
                        vertices[3] = validIndices && isQuad ? (Point3d)validMesh.Vertices[face.D] : vertices[0];

                        int vertCount = isQuad ? 4 : 3;

                        for (int j = 0; j < vertCount; j++) {
                            edgeLengths[j] = vertices[j].DistanceTo(vertices[(j + 1) % vertCount]);
                        }
                        double minEdge = double.MaxValue;
                        double maxEdge = double.MinValue;
                        for (int j = 0; j < vertCount; j++) {
                            minEdge = Math.Min(minEdge, edgeLengths[j]);
                            maxEdge = Math.Max(maxEdge, edgeLengths[j]);
                        }
                        double aspectRatio = maxEdge / (minEdge + context.AbsoluteTolerance);

                        double skewness = isQuad
                            ? ((double[])[
                                Vector3d.VectorAngle(vertices[1] - vertices[0], vertices[3] - vertices[0]),
                                Vector3d.VectorAngle(vertices[2] - vertices[1], vertices[0] - vertices[1]),
                                Vector3d.VectorAngle(vertices[3] - vertices[2], vertices[1] - vertices[2]),
                                Vector3d.VectorAngle(vertices[0] - vertices[3], vertices[2] - vertices[3]),
                            ]).Max(angle => Math.Abs(RhinoMath.ToDegrees(angle) - 90.0)) / 90.0
                            : (vertices[1] - vertices[0], vertices[2] - vertices[0], vertices[2] - vertices[1]) is (Vector3d ab, Vector3d ac, Vector3d bc)
                                ? (
                                    RhinoMath.ToDegrees(Vector3d.VectorAngle(ab, ac)),
                                    RhinoMath.ToDegrees(Vector3d.VectorAngle(bc, -ab)),
                                    RhinoMath.ToDegrees(Vector3d.VectorAngle(-ac, -bc))
                                ) is (double angleA, double angleB, double angleC)
                                    ? Math.Max(Math.Abs(angleA - 60.0), Math.Max(Math.Abs(angleB - 60.0), Math.Abs(angleC - 60.0))) / 60.0
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
                        ? ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(value: (
                            [.. metrics.Select(m => m.AspectRatio),],
                            [.. metrics.Select(m => m.Skewness),],
                            [.. metrics.Select(m => m.Jacobian),],
                            [.. metrics.Select((m, i) => (m, i)).Where(pair => pair.m.AspectRatio > AnalysisConfig.AspectRatioCritical || pair.m.Skewness > AnalysisConfig.SkewnessCritical || pair.m.Jacobian < AnalysisConfig.JacobianCritical).Select(pair => pair.i),],
                            (metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioWarning || m.Skewness > AnalysisConfig.SkewnessWarning || m.Jacobian < AnalysisConfig.JacobianWarning), metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioCritical || m.Skewness > AnalysisConfig.SkewnessCritical || m.Jacobian < AnalysisConfig.JacobianCritical))))
                        : ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(error: E.Geometry.MeshAnalysisFailed);
                } finally {
                    ArrayPool<Point3d>.Shared.Return(vertices, clearArray: true);
                    ArrayPool<double>.Shared.Return(edgeLengths, clearArray: true);
                }
            });
}
