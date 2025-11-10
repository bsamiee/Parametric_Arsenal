using System.Diagnostics.Contracts;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Optimization, relative orientation, and pattern alignment algorithms.</summary>
internal static class OrientCompute {
    /// <summary>Optimize orientation for manufacturing or stability criteria.</summary>
    [Pure]
    internal static Result<(Transform OptimalTransform, double Score, byte[] CriteriaMet)> OptimizeOrientation(
        Brep brep,
        byte criteria,
        double tolerance) =>
        brep.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
            ? ((Func<Result<(Transform, double, byte[])>>)(() => {
                using VolumeMassProperties? vmp = brep.IsSolid ? VolumeMassProperties.Compute(brep) : null;
                Plane[] testPlanes = [
                    new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis),
                    new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis),
                    new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis),
                ];

                (Transform Xform, double Score, byte[] Met)[] results = [.. testPlanes.Select<Plane, (Transform, double, byte[])>(plane => {
                    Transform xf = Transform.PlaneToPlane(plane, Plane.WorldXY);
                    using Brep test = (Brep)brep.Duplicate();
                    return !test.Transform(xf)
                        ? (Transform.Identity, 0.0, [])
                        : test.GetBoundingBox(accurate: true) is BoundingBox testBox && testBox.IsValid
                            ? (xf, criteria switch {
                                1 => 1.0 / (testBox.Diagonal.Length + tolerance),
                                2 => vmp is not null ? testBox.Center.Z - vmp.Centroid.Z : 0.0,
                                3 => testBox.IsDegenerate(tolerance) is int deg && deg != 0 ? 1.0 / deg : 0.0,
                                4 => (testBox.Min.Z >= -tolerance ? 0.5 : 0.0) + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? 0.5 : 0.0),
                                _ => 0.0,
                            }, criteria switch { 1 => [1,], 2 => [2,], 3 => [3,], 4 => [4,], _ => [], })
                            : (Transform.Identity, 0.0, []);
                }),
                ];

                (Transform best, double bestScore, byte[] met) = results.OrderByDescending(r => r.Score).First();
                return ResultFactory.Create(value: (best, bestScore, met));
            }))()
            : ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed);

    /// <summary>Compute relative orientation between two geometries.</summary>
    [Pure]
    internal static Result<(Transform RelativeTransform, double Twist, double Tilt, byte SymmetryType, byte Relationship)> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double tolerance) =>
        (OrientCore.PlaneExtractors.TryGetValue(geometryA.GetType(), out Func<object, Result<Plane>>? extA),
         OrientCore.PlaneExtractors.TryGetValue(geometryB.GetType(), out Func<object, Result<Plane>>? extB))
        switch {
            (true, true) when extA!(geometryA) is Result<Plane> ra && extB!(geometryB) is Result<Plane> rb => (ra, rb) switch {
                (Result<Plane> { IsSuccess: true }, Result<Plane> { IsSuccess: true }) => (ra.Value, rb.Value) is (Plane pa, Plane pb)
                    ? Transform.PlaneToPlane(pa, pb) is Transform xform && Vector3d.VectorAngle(pa.XAxis, pb.XAxis) is double twist && Vector3d.VectorAngle(pa.ZAxis, pb.ZAxis) is double tilt
                        ? ((geometryA, geometryB) switch {
                            (Brep ba, Brep bb) when ((pb.Origin - pa.Origin).Length > RhinoMath.ZeroTolerance
                                ? TestReflectionSymmetry(ba, bb, new Plane(pa.Origin, pb.Origin - pa.Origin), tolerance)
                                : TestReflectionSymmetry(ba, bb, new Plane(pa.Origin, pa.ZAxis), tolerance)) => (byte)1,
                            (Curve ca, Curve cb) when TestRotationSymmetry(ca, cb, pa.Origin, pa.ZAxis, tolerance) => (byte)2,
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
        geometries.Length >= OrientConfig.PatternMinInstances
            ? geometries.Select(g => OrientCore.ExtractCentroid(g, useMassProperties: false)).Where(r => r.IsSuccess).Select(r => r.Value).ToArray() is Point3d[] centroids && centroids.Length >= 3
                ? centroids.Skip(1).Zip(centroids, (c2, c1) => c2 - c1).ToArray() is Vector3d[] deltas && deltas.Average(v => v.Length) is double avgLen && deltas.All(v => Math.Abs(v.Length - avgLen) / avgLen < tolerance)
                    ? ResultFactory.Create(value: ((byte)0, centroids.Select((_, i) => Transform.Translation(deltas[0] * i)).ToArray(), Array.Empty<int>(), deltas.Average(v => Math.Abs(v.Length - avgLen))))
                    : new Point3d(centroids.Average(p => p.X), centroids.Average(p => p.Y), centroids.Average(p => p.Z)) is Point3d center && centroids.Select(p => p.DistanceTo(center)).ToArray() is double[] radii && radii.Average() is double avgRadius && radii.All(r => Math.Abs(r - avgRadius) / avgRadius < tolerance)
                        ? ResultFactory.Create(value: ((byte)1, Enumerable.Range(0, centroids.Length).Select(i => Transform.Rotation(2.0 * Math.PI * i / centroids.Length, Vector3d.ZAxis, center)).ToArray(), Array.Empty<int>(), radii.Average(r => Math.Abs(r - avgRadius))))
                        : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed)
                : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed)
            : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.InsufficientParameters);

    private static bool TestReflectionSymmetry(Brep a, Brep b, Plane mirror, double tolerance) =>
        mirror.IsValid && a.Vertices.Count == b.Vertices.Count && a.Vertices.Zip(b.Vertices, (va, vb) => {
            Point3d reflected = va.Location;
            reflected.Transform(Transform.Mirror(mirrorPlane: mirror));
            return reflected.DistanceTo(vb.Location) < tolerance;
        }).All(match => match);

    private static bool TestRotationSymmetry(Curve a, Curve b, Point3d center, Vector3d axis, double tolerance) {
        const int sampleCount = 8;
        return a.SpanCount == b.SpanCount && axis.IsValid && axis.Length > tolerance && Enumerable.Range(0, sampleCount).All(i => {
            double t = a.Domain.ParameterAt(i / (double)(sampleCount - 1));
            Point3d ptA = a.PointAt(t);
            Point3d ptB = b.PointAt(t);
            Vector3d vecA = ptA - center;
            Vector3d vecB = ptB - center;
            double distA = vecA.Length;
            double distB = vecB.Length;
            return (distA < tolerance && distB < tolerance) || (Math.Abs(distA - distB) < tolerance && Vector3d.VectorAngle(vecA, vecB) < tolerance);
        });
    }
}
