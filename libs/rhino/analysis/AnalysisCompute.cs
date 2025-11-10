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
    internal static Result<(double[] GaussianSamples, double[] MeanSamples, (double U, double V)[] Singularities, double ManufacturingScore)> SurfaceQuality(Surface surface) =>
        Enumerable.Range(0, AnalysisConfig.SurfaceQualitySampleCount)
            .Select(i => (u: surface.Domain(0).ParameterAt(i % 10 / 10.0), v: surface.Domain(1).ParameterAt(i / 10 / 10.0)))
            .Select(uv => surface.CurvatureAt(u: uv.u, v: uv.v))
            .Where(sc => !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian))
            .ToArray() is SurfaceCurvature[] curvatures && curvatures.Length > 0
            ? ((Func<Result<(double[], double[], (double, double)[], double)>>)(() => {
                double[] gaussianSorted = [.. curvatures.Select(sc => Math.Abs(sc.Gaussian)).Order()];
                double medianGaussian = gaussianSorted[gaussianSorted.Length / 2];
                (double, double)[] singularities = [.. Enumerable.Range(0, 20)
                    .SelectMany(i => Enumerable.Range(0, 20).Select(j => (u: surface.Domain(0).ParameterAt(i / 20.0), v: surface.Domain(1).ParameterAt(j / 20.0))))
                    .Where(uv => surface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)),
                ];
                double avgGaussian = curvatures.Average(sc => Math.Abs(sc.Gaussian));
                double score = avgGaussian < medianGaussian * AnalysisConfig.HighCurvatureMultiplier
                    ? 1.0 - (avgGaussian / (medianGaussian * AnalysisConfig.HighCurvatureMultiplier))
                    : 0.5;
                int len = curvatures.Length;
                double[] gaussian = new double[len];
                double[] mean = new double[len];
                for (int i = 0; i < len; i++) {
                    gaussian[i] = curvatures[i].Gaussian;
                    mean[i] = curvatures[i].Mean;
                }
                return ResultFactory.Create(value: (GaussianSamples: gaussian, MeanSamples: mean, Singularities: singularities, ManufacturingScore: score));
            }))()
            : ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Geometry.SurfaceAnalysisFailed);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double SmoothnessScore, double[] CurvatureSamples, (double Parameter, bool IsSharp)[] InflectionPoints, double EnergyMetric)> CurveFairness(Curve curve) =>
        Enumerable.Range(0, AnalysisConfig.CurveFairnessSampleCount)
            .Select(i => curve.Domain.ParameterAt(i / (AnalysisConfig.CurveFairnessSampleCount - 1.0)))
            .Select(t => (Parameter: t, Curvature: curve.CurvatureAt(t)))
            .Where(pair => pair.Curvature.IsValid)
            .ToArray() is (double Parameter, Vector3d Curvature)[] samples && samples.Length > 2
            ? ((Func<Result<(double, double[], (double, bool)[], double)>>)(() => {
                int len = samples.Length;
                double[] curvatures = new double[len];
                double diffSum = 0.0;
                for (int i = 0; i < len; i++) {
                    curvatures[i] = samples[i].Curvature.Length;
                    if (i > 0) {
                        diffSum += Math.Abs(curvatures[i] - curvatures[i - 1]);
                    }
                }
                double avgDiff = diffSum / (len - 1);
                (double, bool)[] inflections = [.. Enumerable.Range(1, len - 2)
                    .Where(i => curvatures[i - 1] * curvatures[i + 1] < 0
                        || Math.Abs(curvatures[i - 1] - curvatures[i + 1]) > AnalysisConfig.InflectionSharpnessThreshold)
                    .Select(i => (samples[i].Parameter, IsSharp: Math.Abs(curvatures[i - 1] - curvatures[i + 1]) > AnalysisConfig.InflectionSharpnessThreshold)),
                ];
                double energy = curvatures.Sum(k => k * k) * (curve.GetLength() / AnalysisConfig.CurveFairnessSampleCount);
                return ResultFactory.Create(value: (SmoothnessScore: 1.0 / (1.0 + (avgDiff * 10.0)), CurvatureSamples: curvatures, InflectionPoints: inflections, EnergyMetric: energy));
            }))()
            : ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaces, (int Warning, int Critical) Counts)> MeshForFEA(Mesh mesh, IGeometryContext context) =>
        Enumerable.Range(0, mesh.Faces.Count).Select(i => {
            Point3f center = (Point3f)mesh.Faces.GetFaceCenter(i);
            MeshFace face = mesh.Faces[i];
            Point3d[] verts = [(Point3d)mesh.Vertices[face.A], (Point3d)mesh.Vertices[face.B], (Point3d)mesh.Vertices[face.C], face.IsQuad ? (Point3d)mesh.Vertices[face.D] : (Point3d)mesh.Vertices[face.A],];
            int vertCount = face.IsQuad ? 4 : 3;
            (double min, double max) = (double.MaxValue, double.MinValue);
            for (int j = 0; j < vertCount; j++) {
                double d = verts[j].DistanceTo((Point3d)center);
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }
            double aspectRatio = max / (min + context.AbsoluteTolerance);
            double skewness = face.IsQuad ? Math.Abs((((verts[1] - verts[0]).Length + (verts[3] - verts[2]).Length) / ((verts[2] - verts[1]).Length + (verts[0] - verts[3]).Length + context.AbsoluteTolerance)) - 1.0) : Math.Abs((Vector3d.CrossProduct(verts[1] - verts[0], verts[2] - verts[0]).Length / (((verts[1] - verts[0]).Length * (verts[2] - verts[0]).Length) + context.AbsoluteTolerance)) - 0.5) * 2.0;
            double jacobian = face.IsQuad ? verts.Take(4).Zip(verts.Skip(1).Take(3).Append(verts[0]), (a, b) => b - a).ToArray() is Vector3d[] edges ? edges.Zip(edges.Skip(1).Append(edges[0]), (e1, e2) => Vector3d.CrossProduct(e1, e2).Length).Min() / ((edges.Average(e => e.Length) * edges.Average(e => e.Length)) + context.AbsoluteTolerance) : 1.0 : 1.0;
            return (AspectRatio: aspectRatio, Skewness: skewness, Jacobian: jacobian);
        }).ToArray() is (double AspectRatio, double Skewness, double Jacobian)[] metrics && metrics.Length > 0
            ? ((Func<Result<(double[], double[], double[], int[], (int, int))>>)(() => {
                int len = metrics.Length;
                double[] aspectRatios = new double[len];
                double[] skewness = new double[len];
                double[] jacobians = new double[len];
                List<int> problematic = [];
                (int warning, int critical) = (0, 0);
                for (int i = 0; i < len; i++) {
                    (double ar, double sk, double jac) = metrics[i];
                    aspectRatios[i] = ar;
                    skewness[i] = sk;
                    jacobians[i] = jac;
                    bool isCritical = ar > AnalysisConfig.AspectRatioCritical || sk > AnalysisConfig.SkewnessCritical || jac < AnalysisConfig.JacobianCritical;
                    bool isWarning = ar > AnalysisConfig.AspectRatioWarning || sk > AnalysisConfig.SkewnessWarning || jac < AnalysisConfig.JacobianWarning;
                    if (isCritical) {
                        problematic.Add(i);
                        critical++;
                    }
                    if (isWarning) {
                        warning++;
                    }
                }
                return ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(value: (aspectRatios, skewness, jacobians, [.. problematic], (warning, critical)));
            }))()
            : ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(error: E.Geometry.MeshAnalysisFailed);
}
