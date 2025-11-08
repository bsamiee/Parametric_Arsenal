using System.Collections.Frozen;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Core dispatch tables for frame and centroid extraction with polymorphic mass properties dispatch.</summary>
internal static class OrientCore {
    internal static readonly FrozenDictionary<Type, Func<object, Result<Plane>>> PlaneExtractors =
        new Dictionary<Type, Func<object, Result<Plane>>> {
            [typeof(Curve)] = g => ((Curve)g).FrameAt(((Curve)g).Domain.Mid, out Plane f) && f.IsValid
                ? ResultFactory.Create(value: f)
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            [typeof(Surface)] = g => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane f) && f.IsValid => ResultFactory.Create(value: f),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = g => ((Brep)g) switch {
                Brep b when b.IsSolid => ((Func<Result<Plane>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(b); return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Brep b when b.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Plane>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(b); return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Brep b => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
            },
            [typeof(Extrusion)] = g => ((Extrusion)g) switch {
                Extrusion e when e.IsSolid => ((Func<Result<Plane>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(e); using LineCurve path = e.PathLineCurve(); return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, path.TangentAtStart)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Extrusion e when e.IsClosed(0) && e.IsClosed(1) => ((Func<Result<Plane>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(e); using LineCurve path = e.PathLineCurve(); return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, path.TangentAtStart)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Extrusion e => ((Func<Result<Plane>>)(() => { using LineCurve path = e.PathLineCurve(); return ResultFactory.Create(value: new Plane(e.GetBoundingBox(accurate: false).Center, path.TangentAtStart)); }))(),
            },
            [typeof(Mesh)] = g => ((Mesh)g) switch {
                Mesh m when m.IsClosed => ((Func<Result<Plane>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(m); return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Mesh m => ((Func<Result<Plane>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(m); return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
            },
            [typeof(Point3d)] = g => ResultFactory.Create(value: new Plane((Point3d)g, Vector3d.ZAxis)),
            [typeof(PointCloud)] = g => ((PointCloud)g) switch {
                PointCloud pc when pc.Count > 0 => ResultFactory.Create(value: new Plane(pc[0].Location, Vector3d.ZAxis)),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();

    internal static readonly Func<GeometryBase, bool, Result<Point3d>> CentroidExtractor = (geometry, useMass) => geometry switch {
        Brep brep when useMass && brep.IsSolid => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
        Brep brep when useMass && brep.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(brep); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
        Extrusion ext when useMass && ext.IsSolid => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(ext); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
        Extrusion ext when useMass && ext.IsClosed(0) && ext.IsClosed(1) => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(ext); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
        Mesh mesh when useMass && mesh.IsClosed => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
        Mesh mesh when useMass => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(mesh); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
        Curve curve when useMass && curve.IsClosed => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(curve); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
        GeometryBase g when !useMass && g.GetBoundingBox(accurate: true) is BoundingBox b && b.IsValid => ResultFactory.Create(value: b.Center),
        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
    };

    internal static readonly Func<GeometryBase, Transform, Result<GeometryBase>> ApplyTransform = (geometry, xform) =>
        geometry.Duplicate() switch {
            GeometryBase dup when dup.Transform(xform) => ResultFactory.Create(value: dup),
            _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
        };

    internal static Result<TOut> DispatchByType<TOut>(
        object item,
        FrozenDictionary<Type, Func<object, Result<TOut>>> dispatch,
        SystemError error) =>
        dispatch.TryGetValue(item.GetType(), out Func<object, Result<TOut>>? op)
            ? op(item)
            : dispatch
                .Where(kv => kv.Key.IsInstanceOfType(item))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create((a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value(item))
                .DefaultIfEmpty(ResultFactory.Create<TOut>(error: error))
                .First();

    internal static readonly FrozenDictionary<Type, Func<object, Result<GeometryBase>>> FlipOperations =
        new Dictionary<Type, Func<object, Result<GeometryBase>>> {
            [typeof(Curve)] = g => ((Curve)g).Duplicate() switch {
                Curve c when c.Reverse() => ResultFactory.Create(value: (GeometryBase)c),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(NurbsCurve)] = g => ((NurbsCurve)g).Duplicate() switch {
                Curve c when c.Reverse() => ResultFactory.Create(value: (GeometryBase)c),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(LineCurve)] = g => ((LineCurve)g).Duplicate() switch {
                Curve c when c.Reverse() => ResultFactory.Create(value: (GeometryBase)c),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(ArcCurve)] = g => ((ArcCurve)g).Duplicate() switch {
                Curve c when c.Reverse() => ResultFactory.Create(value: (GeometryBase)c),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(PolyCurve)] = g => ((PolyCurve)g).Duplicate() switch {
                Curve c when c.Reverse() => ResultFactory.Create(value: (GeometryBase)c),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(PolylineCurve)] = g => ((PolylineCurve)g).Duplicate() switch {
                Curve c when c.Reverse() => ResultFactory.Create(value: (GeometryBase)c),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(Brep)] = g => ((Brep)g).Duplicate() switch {
                Brep b => ((Func<Result<GeometryBase>>)(() => { b.Flip(); return ResultFactory.Create(value: (GeometryBase)b); }))(),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(Extrusion)] = g => ((Extrusion)g).ToBrep() switch {
                Brep b => ((Func<Result<GeometryBase>>)(() => { b.Flip(); return ResultFactory.Create(value: (GeometryBase)b); }))(),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
            [typeof(Mesh)] = g => ((Mesh)g).Duplicate() switch {
                Mesh m => ((Func<Result<GeometryBase>>)(() => { m.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true); return ResultFactory.Create(value: (GeometryBase)m); }))(),
                _ => ResultFactory.Create<GeometryBase>(error: E.Geometry.TransformFailed),
            },
        }.ToFrozenDictionary();
}
