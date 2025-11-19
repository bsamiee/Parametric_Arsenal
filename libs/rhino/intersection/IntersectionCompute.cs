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
    internal static Result<Intersect.ClassificationResult> Classify(Intersect.IntersectionOutput output, GeometryBase geomA, GeometryBase geomB, IGeometryContext context) =>
        IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
            .Bind(entry => {
                (V modeA, V modeB) = entry.Swapped
                    ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                    : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                return (modeA == V.None ? ResultFactory.Create(value: geomA) : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                    .Bind(validA => (modeB == V.None ? ResultFactory.Create(value: geomB) : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                        .Bind(validB => (output.Points.Count, output.ParametersA.Count, output.ParametersB.Count) switch {
                            (0, _, _) => ResultFactory.Create<Intersect.ClassificationResult>(error: E.Geometry.InsufficientIntersectionData),
                            (int count, int parametersA, int parametersB) => (validA, validB) switch {
                                (Curve curveA, Curve curveB) when parametersA >= count && parametersB >= count => Enumerable.Range(0, count)
                                    .Select(index => (curveA.TangentAt(output.ParametersA[index]), curveB.TangentAt(output.ParametersB[index])) is (Vector3d tangentA, Vector3d tangentB) && tangentA.IsValid && tangentB.IsValid
                                        ? Vector3d.VectorAngle(tangentA, tangentB)
                                        : RhinoMath.UnsetValue)
                                    .Where(static angle => RhinoMath.IsValidDouble(angle))
                                    .ToArray() is double[] angles && angles.Length > 0 && Math.Atan2(angles.Sum(Math.Sin) / angles.Length, angles.Sum(Math.Cos) / angles.Length) is double circularMean && RhinoMath.Wrap(circularMean, 0.0, RhinoMath.TwoPI) is double averageAngle
                                        ? ResultFactory.Create(value: new Intersect.ClassificationResult(
                                            Type: averageAngle < IntersectionConfig.Classification.TangentAngleThreshold ? Intersect.ClassificationType.Tangent : Intersect.ClassificationType.Transverse,
                                            ApproachAngles: angles,
                                            IsGrazing: angles.Any(angle => angle < IntersectionConfig.Classification.GrazingAngleThreshold),
                                            BlendScore: averageAngle < IntersectionConfig.Classification.TangentAngleThreshold ? IntersectionConfig.Classification.TangentBlendScore : IntersectionConfig.Classification.PerpendicularBlendScore))
                                        : ResultFactory.Create<Intersect.ClassificationResult>(error: E.Geometry.ClassificationFailed),
                                (Curve curve, Surface surface) when parametersA >= count => Enumerable.Range(0, count)
                                    .Select(index => (Tangent: curve.TangentAt(output.ParametersA[index]), Point: output.Points[index]))
                                    .Select(tuple => tuple.Tangent.IsValid && surface.ClosestPoint(tuple.Point, out double u, out double v) && surface.NormalAt(u, v) is Vector3d normal && normal.IsValid
                                        ? Vector3d.VectorAngle(tuple.Tangent, normal)
                                        : RhinoMath.UnsetValue)
                                    .Where(static angle => RhinoMath.IsValidDouble(angle))
                                    .ToArray() is double[] angles && angles.Length > 0
                                        ? angles.Sum(static angle => Math.Abs(RhinoMath.HalfPI - angle)) / angles.Length is double averageDeviation
                                            ? ResultFactory.Create(value: new Intersect.ClassificationResult(
                                                Type: averageDeviation <= IntersectionConfig.Classification.TangentAngleThreshold ? Intersect.ClassificationType.Tangent : Intersect.ClassificationType.Transverse,
                                                ApproachAngles: angles,
                                                IsGrazing: angles.Any(angle => Math.Abs(RhinoMath.HalfPI - angle) <= IntersectionConfig.Classification.GrazingAngleThreshold),
                                                BlendScore: averageDeviation <= IntersectionConfig.Classification.TangentAngleThreshold ? IntersectionConfig.Classification.CurveSurfaceTangentBlendScore : IntersectionConfig.Classification.CurveSurfacePerpendicularBlendScore))
                                            : ResultFactory.Create<Intersect.ClassificationResult>(error: E.Geometry.ClassificationFailed)
                                        : ResultFactory.Create<Intersect.ClassificationResult>(error: E.Geometry.ClassificationFailed),
                                (Surface surface, Curve curve) when parametersB >= count => Enumerable.Range(0, count)
                                    .Select(index => (Tangent: curve.TangentAt(output.ParametersB[index]), Point: output.Points[index]))
                                    .Select(tuple => tuple.Tangent.IsValid && surface.ClosestPoint(tuple.Point, out double u, out double v) && surface.NormalAt(u, v) is Vector3d normal && normal.IsValid
                                        ? Vector3d.VectorAngle(tuple.Tangent, normal)
                                        : RhinoMath.UnsetValue)
                                    .Where(static angle => RhinoMath.IsValidDouble(angle))
                                    .ToArray() is double[] angles && angles.Length > 0
                                        ? angles.Sum(static angle => Math.Abs(RhinoMath.HalfPI - angle)) / angles.Length is double averageDeviation
                                            ? ResultFactory.Create(value: new Intersect.ClassificationResult(
                                                Type: averageDeviation <= IntersectionConfig.Classification.TangentAngleThreshold ? Intersect.ClassificationType.Tangent : Intersect.ClassificationType.Transverse,
                                                ApproachAngles: angles,
                                                IsGrazing: angles.Any(angle => Math.Abs(RhinoMath.HalfPI - angle) <= IntersectionConfig.Classification.GrazingAngleThreshold),
                                                BlendScore: averageDeviation <= IntersectionConfig.Classification.TangentAngleThreshold ? IntersectionConfig.Classification.CurveSurfaceTangentBlendScore : IntersectionConfig.Classification.CurveSurfacePerpendicularBlendScore))
                                            : ResultFactory.Create<Intersect.ClassificationResult>(error: E.Geometry.ClassificationFailed)
                                        : ResultFactory.Create<Intersect.ClassificationResult>(error: E.Geometry.ClassificationFailed),
                                _ when parametersA < count || parametersB < count => ResultFactory.Create<Intersect.ClassificationResult>(error: E.Geometry.InsufficientIntersectionData),
                                _ => ResultFactory.Create(value: new Intersect.ClassificationResult(Intersect.ClassificationType.Unknown, [], false, 0.0)),
                            },
                        }));
            });

    /// <summary>Finds near-miss locations between geometries within search radius using closest point sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersect.NearMissResult> FindNearMisses(GeometryBase geomA, GeometryBase geomB, double searchRadius, IGeometryContext context) {
        double minDistance = context.AbsoluteTolerance * IntersectionConfig.NearMiss.ToleranceMultiplier;
        return searchRadius <= minDistance
            ? ResultFactory.Create<Intersect.NearMissResult>(error: E.Geometry.InvalidSearchRadius.WithContext(string.Create(CultureInfo.InvariantCulture, $"SearchRadius must exceed tolerance * {IntersectionConfig.NearMiss.ToleranceMultiplier}")))
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
                                    (Curve curveA, Curve curveB) => Math.Max(IntersectionConfig.NearMiss.MinCurveSamples, (int)Math.Ceiling(curveA.GetLength() / searchRadius)) is int samples
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
                                            ? ResultFactory.Create(value: new Intersect.NearMissResult(
                                                [.. curvePairs.Select(static p => p.PointA),],
                                                [.. curvePairs.Select(static p => p.PointB),],
                                                [.. curvePairs.Select(static p => p.Distance),]))
                                            : ResultFactory.Create(value: Intersect.NearMissResult.Empty)
                                        : ResultFactory.Create(value: Intersect.NearMissResult.Empty),
                                    (Curve curve, Surface surface) => Math.Max(IntersectionConfig.NearMiss.MinCurveSamples, (int)Math.Ceiling(curve.GetLength() / searchRadius)) is int samples
                                        ? Enumerable.Range(0, samples)
                                            .Select(index => curve.PointAt(curve.Domain.ParameterAt(index / (double)(samples - 1))))
                                            .Select(point => surface.ClosestPoint(point, out double u, out double v)
                                                ? (PointA: point, PointB: surface.PointAt(u, v), Distance: point.DistanceTo(surface.PointAt(u, v)))
                                                : (PointA: point, PointB: Point3d.Unset, Distance: double.MaxValue))
                                            .Where(candidate => candidate.Distance < searchRadius && candidate.Distance > minDistance)
                                            .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] pairs && pairs.Length > 0
                                            ? ResultFactory.Create(value: new Intersect.NearMissResult(
                                                [.. pairs.Select(static p => p.PointA),],
                                                [.. pairs.Select(static p => p.PointB),],
                                                [.. pairs.Select(static p => p.Distance),]))
                                            : ResultFactory.Create(value: Intersect.NearMissResult.Empty)
                                        : ResultFactory.Create(value: Intersect.NearMissResult.Empty),
                                    (Brep brepA, Brep brepB) => Math.Max(IntersectionConfig.NearMiss.MinBrepSamples, (int)Math.Ceiling(brepA.GetBoundingBox(accurate: false).Diagonal.Length / searchRadius)) is int samples && brepA.GetBoundingBox(accurate: false) is BoundingBox bbox
                                        ? Enumerable.Range(0, samples * samples)
                                            .Select(index => new Point3d(
                                                bbox.Min.X + ((bbox.Max.X - bbox.Min.X) * (index % samples) / (samples - 1)),
                                                bbox.Min.Y + ((bbox.Max.Y - bbox.Min.Y) * (index / samples) / (samples - 1)),
                                                (bbox.Min.Z + bbox.Max.Z) / 2.0))
                                            .Select(point => brepA.ClosestPoint(point) is Point3d closestA && brepB.ClosestPoint(closestA) is Point3d closestB
                                                ? (PointA: closestA, PointB: closestB, Distance: closestA.DistanceTo(closestB))
                                                : (PointA: Point3d.Unset, PointB: Point3d.Unset, Distance: double.MaxValue))
                                            .Where(candidate => (candidate.Distance < searchRadius) && (candidate.Distance > minDistance) && candidate.PointA.IsValid && candidate.PointB.IsValid)
                                            .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] brepPairs && brepPairs.Length > 0
                                            ? ResultFactory.Create(value: new Intersect.NearMissResult(
                                                [.. brepPairs.Select(static p => p.PointA),],
                                                [.. brepPairs.Select(static p => p.PointB),],
                                                [.. brepPairs.Select(static p => p.Distance),]))
                                            : ResultFactory.Create(value: Intersect.NearMissResult.Empty)
                                        : ResultFactory.Create(value: Intersect.NearMissResult.Empty),
                                    (Mesh meshA, Mesh meshB) => (meshA.Vertices.Count > 0)
                                            && (meshB.Vertices.Count > 0)
                                            && Math.Min(meshA.Vertices.Count, IntersectionConfig.NearMiss.MaxVertexSamples) is int samplesA
                                            && Math.Min(meshB.Vertices.Count, IntersectionConfig.NearMiss.MaxVertexSamples) is int samplesB
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
                                            ? ResultFactory.Create(value: new Intersect.NearMissResult(
                                                [.. meshPairs.Select(static p => p.PointA),],
                                                [.. meshPairs.Select(static p => p.PointB),],
                                                [.. meshPairs.Select(static p => p.Distance),]))
                                            : ResultFactory.Create(value: Intersect.NearMissResult.Empty)
                                        : ResultFactory.Create(value: Intersect.NearMissResult.Empty),
                                    _ => ResultFactory.Create<Intersect.NearMissResult>(error: E.Geometry.NearMissSearchFailed),
                                };
                            }));
                });
    }

    /// <summary>Analyzes intersection stability using spherical perturbation sampling and count variation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersect.StabilityResult> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersect.IntersectionOutput baseOutput, IGeometryContext context) =>
        baseOutput.Points.Count switch {
            0 => ResultFactory.Create(value: Intersect.StabilityResult.Stable),
            int count => IntersectionCore.ResolveStrategy(geomA.GetType(), geomB.GetType())
                .Bind(entry => {
                    (V modeA, V modeB) = entry.Swapped
                        ? (entry.Strategy.ModeB, entry.Strategy.ModeA)
                        : (entry.Strategy.ModeA, entry.Strategy.ModeB);

                    return (modeA == V.None ? ResultFactory.Create(value: geomA) : ResultFactory.Create(value: geomA).Validate(args: [context, modeA,]))
                        .Bind(validA => (modeB == V.None ? ResultFactory.Create(value: geomB) : ResultFactory.Create(value: geomB).Validate(args: [context, modeB,]))
                            .Bind(validB => IntersectionCore.NormalizeOptions(new Intersect.IntersectionOptions(), context)
                                .Bind(normalized => {
                                    int sqrtSamples = (int)Math.Ceiling(Math.Sqrt(IntersectionConfig.Stability.SampleCount));
                                    (int phiSteps, int thetaSteps) = (sqrtSamples, (int)Math.Ceiling(IntersectionConfig.Stability.SampleCount / (double)sqrtSamples));
                                    Vector3d[] directions = [.. Enumerable.Range(0, phiSteps)
                                        .SelectMany(phiIndex => Enumerable.Range(0, thetaSteps)
                                            .Select(thetaIndex => {
                                                (double phi, double theta) = ((Math.PI * phiIndex) / phiSteps, (RhinoMath.TwoPI * thetaIndex) / thetaSteps);
                                                return new Vector3d(Math.Sin(phi) * Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi));
                                            }))
                                        .Take(IntersectionConfig.Stability.SampleCount),
                                    ];
                                    double perturbationDistance = validA.GetBoundingBox(accurate: false).Diagonal.Length * IntersectionConfig.Stability.PerturbationFactor;

                                    (double Delta, IDisposable? Resource)[] perturbations = [.. directions.Select(direction => validA switch {
                                        Curve curve when curve.DuplicateCurve() is Curve copy && copy.Translate(direction * perturbationDistance) =>
                                            IntersectionCore.ExecuteWithOptions(copy, validB, context, normalized)
                                                .Map(result => {
                                                    foreach (Curve intersection in result.Curves) {
                                                        intersection?.Dispose();
                                                    }
                                                    return ((double)Math.Abs(result.Points.Count - count), (IDisposable?)copy);
                                                })
                                                .Match(onSuccess: tuple => tuple, onFailure: _ => { copy.Dispose(); return (RhinoMath.UnsetValue, null); }),
                                        Surface surface when surface.Duplicate() is Surface copy && copy.Translate(direction * perturbationDistance) =>
                                            IntersectionCore.ExecuteWithOptions(copy, validB, context, normalized)
                                                .Map(result => {
                                                    foreach (Curve intersection in result.Curves) {
                                                        intersection?.Dispose();
                                                    }
                                                    return ((double)Math.Abs(result.Points.Count - count), (IDisposable?)copy);
                                                })
                                                .Match(onSuccess: tuple => tuple, onFailure: _ => { copy.Dispose(); return (RhinoMath.UnsetValue, null); }),
                                        _ => (RhinoMath.UnsetValue, null),
                                    }),];

                                    try {
                                        double[] filtered = [.. perturbations.Select(static entry => entry.Delta).Where(static delta => RhinoMath.IsValidDouble(delta)),];
                                        return filtered.Length > 0
                                            ? ResultFactory.Create(value: new Intersect.StabilityResult(
                                                Score: 1.0 / (1.0 + filtered.Average()),
                                                Sensitivity: filtered.Max() / count,
                                                UnstableFlags: [.. Enumerable.Range(0, count).Select(index => {
                                                    int start = (int)Math.Round((double)index * filtered.Length / count);
                                                    int end = (int)Math.Round((double)(index + 1) * filtered.Length / count);
                                                    return filtered.Skip(start).Take(end - start).Any(static delta => delta > 1.0);
                                                }),]))
                                            : ResultFactory.Create(value: new Intersect.StabilityResult(Score: 1.0, Sensitivity: 0.0, UnstableFlags: [.. Enumerable.Repeat(element: false, count: count),]));
                                    } finally {
                                        foreach ((double _, IDisposable? Resource) in perturbations) {
                                            Resource?.Dispose();
                                        }
                                    }
                                })));
                }),
        };
}
