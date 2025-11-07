using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Core orientation algorithms with FrozenDictionary dispatch and transform computation.</summary>
internal static class OrientCore {
    /// <summary>Frame extraction dispatch table keyed by geometry runtime type.</summary>
    private static readonly FrozenDictionary<Type, Func<object, IGeometryContext, Result<Plane>>> _planeExtractors =
        new Dictionary<Type, Func<object, IGeometryContext, Result<Plane>>> {
            [typeof(Curve)] = (g, ctx) => ((Curve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid &&
                    frame.ZAxis.Length > OrientConfig.ToleranceDefaults.MinVectorLength =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsCurve)] = (g, ctx) => ((NurbsCurve)g) switch {
                NurbsCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid &&
                    frame.ZAxis.Length > OrientConfig.ToleranceDefaults.MinVectorLength =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(LineCurve)] = (g, ctx) => ((LineCurve)g) switch {
                LineCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(ArcCurve)] = (g, ctx) => ((ArcCurve)g) switch {
                ArcCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PolylineCurve)] = (g, ctx) => ((PolylineCurve)g) switch {
                PolylineCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Surface)] = (g, ctx) => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid &&
                    frame.ZAxis.Length > OrientConfig.ToleranceDefaults.MinVectorLength =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsSurface)] = (g, ctx) => ((NurbsSurface)g) switch {
                NurbsSurface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PlaneSurface)] = (g, ctx) => ((PlaneSurface)g) switch {
                PlaneSurface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = (g, ctx) => {
                Brep brep = (Brep)g;
                VolumeMassProperties? volProps = brep.IsSolid ? VolumeMassProperties.Compute(brep) : null;
                AreaMassProperties? areaProps = volProps is null && brep.IsClosed ? AreaMassProperties.Compute(brep) : null;
                Point3d centroid = volProps?.Centroid ?? areaProps?.Centroid ?? brep.GetBoundingBox(accurate: false).Center;
                Vector3d normal = brep.Faces.Count > 0 && brep.Faces[0].FrameAt(0.5, 0.5, out Plane faceFrame)
                    ? faceFrame.ZAxis
                    : Vector3d.ZAxis;
                return normal.Length > OrientConfig.ToleranceDefaults.MinVectorLength
                    ? ResultFactory.Create(value: new Plane(centroid, normal))
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            },
            [typeof(Mesh)] = (g, ctx) => {
                Mesh mesh = (Mesh)g;
                VolumeMassProperties? volProps = mesh.IsClosed ? VolumeMassProperties.Compute(mesh) : null;
                AreaMassProperties? areaProps = volProps is null ? AreaMassProperties.Compute(mesh) : null;
                Point3d centroid = volProps?.Centroid ?? areaProps?.Centroid ?? mesh.GetBoundingBox(accurate: false).Center;
                Vector3d normal = mesh.Normals.Count > 0
                    ? mesh.Normals[0]
                    : mesh.FaceNormals.Count > 0
                        ? mesh.FaceNormals[0]
                        : Vector3d.ZAxis;
                return normal.Length > OrientConfig.ToleranceDefaults.MinVectorLength
                    ? ResultFactory.Create(value: new Plane(centroid, normal))
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
            },
            [typeof(Point)] = (g, ctx) =>
                ResultFactory.Create(value: new Plane(((Point)g).Location, Vector3d.ZAxis)),
            [typeof(PointCloud)] = (g, ctx) => ((PointCloud)g).Count > 0 switch {
                true => ResultFactory.Create(value: new Plane(((PointCloud)g).GetPoint(0).Location, Vector3d.ZAxis)),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();

    /// <summary>Extracts source plane from geometry using type-based dispatch with fallback hierarchy traversal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractSourcePlane<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        _planeExtractors.TryGetValue(geometry.GetType(), out Func<object, IGeometryContext, Result<Plane>>? extractor) switch {
            true => extractor(geometry, context),
            false => _planeExtractors
                .Where(kv => kv.Key.IsAssignableFrom(geometry.GetType()))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create((a, b) =>
                    a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
                .Select(kv => kv.Value(geometry, context))
                .FirstOrDefault() ?? ResultFactory.Create<Plane>(
                    error: E.Geometry.UnsupportedOrientationType.WithContext($"Type: {geometry.GetType().Name}")),
        };

    /// <summary>Extracts centroid using mass properties computation with fallback to bounding box center.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d> ExtractCentroid<T>(T geometry, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        (geometry, useMassCentroid) switch {
            (Brep brep, true) when brep.IsSolid =>
                VolumeMassProperties.Compute(brep)?.Centroid is Point3d pt
                    ? ResultFactory.Create(value: pt)
                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            (Brep brep, true) when brep.IsClosed =>
                AreaMassProperties.Compute(brep)?.Centroid is Point3d pt
                    ? ResultFactory.Create(value: pt)
                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            (Mesh mesh, true) when mesh.IsClosed =>
                VolumeMassProperties.Compute(mesh)?.Centroid is Point3d pt
                    ? ResultFactory.Create(value: pt)
                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            (Mesh mesh, true) =>
                AreaMassProperties.Compute(mesh)?.Centroid is Point3d pt
                    ? ResultFactory.Create(value: pt)
                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            (Curve curve, true) when curve.IsClosed =>
                AreaMassProperties.Compute(curve)?.Centroid is Point3d pt
                    ? ResultFactory.Create(value: pt)
                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            (GeometryBase geom, _) =>
                geom.GetBoundingBox(accurate: true) is BoundingBox bbox && bbox.IsValid
                    ? ResultFactory.Create(value: bbox.Center)
                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
        };

    /// <summary>Computes canonical positioning transform based on mode byte with inline switch dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeCanonicalTransform<T>(T geometry, byte mode, IGeometryContext context) where T : GeometryBase =>
        (mode, geometry.GetBoundingBox(accurate: true)) switch {
            (1, BoundingBox bbox) when bbox.IsValid =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    from: new Plane(bbox.Center, Vector3d.XAxis, Vector3d.YAxis),
                    to: Plane.WorldXY)),
            (2, BoundingBox bbox) when bbox.IsValid =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    from: new Plane(bbox.Center, Vector3d.YAxis, Vector3d.ZAxis),
                    to: Plane.WorldYZ)),
            (3, BoundingBox bbox) when bbox.IsValid =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    from: new Plane(bbox.Center, Vector3d.XAxis, Vector3d.ZAxis),
                    to: Plane.WorldXZ)),
            (4, _) =>
                ExtractCentroid(geometry, useMassCentroid: false, context)
                    .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (5, _) =>
                ExtractCentroid(geometry, useMassCentroid: true, context)
                    .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (_, BoundingBox bbox) when !bbox.IsValid =>
                ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode.WithContext($"Mode: {mode}")),
        };

    /// <summary>Computes rotation transform to align source vector with target vector handling parallel and antiparallel edge cases.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeVectorAlignment(
        Vector3d source,
        Vector3d target,
        Point3d center,
        IGeometryContext context) =>
        (source.Length <= OrientConfig.ToleranceDefaults.MinVectorLength ||
         target.Length <= OrientConfig.ToleranceDefaults.MinVectorLength) switch {
            true => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
            false => (source.Unitize(), target.Unitize(), Vector3d.CrossProduct(source, target)) switch {
                (true, true, Vector3d cross) when cross.Length > OrientConfig.ToleranceDefaults.MinVectorLength =>
                    ResultFactory.Create(value: Transform.Rotation(source, target, center)),
                (true, true, _) when source * target > 0.999999 =>
                    ResultFactory.Create(value: Transform.Identity),
                (true, true, _) =>
                    ResultFactory.Create(value: Transform.Rotation(
                        angleRadians: Math.PI,
                        rotationAxis: Math.Abs(source * Vector3d.XAxis) < 0.9
                            ? Vector3d.CrossProduct(source, Vector3d.XAxis)
                            : Vector3d.CrossProduct(source, Vector3d.YAxis),
                        rotationCenter: center)),
                _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
            },
        };

    /// <summary>Flips geometry direction using type-specific in-place mutations with result wrapping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipGeometryDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        geometry.Duplicate() switch {
            Curve curve when curve.Reverse() =>
                ResultFactory.Create(value: (T)(object)curve),
            Brep brep => (brep.Flip(), brep) switch {
                (_, Brep flipped) => ResultFactory.Create(value: (T)(object)flipped),
            },
            Mesh mesh when mesh.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true) =>
                ResultFactory.Create(value: (T)(object)mesh),
            Surface surface => surface.Reverse(direction: 0) switch {
                true => ResultFactory.Create(value: (T)(object)surface),
                false => ResultFactory.Create<T>(error: E.Geometry.TransformFailed.WithContext("Surface reverse failed")),
            },
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(
                $"FlipDirection not supported for {geometry.GetType().Name}")),
        };

    /// <summary>Extracts plane from OrientSpec target with polymorphic dispatch on target type.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractTargetPlane(object target, (double u, double v) surfaceUV, double curveParam, IGeometryContext context) =>
        target switch {
            Plane p when p.IsValid => ResultFactory.Create(value: p),
            Curve c when c.FrameAt(curveParam, out Plane frame) && frame.IsValid =>
                ResultFactory.Create(value: frame),
            Curve => ResultFactory.Create<Plane>(error: E.Geometry.InvalidCurveParameter.WithContext($"t={curveParam}")),
            Surface s when s.FrameAt(surfaceUV.u, surfaceUV.v, out Plane frame) && frame.IsValid =>
                ResultFactory.Create(value: frame),
            Surface => ResultFactory.Create<Plane>(error: E.Geometry.InvalidSurfaceUV.WithContext($"uv=({surfaceUV.u},{surfaceUV.v})")),
            _ => ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext($"Type: {target.GetType().Name}")),
        };
}
