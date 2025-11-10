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
                double[] curvatures = [.. samples.Select(s => s.Curvature.Length),];
                double[] diffs = [.. curvatures.Zip(curvatures.Skip(1), (a, b) => Math.Abs(b - a)),];
                double avgDiff = diffs.Average();
                (double, bool)[] inflections = [.. Enumerable.Range(1, samples.Length - 2)
                    .Where(i => samples[i - 1].Curvature.Length * samples[i + 1].Curvature.Length < 0
                        || Math.Abs(samples[i - 1].Curvature.Length - samples[i + 1].Curvature.Length) > AnalysisConfig.InflectionSharpnessThreshold)
                    .Select(i => (samples[i].Parameter, IsSharp: Math.Abs(samples[i - 1].Curvature.Length - samples[i + 1].Curvature.Length) > AnalysisConfig.InflectionSharpnessThreshold)),
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
            double[] dists = [.. verts.Take(face.IsQuad ? 4 : 3).Select(v => v.DistanceTo((Point3d)center)),];
            double aspectRatio = dists.Max() / (dists.Min() + context.AbsoluteTolerance);
            double skewness = face.IsQuad ? Math.Abs((((verts[1] - verts[0]).Length + (verts[3] - verts[2]).Length) / ((verts[2] - verts[1]).Length + (verts[0] - verts[3]).Length + context.AbsoluteTolerance)) - 1.0) : Math.Abs((Vector3d.CrossProduct(verts[1] - verts[0], verts[2] - verts[0]).Length / (((verts[1] - verts[0]).Length * (verts[2] - verts[0]).Length) + context.AbsoluteTolerance)) - 0.5) * 2.0;
            double jacobian = face.IsQuad ? verts.Take(4).Zip(verts.Skip(1).Take(3).Append(verts[0]), (a, b) => b - a).ToArray() is Vector3d[] edges ? edges.Zip(edges.Skip(1).Append(edges[0]), (e1, e2) => Vector3d.CrossProduct(e1, e2).Length).Min() / ((edges.Average(e => e.Length) * edges.Average(e => e.Length)) + context.AbsoluteTolerance) : 1.0 : 1.0;
            return (AspectRatio: aspectRatio, Skewness: skewness, Jacobian: jacobian);
        }).ToArray() is (double AspectRatio, double Skewness, double Jacobian)[] metrics && metrics.Length > 0
            ? ResultFactory.Create(value: (AspectRatios: metrics.Select(m => m.AspectRatio).ToArray(), Skewness: metrics.Select(m => m.Skewness).ToArray(), Jacobians: metrics.Select(m => m.Jacobian).ToArray(), ProblematicFaces: metrics.Select((m, i) => (m, i)).Where(pair => pair.m.AspectRatio > AnalysisConfig.AspectRatioCritical || pair.m.Skewness > AnalysisConfig.SkewnessCritical || pair.m.Jacobian < AnalysisConfig.JacobianCritical).Select(pair => pair.i).ToArray(), Counts: (Warning: metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioWarning || m.Skewness > AnalysisConfig.SkewnessWarning || m.Jacobian < AnalysisConfig.JacobianWarning), Critical: metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioCritical || m.Skewness > AnalysisConfig.SkewnessCritical || m.Jacobian < AnalysisConfig.JacobianCritical))))
            : ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(error: E.Geometry.MeshAnalysisFailed);
}
