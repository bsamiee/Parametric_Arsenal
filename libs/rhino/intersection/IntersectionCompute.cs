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
    [Pure]
    private static Result<IntersectionConfig.IntersectionOperationMetadata> ResolveMetadata(Type typeA, Type typeB) {
        (Type[] chainA, Type[] chainB) = (IntersectionConfig.GetTypeChain(typeA), IntersectionConfig.GetTypeChain(typeB));
        return chainA.SelectMany(a => chainB.Select(b => (a, b))).Concat(chainB.SelectMany(a => chainA.Select(b => (a, b))))
            .Select(key => (IntersectionConfig.Operations.TryGetValue(key, out IntersectionConfig.IntersectionOperationMetadata? m), key, m))
            .FirstOrDefault(x => x.Item1) switch {
                (true, _, IntersectionConfig.IntersectionOperationMetadata metadata) => ResultFactory.Create(value: metadata),
                _ => ResultFactory.Create<IntersectionConfig.IntersectionOperationMetadata>(error: E.Geometry.UnsupportedIntersection.WithContext($"{typeA.Name} × {typeB.Name}")),
            };
    }

    /// <summary>Classifies intersection type using tangent angle analysis and circular mean calculation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.ClassificationResult> Classify(Intersection.IntersectionResult result, GeometryBase geomA, GeometryBase geomB, IGeometryContext context) {
        Intersection.ClassificationType toType(int code) => code switch { 0 => new Intersection.TangentClassification(), 1 => new Intersection.TransverseClassification(), _ => new Intersection.UnknownClassification(), };

        Result<Intersection.ClassificationResult> curveSurfaceClassifier(double[] angles) {
            double averageDeviation = angles.Sum(static angle => Math.Abs(RhinoMath.HalfPI - angle)) / angles.Length;
            bool grazing = angles.Any(static angle => Math.Abs(RhinoMath.HalfPI - angle) <= IntersectionConfig.GrazingAngleThreshold);
            bool tangent = averageDeviation <= IntersectionConfig.TangentAngleThreshold;
            return ResultFactory.Create(value: new Intersection.ClassificationResult(
                Type: toType(tangent ? 0 : 1),
                ApproachAngles: angles,
                IsGrazing: grazing,
                BlendScore: tangent ? IntersectionConfig.CurveSurfaceTangentBlendScore : IntersectionConfig.CurveSurfacePerpendicularBlendScore));
        }

        Result<T> validate<T>(T geometry, IGeometryContext ctx, V mode) where T : notnull =>
            mode == V.None ? ResultFactory.Create(value: geometry) : ResultFactory.Create(value: geometry).Validate(args: [ctx, mode,]);

        Result<Intersection.ClassificationResult> computeCurveSurfaceAngles(Curve curve, Surface surface, int count, double[] parameters) =>
            Enumerable.Range(0, count)
                .Select(idx => (Tangent: curve.TangentAt(parameters[idx]), Point: result.Points[idx]))
                .Select(tuple => tuple.Tangent.IsValid && surface.ClosestPoint(tuple.Point, out double u, out double v) && surface.NormalAt(u, v) is Vector3d normal && normal.IsValid
                    ? Vector3d.VectorAngle(tuple.Tangent, normal)
                    : RhinoMath.UnsetValue)
                .Where(static angle => RhinoMath.IsValidDouble(angle))
                .ToArray() is double[] angles && angles.Length > 0
                    ? curveSurfaceClassifier(angles)
                    : ResultFactory.Create<Intersection.ClassificationResult>(error: E.Geometry.ClassificationFailed);

        return ResolveMetadata(geomA.GetType(), geomB.GetType())
            .Bind(metadata => {
                (V modeA, V modeB) = (metadata.ModeA, metadata.ModeB);
                return validate(geomA, context, modeA)
                    .Bind(validA => validate(geomB, context, modeB)
                        .Bind(validB => (result.Points.Count, result.ParametersA.Count, result.ParametersB.Count) switch {
                            (0, _, _) => ResultFactory.Create<Intersection.ClassificationResult>(error: E.Geometry.InsufficientIntersectionData),
                            (int count, int parametersA, int parametersB) => (validA, validB) switch {
                                (Curve curveA, Curve curveB) when parametersA >= count && parametersB >= count => Enumerable.Range(0, count)
                                    .Select(idx => (curveA.TangentAt(result.ParametersA[idx]), curveB.TangentAt(result.ParametersB[idx])) is (Vector3d tA, Vector3d tB) && tA.IsValid && tB.IsValid
                                        ? Vector3d.VectorAngle(tA, tB)
                                        : RhinoMath.UnsetValue)
                                    .Where(static angle => RhinoMath.IsValidDouble(angle))
                                    .ToArray() is double[] angles && angles.Length > 0 && Math.Atan2(angles.Sum(Math.Sin) / angles.Length, angles.Sum(Math.Cos) / angles.Length) is double circularMean && RhinoMath.Wrap(circularMean, 0.0, RhinoMath.TwoPI) is double averageAngle
                                        ? ResultFactory.Create(value: new Intersection.ClassificationResult(
                                            Type: toType(averageAngle < IntersectionConfig.TangentAngleThreshold ? 0 : 1),
                                            ApproachAngles: angles,
                                            IsGrazing: angles.Any(static angle => angle < IntersectionConfig.GrazingAngleThreshold),
                                            BlendScore: averageAngle < IntersectionConfig.TangentAngleThreshold ? IntersectionConfig.TangentBlendScore : IntersectionConfig.PerpendicularBlendScore))
                                        : ResultFactory.Create<Intersection.ClassificationResult>(error: E.Geometry.ClassificationFailed),
                                (Curve curve, Surface surface) when parametersA >= count => computeCurveSurfaceAngles(curve, surface, count, [.. result.ParametersA,]),
                                (Surface surface, Curve curve) when parametersB >= count => computeCurveSurfaceAngles(curve, surface, count, [.. result.ParametersB,]),
                                _ when parametersA < count || parametersB < count => ResultFactory.Create<Intersection.ClassificationResult>(error: E.Geometry.InsufficientIntersectionData),
                                _ => ResultFactory.Create(value: new Intersection.ClassificationResult(toType(2), [], false, 0.0)),
                            },
                        }));
            });
    }

    /// <summary>Finds near-miss locations between geometries within search radius using closest point sampling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.NearMissResult> FindNearMisses(GeometryBase geomA, GeometryBase geomB, double searchRadius, IGeometryContext context) {
        Result<T> validate<T>(T geometry, IGeometryContext ctx, V mode) where T : notnull =>
            mode == V.None ? ResultFactory.Create(value: geometry) : ResultFactory.Create(value: geometry).Validate(args: [ctx, mode,]);

        Intersection.NearMissResult toArrays((Point3d PointA, Point3d PointB, double Distance)[] pairs) {
            int count = pairs.Length;
            Point3d[] pointsA = new Point3d[count];
            Point3d[] pointsB = new Point3d[count];
            double[] distances = new double[count];
            for (int i = 0; i < count; i++) {
                pointsA[i] = pairs[i].PointA;
                pointsB[i] = pairs[i].PointB;
                distances[i] = pairs[i].Distance;
            }
            return new(pointsA, pointsB, distances);
        }

        double minDistance = context.AbsoluteTolerance * IntersectionConfig.NearMissToleranceMultiplier;
        return searchRadius <= minDistance
            ? ResultFactory.Create<Intersection.NearMissResult>(error: E.Geometry.InvalidSearchRadius.WithContext(string.Create(CultureInfo.InvariantCulture, $"SearchRadius must exceed tolerance * {IntersectionConfig.NearMissToleranceMultiplier}")))
            : ResolveMetadata(geomA.GetType(), geomB.GetType())
                .Bind(metadata => {
                    (V modeA, V modeB) = (metadata.ModeA, metadata.ModeB);
                    return validate(geomA, context, modeA)
                        .Bind(validA => validate(geomB, context, modeB)
                            .Bind(validB => {
                                (GeometryBase primary, GeometryBase secondary) = (validA, validB) switch {
                                    (Curve c, Surface s) => (c, s),
                                    (Surface s, Curve c) => (c, s),
                                    _ => (validA, validB),
                                };
                                return (primary, secondary) switch {
                                    (Curve curveA, Curve curveB) => Math.Max(IntersectionConfig.MinCurveNearMissSamples, (int)Math.Ceiling(curveA.GetLength() / searchRadius)) is int samples
                                        ? Enumerable.Range(0, samples)
                                            .Select(idx => curveA.PointAt(curveA.Domain.ParameterAt(idx / (double)(samples - 1))))
                                            .Select(pt => curveB.ClosestPoint(pt, out double t) && curveB.PointAt(t) is Point3d cb
                                                ? (PointA: pt, PointB: cb, Distance: pt.DistanceTo(cb))
                                                : (PointA: pt, PointB: Point3d.Unset, Distance: double.MaxValue))
                                            .Where(c => c.Distance < searchRadius && c.Distance > minDistance)
                                            .Concat(Enumerable.Range(0, samples)
                                                .Select(idx => curveB.PointAt(curveB.Domain.ParameterAt(idx / (double)(samples - 1))))
                                                .Select(pt => curveA.ClosestPoint(pt, out double t) && curveA.PointAt(t) is Point3d ca
                                                    ? (PointA: ca, PointB: pt, Distance: ca.DistanceTo(pt))
                                                    : (PointA: Point3d.Unset, PointB: pt, Distance: double.MaxValue))
                                                .Where(c => c.Distance < searchRadius && c.Distance > minDistance))
                                            .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] curvePairs && curvePairs.Length > 0
                                            ? ResultFactory.Create(value: toArrays(curvePairs))
                                            : ResultFactory.Create(value: new Intersection.NearMissResult([], [], []))
                                        : ResultFactory.Create(value: new Intersection.NearMissResult([], [], [])),
                                    (Curve curve, Surface surface) => Math.Max(IntersectionConfig.MinCurveNearMissSamples, (int)Math.Ceiling(curve.GetLength() / searchRadius)) is int samples
                                        ? Enumerable.Range(0, samples)
                                            .Select(idx => curve.PointAt(curve.Domain.ParameterAt(idx / (double)(samples - 1))))
                                            .Select(pt => surface.ClosestPoint(pt, out double u, out double v)
                                                ? (PointA: pt, PointB: surface.PointAt(u, v), Distance: pt.DistanceTo(surface.PointAt(u, v)))
                                                : (PointA: pt, PointB: Point3d.Unset, Distance: double.MaxValue))
                                            .Where(c => c.Distance < searchRadius && c.Distance > minDistance)
                                            .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] pairs && pairs.Length > 0
                                            ? ResultFactory.Create(value: toArrays(pairs))
                                            : ResultFactory.Create(value: new Intersection.NearMissResult([], [], []))
                                        : ResultFactory.Create(value: new Intersection.NearMissResult([], [], [])),
                                    (Brep brepA, Brep brepB) => Math.Max(IntersectionConfig.MinBrepNearMissSamples, (int)Math.Ceiling(brepA.GetBoundingBox(accurate: false).Diagonal.Length / searchRadius)) is int samples && brepA.GetBoundingBox(accurate: false) is BoundingBox bbox
                                        ? Enumerable.Range(0, samples * samples)
                                            .Select(idx => new Point3d(bbox.Min.X + ((bbox.Max.X - bbox.Min.X) * (idx % samples) / (samples - 1)), bbox.Min.Y + ((bbox.Max.Y - bbox.Min.Y) * (idx / samples) / (samples - 1)), (bbox.Min.Z + bbox.Max.Z) / 2.0))
                                            .Select(pt => brepA.ClosestPoint(pt) is Point3d ca && brepB.ClosestPoint(ca) is Point3d cb
                                                ? (PointA: ca, PointB: cb, Distance: ca.DistanceTo(cb))
                                                : (PointA: Point3d.Unset, PointB: Point3d.Unset, Distance: double.MaxValue))
                                            .Where(c => (c.Distance < searchRadius) && (c.Distance > minDistance) && c.PointA.IsValid && c.PointB.IsValid)
                                            .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] brepPairs && brepPairs.Length > 0
                                            ? ResultFactory.Create(value: toArrays(brepPairs))
                                            : ResultFactory.Create(value: new Intersection.NearMissResult([], [], []))
                                        : ResultFactory.Create(value: new Intersection.NearMissResult([], [], [])),
                                    (Mesh meshA, Mesh meshB) => (meshA.Vertices.Count > 0) && (meshB.Vertices.Count > 0) && Math.Min(meshA.Vertices.Count, IntersectionConfig.MaxNearMissSamples) is int samplesA && Math.Min(meshB.Vertices.Count, IntersectionConfig.MaxNearMissSamples) is int samplesB
                                        ? Enumerable.Range(0, samplesA)
                                            .Select(idx => new Point3d(meshA.Vertices[idx * meshA.Vertices.Count / samplesA]))
                                            .Select(pt => meshB.ClosestMeshPoint(pt, searchRadius) is MeshPoint mp && mp.Point.IsValid
                                                ? (PointA: pt, PointB: mp.Point, Distance: pt.DistanceTo(mp.Point))
                                                : (PointA: Point3d.Unset, PointB: Point3d.Unset, Distance: double.MaxValue))
                                            .Concat(Enumerable.Range(0, samplesB)
                                                .Select(idx => new Point3d(meshB.Vertices[idx * meshB.Vertices.Count / samplesB]))
                                                .Select(pt => meshA.ClosestMeshPoint(pt, searchRadius) is MeshPoint mp && mp.Point.IsValid
                                                    ? (PointA: mp.Point, PointB: pt, Distance: mp.Point.DistanceTo(pt))
                                                    : (PointA: Point3d.Unset, PointB: Point3d.Unset, Distance: double.MaxValue)))
                                            .Where(c => (c.Distance < searchRadius) && (c.Distance > minDistance) && c.PointA.IsValid && c.PointB.IsValid)
                                            .ToArray() is (Point3d PointA, Point3d PointB, double Distance)[] meshPairs && meshPairs.Length > 0
                                            ? ResultFactory.Create(value: toArrays(meshPairs))
                                            : ResultFactory.Create(value: new Intersection.NearMissResult([], [], []))
                                        : ResultFactory.Create(value: new Intersection.NearMissResult([], [], [])),
                                    _ => ResultFactory.Create<Intersection.NearMissResult>(error: E.Geometry.NearMissSearchFailed),
                                };
                            }));
                });
    }

    /// <summary>Analyzes intersection stability using spherical perturbation sampling and count variation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Intersection.StabilityResult> AnalyzeStability(GeometryBase geomA, GeometryBase geomB, Intersection.IntersectionResult baseResult, IGeometryContext context) {
        Result<T> validate<T>(T geometry, IGeometryContext ctx, V mode) where T : notnull =>
            mode == V.None ? ResultFactory.Create(value: geometry) : ResultFactory.Create(value: geometry).Validate(args: [ctx, mode,]);

        return baseResult.Points.Count switch {
            0 => ResultFactory.Create(value: new Intersection.StabilityResult(1.0, 0.0, [])),
            int count => ResolveMetadata(geomA.GetType(), geomB.GetType())
                .Bind(metadata => {
                    (V modeA, V modeB) = (metadata.ModeA, metadata.ModeB);
                    return validate(geomA, context, modeA)
                        .Bind(validA => validate(geomB, context, modeB)
                            .Bind(validB => {
                                int sqrtSamples = (int)Math.Ceiling(Math.Sqrt(IntersectionConfig.StabilitySampleCount));
                                (int phiSteps, int thetaSteps) = (sqrtSamples, (int)Math.Ceiling(IntersectionConfig.StabilitySampleCount / (double)sqrtSamples));
                                Vector3d[] directions = [.. Enumerable.Range(0, phiSteps)
                                    .SelectMany(pi => Enumerable.Range(0, thetaSteps)
                                        .Select(ti => {
                                            (double phi, double theta) = ((Math.PI * pi) / phiSteps, (RhinoMath.TwoPI * ti) / thetaSteps);
                                            return new Vector3d(Math.Sin(phi) * Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi));
                                        }))
                                    .Take(IntersectionConfig.StabilitySampleCount),];
                                double perturbationDistance = validA.GetBoundingBox(accurate: false).Diagonal.Length * IntersectionConfig.StabilityPerturbationFactor;
                                Intersection.StabilityResult defaultResult = new(1.0, 0.0, [.. Enumerable.Repeat(element: false, count: count),]);

                                (double Delta, IDisposable? Resource) perturbAndIntersect(Vector3d direction, GeometryBase original) =>
                                    original switch {
                                        Curve curve when curve.DuplicateCurve() is Curve copy && copy.Translate(direction * perturbationDistance) =>
                                            IntersectionCore.Execute(copy, validB, context, new Intersection.StandardMode())
                                                .Map(r => { foreach (Curve c in r.Curves) { c?.Dispose(); } return ((double)Math.Abs(r.Points.Count - count), (IDisposable?)copy); })
                                                .Match(onSuccess: static t => t, onFailure: _ => { copy.Dispose(); return (RhinoMath.UnsetValue, null); }),
                                        Surface surface when surface.Duplicate() is Surface copy && copy.Translate(direction * perturbationDistance) =>
                                            IntersectionCore.Execute(copy, validB, context, new Intersection.StandardMode())
                                                .Map(r => { foreach (Curve c in r.Curves) { c?.Dispose(); } return ((double)Math.Abs(r.Points.Count - count), (IDisposable?)copy); })
                                                .Match(onSuccess: static t => t, onFailure: _ => { copy.Dispose(); return (RhinoMath.UnsetValue, null); }),
                                        _ => (RhinoMath.UnsetValue, null),
                                    };

                                (double Delta, IDisposable? Resource)[] perturbations = [.. directions.Select(dir => perturbAndIntersect(dir, validA)),];
                                try {
                                    double[] filtered = [.. perturbations.Select(static e => e.Delta).Where(static d => RhinoMath.IsValidDouble(d)),];
                                    return filtered.Length > 0
                                        ? ResultFactory.Create(value: new Intersection.StabilityResult(
                                            Score: 1.0 / (1.0 + filtered.Average()),
                                            Sensitivity: filtered.Max() / count,
                                            UnstableFlags: [.. Enumerable.Range(0, count).Select(idx => {
                                                int start = (int)Math.Round((double)idx * filtered.Length / count);
                                                int end = (int)Math.Round((double)(idx + 1) * filtered.Length / count);
                                                return filtered.Skip(start).Take(end - start).Any(static d => d > 1.0);
                                            }),]))
                                        : ResultFactory.Create(value: defaultResult);
                                } finally {
                                    foreach ((double _, IDisposable? Resource) in perturbations) {
                                        Resource?.Dispose();
                                    }
                                }
                            }));
                }),
        };
    }
}
