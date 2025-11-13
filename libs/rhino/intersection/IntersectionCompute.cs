using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Dense intersection analysis algorithms.</summary>
internal static class IntersectionCompute {
    /// <summary>Classifies intersection type using tangent angle analysis and circular mean calculation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(byte Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(Intersect.IntersectionOutput output, GeometryBase geomA, GeometryBase geomB, IGeometryContext context) {
        static Result<(byte, double[], bool, double)> curveSurfaceClassifier(double[] angles) {
            double averageDeviation = angles.Sum(angle => Math.Abs(RhinoMath.HalfPI - angle)) / angles.Length;
            bool grazing = angles.Any(angle => Math.Abs(RhinoMath.HalfPI - angle) <= IntersectionConfig.GrazingAngleThreshold);
            bool tangent = averageDeviation <= IntersectionConfig.TangentAngleThreshold;
            return ResultFactory.Create(value: (
                Type: tangent ? (byte)0 : (byte)1,
                ApproachAngles: angles,
                IsGrazing: grazing,
                BlendScore: tangent ? IntersectionConfig.CurveSurfaceTangentBlendScore : IntersectionConfig.CurveSurfacePerpendicularBlendScore));
        }

        static Result<T> validate<T>(T geometry, IGeometryContext ctx, V mode) where T : notnull =>
            mode == V.None ? ResultFactory.Create(value: geometry) : ResultFactory.Create(value: geometry).Validate(args: [ctx, mode,]);

        static Result<(byte, double[], bool, double)> computeCurveSurfaceAngles(Curve curve, Surface surface, Intersect.IntersectionOutput output, int count, double[] parameters) =>
            Enumerable.Range(0, count)
                .Select(index => (Tangent: curve.TangentAt(parameters[index]), Point: output.Points[index]))
                .Select(tuple => tuple.Tangent.IsValid && surface.ClosestPoint(tuple.Point, out double u, out double v) && surface.NormalAt(u, v) is Vector3d normal && normal.IsValid
                    ? Vector3d.VectorAngle(tuple.Tangent, normal)
                    : double.NaN)
                .Where(angle => !double.IsNaN(angle))
                .ToArray() is double[] angles && angles.Length > 0
                    ? curveSurfaceClassifier(angles)
                    : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed);

        return IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                .Bind(entry => {
                    (V modeA, V modeB) = entry.Swapped
                        ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                        : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                    return validate(geomA, context, modeA)
                        .Bind(validA => validate(geomB, context, modeB)
                            .Bind(validB => (output.Points.Count, output.ParametersA.Count, output.ParametersB.Count) switch {
                                (0, _, _) => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                                (int count, int parametersA, int parametersB) => (validA, validB) switch {
                                    (Curve curveA, Curve curveB) when parametersA >= count && parametersB >= count => Enumerable.Range(0, count)
                                        .Select(index => (curveA.TangentAt(output.ParametersA[index]), curveB.TangentAt(output.ParametersB[index])) is (Vector3d tangentA, Vector3d tangentB) && tangentA.IsValid && tangentB.IsValid
                                            ? Vector3d.VectorAngle(tangentA, tangentB)
                                            : double.NaN)
                                        .Where(angle => !double.IsNaN(angle))
                                        .ToArray() is double[] angles && angles.Length > 0 && Math.Atan2(angles.Sum(Math.Sin) / angles.Length, angles.Sum(Math.Cos) / angles.Length) is double circularMean && (circularMean < 0.0 ? circularMean + RhinoMath.TwoPI : circularMean) is double averageAngle
                                            ? ResultFactory.Create(value: (Type: averageAngle < IntersectionConfig.TangentAngleThreshold ? (byte)0 : (byte)1, ApproachAngles: angles, IsGrazing: angles.Any(angle => angle < IntersectionConfig.GrazingAngleThreshold), BlendScore: averageAngle < IntersectionConfig.TangentAngleThreshold ? IntersectionConfig.TangentBlendScore : IntersectionConfig.PerpendicularBlendScore))
                                            : ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                                    (Curve curve, Surface surface) when parametersA >= count => computeCurveSurfaceAngles(curve, surface, output, count, [.. output.ParametersA]),
                                    (Surface surface, Curve curve) when parametersB >= count => computeCurveSurfaceAngles(curve, surface, output, count, [.. output.ParametersB]),
                                    _ when parametersA < count || parametersB < count => ResultFactory.Create<(byte, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                                    _ => ResultFactory.Create(value: ((byte)2, Array.Empty<double>(), false, 0.0)),
                                },
                            }));
                });
    }

    /// <summary>Finds near-miss locations between geometries within search radius using closest point sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d[], Point3d[], double[])> FindNearMisses(GeometryBase geomA, GeometryBase geomB, double searchRadius, IGeometryContext context) {
        static Result<T> validate<T>(T geometry, IGeometryContext ctx, V mode) where T : notnull =>
            mode == V.None ? ResultFactory.Create(value: geometry) : ResultFactory.Create(value: geometry).Validate(args: [ctx, mode,]);

        static (Point3d[], Point3d[], double[]) toArrays((Point3d PointA, Point3d PointB, double Distance)[] pairs) {
            Point3d[] pointsA = new Point3d[pairs.Length];
            Point3d[] pointsB = new Point3d[pairs.Length];
            double[] distances = new double[pairs.Length];
            for (int i = 0; i < pairs.Length; i++) {
                (pointsA[i], pointsB[i], distances[i]) = pairs[i];
            }
            return (pointsA, pointsB, distances);
        }

        double minDistance = context.AbsoluteTolerance * IntersectionConfig.NearMissToleranceMultiplier;
        return searchRadius <= minDistance
                ? ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.InvalidSearchRadius.WithContext(string.Create(CultureInfo.InvariantCulture, $"SearchRadius must exceed tolerance * {IntersectionConfig.NearMissToleranceMultiplier}")))
                : IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                    .Bind(entry => {
                        (V modeA, V modeB) = entry.Swapped
                            ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                            : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                        return validate(geomA, context, modeA)
                            .Bind(validA => validate(geomB, context, modeB)
                                .Bind(validB => {
                                    (GeometryBase primary, GeometryBase secondary) = (validA, validB) switch {
                                        (Curve c, Surface s) => (c, s),
                                        (Surface s, Curve c) => (c, s),
                                        _ => (validA, validB),
                                    };

                                    return (primary, secondary) switch {
                                        (Curve curveA, Curve curveB) => Math.Max(3, (int)Math.Ceiling(curveA.GetLength() / searchRadius)) is int samples
                                            ? Enumerable.Range(0, samples)
                                                .Select(index => curveA.PointAt(curveA.Domain.ParameterAt(index / (double)(samples - 1))))
                                                .Select(point => curveB.ClosestPoint(point, out double parameter)
                                                    ? (PointA: point, PointB: curveB.PointAt(parameter), Distance: point.DistanceTo(curveB.PointAt(parameter)))
                                                    : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance)
                                                .Concat(Enumerable.Range(0, samples)
                                                    .Select(index => curveB.PointAt(curveB.Domain.ParameterAt(index / (double)(samples - 1))))
                                                    .Select(point => curveA.ClosestPoint(point, out double parameter)
                                                        ? (PointA: curveA.PointAt(parameter), PointB: point, Distance: curveA.PointAt(parameter).DistanceTo(point))
                                                        : (PointA: Point3d.Unset, PointB: point, Distance: double.MaxValue))
                                                    .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance))
                                                .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] curvePairs && curvePairs.Length > 0
                                                ? ResultFactory.Create(value: toArrays(curvePairs))
                                                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                                            : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                                        (Curve curve, Surface surface) => Math.Max(3, (int)Math.Ceiling(curve.GetLength() / searchRadius)) is int samples
                                            ? Enumerable.Range(0, samples)
                                                .Select(index => curve.PointAt(curve.Domain.ParameterAt(index / (double)(samples - 1))))
                                                .Select(point => surface.ClosestPoint(point, out double u, out double v)
                                                    ? (PointA: point, PointB: surface.PointAt(u, v), Distance: point.DistanceTo(surface.PointAt(u, v)))
                                                    : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance)
                                                .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] pairs && pairs.Length > 0
                                                ? ResultFactory.Create(value: toArrays(pairs))
                                                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                                            : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                                        _ => ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.NearMissSearchFailed),
                                    };
                                }));
                    });
    }

    /// <summary>Analyzes intersection stability using spherical perturbation sampling and count variation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(double Score, double Sensitivity, bool[] UnstableFlags)> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersect.IntersectionOutput baseOutput, IGeometryContext context) {
        static Result<T> validate<T>(T geometry, IGeometryContext ctx, V mode) where T : notnull =>
            mode == V.None ? ResultFactory.Create(value: geometry) : ResultFactory.Create(value: geometry).Validate(args: [ctx, mode,]);

        return baseOutput.Points.Count switch {
            0 => ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, [])),
            int count => IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                .Bind(entry => {
                    (V modeA, V modeB) = entry.Swapped
                        ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                        : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                    return validate(geomA, context, modeA)
                        .Bind(validA => validate(geomB, context, modeB)
                            .Bind(validB => IntersectionCore.NormalizeOptions(new Intersect.IntersectionOptions(), context)
                                .Bind(normalized => {
                                    int sqrtSamples = (int)Math.Ceiling(Math.Sqrt(IntersectionConfig.StabilitySampleCount));
                                    (int phiSteps, int thetaSteps) = (sqrtSamples, (int)Math.Ceiling(IntersectionConfig.StabilitySampleCount / (double)sqrtSamples));
                                    Vector3d[] directions = [.. Enumerable.Range(0, phiSteps)
                                        .SelectMany(phiIndex => Enumerable.Range(0, thetaSteps)
                                            .Select(thetaIndex => {
                                                (double phi, double theta) = ((Math.PI * phiIndex) / phiSteps, (RhinoMath.TwoPI * thetaIndex) / thetaSteps);
                                                return new Vector3d(Math.Sin(phi) * Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi));
                                            }))
                                        .Take(IntersectionConfig.StabilitySampleCount),
                                    ];
                                    double perturbationDistance = validA.GetBoundingBox(accurate: false).Diagonal.Length * IntersectionConfig.StabilityPerturbationFactor;
                                    Result<(double Score, double Sensitivity, bool[] UnstableFlags)> defaultResult = ResultFactory.Create<(double Score, double Sensitivity, bool[] UnstableFlags)>(value: (1.0, 0.0, [.. Enumerable.Repeat(element: false, count: count)]));

                                    (double Delta, IDisposable? Resource) perturbAndIntersect(Vector3d direction, GeometryBase original) =>
                                        original switch {
                                            Curve curve when curve.DuplicateCurve() is Curve copy && copy.Translate(direction * perturbationDistance) =>
                                                IntersectionCore.ExecuteWithOptions(copy, validB, context, normalized)
                                                    .Map(result => {
                                                        foreach (Curve intersection in result.Curves) {
                                                            intersection?.Dispose();
                                                        }
                                                        return ((double)Math.Abs(result.Points.Count - count), (IDisposable?)copy);
                                                    })
                                                    .Match(onSuccess: tuple => tuple, onFailure: _ => { copy.Dispose(); return (double.NaN, null); }),
                                            Surface surface when surface.Duplicate() is Surface copy && copy.Translate(direction * perturbationDistance) =>
                                                IntersectionCore.ExecuteWithOptions(copy, validB, context, normalized)
                                                    .Map(result => {
                                                        foreach (Curve intersection in result.Curves) {
                                                            intersection?.Dispose();
                                                        }
                                                        return ((double)Math.Abs(result.Points.Count - count), (IDisposable?)copy);
                                                    })
                                                    .Match(onSuccess: tuple => tuple, onFailure: _ => { copy.Dispose(); return (double.NaN, null); }),
                                            _ => (double.NaN, null),
                                        };

                                    (double Delta, IDisposable? Resource)[] perturbations = [.. directions.Select(dir => perturbAndIntersect(dir, validA))];
                                    try {
                                        double[] filtered = [.. perturbations.Select(entry => entry.Delta).Where(delta => !double.IsNaN(delta))];
                                        return filtered.Length > 0
                                            ? ResultFactory.Create<(double Score, double Sensitivity, bool[] UnstableFlags)>(value: (
                                                Score: 1.0 / (1.0 + filtered.Average()),
                                                Sensitivity: filtered.Max() / count,
                                                UnstableFlags: [.. Enumerable.Range(0, count).Select(index => {
                                                    int start = (int)Math.Round((double)index * filtered.Length / count);
                                                    int end = (int)Math.Round((double)(index + 1) * filtered.Length / count);
                                                    return filtered.Skip(start).Take(end - start).Any(delta => delta > 1.0);
                                                }),
                                                ]))
                                            : defaultResult;
                                    } finally {
                                        foreach ((double Delta, IDisposable? Resource) in perturbations) {
                                            Resource?.Dispose();
                                        }
                                    }
                                })));
                }),
        };
    }
}
