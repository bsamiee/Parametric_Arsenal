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
            [typeof(Curve)] = (g, ctx) => ((Curve)g).FrameAt(((Curve)g).Domain.Mid, out Plane frame) && frame.IsValid
                ? ResultFactory.Create(value: frame)
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            [typeof(Surface)] = (g, ctx) => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = (g, ctx) => ((Brep)g) switch {
                Brep b => (b.IsSolid ? VolumeMassProperties.Compute(b) : null, b.IsClosed && !b.IsSolid ? AreaMassProperties.Compute(b) : null, b) switch {
                    (VolumeMassProperties vmp, _, _) => ResultFactory.Create(value: new Plane(vmp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
                    (_, AreaMassProperties amp, _) => ResultFactory.Create(value: new Plane(amp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
                    (_, _, Brep brep) => ResultFactory.Create(value: new Plane(brep.GetBoundingBox(accurate: false).Center, brep.Faces.Count > 0 ? brep.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
                },
            },
            [typeof(Extrusion)] = (g, ctx) => ((Extrusion)g) switch {
                Extrusion e => (e.IsSolid ? VolumeMassProperties.Compute(e) : null, e.IsClosed(0) && e.IsClosed(1) && !e.IsSolid ? AreaMassProperties.Compute(e) : null, e) switch {
                    (VolumeMassProperties vmp, _, _) => ResultFactory.Create(value: new Plane(vmp.Centroid, e.PathLineCurve().TangentAtStart)),
                    (_, AreaMassProperties amp, _) => ResultFactory.Create(value: new Plane(amp.Centroid, e.PathLineCurve().TangentAtStart)),
                    (_, _, Extrusion ext) => ResultFactory.Create(value: new Plane(ext.GetBoundingBox(accurate: false).Center, ext.PathLineCurve().TangentAtStart)),
                },
            },
            [typeof(Mesh)] = (g, ctx) => ((Mesh)g) switch {
                Mesh m => (m.IsClosed ? VolumeMassProperties.Compute(m) : null, !m.IsClosed ? AreaMassProperties.Compute(m) : null, m) switch {
                    (VolumeMassProperties vmp, _, _) => ResultFactory.Create(value: new Plane(vmp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)),
                    (_, AreaMassProperties amp, _) => ResultFactory.Create(value: new Plane(amp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)),
                    (_, _, Mesh mesh) => ResultFactory.Create(value: new Plane(mesh.GetBoundingBox(accurate: false).Center, mesh.Normals.Count > 0 ? mesh.Normals[0] : Vector3d.ZAxis)),
                },
            },
            [typeof(Point3d)] = (g, ctx) =>
                ResultFactory.Create(value: new Plane((Point3d)g, Vector3d.ZAxis)),
            [typeof(PointCloud)] = (g, ctx) => ((PointCloud)g).Count > 0 switch {
                true => ResultFactory.Create(value: new Plane(((PointCloud)g).GetPoint(0).Location, Vector3d.ZAxis)),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractSourcePlane<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        _planeExtractors.TryGetValue(geometry.GetType(), out Func<object, IGeometryContext, Result<Plane>>? extractor)
            ? extractor(geometry, context)
            : _planeExtractors
                .Where(kv => kv.Key.IsAssignableFrom(geometry.GetType()))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create((a, b) =>
                    a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value(geometry, context))
                .DefaultIfEmpty(ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)))
                .First();

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
            _ => (new Vector3d(source), new Vector3d(target)) switch {
                (Vector3d s, Vector3d t) when s.Unitize() && t.Unitize() =>
                    Vector3d.CrossProduct(s, t).Length < OrientConfig.ToleranceDefaults.ParallelAngleThreshold
                        ? Math.Abs(s * t + 1.0) < OrientConfig.ToleranceDefaults.ParallelAngleThreshold
                            ? ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment)
                            : ResultFactory.Create(value: Transform.Identity)
                        : ResultFactory.Create(value: Transform.Rotation(s, t, center)),
                _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
            },
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipGeometryDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        (geometry.Duplicate(), geometry) switch {
            (Curve duplicated, Curve original) when duplicated.Reverse() =>
                ResultFactory.Create(value: (T)duplicated),
            (Brep duplicated, Brep _) => (duplicated.Flip(), duplicated) switch {
                (_, Brep flipped) => ResultFactory.Create(value: (T)(GeometryBase)flipped),
            },
            (Extrusion duplicated, Extrusion _) => duplicated.ToBrep() switch {
                Brep brepForm when (brepForm.Flip(), true).Item2 => ResultFactory.Create(value: (T)(GeometryBase)brepForm),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            },
            (Mesh duplicated, Mesh _) => (duplicated.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true), duplicated) switch {
                (_, Mesh flipped) => ResultFactory.Create(value: (T)(GeometryBase)flipped),
            },
            (null, _) => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ApplyTransform<T>(T geometry, Transform xform) where T : GeometryBase =>
        (T)geometry.Duplicate() switch {
            T result when result.Transform(xform) => ResultFactory.Create(value: result),
            _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
        };
}
