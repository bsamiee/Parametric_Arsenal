using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Plane/centroid extraction and transformation dispatch with mass property computation.</summary>
internal static class OrientCore {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ComputeMass<TMass, T>(Func<TMass?> compute, Func<TMass, T> extract) where TMass : class, IDisposable {
        using TMass? mass = compute();
        return mass is not null ? ResultFactory.Create(value: extract(mass)) : ResultFactory.Create<T>(error: E.Geometry.FrameExtractionFailed);
    }

    internal static readonly FrozenDictionary<Type, Func<object, Result<Plane>>> PlaneExtractors =
        new Dictionary<Type, Func<object, Result<Plane>>> {
            [typeof(Curve)] = g => ((Curve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane f) && f.IsValid => ResultFactory.Create(value: f),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Surface)] = g => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane f) && f.IsValid => ResultFactory.Create(value: f),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = g => ((Brep)g) switch {
                Brep b when b.IsSolid => ComputeMass(() => VolumeMassProperties.Compute(b), vmp => new Plane(vmp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
                Brep b when b.SolidOrientation != BrepSolidOrientation.None => ComputeMass(() => AreaMassProperties.Compute(b), amp => new Plane(amp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
                Brep b => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
            },
            [typeof(Extrusion)] = g => ((Extrusion)g) switch {
                Extrusion e when e.IsSolid => ((Func<Result<Plane>>)(() => { using LineCurve path = e.PathLineCurve(); return ComputeMass(() => VolumeMassProperties.Compute(e), vmp => new Plane(vmp.Centroid, path.TangentAtStart)); }))(),
                Extrusion e when e.IsClosed(0) && e.IsClosed(1) => ((Func<Result<Plane>>)(() => { using LineCurve path = e.PathLineCurve(); return ComputeMass(() => AreaMassProperties.Compute(e), amp => new Plane(amp.Centroid, path.TangentAtStart)); }))(),
                Extrusion e => ((Func<Result<Plane>>)(() => { using LineCurve path = e.PathLineCurve(); return ResultFactory.Create(value: new Plane(e.GetBoundingBox(accurate: false).Center, path.TangentAtStart)); }))(),
            },
            [typeof(Mesh)] = g => ((Mesh)g) switch {
                Mesh m when m.IsClosed => ComputeMass(() => VolumeMassProperties.Compute(m), vmp => new Plane(vmp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)),
                Mesh m => ComputeMass(() => AreaMassProperties.Compute(m), amp => new Plane(amp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)),
            },
            [typeof(Point3d)] = g => ResultFactory.Create(value: new Plane((Point3d)g, Vector3d.ZAxis)),
            [typeof(PointCloud)] = g => ((PointCloud)g) switch {
                PointCloud pc when pc.Count > 0 => ResultFactory.Create(value: new Plane(pc[0].Location, Vector3d.ZAxis)),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();
}
