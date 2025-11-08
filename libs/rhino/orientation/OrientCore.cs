using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Plane/centroid extraction and transformation dispatch with mass property computation.</summary>
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

    /// <summary>Polymorphic centroid extraction using mass properties or bounding box - dispatches by geometry type and computation mode.</summary>
    internal static Result<Point3d> ExtractCentroid(GeometryBase geometry, bool useMassProperties) =>
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

    /// <summary>Extracts best-fit plane from geometry via PCA using sampled point distribution.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractBestFitPlane(GeometryBase geometry) =>
        ((Func<Result<Plane>>)(() => {
            Point3d[] points = geometry switch {
                PointCloud pc => pc.GetPoints(),
                Mesh m => m.Vertices.ToPoint3dArray(),
                Curve c => Enumerable.Range(0, OrientConfig.BestFitCurveSamples)
                    .Select(i => c.PointAt(c.Domain.ParameterAt((double)i / (OrientConfig.BestFitCurveSamples - 1))))
                    .ToArray(),
                Surface s => Enumerable.Range(0, OrientConfig.BestFitSurfaceSamplesU * OrientConfig.BestFitSurfaceSamplesV)
                    .Select(i => s.PointAt(
                        s.Domain(0).ParameterAt((double)(i % OrientConfig.BestFitSurfaceSamplesU) / (OrientConfig.BestFitSurfaceSamplesU - 1)),
                        s.Domain(1).ParameterAt((double)(i / OrientConfig.BestFitSurfaceSamplesU) / (OrientConfig.BestFitSurfaceSamplesV - 1))))
                    .ToArray(),
                Brep b => b.Vertices.Select(v => v.Location).ToArray(),
                _ => geometry.GetBoundingBox(accurate: true).GetCorners(),
            };
            PlaneFitResult fitResult = Plane.FitPlaneToPoints(points, out Plane bestFit);
            return fitResult == PlaneFitResult.Success && bestFit.IsValid
                ? ResultFactory.Create(value: bestFit)
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
        }))();

    /// <summary>Applies transformation to geometry with duplication and error handling.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(T geometry, Transform xform) where T : GeometryBase =>
        (T)geometry.Duplicate() switch {
            T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
        };
}
