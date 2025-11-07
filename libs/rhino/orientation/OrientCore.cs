using System.Collections.Frozen;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Core dispatch tables for frame extraction without helper methods - all logic inlined in API.</summary>
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
                Brep b when b.IsSolid => VolumeMassProperties.Compute(b) switch {
                    VolumeMassProperties vmp => ((Func<Result<Plane>>)(() => {
                        Point3d c = vmp.Centroid;
                        vmp.Dispose();
                        return ResultFactory.Create(value: new Plane(c, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis));
                    }))(),
                    _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                },
                Brep b when b.SolidOrientation != BrepSolidOrientation.None => AreaMassProperties.Compute(b) switch {
                    AreaMassProperties amp => ((Func<Result<Plane>>)(() => {
                        Point3d c = amp.Centroid;
                        amp.Dispose();
                        return ResultFactory.Create(value: new Plane(c, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis));
                    }))(),
                    _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                },
                Brep b => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
            },
            [typeof(Extrusion)] = g => ((Extrusion)g) switch {
                Extrusion e when e.IsSolid => VolumeMassProperties.Compute(e) switch {
                    VolumeMassProperties vmp => ((Func<Result<Plane>>)(() => {
                        Point3d c = vmp.Centroid;
                        vmp.Dispose();
                        LineCurve path = e.PathLineCurve();
                        Vector3d t = path.TangentAtStart;
                        path.Dispose();
                        return ResultFactory.Create(value: new Plane(c, t));
                    }))(),
                    _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                },
                Extrusion e when e.IsClosed(0) && e.IsClosed(1) => AreaMassProperties.Compute(e) switch {
                    AreaMassProperties amp => ((Func<Result<Plane>>)(() => {
                        Point3d c = amp.Centroid;
                        amp.Dispose();
                        LineCurve path = e.PathLineCurve();
                        Vector3d t = path.TangentAtStart;
                        path.Dispose();
                        return ResultFactory.Create(value: new Plane(c, t));
                    }))(),
                    _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                },
                Extrusion e => ((Func<Result<Plane>>)(() => {
                    LineCurve path = e.PathLineCurve();
                    Vector3d t = path.TangentAtStart;
                    path.Dispose();
                    return ResultFactory.Create(value: new Plane(e.GetBoundingBox(accurate: false).Center, t));
                }))(),
            },
            [typeof(Mesh)] = g => ((Mesh)g) switch {
                Mesh m when m.IsClosed => VolumeMassProperties.Compute(m) switch {
                    VolumeMassProperties vmp => ((Func<Result<Plane>>)(() => {
                        Point3d c = vmp.Centroid;
                        vmp.Dispose();
                        return ResultFactory.Create(value: new Plane(c, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis));
                    }))(),
                    _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                },
                Mesh m => AreaMassProperties.Compute(m) switch {
                    AreaMassProperties amp => ((Func<Result<Plane>>)(() => {
                        Point3d c = amp.Centroid;
                        amp.Dispose();
                        return ResultFactory.Create(value: new Plane(c, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis));
                    }))(),
                    _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                },
            },
            [typeof(Point3d)] = g => ResultFactory.Create(value: new Plane((Point3d)g, Vector3d.ZAxis)),
            [typeof(PointCloud)] = g => ((PointCloud)g) switch {
                PointCloud pc when pc.Count > 0 => ResultFactory.Create(value: new Plane(pc[0].Location, Vector3d.ZAxis)),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();
}
