using System.Diagnostics.Contracts;
using System.Globalization;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Dense intersection analysis algorithms.</summary>
[Pure]
internal static class IntersectionCompute {
    /// <summary>Classifies intersection type using tangent angle analysis and circular mean calculation.</summary>
    [Pure]
    internal static Result<(Intersection.IntersectionType Type, double[] ApproachAngles, bool IsGrazing, double BlendScore)> Classify(Intersection.IntersectionOutput output, GeometryBase geomA, GeometryBase geomB, IGeometryContext context) {
        // Curve-surface: angle between curve tangent and surface normal
        // Parallel (0° or 180°) → tangent intersection (smooth blend)
        // Perpendicular (90°) → transverse intersection (sharp meeting)
        static Result<(Intersection.IntersectionType, double[], bool, double)> curveSurfaceClassifier(double[] angles) {
            static double minAngleToParallel(double angle) => Math.Min(Math.Abs(angle), Math.Abs(Math.PI - angle));
            double averageDeviation = angles.Sum(minAngleToParallel) / angles.Length;
            bool grazing = angles.Any(angle => minAngleToParallel(angle) <= IntersectionConfig.GrazingAngleThreshold);
            bool tangent = averageDeviation <= IntersectionConfig.TangentAngleThreshold;
            (Intersection.IntersectionType, double[], bool, double) result = (
                tangent ? Intersection.IntersectionType.Tangent.Instance : Intersection.IntersectionType.Transverse.Instance,
                angles,
                grazing,
                tangent ? IntersectionConfig.CurveSurfaceTangentBlendScore : IntersectionConfig.CurveSurfacePerpendicularBlendScore);
            return ResultFactory.Create(value: result);
        }

        static Result<(Intersection.IntersectionType, double[], bool, double)> computeCurveSurfaceAngles(Curve curve, Surface surface, Intersection.IntersectionOutput output, int count, double[] parameters) =>
            Enumerable.Range(0, count)
                .Select(index => (Tangent: curve.TangentAt(parameters[index]), Point: output.Points[index]))
                .Select(tuple => tuple.Tangent.IsValid && surface.ClosestPoint(tuple.Point, out double u, out double v) && surface.NormalAt(u, v) is Vector3d normal && normal.IsValid
                    ? Vector3d.VectorAngle(tuple.Tangent, normal)
                    : RhinoMath.UnsetValue)
                .Where(angle => RhinoMath.IsValidDouble(angle))
                .ToArray() is double[] angles && angles.Length > 0
                    ? curveSurfaceClassifier(angles)
                    : ResultFactory.Create<(Intersection.IntersectionType, double[], bool, double)>(error: E.Geometry.ClassificationFailed);

        return IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                .Bind(entry => {
                    (V modeA, V modeB) = entry.Swapped
                        ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                        : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                    return (modeA == V.None ? ResultFactory.Create(value: geomA) : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                        .Bind(validA => (modeB == V.None ? ResultFactory.Create(value: geomB) : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                            .Bind(validB => (output.Points.Count, output.ParametersA.Count, output.ParametersB.Count) switch {
                                (0, _, _) => ResultFactory.Create<(Intersection.IntersectionType, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                                (int count, int parametersA, int parametersB) => (validA, validB) switch {
                                    (Curve curveA, Curve curveB) when parametersA >= count && parametersB >= count => Enumerable.Range(0, count)
                                        .Select(index => (curveA.TangentAt(output.ParametersA[index]), curveB.TangentAt(output.ParametersB[index])) is (Vector3d tangentA, Vector3d tangentB) && tangentA.IsValid && tangentB.IsValid
                                            ? Vector3d.VectorAngle(tangentA, tangentB)
                                            : RhinoMath.UnsetValue)
                                        .Where(static angle => RhinoMath.IsValidDouble(angle))
                                        .ToArray() is double[] angles && angles.Length > 0 && Math.Atan2(angles.Sum(Math.Sin) / angles.Length, angles.Sum(Math.Cos) / angles.Length) is double circularMean && RhinoMath.Wrap(circularMean, 0.0, RhinoMath.TwoPI) is double averageAngle
                                            ? ((Func<Result<(Intersection.IntersectionType, double[], bool, double)>>)(() => {
                                                // Curve-curve: tangent when tangents are parallel or antiparallel (0° or 180°), transverse when near 90°.
                                                double averageParallelDeviation = Math.Min(Math.Min(averageAngle, RhinoMath.TwoPI - averageAngle), Math.Abs(RhinoMath.Pi - averageAngle));
                                                bool isTangent = averageParallelDeviation <= IntersectionConfig.TangentAngleThreshold;
                                                bool isGrazing = angles.Any(static angle => Math.Min(angle, Math.Abs(RhinoMath.Pi - angle)) <= IntersectionConfig.GrazingAngleThreshold);
                                                (Intersection.IntersectionType, double[], bool, double) result = (isTangent ? Intersection.IntersectionType.Tangent.Instance : Intersection.IntersectionType.Transverse.Instance, angles, isGrazing, isTangent ? IntersectionConfig.TangentBlendScore : IntersectionConfig.PerpendicularBlendScore);
                                                return ResultFactory.Create(value: result);
                                            }))()
                                            : ResultFactory.Create<(Intersection.IntersectionType, double[], bool, double)>(error: E.Geometry.ClassificationFailed),
                                    (Curve curve, Surface surface) when parametersA >= count => computeCurveSurfaceAngles(curve, surface, output, count, [.. output.ParametersA,]),
                                    (Surface surface, Curve curve) when parametersB >= count => computeCurveSurfaceAngles(curve, surface, output, count, [.. output.ParametersB,]),
                                    _ when parametersA < count || parametersB < count => ResultFactory.Create<(Intersection.IntersectionType, double[], bool, double)>(error: E.Geometry.InsufficientIntersectionData),
                                    _ => ResultFactory.Create<(Intersection.IntersectionType, double[], bool, double)>(value: (Intersection.IntersectionType.Unknown.Instance, [], false, 0.0)),
                                },
                            }));
                });
    }

    /// <summary>Finds near-miss locations between geometries within search radius using closest point sampling.</summary>
    [Pure]
    internal static Result<(Point3d[], Point3d[], double[])> FindNearMisses(GeometryBase geomA, GeometryBase geomB, double searchRadius, IGeometryContext context) {
        static (Point3d[], Point3d[], double[]) unpackPairs((Point3d PointA, Point3d PointB, double Distance)[] pairs) {
            int n = pairs.Length;
            Point3d[] pointsA = new Point3d[n];
            Point3d[] pointsB = new Point3d[n];
            double[] distances = new double[n];
            for (int i = 0; i < n; i++) {
                pointsA[i] = pairs[i].PointA;
                pointsB[i] = pairs[i].PointB;
                distances[i] = pairs[i].Distance;
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

                        return (modeA == V.None ? ResultFactory.Create(value: geomA) : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                            .Bind(validA => (modeB == V.None ? ResultFactory.Create(value: geomB) : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                                .Bind(validB => {
                                    (GeometryBase primary, GeometryBase secondary) = (validA, validB) switch {
                                        (Curve c, Surface s) => (c, s),
                                        (Surface s, Curve c) => (c, s),
                                        _ => (validA, validB),
                                    };

                                    return (primary, secondary) switch {
                                        (Curve curveA, Curve curveB) => Math.Max(IntersectionConfig.MinCurveNearMissSamples, (int)Math.Ceiling(curveA.GetLength() / searchRadius)) is int samples
                                            ? Enumerable.Range(0, samples)
                                                .Select(index => curveA.PointAt(curveA.Domain.ParameterAt(index / (double)(samples - 1))))
                                                .Select(point => curveB.ClosestPoint(point, out double parameter) && curveB.PointAt(parameter) is Point3d closestB
                                                    ? (PointA: point, PointB: closestB, Distance: point.DistanceTo(closestB))
                                                    : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance)
                                                .Concat(Enumerable.Range(0, samples)
                                                    .Select(index => curveB.PointAt(curveB.Domain.ParameterAt(index / (double)(samples - 1))))
                                                    .Select(point => curveA.ClosestPoint(point, out double parameter) && curveA.PointAt(parameter) is Point3d closestA
                                                        ? (PointA: closestA, PointB: point, Distance: closestA.DistanceTo(point))
                                                        : (PointA: Point3d.Unset, PointB: point, Distance: double.MaxValue))
                                                    .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance))
                                                .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] curvePairs && curvePairs.Length > 0
                                                ? ResultFactory.Create(value: unpackPairs(curvePairs))
                                                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                                            : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                                        (Curve curve, Surface surface) => Math.Max(IntersectionConfig.MinCurveNearMissSamples, (int)Math.Ceiling(curve.GetLength() / searchRadius)) is int samples
                                            ? Enumerable.Range(0, samples)
                                                .Select(index => curve.PointAt(curve.Domain.ParameterAt(index / (double)(samples - 1))))
                                                .Select(point => surface.ClosestPoint(point, out double u, out double v)
                                                    ? (PointA: point, PointB: surface.PointAt(u, v), Distance: point.DistanceTo(surface.PointAt(u, v)))
                                                    : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance)
                                                .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] pairs && pairs.Length > 0
                                                ? ResultFactory.Create(value: unpackPairs(pairs))
                                                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                                            : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                                        (Brep brepA, Brep brepB) when brepA.Faces.Count > 0 => brepA.Faces
                                                .Select(face => (Face: face, Size: face.GetSurfaceSize(out double width, out double height) && width > 0.0 && height > 0.0 ? width * height : 0.0))
                                                .Where(entry => entry.Size > 0.0)
                                                .ToArray() is (BrepFace Face, double Size)[] validFaces && validFaces.Length > 0 && validFaces.Sum(static entry => entry.Size) is double totalArea && totalArea > 0.0
                                                && Math.Max(IntersectionConfig.MinBrepNearMissSamples, (int)Math.Ceiling(brepA.GetBoundingBox(accurate: false).Diagonal.Length / searchRadius)) is int totalBudget
                                                ? validFaces
                                                    .SelectMany(entry => {
                                                        int faceSamples = Math.Max(IntersectionConfig.MinSamplesPerFace, (int)Math.Round(totalBudget * (entry.Size / totalArea)));
                                                        int samplesPerDimension = (int)Math.Ceiling(Math.Sqrt(faceSamples));
                                                        return entry.Face.Domain(0) is Interval uDomain && entry.Face.Domain(1) is Interval vDomain
                                                            ? Enumerable.Range(0, samplesPerDimension)
                                                                .SelectMany(uIndex => Enumerable.Range(0, samplesPerDimension)
                                                                    .Select(vIndex => {
                                                                        double u = samplesPerDimension > 1
                                                                            ? uDomain.ParameterAt(uIndex / (double)(samplesPerDimension - 1))
                                                                            : uDomain.Mid;
                                                                        double v = samplesPerDimension > 1
                                                                            ? vDomain.ParameterAt(vIndex / (double)(samplesPerDimension - 1))
                                                                            : vDomain.Mid;
                                                                        return entry.Face.IsAtSingularity(u, v, exact: true) ? Point3d.Unset : entry.Face.PointAt(u, v);
                                                                    }))
                                                                .Where(static point => point.IsValid)
                                                            : [];
                                                    })
                                                    .Select(point => brepB.ClosestPoint(point) is Point3d closestB
                                                        ? (PointA: point, PointB: closestB, Distance: point.DistanceTo(closestB))
                                                        : (PointA: Point3d.Unset, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                    .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance && candidate.PointA.IsValid && candidate.PointB.IsValid)
                                                    .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] brepPairs && brepPairs.Length > 0
                                                    ? ResultFactory.Create(value: unpackPairs(brepPairs))
                                                    : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                                                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                                        (Mesh meshA, Mesh meshB) => (meshA.Vertices.Count > 0)
                                                && (meshB.Vertices.Count > 0)
                                                && Math.Min(meshA.Vertices.Count, IntersectionConfig.MaxNearMissSamples) is int samplesA
                                                && Math.Min(meshB.Vertices.Count, IntersectionConfig.MaxNearMissSamples) is int samplesB
                                            ? Enumerable.Range(0, samplesA)
                                                .Select(index => new Point3d(meshA.Vertices[index * meshA.Vertices.Count / samplesA]))
                                                .Select(point => meshB.ClosestMeshPoint(point, searchRadius) is MeshPoint meshPoint && meshPoint.Point.IsValid
                                                    ? (PointA: point, PointB: meshPoint.Point, Distance: point.DistanceTo(meshPoint.Point))
                                                    : (PointA: Point3d.Unset, PointB: Point3d.Unset, Distance: double.MaxValue))
                                                .Concat(Enumerable.Range(0, samplesB)
                                                    .Select(index => new Point3d(meshB.Vertices[index * meshB.Vertices.Count / samplesB]))
                                                    .Select(point => meshA.ClosestMeshPoint(point, searchRadius) is MeshPoint meshPoint && meshPoint.Point.IsValid
                                                        ? (PointA: meshPoint.Point, PointB: point, Distance: meshPoint.Point.DistanceTo(point))
                                                        : (PointA: Point3d.Unset, PointB: Point3d.Unset, Distance: double.MaxValue)))
                                                .Where(candidate => (candidate.Distance < searchRadius) && (candidate.Distance > minDistance) && candidate.PointA.IsValid && candidate.PointB.IsValid)
                                                .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] meshPairs && meshPairs.Length > 0
                                                ? ResultFactory.Create(value: unpackPairs(meshPairs))
                                                : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], []))
                                            : ResultFactory.Create<(Point3d[], Point3d[], double[])>(value: ([], [], [])),
                                        _ => ResultFactory.Create<(Point3d[], Point3d[], double[])>(error: E.Geometry.NearMissSearchFailed),
                                    };
                                }));
                    });
    }

    /// <summary>Analyzes intersection stability using spherical perturbation sampling and count variation.</summary>
    [Pure]
    internal static Result<(double Score, double Sensitivity, bool[] UnstableFlags)> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersection.IntersectionOutput baseOutput, IGeometryContext context) =>
        baseOutput.Points.Count switch {
            0 => ResultFactory.Create<(double, double, bool[])>(value: (1.0, 0.0, [])),
            int count => IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                .Bind(entry => {
                    (V modeA, V modeB) = entry.Swapped
                        ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                        : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                    return (modeA == V.None ? ResultFactory.Create(value: geomA) : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                        .Bind(validA => (modeB == V.None ? ResultFactory.Create(value: geomB) : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                            .Bind(validB => IntersectionCore.NormalizeSettings(new Intersection.IntersectionSettings(), context)
                                .Bind(normalized => {
                                    Vector3d[] directions = [.. Enumerable.Range(0, IntersectionConfig.StabilitySampleCount)
                                        .Select(i => {
                                            double theta = RhinoMath.TwoPI * i / IntersectionConfig.GoldenRatio;
                                            double phi = Math.Acos(1.0 - ((2.0 * (i + 0.5)) / IntersectionConfig.StabilitySampleCount));
                                            return new Vector3d(
                                                Math.Cos(theta) * Math.Sin(phi),
                                                Math.Sin(theta) * Math.Sin(phi),
                                                Math.Cos(phi));
                                        }),
                                    ];
                                    double maxDiagonalLength = Math.Max(
                                        validA.GetBoundingBox(accurate: false).Diagonal.Length,
                                        validB.GetBoundingBox(accurate: false).Diagonal.Length);
                                    double perturbationDistance = maxDiagonalLength * IntersectionConfig.StabilityPerturbationFactor;
                                    Result<(double Score, double Sensitivity, bool[] UnstableFlags)> defaultResult = ResultFactory.Create<(double Score, double Sensitivity, bool[] UnstableFlags)>(value: (1.0, 0.0, [.. Enumerable.Repeat(element: false, count: count)]));

                                    (double Delta, IDisposable? Resource) perturbAndIntersect(Vector3d direction, GeometryBase original) =>
                                        original switch {
                                            Curve curve when curve.DuplicateCurve() is Curve copy && copy.Translate(direction * perturbationDistance) =>
                                                IntersectionCore.ResolveStrategy(copy.GetType(), validB.GetType())
                                                    .Bind(stratEntry => IntersectionCore.ExecuteWithSettings(copy, validB, context, normalized, stratEntry.Strategy, stratEntry.Swapped))
                                                    .Map(result => {
                                                        foreach (Curve intersection in result.Curves) {
                                                            intersection?.Dispose();
                                                        }
                                                        return ((double)Math.Abs(result.Points.Count - count), (IDisposable?)copy);
                                                    })
                                                    .Match(onSuccess: tuple => tuple, onFailure: _ => { copy.Dispose(); return (RhinoMath.UnsetValue, null); }),
                                            Surface surface when surface.Duplicate() is Surface copy && copy.Translate(direction * perturbationDistance) =>
                                                IntersectionCore.ResolveStrategy(copy.GetType(), validB.GetType())
                                                    .Bind(stratEntry => IntersectionCore.ExecuteWithSettings(copy, validB, context, normalized, stratEntry.Strategy, stratEntry.Swapped))
                                                    .Map(result => {
                                                        foreach (Curve intersection in result.Curves) {
                                                            intersection?.Dispose();
                                                        }
                                                        return ((double)Math.Abs(result.Points.Count - count), (IDisposable?)copy);
                                                    })
                                                    .Match(onSuccess: tuple => tuple, onFailure: _ => { copy.Dispose(); return (RhinoMath.UnsetValue, null); }),
                                            _ => (RhinoMath.UnsetValue, null),
                                        };

                                    (double Delta, IDisposable? Resource)[] perturbations = [.. directions.Select(dir => perturbAndIntersect(dir, validA))];
                                    try {
                                        double[] filtered = [.. perturbations.Select(entry => entry.Delta).Where(delta => RhinoMath.IsValidDouble(delta))];
                                        return filtered.Length > 0
                                            ? ResultFactory.Create<(double Score, double Sensitivity, bool[] UnstableFlags)>(value: (
                                                Score: 1.0 / (1.0 + filtered.Average()),
                                                Sensitivity: filtered.Max() / count,
                                                UnstableFlags: [.. Enumerable.Range(0, count).Select(index => {
                                                    int start = (int)Math.Round((double)index * filtered.Length / count);
                                                    int end = (int)Math.Round((double)(index + 1) * filtered.Length / count);
                                                    return filtered.Skip(start).Take(end - start).Any(delta => delta > IntersectionConfig.UnstableCountDeltaThreshold);
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
