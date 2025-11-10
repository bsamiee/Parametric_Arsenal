using System.Diagnostics.Contracts;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Optimization, relative orientation, and pattern alignment algorithms.</summary>
internal static class OrientCompute {
    /// <summary>Optimize orientation for canonical alignment and stability criteria.</summary>
    /// <param name="brep">Brep geometry to optimize.</param>
    /// <param name="criteria">Optimization criterion: 1=Minimize bounding box (compact packing), 2=Align centroid to bounding center, 3=Maximize bounding box degeneracy (flatness), 4=Canonical position (ground plane + centered + low profile).</param>
    /// <param name="tolerance">Absolute tolerance for geometric comparisons.</param>
    [Pure]
    internal static Result<(Transform OptimalTransform, double Score, byte[] CriteriaMet)> OptimizeOrientation(
        Brep brep,
        byte criteria,
        double tolerance) =>
        !brep.IsValid
            ? ResultFactory.Create<(Transform, double, byte[])>(error: E.Validation.GeometryInvalid)
            : criteria is < 1 or > 4
                ? ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.InvalidOrientationMode.WithContext($"Criteria must be 1-4, got {criteria}"))
                : tolerance <= 0.0
                    ? ResultFactory.Create<(Transform, double, byte[])>(error: E.Validation.ToleranceAbsoluteInvalid)
                    : brep.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
                        ? ((Func<Result<(Transform, double, byte[])>>)(() => {
                            using VolumeMassProperties? vmp = brep.IsSolid && brep.IsManifold ? VolumeMassProperties.Compute(brep) : null;
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
                                using Brep test = (Brep)brep.Duplicate();
                                return !test.Transform(xf) ? (Transform.Identity, 0.0, Array.Empty<byte>())
                                    : test.GetBoundingBox(accurate: true) is BoundingBox testBox && testBox.IsValid
                                        ? (xf, criteria switch {
                                            1 => testBox.Diagonal.Length > tolerance ? 1.0 / testBox.Diagonal.Length : 0.0,
                                            2 => vmp is not null && testBox.Diagonal.Length > tolerance ? Math.Max(0.0, 1.0 - (Math.Abs(testBox.Center.Z - vmp.Centroid.Z) / testBox.Diagonal.Length)) : 0.0,
                                            3 => testBox.IsDegenerate(tolerance) is int deg ? deg switch {
                                                0 => 0.0,
                                                >= 1 and <= 3 => deg / 3.0,
                                                _ => 0.0,
                                            } : 0.0,
                                            4 => (testBox.Min.Z >= -tolerance ? OrientConfig.OrientationScoreWeight1 : 0.0) + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? OrientConfig.OrientationScoreWeight2 : 0.0) + ((testBox.Max.Z - testBox.Min.Z) < (testBox.Diagonal.Length * OrientConfig.LowProfileAspectRatio) ? OrientConfig.OrientationScoreWeight3 : 0.0),
                                            _ => 0.0,
                                        }, criteria is >= 1 and <= 4 ? [criteria,] : Array.Empty<byte>())
                                        : (Transform.Identity, 0.0, Array.Empty<byte>());
                            }),
                            ];

                            return results.MaxBy(r => r.Item2) is (Transform best, double bestScore, byte[] met) && bestScore > OrientConfig.MinimumOptimizationScore
                                ? ResultFactory.Create(value: (best, bestScore, met))
                                : ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed.WithContext("No valid orientation found"));
                        }))()
                        : ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed.WithContext("Invalid bounding box"));

    /// <summary>Compute relative orientation between two geometries.</summary>
    [Pure]
    internal static Result<(Transform RelativeTransform, double Twist, double Tilt, byte SymmetryType, byte Relationship)> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double tolerance) =>
        geometryA is null || geometryB is null
            ? ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Geometry.OrientationFailed.WithContext("Null geometry"))
            : tolerance <= 0.0
                ? ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Validation.ToleranceAbsoluteInvalid)
                : (OrientCore.PlaneExtractors.TryGetValue(geometryA.GetType(), out Func<object, Result<Plane>>? extA),
                   OrientCore.PlaneExtractors.TryGetValue(geometryB.GetType(), out Func<object, Result<Plane>>? extB))
                switch {
                    (true, true) when extA!(geometryA) is Result<Plane> ra && extB!(geometryB) is Result<Plane> rb => (ra, rb) switch {
                        (Result<Plane> { IsSuccess: true }, Result<Plane> { IsSuccess: true }) => (ra.Value, rb.Value) is (Plane pa, Plane pb)
                            ? Transform.PlaneToPlane(pa, pb) is Transform xform && Vector3d.VectorAngle(pa.XAxis, pb.XAxis) is double twist && Vector3d.VectorAngle(pa.ZAxis, pb.ZAxis) is double tilt
                                ? ((geometryA, geometryB) switch {
                                    (Brep ba, Brep bb) when ba.Vertices.Count == bb.Vertices.Count => (pb.Origin - pa.Origin).Length > OrientConfig.MinVectorLength
                                        ? new Plane(pa.Origin, pb.Origin - pa.Origin) is Plane mirror && mirror.IsValid && ba.Vertices.All(va => {
                                            Point3d reflected = va.Location;
                                            reflected.Transform(Transform.Mirror(mirrorPlane: mirror));
                                            return bb.Vertices.Any(vb => reflected.DistanceTo(vb.Location) < tolerance);
                                        }) ? (byte)1 : (byte)0
                                        : new Plane(pa.Origin, pa.ZAxis) is Plane mirror2 && mirror2.IsValid && ba.Vertices.All(va => {
                                            Point3d reflected = va.Location;
                                            reflected.Transform(Transform.Mirror(mirrorPlane: mirror2));
                                            return bb.Vertices.Any(vb => reflected.DistanceTo(vb.Location) < tolerance);
                                        }) ? (byte)1 : (byte)0,
                                    (Curve ca, Curve cb) when ca.SpanCount == cb.SpanCount && pa.ZAxis.IsValid && pa.ZAxis.Length > tolerance => Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount).All(i => {
                                        double t = ca.Domain.ParameterAt(i / (double)(OrientConfig.RotationSymmetrySampleCount - 1));
                                        Point3d ptA = ca.PointAt(t);
                                        Point3d ptB = cb.PointAt(t);
                                        Vector3d vecA = ptA - pa.Origin;
                                        Vector3d vecB = ptB - pa.Origin;
                                        double distA = vecA.Length;
                                        double distB = vecB.Length;
                                        return (distA < tolerance && distB < tolerance) || (Math.Abs(distA - distB) < tolerance && Vector3d.VectorAngle(vecA, vecB) < OrientConfig.SymmetryAngleToleranceRadians);
                                    }) ? (byte)2 : (byte)0,
                                    _ => (byte)0,
                                }, Math.Abs(Vector3d.Multiply(pa.ZAxis, pb.ZAxis)) switch {
                                    double dot when Math.Abs(dot - 1.0) < tolerance => (byte)1,
                                    double dot when Math.Abs(dot) < tolerance => (byte)2,
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

    /// <summary>Detect patterns in geometry array and compute alignment.</summary>
    [Pure]
    internal static Result<(byte PatternType, Transform[] IdealTransforms, int[] Anomalies, double Deviation)> DetectPattern(
        GeometryBase[] geometries,
        double tolerance) =>
        geometries is null
            ? ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.InsufficientParameters.WithContext("Geometries array is null"))
            : tolerance <= 0.0
                ? ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Validation.ToleranceAbsoluteInvalid)
                : geometries.Length >= OrientConfig.PatternMinInstances
                    ? ((Func<Result<(byte, Transform[], int[], double)>>)(() => {
                        Result<Point3d>[] centroidResults = [.. geometries.Select(g => OrientCore.ExtractCentroid(g, useMassProperties: false)),];
                        return centroidResults.All(r => r.IsSuccess)
                            ? centroidResults.Select(r => r.Value).ToArray() is Point3d[] centroids && centroids.Length >= 3 && centroids.Skip(1).Zip(centroids, (c2, c1) => c2 - c1).ToArray() is Vector3d[] deltas && deltas.Average(v => v.Length) is double avgLen && avgLen > tolerance
                                ? deltas.All(v => Math.Abs(v.Length - avgLen) / avgLen < tolerance)
                                    ? ResultFactory.Create<(byte, Transform[], int[], double)>(value: (0, [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Translation((Vector3d)centroids[0] + (deltas[0] * i))),], [.. deltas.Select((v, i) => (v, i)).Where(pair => Math.Abs(pair.v.Length - avgLen) / avgLen >= (tolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.i),], deltas.Sum(v => Math.Abs(v.Length - avgLen)) / centroids.Length))
                                    : new Point3d(centroids.Average(p => p.X), centroids.Average(p => p.Y), centroids.Average(p => p.Z)) is Point3d center && centroids.Select(p => p.DistanceTo(center)).ToArray() is double[] radii && radii.Average() is double avgRadius && avgRadius > tolerance && radii.All(r => Math.Abs(r - avgRadius) / avgRadius < tolerance)
                                        ? ResultFactory.Create<(byte, Transform[], int[], double)>(value: (1, [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Rotation(2.0 * Math.PI * i / centroids.Length, Vector3d.ZAxis, center)),], [.. radii.Select((r, i) => (r, i)).Where(pair => Math.Abs(pair.r - avgRadius) / avgRadius >= (tolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.i),], radii.Sum(r => Math.Abs(r - avgRadius)) / centroids.Length))
                                        : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular"))
                                : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext("Insufficient valid centroids"))
                            : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed.WithContext($"Centroid extraction failed for {centroidResults.Count(r => !r.IsSuccess)} geometries"));
                    }))()
                    : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.InsufficientParameters.WithContext($"Pattern detection requires at least {OrientConfig.PatternMinInstances} geometries, got {geometries.Length}"));
}
