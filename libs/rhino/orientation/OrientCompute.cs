using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Optimization, relative orientation, and pattern alignment algorithmic implementations.</summary>
[Pure]
internal static class OrientCompute {
    /// <summary>Optimize orientation based on algebraic criteria.</summary>
    internal static Result<Orient.OptimizationResult> OptimizeOrientation(
        Brep brep,
        Orient.OptimizationCriteria criteria,
        double tolerance) =>
        tolerance <= 0.0
            ? ResultFactory.Create<Orient.OptimizationResult>(error: E.Validation.ToleranceAbsoluteInvalid)
            : brep.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
                ? ((Func<Result<Orient.OptimizationResult>>)(() => {
                    Result<Point3d> centroidResult = criteria is Orient.CentroidCriteria or Orient.CanonicalCriteria
                        ? OrientCore.ExtractCentroid(brep, useMassProperties: true)
                        : ResultFactory.Create(value: box.Center);
                    return (criteria is Orient.CentroidCriteria && !centroidResult.IsSuccess)
                        ? ResultFactory.Create<Orient.OptimizationResult>(error: E.Geometry.CentroidExtractionFailed)
                        : ComputeOptimalTransform(brep, box, criteria, centroidResult, tolerance);
                }))()
                : ResultFactory.Create<Orient.OptimizationResult>(error: E.Geometry.TransformFailed.WithContext("Invalid bounding box"));

    /// <summary>Compute optimal transform from test orientations.</summary>
    private static Result<Orient.OptimizationResult> ComputeOptimalTransform(
        Brep brep,
        BoundingBox box,
        Orient.OptimizationCriteria criteria,
        Result<Point3d> centroidResult,
        double tolerance) =>
        new (Vector3d V, Vector3d Axis)[] {
            (Vector3d.XAxis, Vector3d.YAxis), (Vector3d.YAxis, Vector3d.ZAxis), (Vector3d.XAxis, Vector3d.ZAxis),
            (new Vector3d(1, 1, 0) is Vector3d d1 && d1.Unitize() ? d1 : d1, Vector3d.ZAxis),
            (new Vector3d(1, 0, 1) is Vector3d d2 && d2.Unitize() ? d2 : d2, Vector3d.YAxis),
            (new Vector3d(0, 1, 1) is Vector3d d3 && d3.Unitize() ? d3 : d3, Vector3d.XAxis),
        }.Select(pair => (Plane: new Plane(box.Center, pair.V, pair.Axis), Xf: Transform.PlaneToPlane(new Plane(box.Center, pair.V, pair.Axis), Plane.WorldXY)))
        .Select(entry => ((Brep)brep.Duplicate()) is Brep test && test.Transform(entry.Xf) && test.GetBoundingBox(accurate: true) is BoundingBox testBox && testBox.IsValid
            ? (entry.Xf, ScoreCriteria(criteria, testBox, centroidResult, tolerance, entry.Xf))
            : (Transform.Identity, 0.0))
        .MaxBy(r => r.Item2) is (Transform best, double bestScore) && bestScore > 0
            ? ResultFactory.Create(value: new Orient.OptimizationResult(OptimalTransform: best, Score: bestScore, CriteriaMet: criteria))
            : ResultFactory.Create<Orient.OptimizationResult>(error: E.Geometry.TransformFailed.WithContext("No valid orientation found"));

    /// <summary>Score a transform based on criteria type.</summary>
    private static double ScoreCriteria(
        Orient.OptimizationCriteria criteria,
        BoundingBox testBox,
        Result<Point3d> centroidResult,
        double tolerance,
        Transform xf) =>
        criteria switch {
            Orient.CompactCriteria => testBox.Diagonal.Length > tolerance ? 1.0 / testBox.Diagonal.Length : 0.0,
            Orient.CentroidCriteria => centroidResult.IsSuccess && testBox.Diagonal.Length > tolerance
                ? ((Func<double>)(() => {
                    Point3d centroid = centroidResult.Value;
                    centroid.Transform(xf);
                    return Math.Max(0.0, 1.0 - (Math.Abs(centroid.Z) / testBox.Diagonal.Length));
                }))()
                : 0.0,
            Orient.FlatnessCriteria => ((Math.Abs(testBox.Max.X - testBox.Min.X) <= tolerance ? 1 : 0)
                + (Math.Abs(testBox.Max.Y - testBox.Min.Y) <= tolerance ? 1 : 0)
                + (Math.Abs(testBox.Max.Z - testBox.Min.Z) <= tolerance ? 1 : 0)) switch {
                    0 => 0.0,
                    int degeneracy and >= 1 and <= 3 => degeneracy / (double)OrientConfig.MaxDegeneracyDimensions,
                    _ => 0.0,
                },
            Orient.CanonicalCriteria => (testBox.Min.Z >= -tolerance ? OrientConfig.ScoreWeightCompact : 0.0)
                + (Math.Abs(testBox.Center.X) < tolerance && Math.Abs(testBox.Center.Y) < tolerance ? OrientConfig.ScoreWeightCentroid : 0.0)
                + ((testBox.Max.Z - testBox.Min.Z) < (testBox.Diagonal.Length * OrientConfig.LowProfileAspectRatio) ? OrientConfig.ScoreWeightProfile : 0.0),
            _ => 0.0,
        };

    /// <summary>Compute relative orientation with symmetry and relationship classification.</summary>
    internal static Result<Orient.RelativeOrientationResult> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        (OrientCore.ExtractPlane(geometryA), OrientCore.ExtractPlane(geometryB)) switch {
            (Result<Plane> { IsSuccess: true, Value: Plane pa }, Result<Plane> { IsSuccess: true, Value: Plane pb }) =>
                ResultFactory.Create(value: new Orient.RelativeOrientationResult(
                    RelativeTransform: Transform.PlaneToPlane(pa, pb),
                    Twist: Vector3d.VectorAngle(pa.XAxis, pb.XAxis),
                    Tilt: Vector3d.VectorAngle(pa.ZAxis, pb.ZAxis),
                    Symmetry: ClassifySymmetry(geometryA, geometryB, pa, pb, context.AbsoluteTolerance, context),
                    Relationship: ClassifyRelationship(pa, pb, context.AngleToleranceRadians))),
            _ => ResultFactory.Create<Orient.RelativeOrientationResult>(error: E.Geometry.UnsupportedOrientationType),
        };

    /// <summary>Classify symmetry between two geometries.</summary>
    private static Orient.SymmetryKind ClassifySymmetry(
        GeometryBase geometryA,
        GeometryBase geometryB,
        Plane pa,
        Plane pb,
        double tolerance,
        IGeometryContext context) =>
        (geometryA, geometryB) switch {
            (Brep ba, Brep bb) when ba.Vertices.Count == bb.Vertices.Count => CheckMirrorSymmetry(ba, bb, pa, pb, tolerance)
                ? new Orient.MirrorSymmetry()
                : new Orient.NoSymmetry(),
            (Curve ca, Curve cb) when ca.SpanCount == cb.SpanCount && pa.ZAxis.IsValid && pa.ZAxis.Length > tolerance =>
                CheckRotationalSymmetry(ca, cb, pa, tolerance, context)
                    ? new Orient.RotationalSymmetry()
                    : new Orient.NoSymmetry(),
            _ => new Orient.NoSymmetry(),
        };

    /// <summary>Check mirror symmetry between two Breps.</summary>
    private static bool CheckMirrorSymmetry(Brep ba, Brep bb, Plane pa, Plane pb, double tolerance) =>
        ((pb.Origin - pa.Origin).Length > RhinoMath.ZeroTolerance
            ? new Plane(pa.Origin + ((pb.Origin - pa.Origin) * 0.5), pb.Origin - pa.Origin)
            : new Plane(pa.Origin, pa.ZAxis)) is Plane mirror
        && mirror.IsValid
        && ba.Vertices.Select(va => va.Location is Point3d pt && pt.Transform(Transform.Mirror(mirrorPlane: mirror)) ? pt : pt).ToArray() is Point3d[] reflectedA
        && reflectedA.All(ra => bb.Vertices.Any(vb => ra.DistanceTo(vb.Location) < tolerance))
        && bb.Vertices.All(vb => reflectedA.Any(ra => ra.DistanceTo(vb.Location) < tolerance));

    /// <summary>Check rotational symmetry between two curves.</summary>
    private static bool CheckRotationalSymmetry(Curve ca, Curve cb, Plane pa, double tolerance, IGeometryContext context) =>
        Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount).Select(i => ca.PointAt(ca.Domain.ParameterAt(i / (double)(OrientConfig.RotationSymmetrySampleCount - 1)))).ToArray() is Point3d[] samplesA
        && Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount).Select(i => cb.PointAt(cb.Domain.ParameterAt(i / (double)(OrientConfig.RotationSymmetrySampleCount - 1)))).ToArray() is Point3d[] samplesB
        && new[] { 0, samplesA.Length / 2, samplesA.Length - 1, }
            .Select(idx => (samplesA[idx] - pa.Origin) is Vector3d va && (samplesB[idx] - pa.Origin) is Vector3d vb
                && (va - ((va * pa.ZAxis) * pa.ZAxis)) is Vector3d projA && (vb - ((vb * pa.ZAxis) * pa.ZAxis)) is Vector3d projB
                    ? projA.Length < tolerance || projB.Length < tolerance
                        ? double.NaN
                        : Vector3d.CrossProduct(projA, projB) * pa.ZAxis < 0 ? -Vector3d.VectorAngle(projA, projB) : Vector3d.VectorAngle(projA, projB)
                    : double.NaN)
            .Where(a => !double.IsNaN(a)).ToArray() is double[] angles
        && angles.Length > 0
        && angles.All(a => Math.Abs(a - angles[0]) < context.AngleToleranceRadians)
        && samplesA.Zip(samplesB, (ptA, ptB) => ptA is Point3d r && r.Transform(Transform.Rotation(angles[0], pa.ZAxis, pa.Origin)) ? r.DistanceTo(ptB) : double.MaxValue)
            .All(dist => dist < tolerance);

    /// <summary>Classify relationship between two planes.</summary>
    private static Orient.RelationshipKind ClassifyRelationship(Plane pa, Plane pb, double angleTolerance) =>
        Math.Abs(Vector3d.Multiply(pa.ZAxis, pb.ZAxis)) switch {
            double dot when Math.Abs(dot - 1.0) < 1.0 - Math.Cos(angleTolerance) => new Orient.ParallelRelationship(),
            double dot when Math.Abs(dot) < Math.Sin(angleTolerance) => new Orient.PerpendicularRelationship(),
            _ => new Orient.ObliqueRelationship(),
        };

    /// <summary>Detect linear or radial patterns with anomaly identification.</summary>
    internal static Result<Orient.PatternResult> DetectPattern(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(g => g.All(item => item?.IsValid == true), error: E.Validation.GeometryInvalid)
            .Bind(validGeometries => validGeometries.Length >= OrientConfig.PatternMinInstances
                ? DetectPatternFromGeometries(validGeometries, context)
                : ResultFactory.Create<Orient.PatternResult>(error: E.Geometry.InsufficientParameters.WithContext($"Pattern detection requires at least {OrientConfig.PatternMinInstances} geometries, got {validGeometries.Length}")));

    /// <summary>Detect pattern from validated geometries.</summary>
    private static Result<Orient.PatternResult> DetectPatternFromGeometries(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        geometries.Select(g => OrientCore.ExtractCentroid(g, useMassProperties: false)).ToArray() is Result<Point3d>[] results
            && results.All(r => r.IsSuccess)
            && results.Select(r => r.Value).ToArray() is Point3d[] centroids
            && centroids.Length >= 3
                ? TryDetectLinearOrRadial(centroids, context)
                : ResultFactory.Create<Orient.PatternResult>(error: E.Geometry.PatternDetectionFailed.WithContext(
                    results.All(r => r.IsSuccess) ? "Insufficient valid centroids" : $"Centroid extraction failed for {results.Count(r => !r.IsSuccess)} geometries"));

    /// <summary>Try to detect linear or radial pattern from centroids.</summary>
    private static Result<Orient.PatternResult> TryDetectLinearOrRadial(Point3d[] centroids, IGeometryContext context) =>
        centroids.Skip(1).Zip(centroids, (c2, c1) => c2 - c1).ToArray() is Vector3d[] deltas
        && deltas.Average(v => v.Length) is double avgLen
        && avgLen > context.AbsoluteTolerance
            ? TryLinearPattern(centroids, deltas, avgLen, context) is Result<Orient.PatternResult> { IsSuccess: true } linear ? linear : TryRadialPattern(centroids, context)
            : ResultFactory.Create<Orient.PatternResult>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular"));

    /// <summary>Try to detect linear pattern.</summary>
    private static Result<Orient.PatternResult> TryLinearPattern(
        Point3d[] centroids,
        Vector3d[] deltas,
        double avgLen,
        IGeometryContext context) =>
        deltas[0].Length >= context.AbsoluteTolerance
            && deltas.Skip(1).All(v => Math.Abs(v.Length - avgLen) / avgLen < context.AbsoluteTolerance
                && Vector3d.VectorAngle(deltas[0], v) <= context.AngleToleranceRadians)
            ? ResultFactory.Create(value: new Orient.PatternResult(
                Pattern: new Orient.LinearPattern(),
                IdealTransforms: [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Translation(deltas[0] * i)),],
                Anomalies: [.. deltas.Select((v, i) => (v, i)).Where(pair => Math.Abs(pair.v.Length - avgLen) / avgLen >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.i),],
                Deviation: deltas.Sum(v => Math.Abs(v.Length - avgLen)) / centroids.Length))
            : ResultFactory.Create<Orient.PatternResult>(error: E.Geometry.PatternDetectionFailed);

    /// <summary>Try to detect radial pattern.</summary>
    private static Result<Orient.PatternResult> TryRadialPattern(Point3d[] centroids, IGeometryContext context) =>
        new Point3d(centroids.Average(p => p.X), centroids.Average(p => p.Y), centroids.Average(p => p.Z)) is Point3d center
        && centroids.Select(p => p.DistanceTo(center)).ToArray() is double[] radii
        && radii.Average() is double avgRadius
        && avgRadius > context.AbsoluteTolerance
        && radii.All(r => Math.Abs(r - avgRadius) / avgRadius < context.AbsoluteTolerance)
            ? ResultFactory.Create(value: new Orient.PatternResult(
                Pattern: new Orient.RadialPattern(),
                IdealTransforms: [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Rotation(RhinoMath.TwoPI * i / centroids.Length, Vector3d.ZAxis, center)),],
                Anomalies: [.. radii.Select((r, i) => (r, i)).Where(pair => Math.Abs(pair.r - avgRadius) / avgRadius >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.i),],
                Deviation: radii.Sum(r => Math.Abs(r - avgRadius)) / centroids.Length))
            : ResultFactory.Create<Orient.PatternResult>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular"));
}
