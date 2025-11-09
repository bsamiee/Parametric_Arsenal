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
        Enumerable.Range(0, AnalysisConfig.SurfaceQualitySampleCount).Select(i => (surface.Domain(0).ParameterAt(i % 10 / 10.0), surface.Domain(1).ParameterAt(i / 10 / 10.0))).Select(uv => surface.CurvatureAt(uv.Item1, uv.Item2)).Where(sc => !double.IsNaN(sc.Gaussian) && !double.IsInfinity(sc.Gaussian)).ToArray() is SurfaceCurvature[] curvatures && curvatures.Length > 0
            ? (curvatures.Select(sc => Math.Abs(sc.Gaussian)).OrderBy(g => g).ToArray() is double[] gaussianSorted && gaussianSorted[gaussianSorted.Length / 2] is double medianGaussian,
               Enumerable.Range(0, 20).SelectMany(i => Enumerable.Range(0, 20).Select(j => (surface.Domain(0).ParameterAt(i / 20.0), surface.Domain(1).ParameterAt(j / 20.0)))).Where(uv => surface.IsAtSingularity(uv.Item1, uv.Item2, exact: false)).ToArray() is (double, double)[] singularities,
               curvatures.Select(sc => Math.Abs(sc.Gaussian)).Average() is double avgGaussian && avgGaussian < medianGaussian * AnalysisConfig.HighCurvatureMultiplier ? 1.0 - (avgGaussian / (medianGaussian * AnalysisConfig.HighCurvatureMultiplier)) : 0.5) switch {
                (double median, (double, double)[] sings, double score) => ResultFactory.Create(value: (curvatures.Select(sc => sc.Gaussian).ToArray(), curvatures.Select(sc => sc.Mean).ToArray(), sings, score)),
            }
            : ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Geometry.SurfaceAnalysisFailed);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double SmoothnessScore, double[] CurvatureSamples, (double Parameter, bool IsSharp)[] InflectionPoints, double EnergyMetric)> CurveFairness(Curve curve) =>
        Enumerable.Range(0, AnalysisConfig.CurveFairnessSampleCount).Select(i => curve.Domain.ParameterAt(i / (AnalysisConfig.CurveFairnessSampleCount - 1.0))).Select(t => (t, curve.CurvatureAt(t))).Where(pair => pair.Item2.IsValid).ToArray() is (double, Vector3d)[] samples && samples.Length > 2
            ? (samples.Select(s => s.Item2.Length).ToArray() is double[] curvatures && curvatures.Zip(curvatures.Skip(1), (a, b) => Math.Abs(b - a)).ToArray() is double[] diffs && diffs.Average() is double avgDiff,
               Enumerable.Range(1, samples.Length - 2).Where(i => samples[i - 1].Item2.Length * samples[i + 1].Item2.Length < 0 || Math.Abs(samples[i - 1].Item2.Length - samples[i + 1].Item2.Length) > AnalysisConfig.InflectionSharpnessThreshold).Select(i => (samples[i].Item1, Math.Abs(samples[i - 1].Item2.Length - samples[i + 1].Item2.Length) > AnalysisConfig.InflectionSharpnessThreshold)).ToArray() is (double, bool)[] inflections,
               curvatures.Select(k => k * k).Sum() * (curve.GetLength() / AnalysisConfig.CurveFairnessSampleCount) is double energy) switch {
                (double avgVariation, (double, bool)[] infl, double en) => ResultFactory.Create(value: (1.0 / (1.0 + avgVariation * 10.0), curvatures, infl, en)),
            }
            : ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaces, (int Warning, int Critical) Counts)> MeshForFEA(Mesh mesh, IGeometryContext context) =>
        Enumerable.Range(0, mesh.Faces.Count).Select(i => {
            Point3f center = mesh.Faces.GetFaceCenter(i);
            MeshFace face = mesh.Faces[i];
            Point3d[] verts = new[] { (Point3d)mesh.Vertices[face.A], (Point3d)mesh.Vertices[face.B], (Point3d)mesh.Vertices[face.C], face.IsQuad ? (Point3d)mesh.Vertices[face.D] : (Point3d)mesh.Vertices[face.A] };
            double[] dists = verts.Take(face.IsQuad ? 4 : 3).Select(v => v.DistanceTo((Point3d)center)).ToArray();
            double aspectRatio = dists.Max() / (dists.Min() + context.AbsoluteTolerance);
            double skewness = face.IsQuad ? Math.Abs(((verts[1] - verts[0]).Length + (verts[3] - verts[2]).Length) / ((verts[2] - verts[1]).Length + (verts[0] - verts[3]).Length + context.AbsoluteTolerance) - 1.0) : Math.Abs(Vector3d.CrossProduct(verts[1] - verts[0], verts[2] - verts[0]).Length / ((verts[1] - verts[0]).Length * (verts[2] - verts[0]).Length + context.AbsoluteTolerance) - 0.5) * 2.0;
            double jacobian = face.IsQuad ? verts.Take(4).Zip(verts.Skip(1).Take(3).Append(verts[0]), (a, b) => b - a).ToArray() is Vector3d[] edges ? edges.Zip(edges.Skip(1).Append(edges[0]), (e1, e2) => Vector3d.CrossProduct(e1, e2).Length).Min() / (edges.Select(e => e.Length).Average() * edges.Select(e => e.Length).Average() + context.AbsoluteTolerance) : 1.0 : 1.0;
            return (aspectRatio, skewness, jacobian);
        }).ToArray() is (double, double, double)[] metrics && metrics.Length > 0
            ? ResultFactory.Create(value: (metrics.Select(m => m.Item1).ToArray(), metrics.Select(m => m.Item2).ToArray(), metrics.Select(m => m.Item3).ToArray(), metrics.Select((m, i) => (m, i)).Where(pair => pair.m.Item1 > AnalysisConfig.AspectRatioCritical || pair.m.Item2 > AnalysisConfig.SkewnessCritical || pair.m.Item3 < AnalysisConfig.JacobianCritical).Select(pair => pair.i).ToArray(), (metrics.Count(m => m.Item1 > AnalysisConfig.AspectRatioWarning || m.Item2 > AnalysisConfig.SkewnessWarning || m.Item3 < AnalysisConfig.JacobianWarning), metrics.Count(m => m.Item1 > AnalysisConfig.AspectRatioCritical || m.Item2 > AnalysisConfig.SkewnessCritical || m.Item3 < AnalysisConfig.JacobianCritical))))
            : ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(error: E.Geometry.MeshAnalysisFailed);
}
