using System.Diagnostics.Contracts;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Dense algorithmic implementations for orientation operations.</summary>
[Pure]
internal static class OrientationCompute {
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(T geometry, Transform transform) where T : GeometryBase =>
        (T)geometry.Duplicate() switch {
            T dup when dup.Transform(transform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
        };

    internal static Result<Point3d> ExtractCentroid(GeometryBase geometry, bool useMassProperties) =>
        useMassProperties
            ? geometry switch {
                Brep b when b.IsSolid => ((Func<Result<Point3d>>)(() => {
                    using VolumeMassProperties? vmp = VolumeMassProperties.Compute(b);
                    return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                }))(),
                Brep b when b.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Point3d>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(b);
                    return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                }))(),
                Extrusion e when e.IsSolid => ((Func<Result<Point3d>>)(() => {
                    using VolumeMassProperties? vmp = VolumeMassProperties.Compute(e);
                    return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                }))(),
                Extrusion e when e.IsClosed(0) && e.IsClosed(1) => ((Func<Result<Point3d>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(e);
                    return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                }))(),
                Mesh m when m.IsClosed => ((Func<Result<Point3d>>)(() => {
                    using VolumeMassProperties? vmp = VolumeMassProperties.Compute(m);
                    return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                }))(),
                Mesh m => ((Func<Result<Point3d>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(m);
                    return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                }))(),
                Curve c => ((Func<Result<Point3d>>)(() => {
                    using AreaMassProperties? amp = AreaMassProperties.Compute(c);
                    return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                }))(),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            }
            : geometry.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
                ? ResultFactory.Create(value: box.Center)
                : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);

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

    internal static Result<Transform> ComputeVectorRotation(GeometryBase geometry, Vector3d target, Vector3d? source, Point3d? anchor) =>
        (geometry.GetBoundingBox(accurate: true), source ?? Vector3d.ZAxis, target) switch {
            (BoundingBox box, Vector3d s, Vector3d t) when box.IsValid && s.Length > RhinoMath.ZeroTolerance && t.Length > RhinoMath.ZeroTolerance =>
                (su: new Vector3d(s), tu: new Vector3d(t), pt: anchor ?? box.Center) switch {
                    var ctx when ctx.su.Unitize() && ctx.tu.Unitize() =>
                        Vector3d.CrossProduct(ctx.su, ctx.tu).Length < Math.Sin(RhinoMath.DefaultAngleTolerance)
                            ? Math.Abs((ctx.su * ctx.tu) - 1.0) < (1.0 - Math.Cos(RhinoMath.DefaultAngleTolerance))
                                ? ResultFactory.Create(value: Transform.Identity)
                                : Math.Abs((ctx.su * ctx.tu) + 1.0) < (1.0 - Math.Cos(RhinoMath.DefaultAngleTolerance))
                                    ? ((Func<Result<Transform>>)(() => {
                                        Vector3d axis = Math.Abs(ctx.su * Vector3d.XAxis) < 0.95 ? Vector3d.CrossProduct(ctx.su, Vector3d.XAxis) : Vector3d.CrossProduct(ctx.su, Vector3d.YAxis);
                                        return axis.Unitize() ? ResultFactory.Create(value: Transform.Rotation(Math.PI, axis, ctx.pt)) : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors);
                                    }))()
                                    : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors)
                            : ResultFactory.Create(value: Transform.Rotation(ctx.su, ctx.tu, ctx.pt)),
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
                },
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
        };

    internal static Result<Orientation.OptimizationResult> OptimizeOrientation(Brep brep, Orientation.OptimizationCriteria criteria, double tolerance) =>
        tolerance <= 0.0
            ? ResultFactory.Create<Orientation.OptimizationResult>(error: E.Validation.ToleranceAbsoluteInvalid)
            : brep.GetBoundingBox(accurate: true) switch {
                BoundingBox box when !box.IsValid => ResultFactory.Create<Orientation.OptimizationResult>(error: E.Geometry.TransformFailed.WithContext("Invalid bounding box")),
                BoundingBox box when criteria is Orientation.CenteredCriteria && ExtractCentroid(geometry: brep, useMassProperties: true) is Result<Point3d> r && !r.IsSuccess =>
                    ResultFactory.Create<Orientation.OptimizationResult>(error: E.Geometry.CentroidExtractionFailed),
                BoundingBox box => (testPlanes: GenerateTestPlanes(box), centroid: criteria is Orientation.CenteredCriteria ? ExtractCentroid(geometry: brep, useMassProperties: true).Value : Point3d.Unset) switch {
                    var ctx => ctx.testPlanes.Select(plane => EvaluateOrientation(brep, plane, criteria, tolerance, ctx.centroid))
                        .MaxBy(r => r.score) is (Transform best, double score, _) && score > 0.0
                            ? ResultFactory.Create(value: new Orientation.OptimizationResult(OptimalTransform: best, Score: score, CriteriaSatisfied: [criteria,]))
                            : ResultFactory.Create<Orientation.OptimizationResult>(error: E.Geometry.TransformFailed.WithContext("No valid orientation found")),
                },
            };

    private static Plane[] GenerateTestPlanes(BoundingBox box) =>
        (d1: new Vector3d(1, 1, 0), d2: new Vector3d(1, 0, 1), d3: new Vector3d(0, 1, 1)) switch {
            var d when d.d1.Unitize() && d.d2.Unitize() && d.d3.Unitize() => [
                new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis),
                new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis),
                new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis),
                new Plane(box.Center, d.d1, Vector3d.ZAxis),
                new Plane(box.Center, d.d2, Vector3d.YAxis),
                new Plane(box.Center, d.d3, Vector3d.XAxis),
            ],
            _ => [],
        };

    private static (Transform xform, double score, Orientation.OptimizationCriteria[] satisfied) EvaluateOrientation(
        Brep brep, Plane plane, Orientation.OptimizationCriteria criteria, double tolerance, Point3d centroid) =>
        Transform.PlaneToPlane(plane, Plane.WorldXY) switch {
            Transform xf => ((Func<(Transform, double, Orientation.OptimizationCriteria[])>)(() => {
                using Brep test = (Brep)brep.Duplicate();
                return test.Transform(xf) && test.GetBoundingBox(accurate: true) is BoundingBox testBox && testBox.IsValid
                    ? (
                        xf,
                        criteria switch {
                            Orientation.CompactCriteria => testBox.Diagonal.Length > tolerance ? 1.0 / testBox.Diagonal.Length : 0.0,
                            Orientation.CenteredCriteria when centroid != Point3d.Unset && testBox.Diagonal.Length > tolerance =>
                                ((Func<double>)(() => { Point3d c = centroid; c.Transform(xf); return Math.Max(0.0, 1.0 - (Math.Abs(c.Z) / testBox.Diagonal.Length)); }))(),
                            Orientation.FlatnessCriteria => ((Math.Abs(testBox.Max.X - testBox.Min.X) <= tolerance ? 1 : 0)
                                + (Math.Abs(testBox.Max.Y - testBox.Min.Y) <= tolerance ? 1 : 0)
                                + (Math.Abs(testBox.Max.Z - testBox.Min.Z) <= tolerance ? 1 : 0)) switch {
                                    int deg and >= 1 and <= 3 => deg / (double)OrientationConfig.MaxDegeneracyDimensions,
                                    _ => 0.0,
                                },
                            Orientation.CanonicalCriteria => (testBox.Min.Z >= -tolerance ? OrientationConfig.OrientationScoreWeight1 : 0.0)
                                + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? OrientationConfig.OrientationScoreWeight2 : 0.0)
                                + ((testBox.Max.Z - testBox.Min.Z) < (testBox.Diagonal.Length * OrientationConfig.LowProfileAspectRatio) ? OrientationConfig.OrientationScoreWeight3 : 0.0),
                            _ => 0.0,
                        },
                        new[] { criteria })
                    : (Transform.Identity, 0.0, Array.Empty<Orientation.OptimizationCriteria>());
            }))(),
        };

    internal static Result<Orientation.PatternDetectionResult> DetectPattern(GeometryBase[] geometries, double absoluteTolerance, double angleTolerance) =>
        geometries.Length < OrientationConfig.PatternMinInstances
            ? ResultFactory.Create<Orientation.PatternDetectionResult>(error: E.Geometry.InsufficientParameters.WithContext($"Pattern detection requires at least {OrientationConfig.PatternMinInstances} geometries, got {geometries.Length}"))
            : geometries.Select(g => ExtractCentroid(geometry: g, useMassProperties: false)).ToArray() switch {
                Result<Point3d>[] results when results.All(r => r.IsSuccess) =>
                    (centroids: results.Select(r => r.Value).ToArray(), deltas: results.Select(r => r.Value).Skip(1).Zip(results.Select(r => r.Value), (c2, c1) => c2 - c1).ToArray()) switch {
                        var ctx when ctx.deltas.Length > 0 && ctx.deltas.Average(v => v.Length) is double avgLen && avgLen > absoluteTolerance =>
                            ctx.deltas[0].Length >= absoluteTolerance && ctx.deltas.Skip(1).All(v => Math.Abs(v.Length - avgLen) / avgLen < absoluteTolerance && Vector3d.VectorAngle(ctx.deltas[0], v) <= angleTolerance)
                                ? ResultFactory.Create(value: new Orientation.PatternDetectionResult(
                                    Pattern: new Orientation.LinearPattern(),
                                    IdealTransforms: [.. Enumerable.Range(0, ctx.centroids.Length).Select(i => Transform.Translation(ctx.deltas[0] * i)),],
                                    Anomalies: [.. ctx.deltas.Select((v, i) => (v, i)).Where(p => Math.Abs(p.v.Length - avgLen) / avgLen >= (absoluteTolerance * OrientationConfig.PatternAnomalyThreshold)).Select(p => p.i),],
                                    Deviation: ctx.deltas.Sum(v => Math.Abs(v.Length - avgLen)) / ctx.centroids.Length))
                                : ((Func<Result<Orientation.PatternDetectionResult>>)(() => {
                                    Point3d center = new(ctx.centroids.Average(p => p.X), ctx.centroids.Average(p => p.Y), ctx.centroids.Average(p => p.Z));
                                    double[] radii = [.. ctx.centroids.Select(p => p.DistanceTo(center)),];
                                    return (center, radii) switch {
                                        (Point3d c, double[] r) when r.Average() is double avgRadius && avgRadius > absoluteTolerance && r.All(rad => Math.Abs(rad - avgRadius) / avgRadius < absoluteTolerance) =>
                                            ResultFactory.Create(value: new Orientation.PatternDetectionResult(
                                                Pattern: new Orientation.RadialPattern(),
                                                IdealTransforms: [.. Enumerable.Range(0, ctx.centroids.Length).Select(i => Transform.Rotation(RhinoMath.TwoPI * i / ctx.centroids.Length, Vector3d.ZAxis, c)),],
                                                Anomalies: [.. r.Select((rad, i) => (rad, i)).Where(p => Math.Abs(p.rad - avgRadius) / avgRadius >= (absoluteTolerance * OrientationConfig.PatternAnomalyThreshold)).Select(p => p.i),],
                                                Deviation: r.Sum(rad => Math.Abs(rad - avgRadius)) / ctx.centroids.Length)),
                                        _ => ResultFactory.Create(value: new Orientation.PatternDetectionResult(Pattern: new Orientation.NoPattern(), IdealTransforms: [], Anomalies: [], Deviation: double.NaN)),
                                    };
                                }))(),
                        _ => ResultFactory.Create<Orientation.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular")),
                    },
                Result<Point3d>[] results => ResultFactory.Create<Orientation.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext($"Centroid extraction failed for {results.Count(r => !r.IsSuccess)} geometries")),
            };

    private static Orientation.RelationshipType ClassifyRelationship(Plane pa, Plane pb, double angleTolerance) =>
        Math.Abs(Vector3d.Multiply(pa.ZAxis, pb.ZAxis)) switch {
            double dot when Math.Abs(dot - 1.0) < 1.0 - Math.Cos(angleTolerance) => new Orientation.Parallel(),
            double dot when Math.Abs(dot) < Math.Sin(angleTolerance) => new Orientation.Perpendicular(),
            _ => new Orientation.Oblique(),
        };

    private static Orientation.SymmetryType ClassifySymmetry(GeometryBase geometryA, GeometryBase geometryB, Plane pa, Plane pb, double tolerance, double angleTol) =>
        (geometryA, geometryB) switch {
            (Brep ba, Brep bb) when ba.Vertices.Count == bb.Vertices.Count =>
                (mirror: (pb.Origin - pa.Origin).Length > RhinoMath.ZeroTolerance
                    ? new Plane(pa.Origin + ((pb.Origin - pa.Origin) * 0.5), pb.Origin - pa.Origin)
                    : new Plane(pa.Origin, pa.ZAxis),
                 reflected: ba.Vertices.Select(v => { Point3d p = v.Location; p.Transform(Transform.Mirror((pb.Origin - pa.Origin).Length > RhinoMath.ZeroTolerance ? new Plane(pa.Origin + ((pb.Origin - pa.Origin) * 0.5), pb.Origin - pa.Origin) : new Plane(pa.Origin, pa.ZAxis))); return p; }).ToArray()) switch {
                     var ctx when ctx.mirror.IsValid && ctx.reflected.All(ra => bb.Vertices.Any(vb => ra.DistanceTo(vb.Location) < tolerance))
                         && bb.Vertices.All(vb => ctx.reflected.Any(ra => ra.DistanceTo(vb.Location) < tolerance)) => new Orientation.MirrorSymmetry(),
                     _ => new Orientation.NoSymmetry(),
                 },
            (Curve ca, Curve cb) when ca.SpanCount == cb.SpanCount && pa.ZAxis.IsValid && pa.ZAxis.Length > tolerance =>
                (samplesA: Enumerable.Range(0, OrientationConfig.RotationSymmetrySampleCount).Select(i => ca.PointAt(ca.Domain.ParameterAt(i / (double)(OrientationConfig.RotationSymmetrySampleCount - 1)))).ToArray(),
                 samplesB: Enumerable.Range(0, OrientationConfig.RotationSymmetrySampleCount).Select(i => cb.PointAt(cb.Domain.ParameterAt(i / (double)(OrientationConfig.RotationSymmetrySampleCount - 1)))).ToArray()) switch {
                     var samples => new[] { 0, samples.samplesA.Length / 2, samples.samplesA.Length - 1 }.Select(idx =>
                         (projA: samples.samplesA[idx] - pa.Origin - (((samples.samplesA[idx] - pa.Origin) * pa.ZAxis) * pa.ZAxis),
                          projB: samples.samplesB[idx] - pa.Origin - (((samples.samplesB[idx] - pa.Origin) * pa.ZAxis) * pa.ZAxis)) switch {
                              var p when p.projA.Length < tolerance || p.projB.Length < tolerance => double.NaN,
                              var p => Vector3d.CrossProduct(p.projA, p.projB) * pa.ZAxis < 0 ? -Vector3d.VectorAngle(p.projA, p.projB) : Vector3d.VectorAngle(p.projA, p.projB),
                          }).Where(a => !double.IsNaN(a)).ToArray() switch {
                              double[] angles when angles.Length > 0 && angles.All(a => Math.Abs(a - angles[0]) < angleTol)
                                 && samples.samplesA.Zip(samples.samplesB, (ptA, ptB) => { Point3d r = ptA; r.Transform(Transform.Rotation(angles[0], pa.ZAxis, pa.Origin)); return r.DistanceTo(ptB); }).All(d => d < tolerance)
                                 => new Orientation.RotationalSymmetry(),
                              _ => new Orientation.NoSymmetry(),
                          },
                 },
            _ => new Orientation.NoSymmetry(),
        };
}
