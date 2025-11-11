using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Dense intersection analysis algorithms.</summary>
internal static class IntersectionCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(Intersect.IntersectionOutput output, GeometryBase geomA, GeometryBase geomB, IGeometryContext context) =>
        (geomA, geomB) switch {
            (null, _) or (_, null) => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData.WithContext("Geometry is null")),
            (GeometryBase first, GeometryBase second) => IntersectionCore.ResolveStrategy(first.GetType(), second.GetType())
                .Bind(entry => {
                    (V modeA, V modeB) = entry.Swapped
                        ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                        : (entry.Strategy.ModeA, entry.Strategy.ModeB);
                    return (modeA == V.None
                            ? ResultFactory.Create(value: first)
                            : ResultFactory.Create(value: first).Validate(args: [context, modeA,]))
                        .Bind(validA => (modeB == V.None
                                ? ResultFactory.Create(value: second)
                                : ResultFactory.Create(value: second).Validate(args: [context, modeB,]))
                            .Bind(validB => (output.Points.Count, output.ParametersA.Count, output.ParametersB.Count) switch {
                                (0, _, _) => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                                (int count, int parametersA, int parametersB) when parametersA >= count && parametersB >= count => (validA, validB) switch {
                                    (Curve curveA, Curve curveB) => Enumerable.Range(0, count)
                                        .Select(index => (curveA.TangentAt(output.ParametersA[index]), curveB.TangentAt(output.ParametersB[index])) is (Vector3d tangentA, Vector3d tangentB) && tangentA.IsValid && tangentB.IsValid
                                            ? Vector3d.VectorAngle(tangentA, tangentB)
                                            : double.NaN)
                                        .Where(angle => !double.IsNaN(angle))
                                        .ToArray() is double[] angles && angles.Length > 0 && Math.Atan2(angles.Sum(Math.Sin) / angles.Length, angles.Sum(Math.Cos) / angles.Length) is double circularMean && (circularMean < 0.0 ? circularMean + (2.0 * Math.PI) : circularMean) is double averageAngle
                                            ? ResultFactory.Create(value: (Type: averageAngle < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, ApproachAngles: angles, IsGrazing: angles.Any(angle => angle < IntersectionConfig.GrazingAngleThreshold), BlendScore: averageAngle < IntersectionConfig.TangentAngleThreshold ? IntersectionConfig.TangentBlendScore : IntersectionConfig.PerpendicularBlendScore))
                                            : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                                    (Curve, Surface) or (Surface, Curve) => ResultFactory.Create(value: ((byte)2, Array.Empty<double>(), false, 0.0)),
                                    _ => ResultFactory.Create(value: ((byte)2, Array.Empty<double>(), false, 0.0)),
                                },
                                _ => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                            }));
                }),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[], Point3d[], double[])> FindNearMisses(GeometryBase geomA, GeometryBase geomB, double searchRadius, IGeometryContext context) =>
        geomA is null || geomB is null
            ? ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.InsufficientIntersectionData.WithContext("Geometry is null"))
            : searchRadius <= context.AbsoluteTolerance
                ? ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.InvalidSearchRadius.WithContext("SearchRadius must exceed tolerance"))
                : IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                    .Bind(entry => {
                        (V modeA, V modeB) = entry.Swapped
                            ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                            : (entry.Strategy.ModeA, entry.Strategy.ModeB);
                        Func<IEnumerable<(Point3d PointA, Point3d PointB, double Distance)>, Result<(Point3d[], Point3d[], double[])>> pack =
                            pairs => pairs.ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] array && array.Length > 0
                                ? ResultFactory.Create(value: (array.Select(pair => pair.PointA).ToArray(), array.Select(pair => pair.PointB).ToArray(), array.Select(pair => pair.Distance).ToArray()))
                                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []));
                        Func<Curve, int> sampleCount = curve => Math.Max(3, (int)Math.Ceiling(curve.GetLength() / searchRadius));

                        return (modeA == V.None
                                ? ResultFactory.Create(value: geomA)
                                : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                            .Bind(validA => (modeB == V.None
                                    ? ResultFactory.Create(value: geomB)
                                    : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                                .Bind(validB => (validA, validB) switch {
                                    (Curve curveA, Curve curveB) => pack(
                                        sampleCount(curveA) is int samplesA && sampleCount(curveB) is int samplesB
                                            ? Enumerable.Range(0, samplesA)
                                                .Select(index => curveA.PointAt(curveA.Domain.ParameterAt(index / (double)(samplesA - 1))))
                                                .Select(point => curveB.ClosestPoint(point, out double parameter)
                                                    ? (PointA: point, PointB: curveB.PointAt(parameter), Distance: point.DistanceTo(curveB.PointAt(parameter)))
                                                    : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Concat(Enumerable.Range(0, samplesB)
                                                    .Select(index => curveB.PointAt(curveB.Domain.ParameterAt(index / (double)(samplesB - 1))))
                                                    .Select(point => curveA.ClosestPoint(point, out double parameter)
                                                        ? (PointA: curveA.PointAt(parameter), PointB: point, Distance: curveA.PointAt(parameter).DistanceTo(point))
                                                        : (PointA: Point3d.Unset, PointB: point, Distance: double.MaxValue)))
                                                .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > context.AbsoluteTolerance)
                                            : Enumerable.Empty<(Point3d, Point3d, double)>()),
                                    (Curve curve, Surface surface) => pack(
                                        sampleCount(curve) is int samples
                                            ? Enumerable.Range(0, samples)
                                                .Select(index => curve.PointAt(curve.Domain.ParameterAt(index / (double)(samples - 1))))
                                                .Select(point => surface.ClosestPoint(point, out double u, out double v)
                                                    ? (PointA: point, PointB: surface.PointAt(u, v), Distance: point.DistanceTo(surface.PointAt(u, v)))
                                                    : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > context.AbsoluteTolerance)
                                            : Enumerable.Empty<(Point3d, Point3d, double)>()),
                                    (Surface surface, Curve curve) => pack(
                                        sampleCount(curve) is int samples
                                            ? Enumerable.Range(0, samples)
                                                .Select(index => curve.PointAt(curve.Domain.ParameterAt(index / (double)(samples - 1))))
                                                .Select(point => surface.ClosestPoint(point, out double u, out double v)
                                                    ? (PointA: point, PointB: surface.PointAt(u, v), Distance: point.DistanceTo(surface.PointAt(u, v)))
                                                    : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > context.AbsoluteTolerance)
                                            : Enumerable.Empty<(Point3d, Point3d, double)>()),
                                    _ => ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.NearMissSearchFailed),
                                }));
                    });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double Score, double Sensitivity, bool[] UnstableFlags)> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersect.IntersectionOutput baseOutput, IGeometryContext context) =>
        baseOutput.Points.Count switch {
            0 => ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, [])),
            int count => IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                .Bind(entry => {
                    (V modeA, V modeB) = entry.Swapped
                        ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                        : (entry.Strategy.ModeA, entry.Strategy.ModeB);
                    return (modeA == V.None
                            ? ResultFactory.Create(value: geomA)
                            : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                        .Bind(validA => (modeB == V.None
                                ? ResultFactory.Create(value: geomB)
                                : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                            .Bind(validB => IntersectionCore.NormalizeOptions(new Intersect.IntersectionOptions(), context)
                                .Bind(normalized => {
                                    int phiSteps = (int)Math.Ceiling(Math.Sqrt(IntersectionConfig.StabilitySampleCount));
                                    int thetaSteps = (int)Math.Ceiling(IntersectionConfig.StabilitySampleCount / (double)phiSteps);
                                    Vector3d[] directions = Enumerable.Range(0, phiSteps)
                                        .SelectMany(phiIndex => Enumerable.Range(0, thetaSteps)
                                            .Select(thetaIndex => ((Math.PI * phiIndex) / phiSteps, ((2.0 * Math.PI) * thetaIndex) / thetaSteps) is (double phi, double theta)
                                                ? new Vector3d(Math.Sin(phi) * Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi))
                                                : Vector3d.Unset))
                                        .Take(IntersectionConfig.StabilitySampleCount)
                                        .ToArray();
                                    double perturbationDistance = validA.GetBoundingBox(accurate: false).Diagonal.Length * IntersectionConfig.StabilityPerturbationFactor;
                                    Result<(double Score, double Sensitivity, bool[] UnstableFlags)> defaultResult = ResultFactory.Create(value: (1.0, 0.0, [.. Enumerable.Repeat(element: false, count: count)]));
                                    Func<Vector3d, Result<(double Delta, IDisposable? Resource)>> generator = validA switch {
                                        Curve curve => direction => curve.DuplicateCurve() is Curve copy && copy.Translate(direction * perturbationDistance)
                                            ? IntersectionCore.ExecuteWithOptions(copy, validB, context, normalized)
                                                .Map(result => (Math.Abs(result.Points.Count - count), (IDisposable?)copy))
                                                .OnError(_ => { copy.Dispose(); return ResultFactory.Create(value: (double.NaN, (IDisposable?)null)); })
                                            : ResultFactory.Create(value: (double.NaN, (IDisposable?)null)),
                                        Surface surface => direction => surface.Duplicate() is Surface copy && copy.Translate(direction * perturbationDistance)
                                            ? IntersectionCore.ExecuteWithOptions(copy, validB, context, normalized)
                                                .Map(result => (Math.Abs(result.Points.Count - count), (IDisposable?)copy))
                                                .OnError(_ => { copy.Dispose(); return ResultFactory.Create(value: (double.NaN, (IDisposable?)null)); })
                                            : ResultFactory.Create(value: (double.NaN, (IDisposable?)null)),
                                        _ => _ => ResultFactory.Create(value: (double.NaN, (IDisposable?)null)),
                                    };

                                    return directions.Length == 0
                                        ? defaultResult
                                        : directions
                                            .Select(direction => generator(direction).Match(onSuccess: tuple => tuple, onFailure: _ => (double.NaN, (IDisposable?)null)))
                                            .ToArray() is (double Delta, IDisposable? Resource)[] perturbations && perturbations.Length > 0
                                            ? (perturbations.Select(entry => entry.Delta).Where(delta => !double.IsNaN(delta)).ToArray() is double[] filtered && filtered.Length > 0
                                                ? ResultFactory.Create(value: (
                                                    Score: 1.0 / (1.0 + filtered.Average()),
                                                    Sensitivity: filtered.Max() / count,
                                                    UnstableFlags: [.. Enumerable.Range(0, count).Select(index => filtered.Skip((int)Math.Round(index * filtered.Length / (double)count)).Take((int)Math.Round((index + 1) * filtered.Length / (double)count) - (int)Math.Round(index * filtered.Length / (double)count)).Any(delta => delta > 1.0))]))
                                                : defaultResult)
                                                .Tap(_ => {
                                                    foreach ((double Delta, IDisposable? Resource) entry in perturbations) {
                                                        entry.Resource?.Dispose();
                                                    }
                                                })
                                            : defaultResult;
                                }));
                }),
        };
}
