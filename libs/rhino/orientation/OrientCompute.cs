using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Optimization, relative orientation, and pattern alignment algorithms.</summary>
[Pure]
internal static class OrientCompute {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignToPlane<T>(T geometry, Plane target) where T : GeometryBase =>
        !target.IsValid
            ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane)
            : ExtractPlane(geometry)
                .Map(source => Transform.PlaneToPlane(source, target))
                .Bind(xform => ApplyTransform(geometry, xform));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignToCurveFrame<T>(T geometry, Curve curve, double parameter) where T : GeometryBase =>
        curve is null
            ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Validation.GeometryInvalid.WithContext("Curve cannot be null"))
            : curve.FrameAt(parameter, out Plane frame) && frame.IsValid
                ? AlignToPlane(geometry, frame)
                : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidCurveParameter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignToSurfaceFrame<T>(T geometry, Surface surface, double u, double v) where T : GeometryBase =>
        surface is null
            ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Validation.GeometryInvalid.WithContext("Surface cannot be null"))
            : surface.FrameAt(u, v, out Plane frame) && frame.IsValid
                ? AlignToPlane(geometry, frame)
                : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidSurfaceUV);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignToWorldPlane<T>(T geometry, Plane targetPlane, Vector3d sourceXAxis, Vector3d sourceYAxis) where T : GeometryBase =>
        geometry.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
            ? ApplyTransform(geometry, Transform.PlaneToPlane(new Plane(box.Center, sourceXAxis, sourceYAxis), targetPlane))
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Validation.BoundingBoxInvalid);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignBoundingBoxToOrigin<T>(T geometry) where T : GeometryBase =>
        geometry.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
            ? ApplyTransform(geometry, Transform.Translation(Point3d.Origin - box.Center))
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Validation.BoundingBoxInvalid);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignCentroidToOrigin<T>(T geometry) where T : GeometryBase =>
        ExtractCentroid(geometry, useMassProperties: true)
            .Map(center => Transform.Translation(Point3d.Origin - center))
            .Bind(xform => ApplyTransform(geometry, xform));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignBoundingBoxToPoint<T>(T geometry, Point3d target) where T : GeometryBase =>
        ExtractCentroid(geometry, useMassProperties: false)
            .Map(center => Transform.Translation(target - center))
            .Bind(xform => ApplyTransform(geometry, xform));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignCentroidToPoint<T>(T geometry, Point3d target) where T : GeometryBase =>
        ExtractCentroid(geometry, useMassProperties: true)
            .Map(center => Transform.Translation(target - center))
            .Bind(xform => ApplyTransform(geometry, xform));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignVector<T>(T geometry, Vector3d source, Vector3d target, Orient.AnchorSpecification anchor) where T : GeometryBase =>
        (source.Length > RhinoMath.ZeroTolerance, target.Length > RhinoMath.ZeroTolerance) switch {
            (true, true) => ResolveAnchor(geometry, anchor)
                .Bind(anchorPoint => BuildVectorRotation(source, target, anchorPoint))
                .Bind(xform => ApplyTransform(geometry, xform)),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationVectors),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> AlignBestFit<T>(T geometry) where T : GeometryBase =>
        ExtractBestFitPlane(geometry)
            .Bind(plane => ApplyTransform(geometry, Transform.PlaneToPlane(plane, Plane.WorldXY)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> MirrorGeometry<T>(T geometry, Plane plane) where T : GeometryBase =>
        plane.IsValid
            ? ApplyTransform(geometry, Transform.Mirror(plane))
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> FlipGeometry<T>(T geometry) where T : GeometryBase =>
        geometry.Duplicate() switch {
            Curve c when c.Reverse() => ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)c,]),
            Brep b => ((Func<Result<IReadOnlyList<T>>>)(() => { b.Flip(); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)b,]); }))(),
            Extrusion e => e.ToBrep() is Brep br
                ? ((Func<Result<IReadOnlyList<T>>>)(() => { br.Flip(); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)br,]); }))()
                : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
            Mesh m => ((Func<Result<IReadOnlyList<T>>>)(() => { m.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)m,]); }))(),
            null => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    internal static Result<Orient.OrientationOptimizationResult> OptimizeOrientation(
        Brep brep,
        Orient.OptimizationCriterion criterion,
        IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, V.Standard | V.Topology | V.BoundingBox | V.MassProperties,])
            .Bind(validBrep => criterion.Code is < 1 or > 4
                ? ResultFactory.Create<Orient.OrientationOptimizationResult>(error: E.Geometry.InvalidOrientationMode.WithContext($"Criterion code {criterion.Code} is unsupported"))
                : context.AbsoluteTolerance <= 0.0
                    ? ResultFactory.Create<Orient.OrientationOptimizationResult>(error: E.Validation.ToleranceAbsoluteInvalid)
                    : validBrep.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
                        ? EvaluateOrientationCriteria(validBrep, criterion, box, context)
                        : ResultFactory.Create<Orient.OrientationOptimizationResult>(error: E.Geometry.TransformFailed.WithContext("Invalid bounding box")));

    internal static Result<Orient.RelativeOrientationResult> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) {
        Result<Plane> planeA = ExtractPlane(geometryA);
        if (!planeA.IsSuccess) {
            return ResultFactory.Create<Orient.RelativeOrientationResult>(error: planeA.Errors.Length > 0 ? planeA.Errors[0] : E.Geometry.OrientationFailed);
        }

        Result<Plane> planeB = ExtractPlane(geometryB);
        if (!planeB.IsSuccess) {
            return ResultFactory.Create<Orient.RelativeOrientationResult>(error: planeB.Errors.Length > 0 ? planeB.Errors[0] : E.Geometry.OrientationFailed);
        }

        Plane pa = planeA.Value;
        Plane pb = planeB.Value;
        Transform xform = Transform.PlaneToPlane(pa, pb);
        double twist = Vector3d.VectorAngle(pa.XAxis, pb.XAxis);
        double tilt = Vector3d.VectorAngle(pa.ZAxis, pb.ZAxis);
        byte symmetry = ClassifySymmetry(geometryA, geometryB, pa, pb, context.AbsoluteTolerance, context);
        byte relationship = ClassifyRelationship(pa, pb, context.AngleToleranceRadians);

        return double.IsNaN(twist) || double.IsNaN(tilt)
            ? ResultFactory.Create<Orient.RelativeOrientationResult>(error: E.Geometry.OrientationFailed)
            : ResultFactory.Create(value: new Orient.RelativeOrientationResult(
                RelativeTransform: xform,
                Twist: twist,
                Tilt: tilt,
                Symmetry: CreateSymmetry(symmetry),
                Relationship: CreateRelationship(relationship)));
    }

    internal static Result<Orient.PatternDetectionResult> DetectPattern(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(g => g.All(item => item?.IsValid == true), error: E.Validation.GeometryInvalid)
            .Bind(validGeometries => validGeometries.Length >= OrientConfig.PatternMinInstances
                ? IdentifyPattern(validGeometries, context)
                : ResultFactory.Create<Orient.PatternDetectionResult>(error: E.Geometry.InsufficientParameters.WithContext($"Pattern detection requires at least {OrientConfig.PatternMinInstances} geometries, got {validGeometries.Length}")));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ApplyTransform<T>(T geometry, Transform transform) where T : GeometryBase =>
        (T)geometry.Duplicate() switch {
            T dup when dup.Transform(transform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Point3d> ExtractCentroid(GeometryBase geometry, bool useMassProperties) =>
        (geometry, useMassProperties) switch {
            (Brep brep, true) when brep.IsSolid => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Brep brep, true) when brep.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(brep); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Extrusion ext, true) when ext.IsSolid => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(ext); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Extrusion ext, true) when ext.IsClosed(0) && ext.IsClosed(1) => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(ext); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Mesh mesh, true) when mesh.IsClosed => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Mesh mesh, true) => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(mesh); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Curve curve, true) => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(curve); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (GeometryBase g, false) => g.GetBoundingBox(accurate: true) switch {
                BoundingBox b when b.IsValid => ResultFactory.Create(value: b.Center),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Point3d> ResolveAnchor(GeometryBase geometry, Orient.AnchorSpecification anchor) =>
        anchor switch {
            Orient.AnchorSpecification.CustomAnchor custom => ResultFactory.Create(value: custom.Anchor),
            Orient.AnchorSpecification.BoundingBoxAnchor => geometry.GetBoundingBox(accurate: true) is BoundingBox box && box.IsValid
                ? ResultFactory.Create(value: box.Center)
                : ResultFactory.Create<Point3d>(error: E.Validation.BoundingBoxInvalid),
            _ => ResultFactory.Create<Point3d>(error: E.Geometry.InvalidOrientationMode.WithContext("Unsupported anchor specification")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Plane> ExtractPlane(GeometryBase geometry) {
        Type runtimeType = geometry.GetType();
        if (OrientConfig.PlaneExtractors.TryGetValue(runtimeType, out OrientConfig.PlaneExtractorMetadata? metadata)) {
            return metadata.Extractor(geometry);
        }

        foreach (KeyValuePair<Type, OrientConfig.PlaneExtractorMetadata> entry in OrientConfig.PlaneExtractors) {
            if (entry.Key.IsAssignableFrom(runtimeType)) {
                return entry.Value.Extractor(geometry);
            }
        }

        return ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(runtimeType.Name));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Plane> ExtractBestFitPlane(GeometryBase geometry) =>
        geometry switch {
            PointCloud pc when pc.Count >= OrientConfig.BestFitMinPoints => FitPlane(pc.GetPoints()),
            PointCloud pc => ResultFactory.Create<Plane>(error: E.Geometry.InsufficientParameters.WithContext($"Best-fit plane requires {OrientConfig.BestFitMinPoints} points, got {pc.Count}")),
            Mesh mesh when mesh.Vertices.Count >= OrientConfig.BestFitMinPoints => FitPlane(mesh.Vertices.ToPoint3dArray()),
            Mesh mesh => ResultFactory.Create<Plane>(error: E.Geometry.InsufficientParameters.WithContext($"Best-fit plane requires {OrientConfig.BestFitMinPoints} points, got {mesh.Vertices.Count}")),
            _ => ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Plane> FitPlane(Point3d[] points) =>
        Plane.FitPlaneToPoints(points, out Plane plane) == PlaneFitResult.Success && ComputeRms(points, plane) <= OrientConfig.BestFitResidualThreshold
            ? ResultFactory.Create(value: plane)
            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ComputeRms(Point3d[] points, Plane plane) {
        double sum = 0.0;
        for (int i = 0; i < points.Length; i++) {
            double distance = plane.DistanceTo(points[i]);
            sum += distance * distance;
        }
        return Math.Sqrt(sum / points.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> BuildVectorRotation(Vector3d source, Vector3d target, Point3d anchor) {
        Vector3d su = new(source);
        Vector3d tu = new(target);
        _ = su.Unitize();
        _ = tu.Unitize();
        double dot = su * tu;
        double crossLength = Vector3d.CrossProduct(su, tu).Length;

        return crossLength < RhinoMath.SqrtEpsilon
            ? Math.Abs(dot - 1.0) < RhinoMath.SqrtEpsilon
                ? ResultFactory.Create(value: Transform.Identity)
                : Math.Abs(dot + 1.0) < RhinoMath.SqrtEpsilon
                    ? BuildHalfTurn(su, anchor)
                    : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors)
            : ResultFactory.Create(value: Transform.Rotation(su, tu, anchor));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> BuildHalfTurn(Vector3d source, Point3d anchor) {
        Vector3d axisCandidate = Math.Abs(source * Vector3d.XAxis) < 0.95
            ? Vector3d.CrossProduct(source, Vector3d.XAxis)
            : Vector3d.CrossProduct(source, Vector3d.YAxis);
        return axisCandidate.Unitize()
            ? ResultFactory.Create(value: Transform.Rotation(Math.PI, axisCandidate, anchor))
            : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors);
    }

    private static Result<Orient.OrientationOptimizationResult> EvaluateOrientationCriteria(
        Brep brep,
        Orient.OptimizationCriterion criterion,
        BoundingBox box,
        IGeometryContext context) {
        Result<Point3d> centroidResult = ExtractCentroid(brep, useMassProperties: true);
        if (criterion.Code == 2 && !centroidResult.IsSuccess) {
            return ResultFactory.Create<Orient.OrientationOptimizationResult>(error: E.Geometry.CentroidExtractionFailed);
        }

        Vector3d diag1 = new Vector3d(1, 1, 0);
        Vector3d diag2 = new Vector3d(1, 0, 1);
        Vector3d diag3 = new Vector3d(0, 1, 1);
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

        (Transform Transform, double Score, Orient.OptimizationCriterion[] Criteria)[] results = [
            .. testPlanes.Select(plane => EvaluatePlaneCandidate(brep, plane, criterion, centroidResult, context)),
        ];

        (Transform Transform, double Score, Orient.OptimizationCriterion[] Criteria)? best = results.MaxBy(result => result.Score);
        return best is { Score: > 0.0 } winner
            ? ResultFactory.Create(value: new Orient.OrientationOptimizationResult(winner.Transform, winner.Score, winner.Criteria))
            : ResultFactory.Create<Orient.OrientationOptimizationResult>(error: E.Geometry.TransformFailed.WithContext("No valid orientation found"));
    }

    private static (Transform Transform, double Score, Orient.OptimizationCriterion[] Criteria) EvaluatePlaneCandidate(
        Brep brep,
        Plane plane,
        Orient.OptimizationCriterion criterion,
        Result<Point3d> centroid,
        IGeometryContext context) {
        Transform xf = Transform.PlaneToPlane(plane, Plane.WorldXY);
        using Brep test = (Brep)brep.Duplicate();
        if (!test.Transform(xf)) {
            return (Transform.Identity, 0.0, Array.Empty<Orient.OptimizationCriterion>());
        }

        BoundingBox testBox = test.GetBoundingBox(accurate: true);
        if (!testBox.IsValid) {
            return (Transform.Identity, 0.0, Array.Empty<Orient.OptimizationCriterion>());
        }

        double score = criterion.Code switch {
            1 => testBox.Diagonal.Length > context.AbsoluteTolerance ? 1.0 / testBox.Diagonal.Length : 0.0,
            2 => centroid.IsSuccess && testBox.Diagonal.Length > context.AbsoluteTolerance
                ? ((Func<double>)(() => {
                    Point3d c = centroid.Value;
                    c.Transform(xf);
                    return Math.Max(0.0, 1.0 - (Math.Abs(c.Z) / testBox.Diagonal.Length));
                }))()
                : 0.0,
            3 => CountDegeneracy(testBox, context.AbsoluteTolerance),
            4 => ((testBox.Min.Z >= -context.AbsoluteTolerance ? OrientConfig.OrientationScoreWeight1 : 0.0)
                + (Math.Abs(testBox.Center.X) < context.AbsoluteTolerance && Math.Abs(testBox.Center.Y) < context.AbsoluteTolerance ? OrientConfig.OrientationScoreWeight2 : 0.0)
                + ((testBox.Max.Z - testBox.Min.Z) < (testBox.Diagonal.Length * OrientConfig.LowProfileAspectRatio) ? OrientConfig.OrientationScoreWeight3 : 0.0)),
            _ => 0.0,
        };

        return (xf, score, score > 0.0 ? [criterion,] : Array.Empty<Orient.OptimizationCriterion>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CountDegeneracy(BoundingBox box, double tolerance) {
        double degeneracy = 0.0;
        degeneracy += Math.Abs(box.Max.X - box.Min.X) <= tolerance ? 1.0 : 0.0;
        degeneracy += Math.Abs(box.Max.Y - box.Min.Y) <= tolerance ? 1.0 : 0.0;
        degeneracy += Math.Abs(box.Max.Z - box.Min.Z) <= tolerance ? 1.0 : 0.0;
        return degeneracy switch {
            0.0 => 0.0,
            _ => degeneracy / OrientConfig.MaxDegeneracyDimensions,
        };
    }

    private static byte ClassifySymmetry(
        GeometryBase geometryA,
        GeometryBase geometryB,
        Plane planeA,
        Plane planeB,
        double tolerance,
        IGeometryContext context) {
        return (geometryA, geometryB) switch {
            (Brep brepA, Brep brepB) when brepA.Vertices.Count == brepB.Vertices.Count =>
                EvaluateMirrorSymmetry(brepA, brepB, planeA, planeB, tolerance),
            (Curve curveA, Curve curveB) when curveA.SpanCount == curveB.SpanCount =>
                EvaluateRotationalSymmetry(curveA, curveB, planeA, tolerance, context),
            _ => 0,
        };
    }

    private static byte EvaluateMirrorSymmetry(Brep first, Brep second, Plane planeA, Plane planeB, double tolerance) {
        if ((planeB.Origin - planeA.Origin).Length > RhinoMath.ZeroTolerance) {
            Plane mirror = new Plane(planeA.Origin + ((planeB.Origin - planeA.Origin) * 0.5), planeB.Origin - planeA.Origin);
            return mirror.IsValid && CheckMirror(first, second, mirror, tolerance) ? (byte)1 : (byte)0;
        }

        Plane mirrorFallback = new Plane(planeA.Origin, planeA.ZAxis);
        return mirrorFallback.IsValid && CheckMirror(first, second, mirrorFallback, tolerance) ? (byte)1 : (byte)0;
    }

    private static bool CheckMirror(Brep first, Brep second, Plane mirror, double tolerance) {
        Point3d[] reflected = first.Vertices.Select(vertex => {
            Point3d pt = vertex.Location;
            pt.Transform(Transform.Mirror(mirror));
            return pt;
        }).ToArray();
        return reflected.All(pt => second.Vertices.Any(other => pt.DistanceTo(other.Location) < tolerance))
            && second.Vertices.All(pt => reflected.Any(other => other.DistanceTo(pt.Location) < tolerance));
    }

    private static byte EvaluateRotationalSymmetry(
        Curve curveA,
        Curve curveB,
        Plane planeA,
        double tolerance,
        IGeometryContext context) {
        Point3d[] samplesA = [.. Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount).Select(i => curveA.PointAt(curveA.Domain.ParameterAt(i / (double)(OrientConfig.RotationSymmetrySampleCount - 1))))];
        Point3d[] samplesB = [.. Enumerable.Range(0, OrientConfig.RotationSymmetrySampleCount).Select(i => curveB.PointAt(curveB.Domain.ParameterAt(i / (double)(OrientConfig.RotationSymmetrySampleCount - 1))))];
        int[] testIndices = [0, samplesA.Length / 2, samplesA.Length - 1,];
        double[] candidateAngles = [.. testIndices.Select(idx => {
            Vector3d vecA = samplesA[idx] - planeA.Origin;
            Vector3d vecB = samplesB[idx] - planeA.Origin;
            Vector3d projA = vecA - ((vecA * planeA.ZAxis) * planeA.ZAxis);
            Vector3d projB = vecB - ((vecB * planeA.ZAxis) * planeA.ZAxis);
            return projA.Length < tolerance || projB.Length < tolerance
                ? double.NaN
                : Vector3d.CrossProduct(projA, projB) * planeA.ZAxis < 0
                    ? -Vector3d.VectorAngle(projA, projB)
                    : Vector3d.VectorAngle(projA, projB);
        }).Where(angle => !double.IsNaN(angle)),];

        if (candidateAngles.Length == 0) {
            return 0;
        }

        double reference = candidateAngles[0];
        if (!candidateAngles.All(angle => Math.Abs(angle - reference) < context.AngleToleranceRadians)) {
            return 0;
        }

        Transform rotation = Transform.Rotation(reference, planeA.ZAxis, planeA.Origin);
        return samplesA.Zip(samplesB, (ptA, ptB) => {
            Point3d rotated = ptA;
            rotated.Transform(rotation);
            return rotated.DistanceTo(ptB);
        }).All(dist => dist < tolerance) ? (byte)2 : (byte)0;
    }

    private static byte ClassifyRelationship(Plane planeA, Plane planeB, double angleTolerance) {
        double dot = Math.Abs(Vector3d.Multiply(planeA.ZAxis, planeB.ZAxis));
        return dot switch {
            _ when Math.Abs(dot - 1.0) < 1.0 - Math.Cos(angleTolerance) => (byte)1,
            _ when Math.Abs(dot) < Math.Sin(angleTolerance) => (byte)2,
            _ => (byte)3,
        };
    }

    private static Result<Orient.PatternDetectionResult> IdentifyPattern(GeometryBase[] geometries, IGeometryContext context) {
        Result<Point3d>[] centroidResults = [.. geometries.Select(g => ExtractCentroid(g, useMassProperties: false)),];
        if (centroidResults.Any(result => !result.IsSuccess)) {
            int failed = centroidResults.Count(result => !result.IsSuccess);
            return ResultFactory.Create<Orient.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext($"Centroid extraction failed for {failed} geometries"));
        }

        Point3d[] centroids = centroidResults.Select(result => result.Value).ToArray();
        if (centroids.Length < OrientConfig.PatternMinInstances) {
            return ResultFactory.Create<Orient.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext("Insufficient valid centroids"));
        }

        Vector3d[] deltas = centroids.Skip(1).Zip(centroids, (second, first) => second - first).ToArray();
        double avgLen = deltas.Average(vector => vector.Length);
        if (avgLen <= context.AbsoluteTolerance) {
            return ResultFactory.Create<Orient.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext("Degenerate centroid spacing"));
        }

        bool linear = deltas[0].Length >= context.AbsoluteTolerance
            && deltas.Skip(1).All(v => Math.Abs(v.Length - avgLen) / avgLen < context.AbsoluteTolerance && Vector3d.VectorAngle(deltas[0], v) <= context.AngleToleranceRadians);
        if (linear) {
            Transform[] transforms = [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Translation(deltas[0] * i)),];
            int[] anomalies = [.. deltas.Select((v, i) => (Vector: v, Index: i)).Where(pair => Math.Abs(pair.Vector.Length - avgLen) / avgLen >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.Index),];
            double deviation = deltas.Sum(v => Math.Abs(v.Length - avgLen)) / centroids.Length;
            return ResultFactory.Create(value: new Orient.PatternDetectionResult(new Orient.PatternClassification.Linear(), transforms, anomalies, deviation));
        }

        Point3d center = new Point3d(centroids.Average(p => p.X), centroids.Average(p => p.Y), centroids.Average(p => p.Z));
        double[] radii = centroids.Select(p => p.DistanceTo(center)).ToArray();
        double avgRadius = radii.Average();
        if (avgRadius <= context.AbsoluteTolerance || radii.Any(r => Math.Abs(r - avgRadius) / avgRadius >= context.AbsoluteTolerance)) {
            return ResultFactory.Create<Orient.PatternDetectionResult>(error: E.Geometry.PatternDetectionFailed.WithContext("Pattern too irregular"));
        }

        Transform[] radialTransforms = [.. Enumerable.Range(0, centroids.Length).Select(i => Transform.Rotation(RhinoMath.TwoPI * i / centroids.Length, Vector3d.ZAxis, center)),];
        int[] radialAnomalies = [.. radii.Select((radius, index) => (radius, index)).Where(pair => Math.Abs(pair.radius - avgRadius) / avgRadius >= (context.AbsoluteTolerance * OrientConfig.PatternAnomalyThreshold)).Select(pair => pair.index),];
        double radialDeviation = radii.Sum(r => Math.Abs(r - avgRadius)) / centroids.Length;
        return ResultFactory.Create(value: new Orient.PatternDetectionResult(new Orient.PatternClassification.Radial(), radialTransforms, radialAnomalies, radialDeviation));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Orient.SymmetryClassification CreateSymmetry(byte code) => code switch {
        1 => new Orient.SymmetryClassification.Mirror(),
        2 => new Orient.SymmetryClassification.Rotational(),
        _ => new Orient.SymmetryClassification.None(),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Orient.OrientationRelationship CreateRelationship(byte code) => code switch {
        1 => new Orient.OrientationRelationship.Aligned(),
        2 => new Orient.OrientationRelationship.Orthogonal(),
        _ => new Orient.OrientationRelationship.Skew(),
    };
}
