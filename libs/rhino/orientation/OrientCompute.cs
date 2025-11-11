using System;
using System.Diagnostics.Contracts;
using System.Linq;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Optimization, relative orientation, pattern alignment algorithms.</summary>
internal static class OrientCompute {
    [Pure]
    internal static Result<(Transform OptimalTransform, double Score, byte[] CriteriaMet)> OptimizeOrientation(
        Brep brep,
        byte criteria,
        double tolerance,
        IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, V.Standard | V.Topology | V.BoundingBox | V.MassProperties,])
            .Bind(validBrep => criteria is < 1 or > 4
                ? ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.InvalidOrientationMode.WithContext($"Criteria must be 1-4, got {criteria}"))
                : tolerance <= 0.0
                    ? ResultFactory.Create<(Transform, double, byte[])>(error: E.Validation.ToleranceAbsoluteInvalid)
                    : validBrep.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
                        ? ((Func<Result<(Transform, double, byte[])>>)(() => {
                            using VolumeMassProperties? properties = validBrep.IsSolid && validBrep.IsManifold ? VolumeMassProperties.Compute(validBrep) : null;
                            Plane[] candidates = [
                                new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis),
                                new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis),
                                new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis),
                                new Plane(box.Center, new Vector3d(1, 1, 0) / Math.Sqrt(2), Vector3d.ZAxis),
                                new Plane(box.Center, new Vector3d(1, 0, 1) / Math.Sqrt(2), Vector3d.YAxis),
                                new Plane(box.Center, new Vector3d(0, 1, 1) / Math.Sqrt(2), Vector3d.XAxis),
                            ];

                            (Transform, double, byte[])[] evaluations = [.. candidates.Select(candidate => {
                                Transform transform = Transform.PlaneToPlane(candidate, Plane.WorldXY);
                                using Brep test = (Brep)validBrep.Duplicate();
                                return !test.Transform(transform)
                                    ? (Transform.Identity, 0.0, Array.Empty<byte>())
                                    : test.GetBoundingBox(accurate: true) is BoundingBox testBox && testBox.IsValid
                                        ? (transform, criteria switch {
                                            1 => testBox.Diagonal.Length > tolerance ? 1.0 / testBox.Diagonal.Length : 0.0,
                                            2 => properties is not null && testBox.Diagonal.Length > tolerance ? Math.Max(0.0, 1.0 - (Math.Abs(testBox.Center.Z - properties.Centroid.Z) / testBox.Diagonal.Length)) : 0.0,
                                            3 => testBox.IsDegenerate(tolerance) is int degeneracy ? degeneracy switch {
                                                0 => 0.0,
                                                >= 1 and <= 3 => degeneracy / 3.0,
                                                _ => 0.0,
                                            } : 0.0,
                                            4 => (testBox.Min.Z >= -tolerance ? OrientConfig.OrientationScoreWeight1 : 0.0)
                                                + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? OrientConfig.OrientationScoreWeight2 : 0.0)
                                                + ((testBox.Max.Z - testBox.Min.Z) < (testBox.Diagonal.Length * OrientConfig.LowProfileAspectRatio) ? OrientConfig.OrientationScoreWeight3 : 0.0),
                                            _ => 0.0,
                                        }, criteria is >= 1 and <= 4 ? [criteria,] : Array.Empty<byte>())
                                        : (Transform.Identity, 0.0, Array.Empty<byte>());
                            }),
                            ];

                            return evaluations.MaxBy(result => result.Item2) is (Transform best, double score, byte[] met) && score > 0.0
                                ? ResultFactory.Create(value: (best, score, met))
                                : ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed.WithContext("No valid orientation found"));
                        }))()
                        : ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed.WithContext("Invalid bounding box")));

    [Pure]
    internal static Result<(Transform RelativeTransform, double Twist, double Tilt, byte SymmetryType, byte Relationship)> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        ResultFactory.Create(value: (geometryA, geometryB))
            .Ensure(pair => pair.geometryA is not null && pair.geometryB is not null, error: E.Geometry.OrientationFailed.WithContext("Null geometry"))
            .Bind(pair => OrientCore.ExtractPlane(pair.geometryA!, context)
                .Bind(planeA => OrientCore.ExtractPlane(pair.geometryB!, context)
                    .Bind(planeB => {
                        Transform relative = Transform.PlaneToPlane(planeA, planeB);
                        double twist = Vector3d.VectorAngle(planeA.XAxis, planeB.XAxis);
                        double tilt = Vector3d.VectorAngle(planeA.ZAxis, planeB.ZAxis);

                        Result<byte> symmetry = (pair.geometryA, pair.geometryB) switch {
                            (Brep brepA, Brep brepB) when brepA.Vertices.Count == brepB.Vertices.Count => ((Func<Result<byte>>)(() => {
                                Vector3d originDelta = planeB.Origin - planeA.Origin;
                                Plane primaryMirror = originDelta.Length > OrientConfig.MinVectorLength ? new Plane(planeA.Origin, originDelta) : new Plane(planeA.Origin, planeA.ZAxis);
                                Plane fallbackMirror = new Plane(planeA.Origin, planeA.ZAxis);
                                bool primaryValid = primaryMirror.IsValid;
                                Plane selectedMirror = primaryValid ? primaryMirror : fallbackMirror;

                                bool mirrored = brepA.Vertices
                                    .Select(vertex => {
                                        Point3d reflected = vertex.Location;
                                        reflected.Transform(Transform.Mirror(mirrorPlane: selectedMirror));
                                        return reflected;
                                    })
                                    .All(reflected => brepB.Vertices.Any(target => reflected.DistanceTo(target.Location) < context.AbsoluteTolerance));

                                return ResultFactory.Create(value: mirrored ? (byte)1 : (byte)0);
                            }))(),
                            (Curve curveA, Curve curveB) when curveA.SpanCount == curveB.SpanCount && planeA.ZAxis.IsValid && planeA.ZAxis.Length > context.AbsoluteTolerance => ((Func<Result<byte>>)(() => {
                                bool rotational = Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount)
                                    .Select(index => {
                                        double parameter = curveA.Domain.ParameterAt(index / (double)(OrientConfig.RotationSymmetrySampleCount - 1));
                                        Point3d pointA = curveA.PointAt(parameter);
                                        Point3d pointB = curveB.PointAt(parameter);
                                        Vector3d vectorA = pointA - planeA.Origin;
                                        Vector3d vectorB = pointB - planeA.Origin;
                                        double distanceA = vectorA.Length;
                                        double distanceB = vectorB.Length;
                                        return (distanceA < context.AbsoluteTolerance && distanceB < context.AbsoluteTolerance)
                                            || (Math.Abs(distanceA - distanceB) < context.AbsoluteTolerance && Vector3d.VectorAngle(vectorA, vectorB) < OrientConfig.SymmetryAngleToleranceRadians);
                                    })
                                    .All(result => result);

                                return ResultFactory.Create(value: rotational ? (byte)2 : (byte)0);
                            }))(),
                            _ => ResultFactory.Create(value: (byte)0),
                        };

                        byte relationship = Math.Abs(Vector3d.Multiply(planeA.ZAxis, planeB.ZAxis)) switch {
                            double dot when Math.Abs(dot - 1.0) < context.AbsoluteTolerance => (byte)1,
                            double dot when Math.Abs(dot) < context.AbsoluteTolerance => (byte)2,
                            _ => (byte)3,
                        };

                        return symmetry.Map(symmetryValue => (relative, twist, tilt, symmetryValue, relationship));
                    })));

    [Pure]
    internal static Result<(byte PatternType, Transform[] IdealTransforms, int[] Anomalies, double Deviation)> DetectPattern(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        geometries switch {
            null => ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.InsufficientParameters.WithContext("Geometries array is null")),
            _ => ResultFactory.Create(value: geometries)
                .Ensure(array => array.Length >= OrientConfig.PatternMinInstances, error: E.Geometry.InsufficientParameters.WithContext($"Pattern detection requires at least {OrientConfig.PatternMinInstances} geometries, got {geometries.Length}"))
                .Bind(validGeometries => ((Func<Result<(byte, Transform[], int[], double)>>)(() => {
                    Result<Point3d>[] centroidResults = [.. validGeometries.Select(geometry => OrientCore.ExtractCentroid(geometry, useMassProperties: false, context)),];
                    return centroidResults.All(result => result.IsSuccess)
                        ? centroidResults.Select(result => result.Value).ToArray() is Point3d[] centroids && centroids.Length >= 3
                            ? ((Func<Result<(byte, Transform[], int[], double)>>)(() => {
                                Vector3d[] deltas = centroids.Skip(1).Zip(centroids, (next, current) => next - current).ToArray();
                                double averageLength = deltas.Average(vector => vector.Length);
                                return averageLength > context.AbsoluteTolerance
                                    ? deltas.All(vector => Math.Abs(vector.Length - averageLength) / averageLength < context.AbsoluteTolerance)
                                        ? ResultFactory.Create(value: (
                                            PatternType: (byte)0,
                                            IdealTransforms: [.. Enumerable.Range(0, centroids.Length).Select(index => Transform.Translation(deltas[0] * index)),],
                                            Anomalies: [.. deltas.Select((vector, index) => (vector, index)).Where(pair => Math.Abs(pair.vector.Length - averageLength) / averageLength >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.index),],
                                            Deviation: deltas.Sum(vector => Math.Abs(vector.Length - averageLength)) / centroids.Length))
                                        : new Point3d(centroids.Average(point => point.X), centroids.Average(point => point.Y), centroids.Average(point => point.Z)) is Point3d center && centroids.Select(point => point.DistanceTo(center)).ToArray() is double[] radii && radii.Average() is double averageRadius && averageRadius > context.AbsoluteTolerance && radii.All(radius => Math.Abs(radius - averageRadius) / averageRadius < context.AbsoluteTolerance)
                                            ? ResultFactory.Create(value: (
                                                PatternType: (byte)1,
                                                IdealTransforms: [.. Enumerable.Range(0, centroids.Length).Select(index => Transform.Rotation(2.0 * Math.PI * index / centroids.Length, Vector3d.ZAxis, center)),],
                                                Anomalies: [.. radii.Select((radius, index) => (radius, index)).Where(pair => Math.Abs(pair.radius - averageRadius) / averageRadius >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.index),],
                                                Deviation: radii.Sum(radius => Math.Abs(radius - averageRadius)) / centroids.Length))
                                            : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular"))
                                    : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext("Insufficient valid centroids"));
                            }))()
                            : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext("Insufficient valid centroids"))
                        : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext($"Centroid extraction failed for {centroidResults.Count(result => !result.IsSuccess)} geometries"));
                }))()),
        };
}
