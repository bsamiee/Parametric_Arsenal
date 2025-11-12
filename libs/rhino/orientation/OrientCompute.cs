using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Optimization, relative orientation, pattern alignment algorithms.</summary>
internal static class OrientCompute {
    /// <summary>Optimizes orientation for canonical alignment and stability using specified criteria (1=compact packing, 2=centroid alignment, 3=flatness, 4=canonical position).</summary>
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
                            using VolumeMassProperties? vmp = validBrep.IsSolid && validBrep.IsManifold ? VolumeMassProperties.Compute(validBrep) : null;
                            Plane[] testPlanes = [
                                new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis),
                                new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis),
                                new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis),
                                new Plane(box.Center, new Vector3d(1, 1, 0) / Math.Sqrt(2), Vector3d.ZAxis),
                                new Plane(box.Center, new Vector3d(1, 0, 1) / Math.Sqrt(2), Vector3d.YAxis),
                                new Plane(box.Center, new Vector3d(0, 1, 1) / Math.Sqrt(2), Vector3d.XAxis),
                            ];

                            (Transform, double, byte[])[] results = [.. testPlanes.Select(plane => {
                                Transform xf = Transform.PlaneToPlane(plane, Plane.WorldXY);
                                using Brep test = (Brep)validBrep.Duplicate();
                                return !test.Transform(xf) ? (Transform.Identity, 0.0, Array.Empty<byte>())
                                    : test.GetBoundingBox(accurate: true) is BoundingBox testBox && testBox.IsValid
                                        ? (xf, criteria switch {
                                            1 => testBox.Diagonal.Length > tolerance ? 1.0 / testBox.Diagonal.Length : 0.0,
                                            2 => vmp is not null && testBox.Diagonal.Length > tolerance ? Math.Max(0.0, 1.0 - (Math.Abs(testBox.Center.Z - vmp.Centroid.Z) / testBox.Diagonal.Length)) : 0.0,
                                            3 => ((Math.Abs(testBox.Max.X - testBox.Min.X) <= tolerance ? 1 : 0)
                                                + (Math.Abs(testBox.Max.Y - testBox.Min.Y) <= tolerance ? 1 : 0)
                                                + (Math.Abs(testBox.Max.Z - testBox.Min.Z) <= tolerance ? 1 : 0)) switch {
                                                    0 => 0.0,
                                                    int degeneracy and >= 1 and <= 3 => degeneracy / (double)OrientConfig.MaxDegeneracyDimensions,
                                                    _ => 0.0,
                                                },
                                            4 => (testBox.Min.Z >= -tolerance ? OrientConfig.OrientationScoreWeight1 : 0.0) + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? OrientConfig.OrientationScoreWeight2 : 0.0) + ((testBox.Max.Z - testBox.Min.Z) < (testBox.Diagonal.Length * OrientConfig.LowProfileAspectRatio) ? OrientConfig.OrientationScoreWeight3 : 0.0),
                                            _ => 0.0,
                                        }, criteria is >= 1 and <= 4 ? [criteria,] : Array.Empty<byte>())
                                        : (Transform.Identity, 0.0, Array.Empty<byte>());
                            }),
                            ];

                            return results.MaxBy(r => r.Item2) is (Transform best, double bestScore, byte[] met) && bestScore > 0
                                ? ResultFactory.Create(value: (best, bestScore, met))
                                : ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed.WithContext("No valid orientation found"));
                        }))()
                        : ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed.WithContext("Invalid bounding box")));

    /// <summary>Compute relative orientation between two geometries.</summary>
    [Pure]
    internal static Result<(Transform RelativeTransform, double Twist, double Tilt, byte SymmetryType, byte Relationship)> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) {
        double symmetryTolerance = Math.Max(context.AbsoluteTolerance, OrientConfig.SymmetryTestTolerance);
        double angleTolerance = Math.Max(context.AngleToleranceRadians, OrientConfig.SymmetryTestTolerance);

        return (OrientCore.PlaneExtractors.TryGetValue(geometryA.GetType(), out Func<object, Result<Plane>>? extA),
            OrientCore.PlaneExtractors.TryGetValue(geometryB.GetType(), out Func<object, Result<Plane>>? extB))
                switch {
                    (true, true) when extA!(geometryA) is Result<Plane> ra && extB!(geometryB) is Result<Plane> rb => (ra, rb) switch {
                        (Result<Plane> { IsSuccess: true }, Result<Plane> { IsSuccess: true }) => (ra.Value, rb.Value) is (Plane pa, Plane pb)
                            ? Transform.PlaneToPlane(pa, pb) is Transform xform && Vector3d.VectorAngle(pa.XAxis, pb.XAxis) is double twist && Vector3d.VectorAngle(pa.ZAxis, pb.ZAxis) is double tilt
                                ? ((geometryA, geometryB) switch {
                                    (Brep ba, Brep bb) when ba.Vertices.Count == bb.Vertices.Count => (pb.Origin - pa.Origin).Length > OrientConfig.MinVectorLength
                                        ? new Plane(pa.Origin, pb.Origin - pa.Origin) is Plane mirror && mirror.IsValid
                                            && ba.Vertices.Select(va => {
                                                Point3d reflected = va.Location;
                                                reflected.Transform(Transform.Mirror(mirrorPlane: mirror));
                                                return reflected;
                                            }).ToArray() is Point3d[] reflectedA
                                            && reflectedA.All(ra => bb.Vertices.Any(vb => ra.DistanceTo(vb.Location) < symmetryTolerance))
                                            && bb.Vertices.All(vb => reflectedA.Any(ra => ra.DistanceTo(vb.Location) < symmetryTolerance))
                                                ? (byte)1 : (byte)0
                                        : new Plane(pa.Origin, pa.ZAxis) is Plane mirror2 && mirror2.IsValid
                                            && ba.Vertices.Select(va => {
                                                Point3d reflected = va.Location;
                                                reflected.Transform(Transform.Mirror(mirrorPlane: mirror2));
                                                return reflected;
                                            }).ToArray() is Point3d[] reflectedA2
                                            && reflectedA2.All(ra => bb.Vertices.Any(vb => ra.DistanceTo(vb.Location) < symmetryTolerance))
                                            && bb.Vertices.All(vb => reflectedA2.Any(ra => ra.DistanceTo(vb.Location) < symmetryTolerance))
                                                ? (byte)1 : (byte)0,
                                    (Curve ca, Curve cb) when ca.SpanCount == cb.SpanCount && pa.ZAxis.IsValid && pa.ZAxis.Length > symmetryTolerance => ((Func<byte>)(() => {
                                        Point3d[] samplesA = [.. Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount).Select(i => ca.PointAt(ca.Domain.ParameterAt(i / (double)(OrientConfig.RotationSymmetrySampleCount - 1))))];
                                        Point3d[] samplesB = [.. Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount).Select(i => cb.PointAt(cb.Domain.ParameterAt(i / (double)(OrientConfig.RotationSymmetrySampleCount - 1))))];
                                        int[] testIndices = [0, samplesA.Length / 2, samplesA.Length - 1,];
                                        double[] candidateAngles = [.. testIndices.Select(idx => {
                                            Vector3d vecA = samplesA[idx] - pa.Origin;
                                            Vector3d vecB = samplesB[idx] - pa.Origin;
                                            Vector3d projA = vecA - ((vecA * pa.ZAxis) * pa.ZAxis);
                                            Vector3d projB = vecB - ((vecB * pa.ZAxis) * pa.ZAxis);
                                            return projA.Length < symmetryTolerance || projB.Length < symmetryTolerance
                                                ? double.NaN
                                                : Vector3d.CrossProduct(projA, projB) * pa.ZAxis < 0
                                                    ? -Vector3d.VectorAngle(projA, projB)
                                                    : Vector3d.VectorAngle(projA, projB);
                                        }).Where(a => !double.IsNaN(a)),
                                        ];
                                        return candidateAngles.Length == 0
                                            ? (byte)0
                                            : candidateAngles.All(a => Math.Abs(a - candidateAngles[0]) < Math.Max(context.AngleToleranceRadians, OrientConfig.SymmetryTestTolerance))
                                                && Transform.Rotation(candidateAngles[0], pa.ZAxis, pa.Origin) is Transform rotation
                                                && samplesA.Zip(samplesB, (ptA, ptB) => {
                                                    Point3d rotated = ptA;
                                                    rotated.Transform(rotation);
                                                    return rotated.DistanceTo(ptB);
                                                }).All(dist => dist < symmetryTolerance)
                                                    ? (byte)2 : (byte)0;
                                    }))(),
                                    _ => (byte)0,
                                }, Math.Abs(Vector3d.Multiply(pa.ZAxis, pb.ZAxis)) switch {
                                    double dot when Math.Abs(dot - 1.0) < 1.0 - Math.Cos(angleTolerance) => (byte)1,
                                    double dot when Math.Abs(dot) < Math.Sin(angleTolerance) => (byte)2,
                                    _ => (byte)3,
                                }) is (byte symmetry, byte relationship)
                                    ? ResultFactory.Create(value: (xform, twist, tilt, symmetry, relationship))
                                    : ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Geometry.OrientationFailed)
                                : ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Geometry.OrientationFailed)
                            : ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Geometry.OrientationFailed),
                        _ => ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Geometry.OrientationFailed),
                    },
                    _ => ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Geometry.UnsupportedOrientationType),
                };
    }

    /// <summary>Detect patterns in geometry array and compute alignment.</summary>
    [Pure]
    internal static Result<(byte PatternType, Transform[] IdealTransforms, int[] Anomalies, double Deviation)> DetectPattern(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(g => g.All(item => item?.IsValid == true), error: E.Validation.GeometryInvalid)
                .Bind(validGeometries => validGeometries.Length >= OrientConfig.PatternMinInstances
                    ? ((Func<Result<(byte, Transform[], int[], double)>>)(() => {
                        Result<Point3d>[] centroidResults = [.. validGeometries.Select(g => OrientCore.ExtractCentroid(g, useMassProperties: false)),];
                        return centroidResults.All(r => r.IsSuccess)
                            ? centroidResults.Select(r => r.Value).ToArray() is Point3d[] centroids && centroids.Length >= 3 && centroids.Skip(1).Zip(centroids, (c2, c1) => c2 - c1).ToArray() is Vector3d[] deltas && deltas.Average(v => v.Length) is double avgLen && avgLen > context.AbsoluteTolerance
                                ? (deltas[0].Length >= context.AbsoluteTolerance
                                    && deltas.Skip(1).All(v => Math.Abs(v.Length - avgLen) / avgLen < context.AbsoluteTolerance
                                        && Vector3d.VectorAngle(deltas[0], v) <= context.AngleToleranceRadians))
                                    ? ResultFactory.Create<(byte, Transform[], int[], double)>(value: (0, [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Translation(deltas[0] * i)),], [.. deltas.Select((v, i) => (v, i)).Where(pair => Math.Abs(pair.v.Length - avgLen) / avgLen >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.i),], deltas.Sum(v => Math.Abs(v.Length - avgLen)) / centroids.Length))
                                    : new Point3d(centroids.Average(p => p.X), centroids.Average(p => p.Y), centroids.Average(p => p.Z)) is Point3d center && centroids.Select(p => p.DistanceTo(center)).ToArray() is double[] radii && radii.Average() is double avgRadius && avgRadius > context.AbsoluteTolerance && radii.All(r => Math.Abs(r - avgRadius) / avgRadius < context.AbsoluteTolerance)
                                        ? ResultFactory.Create<(byte, Transform[], int[], double)>(value: (1, [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Rotation(RhinoMath.TwoPI * i / centroids.Length, Vector3d.ZAxis, center)),], [.. radii.Select((r, i) => (r, i)).Where(pair => Math.Abs(pair.r - avgRadius) / avgRadius >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.i),], radii.Sum(r => Math.Abs(r - avgRadius)) / centroids.Length))
                                        : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular"))
                                : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext("Insufficient valid centroids"))
                            : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext($"Centroid extraction failed for {centroidResults.Count(r => !r.IsSuccess)} geometries"));
                    }))()
                    : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.InsufficientParameters.WithContext($"Pattern detection requires at least {OrientConfig.PatternMinInstances} geometries, got {validGeometries.Length}")));
}
