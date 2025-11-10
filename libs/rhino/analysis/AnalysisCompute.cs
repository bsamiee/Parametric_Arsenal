using System.Buffers;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Dense geometric quality analysis algorithms with zero duplication.</summary>
internal static class AnalysisCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] GaussianSamples, double[] MeanSamples, (double U, double V)[] Singularities, double UniformityScore)> SurfaceQuality(Surface surface, IGeometryContext context) =>
        !surface.IsValid
            ? ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Validation.GeometryInvalid)
            : surface.Domain(0).Length <= context.AbsoluteTolerance || surface.Domain(1).Length <= context.AbsoluteTolerance
                ? ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Geometry.SurfaceAnalysisFailed.WithContext("Surface domain too small"))
                : ((Func<Result<(double[], double[], (double, double)[], double)>>)(() => {
                    int gridSize = Math.Max(2, (int)Math.Sqrt(AnalysisConfig.SurfaceQualitySampleCount));
                    (double u, double v)[] uvGrid = [.. Enumerable.Range(0, gridSize)
                        .SelectMany(i => Enumerable.Range(0, gridSize).Select(j => (u: surface.Domain(0).ParameterAt(i / (gridSize - 1.0)), v: surface.Domain(1).ParameterAt(j / (gridSize - 1.0))))),
                    ];
                    SurfaceCurvature[] curvatures = [.. uvGrid
                        .Select(uv => surface.CurvatureAt(u: uv.u, v: uv.v))
                        .Where(sc => !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian) && !double.IsNaN(sc.Mean) && !double.IsInfinity(sc.Mean)),
                    ];
                    return curvatures.Length > 0
                        ? ((Func<Result<(double[], double[], (double, double)[], double)>>)(() => {
                            double[] gaussianSorted = [.. curvatures.Select(sc => Math.Abs(sc.Gaussian)).Order()];
                            double medianGaussian = gaussianSorted.Length > 0
                                ? (gaussianSorted.Length % 2 is 0
                                    ? (gaussianSorted[(gaussianSorted.Length / 2) - 1] + gaussianSorted[gaussianSorted.Length / 2]) / 2.0
                                    : gaussianSorted[gaussianSorted.Length / 2])
                                : 0.0;
                            double maxGaussian = gaussianSorted.Length > 0 ? gaussianSorted[^1] : 0.0;
                            double avgGaussian = curvatures.Average(sc => Math.Abs(sc.Gaussian));
                            double stdDevGaussian = Math.Sqrt(curvatures.Sum(sc => Math.Pow(Math.Abs(sc.Gaussian) - avgGaussian, 2)) / curvatures.Length);

                            (double, double)[] singularities = [.. uvGrid.Where(uv => surface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)),];

                            // Uniformity score penalizes surfaces with high curvature variation relative to typical magnitude.
                            // Formula: 1 - (stdDev / (median × threshold)) measures consistency where high variation reduces score.
                            // Surfaces with uniform curvature (low stdDev) score near 1.0; highly varying curvature scores near 0.0.
                            double score = medianGaussian > context.AbsoluteTolerance
                                ? Math.Max(0.0, 1.0 - (stdDevGaussian / (medianGaussian * AnalysisConfig.HighCurvatureMultiplier)))
                                : maxGaussian < context.AbsoluteTolerance ? 1.0 : 0.0;

                            return ResultFactory.Create(value: (GaussianSamples: curvatures.Select(sc => sc.Gaussian).ToArray(), MeanSamples: curvatures.Select(sc => sc.Mean).ToArray(), Singularities: singularities, UniformityScore: Math.Clamp(score, 0.0, 1.0)));
                        }))()
                        : ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Geometry.SurfaceAnalysisFailed.WithContext("No valid curvature samples"));
                }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double SmoothnessScore, double[] CurvatureSamples, (double Parameter, bool IsSharp)[] InflectionPoints, double EnergyMetric)> CurveFairness(Curve curve, IGeometryContext context) =>
        !curve.IsValid
            ? ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Validation.GeometryInvalid)
            : curve.GetLength() <= context.AbsoluteTolerance
                ? ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed.WithContext("Curve length too small"))
                : Enumerable.Range(0, AnalysisConfig.CurveFairnessSampleCount)
                    .Select(i => curve.Domain.ParameterAt(i / (AnalysisConfig.CurveFairnessSampleCount - 1.0)))
                    .Select(t => (Parameter: t, Curvature: curve.CurvatureAt(t)))
                    .Where(pair => pair.Curvature.IsValid)
                    .ToArray() is (double Parameter, Vector3d Curvature)[] samples && samples.Length > 2
                ? samples.Select(s => s.Curvature.Length).ToArray() is double[] curvatures
                    ? ((Func<Result<(double, double[], (double, bool)[], double)>>)(() => {
                        double avgDiff = Enumerable.Range(1, curvatures.Length - 1).Sum(i => Math.Abs(curvatures[i] - curvatures[i - 1])) / (curvatures.Length - 1);
                        double maxCurvature = curvatures.Max();

                        (double, bool)[] inflections = [.. Enumerable.Range(1, curvatures.Length - 2)
                            .Where(i => {
                                double prevSlope = curvatures[i] - curvatures[i - 1];
                                double nextSlope = curvatures[i + 1] - curvatures[i];
                                return Math.Abs(prevSlope - nextSlope) > AnalysisConfig.InflectionSharpnessThreshold || ((prevSlope * nextSlope) < 0);
                            })
                            .Select(i => (samples[i].Parameter, Math.Abs(curvatures[i] - curvatures[i - 1]) > AnalysisConfig.InflectionSharpnessThreshold)),
                        ];

                        double curveLength = curve.GetLength();
                        double arcLength = curveLength / (AnalysisConfig.CurveFairnessSampleCount - 1);
                        double energy = curvatures.Sum(k => k * k) * arcLength;
                        // Energy normalization: divide by (maxCurvature × length) to produce dimensionless metric
                        // comparing bending energy to characteristic scale, enabling cross-curve comparisons.
                        double normalizedEnergy = maxCurvature > context.AbsoluteTolerance ? energy / (maxCurvature * curveLength) : 0.0;

                        return ResultFactory.Create(value: (SmoothnessScore: Math.Clamp(1.0 / (1.0 + (avgDiff * AnalysisConfig.SmoothnessSensitivity)), 0.0, 1.0), CurvatureSamples: curvatures, InflectionPoints: inflections, EnergyMetric: normalizedEnergy));
                    }))()
                    : ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed)
                : ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed.WithContext("Insufficient valid curvature samples"));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaces, (int Warning, int Critical) Counts)> MeshForFEA(Mesh mesh, IGeometryContext context) =>
        !mesh.IsValid || mesh.Faces.Count == 0
            ? ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(error: E.Validation.GeometryInvalid)
            : ((Func<Result<(double[], double[], double[], int[], (int, int))>>)(() => {
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

                        // FEA Aspect Ratio: ratio of longest to shortest edge (industry standard)
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

                        // FEA Skewness: angular deviation from ideal (90° for quads, 60° for triangles)
                        double skewness = isQuad
                            ? ((double[])[
                                Vector3d.VectorAngle(vertices[1] - vertices[0], vertices[3] - vertices[0]),
                                Vector3d.VectorAngle(vertices[2] - vertices[1], vertices[0] - vertices[1]),
                                Vector3d.VectorAngle(vertices[3] - vertices[2], vertices[1] - vertices[2]),
                                Vector3d.VectorAngle(vertices[0] - vertices[3], vertices[2] - vertices[3]),
                            ]).Max(angle => Math.Abs((angle * (180.0 / Math.PI)) - 90.0)) / 90.0
                            : (vertices[1] - vertices[0], vertices[2] - vertices[0], vertices[2] - vertices[1]) is (Vector3d ab, Vector3d ac, Vector3d bc)
                                ? (
                                    Vector3d.VectorAngle(ab, ac) * (180.0 / Math.PI),
                                    Vector3d.VectorAngle(bc, -ab) * (180.0 / Math.PI),
                                    Vector3d.VectorAngle(-ac, -bc) * (180.0 / Math.PI)
                                ) is (double angleA, double angleB, double angleC)
                                    ? Math.Max(Math.Abs(angleA - 60.0), Math.Max(Math.Abs(angleB - 60.0), Math.Abs(angleC - 60.0))) / 60.0
                                    : 1.0
                                : 1.0;

                        // Jacobian: simplified approximation using cross products (full Jacobian requires isoparametric mapping).
                        // Measures element shape quality via ratio of minimum cross-product to average edge length squared.
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
            }))();
}
