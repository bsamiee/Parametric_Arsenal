using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Dense algorithmic implementations for orientation operations.</summary>
[Pure]
internal static class OrientationCompute {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d> ExtractCentroid(GeometryBase geometry, bool useMassProperties) =>
        (geometry, useMassProperties) switch {
            (Brep brep, true) when brep.IsSolid => ((Func<Result<Point3d>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
            }))(),
            (Brep brep, true) when brep.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Point3d>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(brep);
                return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
            }))(),
            (Extrusion ext, true) when ext.IsSolid => ((Func<Result<Point3d>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(ext);
                return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
            }))(),
            (Extrusion ext, true) when ext.IsClosed(0) && ext.IsClosed(1) => ((Func<Result<Point3d>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(ext);
                return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
            }))(),
            (Mesh mesh, true) when mesh.IsClosed => ((Func<Result<Point3d>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
                return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
            }))(),
            (Mesh mesh, true) => ((Func<Result<Point3d>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(mesh);
                return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
            }))(),
            (Curve curve, true) => ((Func<Result<Point3d>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(curve);
                return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
            }))(),
            (GeometryBase g, false) => g.GetBoundingBox(accurate: true) switch {
                BoundingBox b when b.IsValid => ResultFactory.Create(value: b.Center),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(T geometry, Transform transform) where T : GeometryBase =>
        (T)geometry.Duplicate() switch {
            T dup when dup.Transform(transform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeCanonicalTransform(GeometryBase geometry, Orientation.CanonicalMode mode) =>
        (mode, geometry.GetBoundingBox(accurate: true)) switch {
            (_, BoundingBox box) when !box.IsValid && mode is not Orientation.VolumeCentroid =>
                ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
            (Orientation.WorldXY, BoundingBox box) =>
                ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY)),
            (Orientation.WorldYZ, BoundingBox box) =>
                ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ)),
            (Orientation.WorldXZ, BoundingBox box) =>
                ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis), new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis))),
            (Orientation.AreaCentroid, BoundingBox box) =>
                ResultFactory.Create(value: Transform.Translation(Point3d.Origin - box.Center)),
            (Orientation.VolumeCentroid, _) =>
                ExtractCentroid(geometry: geometry, useMassProperties: true).Map(c => Transform.Translation(Point3d.Origin - c)),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeVectorRotation(GeometryBase geometry, Vector3d target, Vector3d? source, Point3d? anchor) =>
        (geometry.GetBoundingBox(accurate: true), source ?? Vector3d.ZAxis, target) switch {
            (BoundingBox box, Vector3d s, Vector3d t) when box.IsValid && s.Length > RhinoMath.ZeroTolerance && t.Length > RhinoMath.ZeroTolerance =>
                ((Func<Result<Transform>>)(() => {
                    Vector3d su = new(s);
                    Vector3d tu = new(t);
                    _ = su.Unitize();
                    _ = tu.Unitize();
                    Point3d pt = anchor ?? box.Center;
                    return Vector3d.CrossProduct(su, tu).Length < RhinoMath.SqrtEpsilon
                        ? Math.Abs((su * tu) - 1.0) < RhinoMath.SqrtEpsilon
                            ? ResultFactory.Create(value: Transform.Identity)
                            : Math.Abs((su * tu) + 1.0) < RhinoMath.SqrtEpsilon
                                ? ((Func<Result<Transform>>)(() => {
                                    Vector3d axisCandidate = Math.Abs(su * Vector3d.XAxis) < 0.95
                                        ? Vector3d.CrossProduct(su, Vector3d.XAxis)
                                        : Vector3d.CrossProduct(su, Vector3d.YAxis);
                                    bool normalized = axisCandidate.Unitize();
                                    return normalized
                                        ? ResultFactory.Create(value: Transform.Rotation(Math.PI, axisCandidate, pt))
                                        : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors);
                                }))()
                                : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors)
                        : ResultFactory.Create(value: Transform.Rotation(su, tu, pt));
                }))(),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractBestFitPlane(GeometryBase geometry) =>
        geometry switch {
            PointCloud pc when pc.Count >= OrientationConfig.BestFitMinPoints => ((Func<Result<Plane>>)(() => {
                Point3d[] points = pc.GetPoints();
                return Plane.FitPlaneToPoints(points, out Plane plane) == PlaneFitResult.Success
                    ? Math.Sqrt(points.Sum(p => { double d = plane.DistanceTo(p); return d * d; }) / points.Length) <= OrientationConfig.BestFitResidualThreshold
                        ? ResultFactory.Create(value: plane)
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed)
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))(),
            PointCloud pc => ResultFactory.Create<Plane>(error: E.Geometry.InsufficientParameters.WithContext($"Best-fit plane requires {OrientationConfig.BestFitMinPoints} points, got {pc.Count}")),
            Mesh m when m.Vertices.Count >= OrientationConfig.BestFitMinPoints => ((Func<Result<Plane>>)(() => {
                Point3d[] points = m.Vertices.ToPoint3dArray();
                return Plane.FitPlaneToPoints(points, out Plane plane) == PlaneFitResult.Success
                    ? Math.Sqrt(points.Sum(p => { double d = plane.DistanceTo(p); return d * d; }) / points.Length) <= OrientationConfig.BestFitResidualThreshold
                        ? ResultFactory.Create(value: plane)
                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed)
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            }))(),
            Mesh m => ResultFactory.Create<Plane>(error: E.Geometry.InsufficientParameters.WithContext($"Best-fit plane requires {OrientationConfig.BestFitMinPoints} points, got {m.Vertices.Count}")),
            _ => ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> FlipDirection<T>(T geometry) where T : GeometryBase =>
        geometry.Duplicate() switch {
            Curve c when c.Reverse() => ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)c,]),
            Brep b => ((Func<Result<IReadOnlyList<T>>>)(() => { b.Flip(); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)b,]); }))(),
            Extrusion e => e.ToBrep() switch {
                Brep br => ((Func<Result<IReadOnlyList<T>>>)(() => { br.Flip(); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)br,]); }))(),
                _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
            },
            Mesh m => ((Func<Result<IReadOnlyList<T>>>)(() => { m.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)m,]); }))(),
            null => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    internal static Result<Orientation.OptimizationResult> OptimizeOrientation(Brep brep, Orientation.OptimizationCriteria criteria, double tolerance) =>
        tolerance <= 0.0
            ? ResultFactory.Create<Orientation.OptimizationResult>(error: E.Validation.ToleranceAbsoluteInvalid)
            : brep.GetBoundingBox(accurate: true) is BoundingBox box && !box.IsValid
                ? ResultFactory.Create<Orientation.OptimizationResult>(error: E.Geometry.TransformFailed.WithContext("Invalid bounding box"))
                : ExtractCentroid(geometry: brep, useMassProperties: true) is Result<Point3d> centroidResult && criteria is Orientation.CenteredCriteria && !centroidResult.IsSuccess
                    ? ResultFactory.Create<Orientation.OptimizationResult>(error: E.Geometry.CentroidExtractionFailed)
                    : ((Func<Result<Orientation.OptimizationResult>>)(() => {
                        Vector3d diag1 = new(1, 1, 0);
                        Vector3d diag2 = new(1, 0, 1);
                        Vector3d diag3 = new(0, 1, 1);
                        _ = diag1.Unitize();
                        _ = diag2.Unitize();
                        _ = diag3.Unitize();
                        Plane[] testPlanes = [
                            new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis),
                            new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis),
                            new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis),
                            new Plane(box.Center, diag1, Vector3d.ZAxis),
                            new Plane(box.Center, diag2, Vector3d.YAxis),
                            new Plane(box.Center, diag3, Vector3d.XAxis),
                        ];
                        (Transform xform, double score, Orientation.OptimizationCriteria[] satisfied)[] results = [.. testPlanes.Select(plane => {
                            Transform xf = Transform.PlaneToPlane(plane, Plane.WorldXY);
                            using Brep test = (Brep)brep.Duplicate();
                            return !test.Transform(xf) ? (Transform.Identity, 0.0, Array.Empty<Orientation.OptimizationCriteria>())
                                : test.GetBoundingBox(accurate: true) is BoundingBox testBox && testBox.IsValid
                                    ? (xf, criteria switch {
                                        Orientation.CompactCriteria => testBox.Diagonal.Length > tolerance ? 1.0 / testBox.Diagonal.Length : 0.0,
                                        Orientation.CenteredCriteria => centroidResult.IsSuccess && testBox.Diagonal.Length > tolerance
                                            ? ((Func<double>)(() => {
                                                Point3d centroid = centroidResult.Value;
                                                centroid.Transform(xf);
                                                return Math.Max(0.0, 1.0 - (Math.Abs(centroid.Z) / testBox.Diagonal.Length));
                                            }))()
                                            : 0.0,
                                        Orientation.FlatnessCriteria => ((Math.Abs(testBox.Max.X - testBox.Min.X) <= tolerance ? 1 : 0)
                                            + (Math.Abs(testBox.Max.Y - testBox.Min.Y) <= tolerance ? 1 : 0)
                                            + (Math.Abs(testBox.Max.Z - testBox.Min.Z) <= tolerance ? 1 : 0)) switch {
                                                0 => 0.0,
                                                int degeneracy and >= 1 and <= 3 => degeneracy / (double)OrientationConfig.MaxDegeneracyDimensions,
                                                _ => 0.0,
                                            },
                                        Orientation.CanonicalCriteria => (testBox.Min.Z >= -tolerance ? OrientationConfig.OrientationScoreWeight1 : 0.0)
                                            + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? OrientationConfig.OrientationScoreWeight2 : 0.0)
                                            + ((testBox.Max.Z - testBox.Min.Z) < (testBox.Diagonal.Length * OrientationConfig.LowProfileAspectRatio) ? OrientationConfig.OrientationScoreWeight3 : 0.0),
                                        _ => 0.0,
                                    }, [criteria,])
                                    : (Transform.Identity, 0.0, Array.Empty<Orientation.OptimizationCriteria>());
                        }),
                        ];
                        return results.MaxBy(r => r.score) is (Transform best, double bestScore, Orientation.OptimizationCriteria[] met) && bestScore > 0
                            ? ResultFactory.Create(value: new Orientation.OptimizationResult(OptimalTransform: best, Score: bestScore, CriteriaSatisfied: met))
                            : ResultFactory.Create<Orientation.OptimizationResult>(error: E.Geometry.TransformFailed.WithContext("No valid orientation found"));
                    }))();

    internal static Result<Orientation.RelativeOrientationResult> ComputeRelative(GeometryBase geometryA, GeometryBase geometryB, double symmetryTolerance, double angleTolerance) =>
        (OrientationConfig.PlaneExtractors.TryGetValue(geometryA.GetType(), out OrientationConfig.PlaneExtractorMetadata? extA),
         OrientationConfig.PlaneExtractors.TryGetValue(geometryB.GetType(), out OrientationConfig.PlaneExtractorMetadata? extB)) switch {
             (true, true) when extA!.Extractor(geometryA) is Result<Plane> ra && extB!.Extractor(geometryB) is Result<Plane> rb =>
                 (ra, rb) switch {
                     ( { IsSuccess: true }, { IsSuccess: true }) =>
                         (ra.Value, rb.Value) is (Plane pa, Plane pb)
                             ? Transform.PlaneToPlane(pa, pb) is Transform xform
                               && Vector3d.VectorAngle(pa.XAxis, pb.XAxis) is double twist
                               && Vector3d.VectorAngle(pa.ZAxis, pb.ZAxis) is double tilt
                                 ? ResultFactory.Create(value: new Orientation.RelativeOrientationResult(
                                     RelativeTransform: xform,
                                     Twist: twist,
                                     Tilt: tilt,
                                     Symmetry: ClassifySymmetry(geometryA: geometryA, geometryB: geometryB, pa: pa, pb: pb, tolerance: symmetryTolerance, angleTol: angleTolerance),
                                     Relationship: ClassifyRelationship(pa: pa, pb: pb, angleTolerance: angleTolerance)))
                                 : ResultFactory.Create<Orientation.RelativeOrientationResult>(error: E.Geometry.OrientationFailed)
                             : ResultFactory.Create<Orientation.RelativeOrientationResult>(error: E.Geometry.OrientationFailed),
                     _ => ResultFactory.Create<Orientation.RelativeOrientationResult>(error: E.Geometry.OrientationFailed),
                 },
             _ => ResultFactory.Create<Orientation.RelativeOrientationResult>(error: E.Geometry.UnsupportedOrientationType),
         };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Orientation.SymmetryType ClassifySymmetry(GeometryBase geometryA, GeometryBase geometryB, Plane pa, Plane pb, double tolerance, double angleTol) =>
        (geometryA, geometryB) switch {
            (Brep ba, Brep bb) when ba.Vertices.Count == bb.Vertices.Count =>
                CheckMirrorSymmetry(ba: ba, bb: bb, pa: pa, pb: pb, tolerance: tolerance),
            (Curve ca, Curve cb) when ca.SpanCount == cb.SpanCount && pa.ZAxis.IsValid && pa.ZAxis.Length > tolerance =>
                ((Func<Orientation.SymmetryType>)(() => {
                    Point3d[] samplesA = [.. Enumerable.Range(0, OrientationConfig.RotationSymmetrySampleCount).Select(i => ca.PointAt(ca.Domain.ParameterAt(i / (double)(OrientationConfig.RotationSymmetrySampleCount - 1)))),];
                    Point3d[] samplesB = [.. Enumerable.Range(0, OrientationConfig.RotationSymmetrySampleCount).Select(i => cb.PointAt(cb.Domain.ParameterAt(i / (double)(OrientationConfig.RotationSymmetrySampleCount - 1)))),];
                    int[] testIndices = [0, samplesA.Length / 2, samplesA.Length - 1,];
                    double[] candidateAngles = [.. testIndices.Select(idx => {
                        Vector3d vecA = samplesA[idx] - pa.Origin;
                        Vector3d vecB = samplesB[idx] - pa.Origin;
                        Vector3d projA = vecA - ((vecA * pa.ZAxis) * pa.ZAxis);
                        Vector3d projB = vecB - ((vecB * pa.ZAxis) * pa.ZAxis);
                        return projA.Length < tolerance || projB.Length < tolerance
                            ? double.NaN
                            : Vector3d.CrossProduct(projA, projB) * pa.ZAxis < 0
                                ? -Vector3d.VectorAngle(projA, projB)
                                : Vector3d.VectorAngle(projA, projB);
                    }).Where(static a => !double.IsNaN(a)),
                    ];
                    return candidateAngles.Length == 0
                        ? new Orientation.NoSymmetry()
                        : candidateAngles.All(a => Math.Abs(a - candidateAngles[0]) < angleTol)
                          && Transform.Rotation(candidateAngles[0], pa.ZAxis, pa.Origin) is Transform rotation
                          && samplesA.Zip(samplesB, (ptA, ptB) => { Point3d r = ptA; r.Transform(rotation); return r.DistanceTo(ptB); }).All(dist => dist < tolerance)
                            ? new Orientation.RotationalSymmetry()
                            : new Orientation.NoSymmetry();
                }))(),
            _ => new Orientation.NoSymmetry(),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Orientation.SymmetryType CheckMirrorSymmetry(Brep ba, Brep bb, Plane pa, Plane pb, double tolerance) {
        Plane mirror = (pb.Origin - pa.Origin).Length > RhinoMath.ZeroTolerance
            ? new Plane(pa.Origin + ((pb.Origin - pa.Origin) * 0.5), pb.Origin - pa.Origin)
            : new Plane(pa.Origin, pa.ZAxis);
        if (!mirror.IsValid) {
            return new Orientation.NoSymmetry();
        }
        Point3d[] reflectedA = [.. ba.Vertices.Select(va => {
            Point3d r = va.Location;
            r.Transform(Transform.Mirror(mirrorPlane: mirror));
            return r;
        }),];
        return reflectedA.All(ra => bb.Vertices.Any(vb => ra.DistanceTo(vb.Location) < tolerance))
               && bb.Vertices.All(vb => reflectedA.Any(ra => ra.DistanceTo(vb.Location) < tolerance))
            ? new Orientation.MirrorSymmetry()
            : new Orientation.NoSymmetry();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Orientation.RelationshipType ClassifyRelationship(Plane pa, Plane pb, double angleTolerance) =>
        Math.Abs(Vector3d.Multiply(pa.ZAxis, pb.ZAxis)) switch {
            double dot when Math.Abs(dot - 1.0) < 1.0 - Math.Cos(angleTolerance) => new Orientation.Parallel(),
            double dot when Math.Abs(dot) < Math.Sin(angleTolerance) => new Orientation.Perpendicular(),
            _ => new Orientation.Oblique(),
        };

    internal static Result<Orientation.PatternDetectionResult> DetectPattern(GeometryBase[] geometries, double absoluteTolerance, double angleTolerance) =>
        geometries.Length < OrientationConfig.PatternMinInstances
            ? ResultFactory.Create<Orientation.PatternDetectionResult>(error: E.Geometry.InsufficientParameters.WithContext($"Pattern detection requires at least {OrientationConfig.PatternMinInstances} geometries, got {geometries.Length}"))
            : ((Func<Result<Orientation.PatternDetectionResult>>)(() => {
                Result<Point3d>[] centroidResults = [.. geometries.Select(static g => ExtractCentroid(geometry: g, useMassProperties: false)),];
                return centroidResults.All(static r => r.IsSuccess)
                    ? centroidResults.Select(static r => r.Value).ToArray() is Point3d[] centroids && centroids.Length >= 3
                      && centroids.Skip(1).Zip(centroids, static (c2, c1) => c2 - c1).ToArray() is Vector3d[] deltas
                      && deltas.Average(static v => v.Length) is double avgLen && avgLen > absoluteTolerance
                        ? deltas[0].Length >= absoluteTolerance
                          && deltas.Skip(1).All(v => Math.Abs(v.Length - avgLen) / avgLen < absoluteTolerance && Vector3d.VectorAngle(deltas[0], v) <= angleTolerance)
                            ? ResultFactory.Create(value: new Orientation.PatternDetectionResult(
                                Pattern: new Orientation.LinearPattern(),
                                IdealTransforms: [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Translation(deltas[0] * i)),],
                                Anomalies: [.. deltas.Select((v, i) => (v, i)).Where(pair => Math.Abs(pair.v.Length - avgLen) / avgLen >= (absoluteTolerance * OrientationConfig.PatternAnomalyThreshold)).Select(static pair => pair.i),],
                                Deviation: deltas.Sum(v => Math.Abs(v.Length - avgLen)) / centroids.Length))
                            : new Point3d(centroids.Average(static p => p.X), centroids.Average(static p => p.Y), centroids.Average(static p => p.Z)) is Point3d center
                              && centroids.Select(p => p.DistanceTo(center)).ToArray() is double[] radii
                              && radii.Average() is double avgRadius && avgRadius > absoluteTolerance
                              && radii.All(r => Math.Abs(r - avgRadius) / avgRadius < absoluteTolerance)
                                ? ResultFactory.Create(value: new Orientation.PatternDetectionResult(
                                    Pattern: new Orientation.RadialPattern(),
                                    IdealTransforms: [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Rotation(RhinoMath.TwoPI * i / centroids.Length, Vector3d.ZAxis, center)),],
                                    Anomalies: [.. radii.Select((r, i) => (r, i)).Where(pair => Math.Abs(pair.r - avgRadius) / avgRadius >= (absoluteTolerance * OrientationConfig.PatternAnomalyThreshold)).Select(static pair => pair.i),],
                                    Deviation: radii.Sum(r => Math.Abs(r - avgRadius)) / centroids.Length))
                                : ResultFactory.Create(value: new Orientation.PatternDetectionResult(
                                    Pattern: new Orientation.NoPattern(),
                                    IdealTransforms: [],
                                    Anomalies: [],
                                    Deviation: double.NaN))
                        : ResultFactory.Create<Orientation.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular"))
                    : ResultFactory.Create<Orientation.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext($"Centroid extraction failed for {centroidResults.Count(static r => !r.IsSuccess)} geometries"));
            }))();
}
