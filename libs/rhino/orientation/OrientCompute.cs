using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Optimization, relative orientation, and pattern alignment algorithms.</summary>
internal static class OrientCompute {
    /// <summary>Optimize orientation for manufacturing or stability criteria.</summary>
    internal static Result<(Transform OptimalTransform, double Score, byte[] CriteriaMet)> OptimizeOrientation(
        Brep brep,
        byte criteria,
        double tolerance) =>
        brep.GetBoundingBox(accurate: true) switch {
            BoundingBox box when box.IsValid => ((Func<Result<(Transform, double, byte[])>>)(() => {
                VolumeMassProperties? vmp = brep.IsSolid ? VolumeMassProperties.Compute(brep) : null;
                (Transform best, double bestScore, byte[] met) = (Transform.Identity, double.MinValue, []);
                Plane[] testPlanes = [
                    new(box.Center, Vector3d.XAxis, Vector3d.YAxis),
                    new(box.Center, Vector3d.YAxis, Vector3d.ZAxis),
                    new(box.Center, Vector3d.XAxis, Vector3d.ZAxis),
                ];
                foreach (Plane plane in testPlanes) {
                    Transform xf = Transform.PlaneToPlane(plane, Plane.WorldXY);
                    Brep test = (Brep)brep.Duplicate();
                    _ = test.Transform(xf);
                    BoundingBox testBox = test.GetBoundingBox(accurate: true);
                    double score = criteria switch {
                        1 => 1.0 / (testBox.Diagonal.Length + tolerance),
                        2 => vmp is not null ? testBox.Center.Z - vmp.Centroid.Z : 0.0,
                        3 => testBox.IsDegenerate(tolerance) switch {
                            0b001 => 1.0,
                            0b010 => 1.0,
                            0b100 => 1.0,
                            _ => 0.0,
                        },
                        4 => (testBox.Min.Z >= -tolerance ? 0.5 : 0.0) + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? 0.5 : 0.0),
                        _ => 0.0,
                    };
                    (best, bestScore, met) = score > bestScore
                        ? (xf, score, criteria switch { 1 => [1,], 2 => [2,], 3 => [3,], 4 => [4,], _ => [] })
                        : (best, bestScore, met);
                    test.Dispose();
                }
                vmp?.Dispose();
                return ResultFactory.Create(value: (best, bestScore, met));
            }))(),
            _ => ResultFactory.Create<(Transform, double, byte[])>(error: E.Geometry.TransformFailed),
        };

    /// <summary>Compute relative orientation between two geometries.</summary>
    internal static Result<(Transform RelativeTransform, double Twist, double Tilt, byte SymmetryType, byte Relationship)> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        double tolerance) =>
        (OrientCore.PlaneExtractors.TryGetValue(geometryA.GetType(), out Func<object, Result<Plane>>? extA) ? extA(geometryA) : ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType),
         OrientCore.PlaneExtractors.TryGetValue(geometryB.GetType(), out Func<object, Result<Plane>>? extB) ? extB(geometryB) : ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType))
        switch {
            (Result<Plane> { IsSuccess: true } ra, Result<Plane> { IsSuccess: true } rb) => ((Func<Result<(Transform, double, double, byte, byte)>>)(() => {
                Plane pa = ra.Value;
                Plane pb = rb.Value;
                Transform xform = Transform.PlaneToPlane(pa, pb);
                double twist = Vector3d.VectorAngle(pa.XAxis, pb.XAxis);
                double tilt = Vector3d.VectorAngle(pa.ZAxis, pb.ZAxis);
                byte symmetry = (geometryA, geometryB) switch {
                    (Brep ba, Brep bb) when TestReflectionSymmetry(ba, bb, new Plane(pa.Origin, (pb.Origin - pa.Origin)), tolerance) => (byte)1,
                    (Curve ca, Curve cb) when TestRotationSymmetry(ca, cb, pa.Origin, pa.ZAxis, tolerance) => (byte)2,
                    _ => (byte)0,
                };
                byte relationship = Math.Abs(Vector3d.Multiply(pa.ZAxis, pb.ZAxis)) switch {
                    double dot when Math.Abs(dot - 1.0) < tolerance => (byte)1,
                    double dot when Math.Abs(dot) < tolerance => (byte)2,
                    _ => (byte)3,
                };
                return ResultFactory.Create(value: (xform, twist, tilt, symmetry, relationship));
            }))(),
            _ => ResultFactory.Create<(Transform, double, double, byte, byte)>(error: E.Geometry.OrientationFailed),
        };

    /// <summary>Detect patterns in geometry array and compute alignment.</summary>
    internal static Result<(byte PatternType, Transform[] IdealTransforms, int[] Anomalies, double Deviation)> DetectPattern(
        GeometryBase[] geometries,
        double tolerance) =>
        geometries.Length >= OrientConfig.PatternMinInstances
            ? ((Func<Result<(byte, Transform[], int[], double)>>)(() => {
                Point3d[] centroids = geometries.Select(g => OrientCore.ExtractCentroid(g, useMassProperties: false))
                    .Where(r => r.IsSuccess)
                    .Select(r => r.Value)
                    .ToArray();
                return centroids.Length >= 3
                    ? ((Func<Result<(byte, Transform[], int[], double)>>)(() => {
                        Vector3d[] deltas = centroids.Skip(1).Zip(centroids, (c2, c1) => c2 - c1).ToArray();
                        double avgLen = deltas.Average(v => v.Length);
                        bool isLinear = deltas.All(v => Math.Abs(v.Length - avgLen) / avgLen < tolerance);
                        return isLinear
                            ? ResultFactory.Create(value: ((byte)0, centroids.Select((c, i) => Transform.Translation(deltas[0] * i)).ToArray(), Array.Empty<int>(), deltas.Select(v => Math.Abs(v.Length - avgLen)).Average()))
                            : ((Func<Result<(byte, Transform[], int[], double)>>)(() => {
                                Point3d center = new(centroids.Average(p => p.X), centroids.Average(p => p.Y), centroids.Average(p => p.Z));
                                double[] radii = centroids.Select(p => p.DistanceTo(center)).ToArray();
                                double avgRadius = radii.Average();
                                bool isRotational = radii.All(r => Math.Abs(r - avgRadius) / avgRadius < tolerance);
                                return isRotational
                                    ? ResultFactory.Create(value: ((byte)1, Enumerable.Range(0, centroids.Length).Select(i => Transform.Rotation((2.0 * Math.PI * i) / centroids.Length, Vector3d.ZAxis, center)).ToArray(), Array.Empty<int>(), radii.Select(r => Math.Abs(r - avgRadius)).Average()))
                                    : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed);
                            }))();
                    }))()
                    : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.PatternDetectionFailed);
            }))()
            : ResultFactory.Create<(byte, Transform[], int[], double)>(error: E.Geometry.InsufficientParameters);

    private static bool TestReflectionSymmetry(Brep a, Brep b, Plane mirror, double tolerance) =>
        a.Vertices.Count == b.Vertices.Count && a.Vertices.Zip(b.Vertices, (va, vb) => {
            Point3d reflected = va.Location;
            reflected.Transform(Transform.Mirror(mirror));
            return reflected.DistanceTo(vb.Location) < tolerance;
        }).All(match => match);

    private static bool TestRotationSymmetry(Curve a, Curve b, Point3d center, Vector3d axis, double tolerance) =>
        a.SpanCount == b.SpanCount && Enumerable.Range(0, 8).All(i => {
            double t = a.Domain.ParameterAt(i / 7.0);
            Point3d ptA = a.PointAt(t);
            Point3d ptB = b.PointAt(t);
            double angleA = Math.Atan2((ptA - center).Y, (ptA - center).X);
            double angleB = Math.Atan2((ptB - center).Y, (ptB - center).X);
            return Math.Abs(angleA - angleB) < tolerance;
        });
}
