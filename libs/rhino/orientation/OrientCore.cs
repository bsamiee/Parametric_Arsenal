using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
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
            [typeof(Brep)] = (g, ctx) => {
                Brep brep = (Brep)g;
                BoundingBox bbox = brep.GetBoundingBox(accurate: false);
                Point3d centroid = (brep.IsSolid, brep.IsClosed) switch {
                    (true, _) => VolumeMassProperties.Compute(brep) switch {
                        VolumeMassProperties props when props is not null => props.Centroid,
                        _ => bbox.Center,
                    },
                    (_, true) => AreaMassProperties.Compute(brep) switch {
                        AreaMassProperties props when props is not null => props.Centroid,
                        _ => bbox.Center,
                    },
                    _ => bbox.Center,
                };
                Vector3d normal = brep.Faces.Count > 0 switch {
                    true => brep.Faces[0].NormalAt(0.5, 0.5),
                    false => Vector3d.ZAxis,
                };
                return ResultFactory.Create(value: new Plane(centroid, normal));
            },
            [typeof(Extrusion)] = (g, ctx) => {
                Extrusion extrusion = (Extrusion)g;
                BoundingBox bbox = extrusion.GetBoundingBox(accurate: false);
                Point3d centroid = (extrusion.IsSolid, extrusion.IsClosed(0) && extrusion.IsClosed(1)) switch {
                    (true, _) => VolumeMassProperties.Compute(brep: extrusion.ToBrep()) switch {
                        VolumeMassProperties props when props is not null => props.Centroid,
                        _ => bbox.Center,
                    },
                    (_, true) => AreaMassProperties.Compute(brep: extrusion.ToBrep()) switch {
                        AreaMassProperties props when props is not null => props.Centroid,
                        _ => bbox.Center,
                    },
                    _ => bbox.Center,
                };
                Vector3d normal = extrusion.GetProfileTransformation(0.5) switch {
                    Transform xform when xform.IsValid => xform.ZAxis,
                    _ => Vector3d.ZAxis,
                };
                return ResultFactory.Create(value: new Plane(centroid, normal));
            },
            [typeof(Mesh)] = (g, ctx) => {
                Mesh mesh = (Mesh)g;
                BoundingBox bbox = mesh.GetBoundingBox(accurate: false);
                Point3d centroid = mesh.IsClosed switch {
                    true => VolumeMassProperties.Compute(mesh) switch {
                        VolumeMassProperties props when props is not null => props.Centroid,
                        _ => bbox.Center,
                    },
                    false => AreaMassProperties.Compute(mesh) switch {
                        AreaMassProperties props when props is not null => props.Centroid,
                        _ => bbox.Center,
                    },
                };
                Vector3d normal = mesh.Normals.Count > 0 switch {
                    true => mesh.Normals[0],
                    false => Vector3d.ZAxis,
                };
                return ResultFactory.Create(value: new Plane(centroid, normal));
            },
            [typeof(Point)] = (g, ctx) =>
                ResultFactory.Create(value: new Plane(((Point)g).Location, Vector3d.ZAxis)),
            [typeof(PointCloud)] = (g, ctx) => ((PointCloud)g).Count > 0 switch {
                true => ResultFactory.Create(value: new Plane(((PointCloud)g).GetPoint(0).Location, Vector3d.ZAxis)),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed.WithContext("Empty PointCloud")),
            },
        }.ToFrozenDictionary();

    /// <summary>Extracts source plane from geometry using type-based FrozenDictionary dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractSourcePlane<T>(T geometry, IGeometryContext context) where T : GeometryBase {
        Type runtimeType = geometry.GetType();
        return _planeExtractors.TryGetValue(runtimeType, out Func<object, IGeometryContext, Result<Plane>>? extractor) switch {
            true => extractor(geometry, context),
            false => _planeExtractors
                .Where(kv => kv.Key.IsAssignableFrom(runtimeType))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create((a, b) =>
                    a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
                .Select(kv => kv.Value(geometry, context))
                .FirstOrDefault() ?? ResultFactory.Create<Plane>(
                    error: E.Geometry.UnsupportedOrientationType.WithContext(runtimeType.Name)),
        };
    }

    /// <summary>Extracts centroid from geometry using mass properties or bounding box fallback.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d> ExtractCentroid<T>(T geometry, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        (geometry, useMassCentroid) switch {
            (Brep brep, true) when brep.IsSolid =>
                VolumeMassProperties.Compute(brep) switch {
                    VolumeMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: brep.GetBoundingBox(accurate: false).Center),
                },
            (Brep brep, true) when brep.IsClosed =>
                AreaMassProperties.Compute(brep) switch {
                    AreaMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: brep.GetBoundingBox(accurate: false).Center),
                },
            (Mesh mesh, true) when mesh.IsClosed =>
                VolumeMassProperties.Compute(mesh) switch {
                    VolumeMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: mesh.GetBoundingBox(accurate: false).Center),
                },
            (Mesh mesh, true) =>
                AreaMassProperties.Compute(mesh) switch {
                    AreaMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: mesh.GetBoundingBox(accurate: false).Center),
                },
            (Curve curve, true) when curve.IsClosed =>
                AreaMassProperties.Compute(curve) switch {
                    AreaMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: curve.GetBoundingBox(accurate: false).Center),
                },
            (Extrusion extrusion, true) when extrusion.IsSolid =>
                VolumeMassProperties.Compute(brep: extrusion.ToBrep()) switch {
                    VolumeMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: extrusion.GetBoundingBox(accurate: false).Center),
                },
            (Extrusion extrusion, true) when extrusion.IsClosed(0) && extrusion.IsClosed(1) =>
                AreaMassProperties.Compute(brep: extrusion.ToBrep()) switch {
                    AreaMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: extrusion.GetBoundingBox(accurate: false).Center),
                },
            (GeometryBase geom, _) =>
                ResultFactory.Create(value: geom.GetBoundingBox(accurate: false).Center),
        };

    /// <summary>Computes canonical transform for world plane alignment or centroid positioning.</summary>
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
            (4, _) =>
                ExtractCentroid(geometry, useMassCentroid: false, context)
                    .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (5, _) =>
                ExtractCentroid(geometry, useMassCentroid: true, context)
                    .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (_, BoundingBox bbox) when !bbox.IsValid =>
                ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode.WithContext($"Mode: {mode.Mode}")),
        };

    /// <summary>Computes rotation transform to align source vector with target direction.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeVectorAlignment(Vector3d source, Vector3d target, Point3d center, IGeometryContext context) =>
        (source.Length, target.Length, source * target / (source.Length * target.Length)) switch {
            (double sLen, double tLen, double cosAngle) when sLen < OrientConfig.ToleranceDefaults.MinVectorLength || tLen < OrientConfig.ToleranceDefaults.MinVectorLength =>
                ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors.WithContext("Zero-length vector")),
            (_, _, double cosAngle) when cosAngle >= OrientConfig.ToleranceDefaults.ParallelCosineThreshold =>
                ResultFactory.Create(value: Transform.Identity),
            (_, _, double cosAngle) when cosAngle <= OrientConfig.ToleranceDefaults.AntiparallelCosineThreshold =>
                ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment.WithContext("Antiparallel vectors require reference plane")),
            (_, _, _) =>
                ResultFactory.Create(value: Transform.Rotation(source, target, center)),
        };

    /// <summary>Flips geometry direction using type-specific in-place mutation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipGeometryDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        geometry switch {
            Curve curve when curve.Reverse() =>
                ResultFactory.Create(value: (T)(object)curve),
            Curve curve =>
                ResultFactory.Create<T>(error: E.Geometry.TransformFailed.WithContext("Curve.Reverse failed")),
            Brep brep => (brep.Flip(), ResultFactory.Create(value: (T)(object)brep)) switch {
                (_, Result<T> result) => result,
            },
            Mesh mesh when mesh.Flip(flipVertexNormals: true, flipFaceNormals: true, flipFaceOrientation: true) =>
                ResultFactory.Create(value: (T)(object)mesh),
            Mesh mesh =>
                ResultFactory.Create<T>(error: E.Geometry.TransformFailed.WithContext("Mesh.Flip failed")),
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext($"Type: {geometry.GetType().Name}")),
        };
}

/// <summary>Cached geometry properties to avoid recomputation in chained operations.</summary>
internal readonly record struct GeometryInfo(BoundingBox BoundingBox, Point3d Centroid, Plane LocalPlane);
