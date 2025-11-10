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
        !surface.IsValid
            ? ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Validation.GeometryInvalid)
            : ((Func<Result<(double[], double[], (double, double)[], double)>>)(() => {
                int gridSize = (int)Math.Sqrt(AnalysisConfig.SurfaceQualitySampleCount);
                gridSize = gridSize < 2 ? 2 : gridSize;
                SurfaceCurvature[] curvatures = [.. Enumerable.Range(0, gridSize)
                    .SelectMany(i => Enumerable.Range(0, gridSize).Select(j => (u: surface.Domain(0).ParameterAt(i / (gridSize - 1.0)), v: surface.Domain(1).ParameterAt(j / (gridSize - 1.0)))))
                    .Select(uv => (UV: uv, Curvature: surface.CurvatureAt(u: uv.u, v: uv.v)))
                    .Where(pair => !double.IsNaN(pair.Curvature.Gaussian) && !double.IsInfinity(pair.Curvature.Gaussian))
                    .Select(pair => pair.Curvature),
                ];
                return curvatures.Length > 0
                    ? ((Func<Result<(double[], double[], (double, double)[], double)>>)(() => {
                        double[] gaussianSorted = [.. curvatures.Select(sc => Math.Abs(sc.Gaussian)).Order()];
                        double medianGaussian = gaussianSorted[gaussianSorted.Length / 2];
                        (double, double)[] singularities = [.. Enumerable.Range(0, gridSize)
                            .SelectMany(i => Enumerable.Range(0, gridSize).Select(j => (u: surface.Domain(0).ParameterAt(i / (gridSize - 1.0)), v: surface.Domain(1).ParameterAt(j / (gridSize - 1.0)))))
                            .Where(uv => surface.IsAtSingularity(u: uv.u, v: uv.v, exact: false)),
                        ];
                        double avgGaussian = curvatures.Average(sc => Math.Abs(sc.Gaussian));
                        double score = avgGaussian < medianGaussian * AnalysisConfig.HighCurvatureMultiplier
                            ? 1.0 - (avgGaussian / (medianGaussian * AnalysisConfig.HighCurvatureMultiplier))
                            : 0.5;
                        return ResultFactory.Create(value: (GaussianSamples: curvatures.Select(sc => sc.Gaussian).ToArray(), MeanSamples: curvatures.Select(sc => sc.Mean).ToArray(), Singularities: singularities, ManufacturingScore: score));
                    }))()
                    : ResultFactory.Create<(double[], double[], (double, double)[], double)>(error: E.Geometry.SurfaceAnalysisFailed);
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double SmoothnessScore, double[] CurvatureSamples, (double Parameter, bool IsSharp)[] InflectionPoints, double EnergyMetric)> CurveFairness(Curve curve) =>
        !curve.IsValid
            ? ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Validation.GeometryInvalid)
            : Enumerable.Range(0, AnalysisConfig.CurveFairnessSampleCount)
                .Select(i => curve.Domain.ParameterAt(i / (AnalysisConfig.CurveFairnessSampleCount - 1.0)))
                .Select(t => (Parameter: t, Curvature: curve.CurvatureAt(t)))
                .Where(pair => pair.Curvature.IsValid)
                .ToArray() is (double Parameter, Vector3d Curvature)[] samples && samples.Length > 2
            ? samples.Select(s => s.Curvature.Length).ToArray() is double[] curvatures
                ? ((Func<Result<(double, double[], (double, bool)[], double)>>)(() => {
                    double avgDiff = Enumerable.Range(1, curvatures.Length - 1).Sum(i => Math.Abs(curvatures[i] - curvatures[i - 1])) / (curvatures.Length - 1);
                    (double, bool)[] inflections = [.. Enumerable.Range(1, curvatures.Length - 2)
                        .Where(i => Math.Abs(curvatures[i - 1] - curvatures[i + 1]) > AnalysisConfig.InflectionSharpnessThreshold)
                        .Select(i => (samples[i].Parameter, true)),
                    ];
                    double energy = curvatures.Sum(k => k * k) * (curve.GetLength() / AnalysisConfig.CurveFairnessSampleCount);
                    return ResultFactory.Create(value: (SmoothnessScore: 1.0 / (1.0 + (avgDiff * 10.0)), CurvatureSamples: curvatures, InflectionPoints: inflections, EnergyMetric: energy));
                }))()
                : ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed)
            : ResultFactory.Create<(double, double[], (double, bool)[], double)>(error: E.Geometry.CurveAnalysisFailed);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double[] AspectRatios, double[] Skewness, double[] Jacobians, int[] ProblematicFaces, (int Warning, int Critical) Counts)> MeshForFEA(Mesh mesh, IGeometryContext context) =>
        !mesh.IsValid || mesh.Faces.Count == 0
            ? ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(error: E.Validation.GeometryInvalid)
            : Enumerable.Range(0, mesh.Faces.Count).Select(i => {
                Point3f center = (Point3f)mesh.Faces.GetFaceCenter(i);
                MeshFace face = mesh.Faces[i];
                bool isQuad = face.IsQuad;
                bool validIndices = face.A >= 0 && face.A < mesh.Vertices.Count
                    && face.B >= 0 && face.B < mesh.Vertices.Count
                    && face.C >= 0 && face.C < mesh.Vertices.Count
                    && (!isQuad || (face.D >= 0 && face.D < mesh.Vertices.Count));
                Point3d[] verts = validIndices
                    ? [(Point3d)mesh.Vertices[face.A], (Point3d)mesh.Vertices[face.B], (Point3d)mesh.Vertices[face.C], isQuad ? (Point3d)mesh.Vertices[face.D] : (Point3d)mesh.Vertices[face.A],]
                    : [(Point3d)center, (Point3d)center, (Point3d)center, (Point3d)center,];
                int vertCount = isQuad ? 4 : 3;
                (double min, double max) = (double.MaxValue, double.MinValue);
                for (int j = 0; j < vertCount; j++) {
                    double d = verts[j].DistanceTo((Point3d)center);
                    min = Math.Min(min, d);
                    max = Math.Max(max, d);
                }
                double aspectRatio = max / (min + context.AbsoluteTolerance);
                double skewness = isQuad
                    ? Math.Abs((((verts[1] - verts[0]).Length + (verts[3] - verts[2]).Length) / ((verts[2] - verts[1]).Length + (verts[0] - verts[3]).Length + context.AbsoluteTolerance)) - 1.0)
                    : (verts[1] - verts[0], verts[2] - verts[0], verts[2] - verts[1]) is (Vector3d ab, Vector3d ac, Vector3d bc)
                        ? (
                            Vector3d.VectorAngle(ab, ac) * (180.0 / Math.PI),
                            Vector3d.VectorAngle(bc, -ab) * (180.0 / Math.PI),
                            Vector3d.VectorAngle(-ac, -bc) * (180.0 / Math.PI)
                        ) is (double angleA, double angleB, double angleC)
                            ? Math.Max(Math.Abs(angleA - 60.0), Math.Max(Math.Abs(angleB - 60.0), Math.Abs(angleC - 60.0))) / 60.0
                            : 1.0
                        : 1.0;
                double jacobian = isQuad
                    ? verts.Take(4).Zip(verts.Skip(1).Take(3).Append(verts[0]), (a, b) => b - a).ToArray() is Vector3d[] edges && edges.Average(e => e.Length) is double avgLen
                        ? edges.Zip(edges.Skip(1).Append(edges[0]), (e1, e2) => Vector3d.CrossProduct(e1, e2).Length).Min() / ((avgLen * avgLen) + context.AbsoluteTolerance)
                        : 1.0
                    : 1.0;
                return (AspectRatio: aspectRatio, Skewness: skewness, Jacobian: jacobian);
            }).ToArray() is (double AspectRatio, double Skewness, double Jacobian)[] metrics && metrics.Length > 0
            ? ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(value: (
                [.. metrics.Select(m => m.AspectRatio),],
                [.. metrics.Select(m => m.Skewness),],
                [.. metrics.Select(m => m.Jacobian),],
                [.. metrics.Select((m, i) => (m, i)).Where(pair => pair.m.AspectRatio > AnalysisConfig.AspectRatioCritical || pair.m.Skewness > AnalysisConfig.SkewnessCritical || pair.m.Jacobian < AnalysisConfig.JacobianCritical).Select(pair => pair.i),],
                (metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioWarning || m.Skewness > AnalysisConfig.SkewnessWarning || m.Jacobian < AnalysisConfig.JacobianWarning), metrics.Count(m => m.AspectRatio > AnalysisConfig.AspectRatioCritical || m.Skewness > AnalysisConfig.SkewnessCritical || m.Jacobian < AnalysisConfig.JacobianCritical))))
            : ResultFactory.Create<(double[], double[], double[], int[], (int, int))>(error: E.Geometry.MeshAnalysisFailed);
}
