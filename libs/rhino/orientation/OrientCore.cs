using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Core orientation algorithms with FrozenDictionary dispatch and transform composition.</summary>
internal static class OrientCore {
    /// <summary>Type-based plane extraction dispatch for source frame determination.</summary>
    private static readonly FrozenDictionary<Type, Func<object, IGeometryContext, Result<Plane>>> PlaneExtractors =
        new Dictionary<Type, Func<object, IGeometryContext, Result<Plane>>> {
            [typeof(Curve)] = (g, ctx) => ((Curve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsCurve)] = (g, ctx) => ((NurbsCurve)g) switch {
                NurbsCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(LineCurve)] = (g, ctx) => ((LineCurve)g) switch {
                LineCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PolylineCurve)] = (g, ctx) => ((PolylineCurve)g) switch {
                PolylineCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(ArcCurve)] = (g, ctx) => ((ArcCurve)g) switch {
                ArcCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Surface)] = (g, ctx) => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
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
            [typeof(Brep)] = (g, ctx) => ((Brep)g) switch {
                Brep { IsSolid: true } b => VolumeMassProperties.Compute(b) switch {
                    { Centroid: Point3d ct } when ct.IsValid => b.Faces.Count > 0 && b.Faces[0].NormalAt(0.5, 0.5) is Vector3d n && n.IsValid
                        ? ResultFactory.Create(value: new Plane(ct, n))
                        : ResultFactory.Create(value: new Plane(ct, Vector3d.ZAxis)),
                    _ => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, Vector3d.ZAxis)),
                },
                Brep b => AreaMassProperties.Compute(b) switch {
                    { Centroid: Point3d ct } when ct.IsValid => b.Faces.Count > 0 && b.Faces[0].NormalAt(0.5, 0.5) is Vector3d n && n.IsValid
                        ? ResultFactory.Create(value: new Plane(ct, n))
                        : ResultFactory.Create(value: new Plane(ct, Vector3d.ZAxis)),
                    _ => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, Vector3d.ZAxis)),
                },
            },
            [typeof(Extrusion)] = (g, ctx) => ((Extrusion)g).ToBrep(splitKinkyFaces: true) switch {
                Brep b => PlaneExtractors[typeof(Brep)](b, ctx).Tap(onSuccess: _ => b.Dispose()),
            },
            [typeof(SubD)] = (g, ctx) => ((SubD)g).ToBrep() switch {
                Brep b => PlaneExtractors[typeof(Brep)](b, ctx).Tap(onSuccess: _ => b.Dispose()),
            },
            [typeof(Mesh)] = (g, ctx) => ((Mesh)g) switch {
                Mesh { IsClosed: true } m => VolumeMassProperties.Compute(m) switch {
                    { Centroid: Point3d ct } when ct.IsValid => m.Normals.Count > 0
                        ? ResultFactory.Create(value: new Plane(ct, m.Normals[0]))
                        : ResultFactory.Create(value: new Plane(ct, Vector3d.ZAxis)),
                    _ => ResultFactory.Create(value: new Plane(m.GetBoundingBox(accurate: false).Center, Vector3d.ZAxis)),
                },
                Mesh m => AreaMassProperties.Compute(m) switch {
                    { Centroid: Point3d ct } when ct.IsValid => m.Normals.Count > 0
                        ? ResultFactory.Create(value: new Plane(ct, m.Normals[0]))
                        : ResultFactory.Create(value: new Plane(ct, Vector3d.ZAxis)),
                    _ => ResultFactory.Create(value: new Plane(m.GetBoundingBox(accurate: false).Center, Vector3d.ZAxis)),
                },
            },
            [typeof(Point)] = (g, ctx) =>
                ResultFactory.Create(value: new Plane(((Point)g).Location, Vector3d.ZAxis)),
            [typeof(PointCloud)] = (g, ctx) => ((PointCloud)g).Count > 0 switch {
                true => ResultFactory.Create(value: new Plane(((PointCloud)g).GetPoint(0).Location, Vector3d.ZAxis)),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();

    /// <summary>Extracts source plane from geometry using type-based dispatch with inheritance fallback.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractSourcePlane<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        PlaneExtractors.TryGetValue(geometry.GetType(), out Func<object, IGeometryContext, Result<Plane>>? extractor) switch {
            true => extractor(geometry, context),
            false => PlaneExtractors
                .Where(kv => kv.Key.IsAssignableFrom(geometry.GetType()))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create(static (a, b) =>
                    a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
                .Select(kv => kv.Value(geometry, context))
                .FirstOrDefault() ?? ResultFactory.Create<Plane>(
                    error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    /// <summary>Extracts centroid using mass properties or bounding box center fallback.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d> ExtractCentroid<T>(T geometry, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        (geometry, useMassCentroid) switch {
            (Brep { IsSolid: true } b, true) => VolumeMassProperties.Compute(b) switch {
                { Centroid: Point3d ct } when ct.IsValid => ResultFactory.Create(value: ct),
                _ => ResultFactory.Create(value: b.GetBoundingBox(accurate: false).Center),
            },
            (Brep b, _) => AreaMassProperties.Compute(b) switch {
                { Centroid: Point3d ct } when ct.IsValid => ResultFactory.Create(value: ct),
                _ => ResultFactory.Create(value: b.GetBoundingBox(accurate: false).Center),
            },
            (Mesh { IsClosed: true } m, true) => VolumeMassProperties.Compute(m) switch {
                { Centroid: Point3d ct } when ct.IsValid => ResultFactory.Create(value: ct),
                _ => ResultFactory.Create(value: m.GetBoundingBox(accurate: false).Center),
            },
            (Mesh m, _) => AreaMassProperties.Compute(m) switch {
                { Centroid: Point3d ct } when ct.IsValid => ResultFactory.Create(value: ct),
                _ => ResultFactory.Create(value: m.GetBoundingBox(accurate: false).Center),
            },
            (Curve { IsClosed: true } c, _) => AreaMassProperties.Compute(c) switch {
                { Centroid: Point3d ct } when ct.IsValid => ResultFactory.Create(value: ct),
                _ => ResultFactory.Create(value: c.GetBoundingBox(accurate: false).Center),
            },
            (Surface s, _) => AreaMassProperties.Compute(s) switch {
                { Centroid: Point3d ct } when ct.IsValid => ResultFactory.Create(value: ct),
                _ => ResultFactory.Create(value: s.GetBoundingBox(accurate: false).Center),
            },
            (Extrusion ext, bool mass) => ext.ToBrep(splitKinkyFaces: true) switch {
                Brep b => ExtractCentroid(b, mass, context).Tap(onSuccess: _ => b.Dispose()),
            },
            (SubD sd, bool mass) => sd.ToBrep() switch {
                Brep b => ExtractCentroid(b, mass, context).Tap(onSuccess: _ => b.Dispose()),
            },
            (Point p, _) => ResultFactory.Create(value: p.Location),
            (PointCloud pc, _) => pc.Count > 0 switch {
                true => ResultFactory.Create(value: pc.GetPoint(0).Location),
                false => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            _ => ResultFactory.Create(value: geometry.GetBoundingBox(accurate: false).Center),
        };

    /// <summary>Computes canonical positioning transform based on mode byte and geometry bounds.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeCanonicalTransform<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        (mode.Mode, geometry.GetBoundingBox(accurate: true)) switch {
            (1, BoundingBox bbox) when bbox.IsValid =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    new Plane(bbox.Center, Vector3d.XAxis, Vector3d.YAxis),
                    Plane.WorldXY)),
            (2, BoundingBox bbox) when bbox.IsValid =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    new Plane(bbox.Center, Vector3d.YAxis, Vector3d.ZAxis),
                    Plane.WorldYZ)),
            (3, BoundingBox bbox) when bbox.IsValid =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    new Plane(bbox.Center, Vector3d.XAxis, Vector3d.ZAxis),
                    Plane.WorldXZ)),
            (4, _) => ExtractCentroid(geometry, useMassCentroid: false, context)
                .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (5, _) => ExtractCentroid(geometry, useMassCentroid: true, context)
                .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (_, BoundingBox bbox) when !bbox.IsValid =>
                ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
        };

    /// <summary>Computes rotation transform to align source vector with target vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeVectorAlignment(
        Vector3d source,
        Vector3d target,
        Point3d center,
        IGeometryContext context) =>
        (source.Length, target.Length) switch {
            (double sLen, double tLen) when sLen <= OrientConfig.ToleranceDefaults.MinVectorLength ||
                                            tLen <= OrientConfig.ToleranceDefaults.MinVectorLength =>
                ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
            _ => Vector3d.VectorAngle(source, target) switch {
                double angle when angle < OrientConfig.ToleranceDefaults.ParallelAngleThreshold =>
                    ResultFactory.Create(value: Transform.Identity),
                double angle when Math.Abs(angle - Math.PI) < OrientConfig.ToleranceDefaults.ParallelAngleThreshold =>
                    ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment),
                double angle => ResultFactory.Create(value: Transform.Rotation(angle, Vector3d.CrossProduct(source, target), center)),
            },
        };

    /// <summary>Flips geometry direction with type-specific in-place mutation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipGeometryDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        geometry switch {
            Curve c => c.Reverse() switch {
                true => ResultFactory.Create(value: geometry),
                false => ResultFactory.Create<T>(error: E.Geometry.TransformFailed.WithContext("Curve.Reverse failed")),
            },
            Brep b => ResultFactory.Create(value: geometry).Tap(onSuccess: _ => b.Flip()),
            Mesh m => ResultFactory.Create(value: geometry).Tap(onSuccess: _ =>
                m.Flip(flipVertexNormals: true, flipFaceNormals: true, flipFaceOrientation: true)),
            Surface s when s is RevSurface or SumSurface or NurbsSurface => s.Reverse(direction: 0) switch {
                true => ResultFactory.Create(value: geometry),
                false => ResultFactory.Create<T>(error: E.Geometry.TransformFailed.WithContext("Surface.Reverse failed")),
            },
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(
                $"FlipDirection not supported for {geometry.GetType().Name}")),
        };
}
