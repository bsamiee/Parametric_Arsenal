using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Dense intersection analysis algorithms with zero duplication.</summary>
internal static class IntersectionCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(Intersect.IntersectionOutput output, GeometryBase geomA, GeometryBase geomB) =>
        geomA is null || geomB is null
            ? ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData.WithContext("Geometry is null"))
            : (output.Points.Count, output.ParametersA.Count, output.ParametersB.Count) switch {
                (0, _, _) => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                (int n, int pa, int pb) when pa >= n && pb >= n => (geomA, geomB) switch {
                (Curve ca, Curve cb) => Enumerable.Range(0, n).Where(i => i < output.ParametersA.Count && i < output.ParametersB.Count).Select(i => (ca.TangentAt(output.ParametersA[i]), cb.TangentAt(output.ParametersB[i])) is (Vector3d ta, Vector3d tb) ? Vector3d.VectorAngle(ta, tb) : double.NaN).Where(a => !double.IsNaN(a)).ToArray() is double[] angles && angles.Length > 0 && angles.Average() is double avgAngle
                    ? ResultFactory.Create(value: (Type: avgAngle < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, ApproachAngles: angles, IsGrazing: angles.Any(a => a < IntersectionConfig.GrazingAngleThreshold), BlendScore: avgAngle < IntersectionConfig.TangentAngleThreshold ? 1.0 : 0.5))
                    : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                (Curve c, Surface s) => Enumerable.Range(0, n).Where(i => i < output.ParametersA.Count && i < output.ParametersB.Count && output.ParametersB.Count >= (2 * i) + 1).Select(i => s.NormalAt(output.ParametersA[i * 2], output.ParametersA[(i * 2) + 1]) is Vector3d sn && c.TangentAt(output.ParametersB[i]) is Vector3d ct ? Math.Abs(Vector3d.VectorAngle(sn, ct) - (Math.PI / 2)) : double.NaN).Where(a => !double.IsNaN(a)).ToArray() is double[] angles2 && angles2.Length > 0 && angles2.Average() is double avgAngle2
                    ? ResultFactory.Create(value: (Type: avgAngle2 < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, ApproachAngles: angles2, IsGrazing: angles2.Any(a => a < IntersectionConfig.GrazingAngleThreshold), BlendScore: avgAngle2 < IntersectionConfig.TangentAngleThreshold ? 0.8 : 0.4))
                    : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                (Surface s, Curve c) => Enumerable.Range(0, n).Where(i => i < output.ParametersA.Count && i < output.ParametersB.Count && output.ParametersA.Count >= (2 * i) + 1).Select(i => s.NormalAt(output.ParametersB[i * 2], output.ParametersB[(i * 2) + 1]) is Vector3d sn && c.TangentAt(output.ParametersA[i]) is Vector3d ct ? Math.Abs(Vector3d.VectorAngle(sn, ct) - (Math.PI / 2)) : double.NaN).Where(a => !double.IsNaN(a)).ToArray() is double[] angles3 && angles3.Length > 0 && angles3.Average() is double avgAngle3
                    ? ResultFactory.Create(value: (Type: avgAngle3 < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, ApproachAngles: angles3, IsGrazing: angles3.Any(a => a < IntersectionConfig.GrazingAngleThreshold), BlendScore: avgAngle3 < IntersectionConfig.TangentAngleThreshold ? 0.8 : 0.4))
                    : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                _ => ResultFactory.Create(value: ((byte)2, Array.Empty<double>(), false, 0.0)),
            },
            _ => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[], Point3d[], double[])> FindNearMisses(GeometryBase geomA, GeometryBase geomB, double searchRadius, IGeometryContext context) =>
        searchRadius <= context.AbsoluteTolerance
            ? ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.InvalidSearchRadius.WithContext("SearchRadius must exceed tolerance"))
            : (geomA, geomB) switch {
            (Curve ca, Curve cb) => Math.Max(3, (int)Math.Ceiling(ca.GetLength() / searchRadius)) is int sampleCount
                ? Enumerable.Range(0, sampleCount).Select(i => ca.Domain.ParameterAt(i / (double)(sampleCount - 1))).Select(t => ca.PointAt(t)).Select(pt => (PointA: pt, Result: cb.ClosestPoint(pt, out double tb) ? (PointB: cb.PointAt(tb), Distance: pt.DistanceTo(cb.PointAt(tb))) : (PointB: Point3d.Unset, Distance: double.MaxValue))).Where(pair => pair.Result.Distance < searchRadius && pair.Result.Distance > context.AbsoluteTolerance)
                    .Concat(Enumerable.Range(0, sampleCount).Select(i => cb.Domain.ParameterAt(i / (double)(sampleCount - 1))).Select(t => cb.PointAt(t)).Select(pt => (PointA: pt, Result: ca.ClosestPoint(pt, out double ta) ? (PointB: ca.PointAt(ta), Distance: pt.DistanceTo(ca.PointAt(ta))) : (PointB: Point3d.Unset, Distance: double.MaxValue))).Where(pair => pair.Result.Distance < searchRadius && pair.Result.Distance > context.AbsoluteTolerance))
                    .ToArray() is (Point3d PointA, (Point3d PointB, double Distance) Result)[] pairs && pairs.Length > 0
                    ? ResultFactory.Create(value: (pairs.Select(p => p.PointA).ToArray(), pairs.Select(p => p.Result.PointB).ToArray(), pairs.Select(p => p.Result.Distance).ToArray()))
                    : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
            (Curve c, Surface s) => Math.Max(3, (int)Math.Ceiling(c.GetLength() / searchRadius)) is int sampleCount2
                ? Enumerable.Range(0, sampleCount2).Select(i => c.Domain.ParameterAt(i / (double)(sampleCount2 - 1))).Select(t => c.PointAt(t)).Select(pt => (PointA: pt, Result: s.ClosestPoint(pt, out double su, out double sv) && (su, sv) is (double u, double v) ? (PointB: s.PointAt(u, v), Distance: pt.DistanceTo(s.PointAt(u, v))) : (PointB: Point3d.Unset, Distance: double.MaxValue))).Where(pair => pair.Result.Distance < searchRadius && pair.Result.Distance > context.AbsoluteTolerance).ToArray() is (Point3d PointA, (Point3d PointB, double Distance) Result)[] pairs2 && pairs2.Length > 0
                    ? ResultFactory.Create(value: (pairs2.Select(p => p.PointA).ToArray(), pairs2.Select(p => p.Result.PointB).ToArray(), pairs2.Select(p => p.Result.Distance).ToArray()))
                    : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
            (Surface s, Curve c) => Math.Max(3, (int)Math.Ceiling(c.GetLength() / searchRadius)) is int sampleCount3
                ? Enumerable.Range(0, sampleCount3).Select(i => c.Domain.ParameterAt(i / (double)(sampleCount3 - 1))).Select(t => c.PointAt(t)).Select(pt => (PointA: pt, Result: s.ClosestPoint(pt, out double su, out double sv) && (su, sv) is (double u, double v) ? (PointB: s.PointAt(u, v), Distance: pt.DistanceTo(s.PointAt(u, v))) : (PointB: Point3d.Unset, Distance: double.MaxValue))).Where(pair => pair.Result.Distance < searchRadius && pair.Result.Distance > context.AbsoluteTolerance).ToArray() is (Point3d PointA, (Point3d PointB, double Distance) Result)[] pairs3 && pairs3.Length > 0
                    ? ResultFactory.Create(value: (pairs3.Select(p => p.PointA).ToArray(), pairs3.Select(p => p.Result.PointB).ToArray(), pairs3.Select(p => p.Result.Distance).ToArray()))
                    : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
            _ => ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.NearMissSearchFailed),
            };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double Score, double Sensitivity, bool[] UnstableFlags)> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersect.IntersectionOutput baseOutput, IGeometryContext context) =>
        baseOutput.Points.Count switch {
            0 => ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, [])),
            int n => (geomA.GetBoundingBox(accurate: false).Diagonal.Length * IntersectionConfig.StabilityPerturbationFactor, Generate3DPerturbationDirections(count: IntersectionConfig.StabilitySampleCount)) switch {
                (double perturbDist, Vector3d[] directions) => directions.SelectMany(dir => {
                        List<(double Delta, IDisposable? Resource)> results = [];
                    (double Delta, IDisposable? Resource) result = geomA switch {
                        Curve ca when ca.DuplicateCurve() is Curve caCopy => caCopy.Translate(dir * perturbDist) && IntersectionCore.ExecutePair(caCopy, geomB, context, new Intersect.IntersectionOptions()) is Result<Intersect.IntersectionOutput> perturbResult && perturbResult.IsSuccess
                            ? (Math.Abs(perturbResult.Value.Points.Count - n), (IDisposable?)caCopy)
                            : (0.0, (IDisposable?)caCopy),
                        Surface sa when sa.Duplicate() is Surface saCopy => saCopy.Translate(dir * perturbDist) && IntersectionCore.ExecutePair(saCopy, geomB, context, new Intersect.IntersectionOptions()) is Result<Intersect.IntersectionOutput> perturbResult && perturbResult.IsSuccess
                            ? (Math.Abs(perturbResult.Value.Points.Count - n), (IDisposable?)saCopy)
                            : (0.0, (IDisposable?)saCopy),
                        _ => (0.0, null),
                    };
                    results.Add(result);
                    return results;
                }).ToArray() is (double Delta, IDisposable? Resource)[] perturbResults && perturbResults.Length > 0 && perturbResults.Select(p => { p.Resource?.Dispose(); return p.Delta; }).ToArray() is double[] deltas
                    ? ResultFactory.Create(value: (Score: 1.0 / (1.0 + deltas.Average()), Sensitivity: deltas.Max() / n, UnstableFlags: Enumerable.Range(0, n).Select(_ => deltas.Any(d => d > 1.0)).ToArray()))
                    : ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, [])),
            },
        };

    private static Vector3d[] Generate3DPerturbationDirections(int count) {
        int phiSteps = (int)Math.Ceiling(Math.Sqrt(count));
        int thetaSteps = (int)Math.Ceiling(count / (double)phiSteps);
        return [.. Enumerable.Range(0, phiSteps).SelectMany(i => Enumerable.Range(0, thetaSteps).Select(j => {
            double phi = (Math.PI * i) / phiSteps;
            double theta = ((2 * Math.PI) * j) / thetaSteps;
            return new Vector3d(Math.Sin(phi) * Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi));
        })).Take(count),];
    }
}
