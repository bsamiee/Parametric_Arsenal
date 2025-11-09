using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Arsenal.Rhino.Intersection;

/// <summary>Dense intersection analysis algorithms with zero duplication.</summary>
internal static class IntersectionCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(Intersect.IntersectionOutput output, GeometryBase geomA, GeometryBase geomB, IGeometryContext context) =>
        (output.Points.Count, output.ParametersA.Count, output.ParametersB.Count) switch {
            (0, _, _) => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
            (int n, int pa, int pb) when pa >= n && pb >= n => (geomA, geomB) switch {
                (Curve ca, Curve cb) => Enumerable.Range(0, n).Select(i => (ca.TangentAt(output.ParametersA[i]), cb.TangentAt(output.ParametersB[i])) is (Vector3d ta, Vector3d tb) ? Vector3d.VectorAngle(ta, tb) : double.NaN).Where(a => !double.IsNaN(a)).ToArray() is double[] angles && angles.Length > 0
                    ? ResultFactory.Create(value: (angles.Average() < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, angles, angles.Any(a => a < IntersectionConfig.GrazingAngleThreshold), angles.Average() < IntersectionConfig.TangentAngleThreshold ? 1.0 : 0.5))
                    : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                (Curve c, Surface s) => Enumerable.Range(0, n).Select(i => s.NormalAt(output.ParametersA[i], 0) is Vector3d sn && c.TangentAt(output.ParametersB[i]) is Vector3d ct ? Math.Abs(Vector3d.VectorAngle(sn, ct) - Math.PI / 2) : double.NaN).Where(a => !double.IsNaN(a)).ToArray() is double[] angles2 && angles2.Length > 0
                    ? ResultFactory.Create(value: (angles2.Average() < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, angles2, angles2.Any(a => a < IntersectionConfig.GrazingAngleThreshold), angles2.Average() < IntersectionConfig.TangentAngleThreshold ? 0.8 : 0.4))
                    : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                _ => ResultFactory.Create(value: ((byte)2, [], false, 0.0)),
            },
            _ => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[], Point3d[], double[])> FindNearMisses(GeometryBase geomA, GeometryBase geomB, double searchRadius, IGeometryContext context) =>
        (geomA, geomB) switch {
            (Curve ca, Curve cb) => Enumerable.Range(0, Math.Max(2, (int)(ca.GetLength() / searchRadius))).Select(i => ca.Domain.ParameterAt(i / (double)Math.Max(1, (int)(ca.GetLength() / searchRadius) - 1))).Select(t => (ca.PointAt(t), cb.ClosestPoint(ca.PointAt(t), out double tb) ? (cb.PointAt(tb), ca.PointAt(t).DistanceTo(cb.PointAt(tb))) : (Point3d.Unset, double.MaxValue))).Where(pair => pair.Item2.Item2 < searchRadius && pair.Item2.Item2 > context.AbsoluteTolerance).ToArray() is (Point3d, (Point3d, double))[] pairs && pairs.Length > 0
                ? ResultFactory.Create(value: (pairs.Select(p => p.Item1).ToArray(), pairs.Select(p => p.Item2.Item1).ToArray(), pairs.Select(p => p.Item2.Item2).ToArray()))
                : ResultFactory.Create(value: ([], [], [])),
            (Curve c, Surface s) => Enumerable.Range(0, Math.Max(2, (int)(c.GetLength() / searchRadius))).Select(i => c.Domain.ParameterAt(i / (double)Math.Max(1, (int)(c.GetLength() / searchRadius) - 1))).Select(t => (c.PointAt(t), s.ClosestPoint(c.PointAt(t), out double su, out double sv) is bool && (su, sv) is (double u, double v) ? (s.PointAt(u, v), c.PointAt(t).DistanceTo(s.PointAt(u, v))) : (Point3d.Unset, double.MaxValue))).Where(pair => pair.Item2.Item2 < searchRadius && pair.Item2.Item2 > context.AbsoluteTolerance).ToArray() is (Point3d, (Point3d, double))[] pairs2 && pairs2.Length > 0
                ? ResultFactory.Create(value: (pairs2.Select(p => p.Item1).ToArray(), pairs2.Select(p => p.Item2.Item1).ToArray(), pairs2.Select(p => p.Item2.Item2).ToArray()))
                : ResultFactory.Create(value: ([], [], [])),
            _ => ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.NearMissSearchFailed),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double Score, double Sensitivity, bool[] UnstableFlags)> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersect.IntersectionOutput baseOutput, IGeometryContext context) =>
        baseOutput.Points.Count switch {
            0 => ResultFactory.Create(value: (1.0, 0.0, [])),
            int n => (geomA.GetBoundingBox(accurate: false).Diagonal.Length * IntersectionConfig.StabilityPerturbationFactor, Enumerable.Range(0, IntersectionConfig.StabilitySampleCount).Select(i => new Vector3d(Math.Cos(2 * Math.PI * i / IntersectionConfig.StabilitySampleCount), Math.Sin(2 * Math.PI * i / IntersectionConfig.StabilitySampleCount), 0)).ToArray()) switch {
                (double perturbDist, Vector3d[] directions) => directions.Select(dir => geomA switch {
                    Curve ca when ca.DuplicateCurve() is Curve caCopy && caCopy.Translate(dir * perturbDist) is bool => IntersectionCore.ExecutePair(caCopy, geomB, context, new Intersect.IntersectionOptions()).IsSuccess switch {
                        true => (Math.Abs(IntersectionCore.ExecutePair(caCopy, geomB, context, new Intersect.IntersectionOptions()).Value.Points.Count - n), caCopy),
                        false => (0.0, caCopy),
                    },
                    _ => (0.0, null),
                }).Where(pair => pair.Item2 is not null).ToArray() is (double, Curve?)[] perturbResults && perturbResults.Length > 0 && perturbResults.Select(p => { p.Item2?.Dispose(); return p.Item1; }).ToArray() is double[] deltas
                    ? ResultFactory.Create(value: (1.0 / (1.0 + deltas.Average()), deltas.Max() / n, Enumerable.Range(0, n).Select(_ => deltas.Any(d => d > 1.0)).ToArray()))
                    : ResultFactory.Create(value: (1.0, 0.0, [])),
            },
        };
}
