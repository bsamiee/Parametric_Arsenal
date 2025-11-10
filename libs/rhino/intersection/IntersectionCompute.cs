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
                    (Curve ca, Curve cb) => Enumerable.Range(0, n)
                        .Select(i => (ca.TangentAt(output.ParametersA[i]), cb.TangentAt(output.ParametersB[i])) is (Vector3d ta, Vector3d tb) && ta.IsValid && tb.IsValid
                            ? Vector3d.VectorAngle(ta, tb)
                            : double.NaN)
                        .Where(a => !double.IsNaN(a))
                        .ToArray() is double[] angles && angles.Length > 0 && Math.Atan2(angles.Sum(Math.Sin) / angles.Length, angles.Sum(Math.Cos) / angles.Length) is double circMean && (circMean < 0.0 ? circMean + (2.0 * Math.PI) : circMean) is double avgAngle
                        ? ResultFactory.Create(value: (Type: avgAngle < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, ApproachAngles: angles, IsGrazing: angles.Any(a => a < IntersectionConfig.GrazingAngleThreshold), BlendScore: avgAngle < IntersectionConfig.TangentAngleThreshold ? IntersectionConfig.TangentBlendScore : IntersectionConfig.PerpendicularBlendScore))
                        : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                    (Curve, Surface) or (Surface, Curve) => ResultFactory.Create(value: ((byte)2, Array.Empty<double>(), false, 0.0)),
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
                    ? Enumerable.Range(0, sampleCount)
                        .Select(i => ca.PointAt(ca.Domain.ParameterAt(i / (double)(sampleCount - 1))))
                        .Select(pt => cb.ClosestPoint(pt, out double tb)
                            ? (PointA: pt, PointB: cb.PointAt(tb), Distance: pt.DistanceTo(cb.PointAt(tb)))
                            : (PointA: pt, PointB: Point3d.Unset, Distance: double.MaxValue))
                        .Where(pair => pair.Distance < searchRadius && pair.Distance > context.AbsoluteTolerance)
                        .Concat(Enumerable.Range(0, sampleCount)
                            .Select(i => cb.PointAt(cb.Domain.ParameterAt(i / (double)(sampleCount - 1))))
                            .Select(pt => ca.ClosestPoint(pt, out double ta)
                                ? (PointA: ca.PointAt(ta), PointB: pt, Distance: ca.PointAt(ta).DistanceTo(pt))
                                : (PointA: Point3d.Unset, PointB: pt, Distance: double.MaxValue))
                            .Where(pair => pair.Distance < searchRadius && pair.Distance > context.AbsoluteTolerance))
                        .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] pairs && pairs.Length > 0
                        ? ResultFactory.Create(value: (pairs.Select(p => p.PointA).ToArray(), pairs.Select(p => p.PointB).ToArray(), pairs.Select(p => p.Distance).ToArray()))
                        : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                    : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                (Curve c, Surface s) => Math.Max(3, (int)Math.Ceiling(c.GetLength() / searchRadius)) is int sampleCount2
                    ? Enumerable.Range(0, sampleCount2)
                        .Select(i => c.PointAt(c.Domain.ParameterAt(i / (double)(sampleCount2 - 1))))
                        .Select(pt => s.ClosestPoint(pt, out double su, out double sv)
                            ? (PointA: pt, PointB: s.PointAt(su, sv), Distance: pt.DistanceTo(s.PointAt(su, sv)))
                            : (PointA: pt, PointB: Point3d.Unset, Distance: double.MaxValue))
                        .Where(pair => pair.Distance < searchRadius && pair.Distance > context.AbsoluteTolerance)
                        .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] pairs2 && pairs2.Length > 0
                        ? ResultFactory.Create(value: (pairs2.Select(p => p.PointA).ToArray(), pairs2.Select(p => p.PointB).ToArray(), pairs2.Select(p => p.Distance).ToArray()))
                        : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                    : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                (Surface s, Curve c) => Math.Max(3, (int)Math.Ceiling(c.GetLength() / searchRadius)) is int sampleCount3
                    ? Enumerable.Range(0, sampleCount3)
                        .Select(i => c.PointAt(c.Domain.ParameterAt(i / (double)(sampleCount3 - 1))))
                        .Select(pt => s.ClosestPoint(pt, out double su, out double sv)
                            ? (PointA: pt, PointB: s.PointAt(su, sv), Distance: pt.DistanceTo(s.PointAt(su, sv)))
                            : (PointA: pt, PointB: Point3d.Unset, Distance: double.MaxValue))
                        .Where(pair => pair.Distance < searchRadius && pair.Distance > context.AbsoluteTolerance)
                        .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] pairs3 && pairs3.Length > 0
                        ? ResultFactory.Create(value: (pairs3.Select(p => p.PointA).ToArray(), pairs3.Select(p => p.PointB).ToArray(), pairs3.Select(p => p.Distance).ToArray()))
                        : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                    : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                _ => ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.NearMissSearchFailed),
            };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double Score, double Sensitivity, bool[] UnstableFlags)> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersect.IntersectionOutput baseOutput, IGeometryContext context) =>
        baseOutput.Points.Count switch {
            0 => ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, [])),
            int n => ((int phiSteps, int thetaSteps) = ((int)Math.Ceiling(Math.Sqrt(IntersectionConfig.StabilitySampleCount)), (int)Math.Ceiling(IntersectionConfig.StabilitySampleCount / Math.Ceiling(Math.Sqrt(IntersectionConfig.StabilitySampleCount)))),
                Enumerable.Range(0, phiSteps)
                    .SelectMany(i => Enumerable.Range(0, thetaSteps)
                        .Select(j => {
                            double phi = (Math.PI * i) / phiSteps;
                            double theta = ((2.0 * Math.PI) * j) / thetaSteps;
                            return new Vector3d(Math.Sin(phi) * Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi));
                        }))
                    .Take(IntersectionConfig.StabilitySampleCount)
                    .ToArray()) is (_, _, Vector3d[] directions) && geomA.GetBoundingBox(accurate: false).Diagonal.Length * IntersectionConfig.StabilityPerturbationFactor is double perturbDist
                ? geomA switch {
                    Curve c => directions
                        .Select(dir => c.DuplicateCurve() is Curve copy && copy.Translate(dir * perturbDist) && IntersectionCore.ExecutePair(copy, geomB, context, new()) is Result<Intersect.IntersectionOutput> perturbResult && perturbResult.IsSuccess
                            ? (Delta: Math.Abs(perturbResult.Value.Points.Count - n), Resource: (IDisposable)copy)
                            : ((Func<(double, IDisposable)>)(() => { IDisposable res = (IDisposable)copy; res?.Dispose(); return (0.0, null); }))())
                        .ToArray() is (double Delta, IDisposable Resource)[] perturbResults && perturbResults.Length > 0
                        ? ((Func<Result<(double, double, bool[])>>)(() => {
                            double[] deltas = perturbResults.Select(p => p.Delta).ToArray();
                            foreach ((double Delta, IDisposable Resource) in perturbResults) {
                                Resource?.Dispose();
                            }
                            return deltas.Length > 0 && deltas.Average() is double avgDelta && deltas.Max() is double maxDelta
                                ? ResultFactory.Create(value: (Score: 1.0 / (1.0 + avgDelta), Sensitivity: maxDelta / n, UnstableFlags: deltas.Select(d => d > 1.0).ToArray()))
                                : ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, Enumerable.Repeat(false, n).ToArray()));
                        }))()
                        : ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, Enumerable.Repeat(false, n).ToArray())),
                    Surface s => directions
                        .Select(dir => s.Duplicate() is Surface copy && copy.Translate(dir * perturbDist) && IntersectionCore.ExecutePair(copy, geomB, context, new()) is Result<Intersect.IntersectionOutput> perturbResult && perturbResult.IsSuccess
                            ? (Delta: Math.Abs(perturbResult.Value.Points.Count - n), Resource: (IDisposable)copy)
                            : ((Func<(double, IDisposable)>)(() => { IDisposable res = (IDisposable)copy; res?.Dispose(); return (0.0, null); }))())
                        .ToArray() is (double Delta, IDisposable Resource)[] perturbResults && perturbResults.Length > 0
                        ? ((Func<Result<(double, double, bool[])>>)(() => {
                            double[] deltas = perturbResults.Select(p => p.Delta).ToArray();
                            foreach ((double Delta, IDisposable Resource) in perturbResults) {
                                Resource?.Dispose();
                            }
                            return deltas.Length > 0 && deltas.Average() is double avgDelta && deltas.Max() is double maxDelta
                                ? ResultFactory.Create(value: (Score: 1.0 / (1.0 + avgDelta), Sensitivity: maxDelta / n, UnstableFlags: deltas.Select(d => d > 1.0).ToArray()))
                                : ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, Enumerable.Repeat(false, n).ToArray()));
                        }))()
                        : ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, Enumerable.Repeat(false, n).ToArray())),
                    _ => ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, Enumerable.Repeat(false, n).ToArray())),
                }
                : ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, Enumerable.Repeat(false, n).ToArray())),
        };
}
