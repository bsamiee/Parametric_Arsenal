using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Core orientation algorithms with FrozenDictionary dispatch and frame extraction.</summary>
internal static class OrientCore {
    private static readonly FrozenDictionary<Type, Func<object, IGeometryContext, Result<Plane>>> _planeExtractors =
        new Dictionary<Type, Func<object, IGeometryContext, Result<Plane>>> {
            [typeof(Curve)] = (g, ctx) => ((Curve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsCurve)] = (g, ctx) => ((NurbsCurve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(LineCurve)] = (g, ctx) => ((LineCurve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(ArcCurve)] = (g, ctx) => ((ArcCurve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PolyCurve)] = (g, ctx) => ((PolyCurve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PolylineCurve)] = (g, ctx) => ((PolylineCurve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Surface)] = (g, ctx) => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsSurface)] = (g, ctx) => ((NurbsSurface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PlaneSurface)] = (g, ctx) => ((PlaneSurface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = (g, ctx) => {
                Brep brep = (Brep)g;
                VolumeMassProperties? volumeMass = brep.IsSolid ? VolumeMassProperties.Compute(brep) : null;
                AreaMassProperties? areaMass = volumeMass is null && brep.IsClosed ? AreaMassProperties.Compute(brep) : null;
                Point3d centroid = volumeMass?.Centroid ?? areaMass?.Centroid ?? brep.GetBoundingBox(accurate: false).Center;
                Vector3d normal = brep.Faces.Count > 0 ? brep.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis;
                return ResultFactory.Create(value: new Plane(centroid, normal));
            },
            [typeof(Extrusion)] = (g, ctx) => {
                Extrusion extrusion = (Extrusion)g;
                VolumeMassProperties? volumeMass = extrusion.IsSolid ? VolumeMassProperties.Compute(extrusion) : null;
                AreaMassProperties? areaMass = volumeMass is null && extrusion.IsClosed(0) && extrusion.IsClosed(1) ? AreaMassProperties.Compute(extrusion) : null;
                Point3d centroid = volumeMass?.Centroid ?? areaMass?.Centroid ?? extrusion.GetBoundingBox(accurate: false).Center;
                Vector3d normal = extrusion.PathLineCurve().TangentAtStart;
                return ResultFactory.Create(value: new Plane(centroid, normal));
            },
            [typeof(Mesh)] = (g, ctx) => {
                Mesh mesh = (Mesh)g;
                VolumeMassProperties? volumeMass = mesh.IsClosed ? VolumeMassProperties.Compute(mesh) : null;
                AreaMassProperties? areaMass = volumeMass is null ? AreaMassProperties.Compute(mesh) : null;
                Point3d centroid = volumeMass?.Centroid ?? areaMass?.Centroid ?? mesh.GetBoundingBox(accurate: false).Center;
                Vector3d normal = mesh.Normals.Count > 0 ? mesh.Normals[0] : Vector3d.ZAxis;
                return ResultFactory.Create(value: new Plane(centroid, normal));
            },
            [typeof(Point)] = (g, ctx) =>
                ResultFactory.Create(value: new Plane(((Point)g).Location, Vector3d.ZAxis)),
            [typeof(PointCloud)] = (g, ctx) => ((PointCloud)g).Count > 0 switch {
                true => ResultFactory.Create(value: new Plane(((PointCloud)g).GetPoint(0).Location, Vector3d.ZAxis)),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();

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
                    error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d> ExtractCentroid<T>(T geometry, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        (geometry, useMassCentroid) switch {
            (Brep brep, true) when brep.IsSolid => VolumeMassProperties.Compute(brep) switch {
                VolumeMassProperties vmp => ResultFactory.Create(value: vmp.Centroid),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Brep brep, true) when brep.IsClosed => AreaMassProperties.Compute(brep) switch {
                AreaMassProperties amp => ResultFactory.Create(value: amp.Centroid),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Extrusion ext, true) when ext.IsSolid => VolumeMassProperties.Compute(ext) switch {
                VolumeMassProperties vmp => ResultFactory.Create(value: vmp.Centroid),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Extrusion ext, true) when ext.IsClosed(0) && ext.IsClosed(1) => AreaMassProperties.Compute(ext) switch {
                AreaMassProperties amp => ResultFactory.Create(value: amp.Centroid),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Mesh mesh, true) when mesh.IsClosed => VolumeMassProperties.Compute(mesh) switch {
                VolumeMassProperties vmp => ResultFactory.Create(value: vmp.Centroid),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Mesh mesh, true) => AreaMassProperties.Compute(mesh) switch {
                AreaMassProperties amp => ResultFactory.Create(value: amp.Centroid),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Curve curve, true) when curve.IsClosed => AreaMassProperties.Compute(curve) switch {
                AreaMassProperties amp => ResultFactory.Create(value: amp.Centroid),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (GeometryBase geom, false) => geometry.GetBoundingBox(accurate: true) switch {
                BoundingBox bbox when bbox.IsValid => ResultFactory.Create(value: bbox.Center),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeCanonicalTransform<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        (mode.Mode, geometry.GetBoundingBox(accurate: true)) switch {
            (1, BoundingBox bbox) when bbox.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(
                new Plane(bbox.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY)),
            (2, BoundingBox bbox) when bbox.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(
                new Plane(bbox.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ)),
            (3, BoundingBox bbox) when bbox.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(
                new Plane(bbox.Center, Vector3d.XAxis, Vector3d.ZAxis), Plane.WorldXZ)),
            (4, _) => ExtractCentroid(geometry, useMassCentroid: false, context)
                .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (5, _) => ExtractCentroid(geometry, useMassCentroid: true, context)
                .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (_, BoundingBox bbox) when !bbox.IsValid =>
                ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeVectorAlignment(Vector3d source, Vector3d target, Point3d center, IGeometryContext context) =>
        (source.Length > OrientConfig.ToleranceDefaults.MinVectorLength, target.Length > OrientConfig.ToleranceDefaults.MinVectorLength) switch {
            (false, _) or (_, false) => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
            _ => (source.Unitize(), target.Unitize(), Vector3d.CrossProduct(source, target).Length) switch {
                (true, true, double cpLen) when cpLen < OrientConfig.ToleranceDefaults.ParallelAngleThreshold =>
                    Math.Abs(source * target + 1.0) < OrientConfig.ToleranceDefaults.ParallelAngleThreshold
                        ? ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment)
                        : ResultFactory.Create(value: Transform.Identity),
                (true, true, _) => ResultFactory.Create(value: Transform.Rotation(source, target, center)),
                _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
            },
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipGeometryDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        geometry switch {
            Curve curve => curve.Reverse() switch {
                true => ResultFactory.Create(value: (T)(GeometryBase)curve),
                false => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            },
            Brep brep => (brep.Flip(), ResultFactory.Create(value: (T)(GeometryBase)brep)).Item2,
            Extrusion ext => (ext.ToBrep() switch {
                Brep tempBrep => (tempBrep.Flip(), tempBrep).Item2,
                _ => null,
            }) switch {
                Brep flippedBrep => ResultFactory.Create(value: (T)(GeometryBase)flippedBrep),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            },
            Mesh mesh => (mesh.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true), ResultFactory.Create(value: (T)(GeometryBase)mesh)).Item2,
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };
}
