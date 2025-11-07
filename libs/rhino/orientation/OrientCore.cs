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
            [typeof(Curve)] = (g, ctx) => ((Curve)g).FrameAt(((Curve)g).Domain.Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsCurve)] = (g, ctx) => ((NurbsCurve)g).FrameAt(((NurbsCurve)g).Domain.Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(LineCurve)] = (g, ctx) => ((LineCurve)g).FrameAt(((LineCurve)g).Domain.Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PolylineCurve)] = (g, ctx) => ((PolylineCurve)g).FrameAt(((PolylineCurve)g).Domain.Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(ArcCurve)] = (g, ctx) => ((ArcCurve)g).FrameAt(((ArcCurve)g).Domain.Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Surface)] = (g, ctx) => ((Surface)g).FrameAt(((Surface)g).Domain(0).Mid, ((Surface)g).Domain(1).Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsSurface)] = (g, ctx) => ((NurbsSurface)g).FrameAt(((NurbsSurface)g).Domain(0).Mid, ((NurbsSurface)g).Domain(1).Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PlaneSurface)] = (g, ctx) => ((PlaneSurface)g).FrameAt(((PlaneSurface)g).Domain(0).Mid, ((PlaneSurface)g).Domain(1).Mid, out Plane frame) && frame.IsValid switch {
                true => ResultFactory.Create(value: frame),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = (g, ctx) => {
                Brep brep = (Brep)g;
                return ExtractCentroid(brep, useMassCentroid: true, ctx)
                    .Map(centroid => new Plane(
                        centroid,
                        brep.Faces.Count > 0 switch {
                            true => brep.Faces
                                .Cast<BrepFace>()
                                .OrderByDescending(f => AreaMassProperties.Compute(f)?.Area ?? 0.0)
                                .Select(f => f.NormalAt(f.Domain(0).Mid, f.Domain(1).Mid))
                                .FirstOrDefault(Vector3d.ZAxis),
                            false => Vector3d.ZAxis,
                        }));
            },
            [typeof(Extrusion)] = (g, ctx) => {
                Extrusion extrusion = (Extrusion)g;
                return ExtractCentroid(extrusion, useMassCentroid: true, ctx)
                    .Map(centroid => new Plane(
                        centroid,
                        extrusion.GetProfileTransformation(0.5) switch {
                            Transform xform when xform.IsValid => xform.ZAxis,
                            _ => Vector3d.ZAxis,
                        }));
            },
            [typeof(Mesh)] = (g, ctx) => {
                Mesh mesh = (Mesh)g;
                return ExtractCentroid(mesh, useMassCentroid: true, ctx)
                    .Map(centroid => new Plane(
                        centroid,
                        (mesh.FaceNormals.Count, mesh.Normals.Count) switch {
                            (> 0, _) => mesh.FaceNormals.Cast<Vector3f>()
                                .Aggregate(Vector3d.Zero, (acc, n) => acc + new Vector3d(n.X, n.Y, n.Z)) switch {
                                    Vector3d sum when sum.Length > OrientConfig.ToleranceDefaults.MinVectorLength => sum / mesh.FaceNormals.Count,
                                    _ => Vector3d.ZAxis,
                                },
                            (_, > 0) => new Vector3d(mesh.Normals[0].X, mesh.Normals[0].Y, mesh.Normals[0].Z),
                            _ => Vector3d.ZAxis,
                        }));
            },
            [typeof(Point)] = (g, ctx) =>
                ResultFactory.Create(value: new Plane(((Point)g).Location, Vector3d.ZAxis)),
            [typeof(PointCloud)] = (g, ctx) => ((PointCloud)g).Count > 0 switch {
                true => ResultFactory.Create(value: new Plane(((PointCloud)g).GetPoint(0).Location, Vector3d.ZAxis)),
                false => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed.WithContext("Empty PointCloud")),
            },
        }.ToFrozenDictionary();

    /// <summary>Extracts source plane from geometry using type-based FrozenDictionary dispatch with inheritance fallback.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to extract plane from.</param>
    /// <param name="context">Geometry context providing tolerance and validation settings.</param>
    /// <returns>Result containing extracted plane or error if extraction fails.</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractSourcePlane<T>(T geometry, IGeometryContext context) where T : GeometryBase {
        Type runtimeType = geometry.GetType();
        return _planeExtractors.TryGetValue(runtimeType, out Func<object, IGeometryContext, Result<Plane>>? extractor) switch {
            true => extractor(geometry, context),
            false => _planeExtractors
                .Where(kv => kv.Key.IsAssignableFrom(runtimeType))
                .OrderByDescending(kv => kv.Key.IsAssignableFrom(runtimeType) && !runtimeType.IsAssignableFrom(kv.Key))
                .ThenBy(kv => kv.Key.Name)
                .Select(kv => kv.Value(geometry, context))
                .DefaultIfEmpty(ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(runtimeType.Name)))
                .First(),
        };
    }

    /// <summary>Extracts centroid from geometry using mass properties for closed/solid geometry or bounding box center fallback.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to extract centroid from.</param>
    /// <param name="useMassCentroid">When true, uses AreaMassProperties/VolumeMassProperties for accurate centroids; when false, uses bounding box center.</param>
    /// <param name="context">Geometry context providing tolerance and validation settings.</param>
    /// <returns>Result containing centroid point or error if extraction fails.</returns>
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
                VolumeMassProperties.Compute(brep: extrusion) switch {
                    VolumeMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: extrusion.GetBoundingBox(accurate: false).Center),
                },
            (Extrusion extrusion, true) when extrusion.IsClosed(0) && extrusion.IsClosed(1) =>
                AreaMassProperties.Compute(geometry: extrusion) switch {
                    AreaMassProperties props when props is not null => ResultFactory.Create(value: props.Centroid),
                    _ => ResultFactory.Create(value: extrusion.GetBoundingBox(accurate: false).Center),
                },
            (GeometryBase _, _) =>
                ResultFactory.Create(value: geometry.GetBoundingBox(accurate: false).Center),
        };

    /// <summary>Extracts principal axis vector from geometry's local coordinate frame for vector alignment operations.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to extract principal axis from.</param>
    /// <param name="context">Geometry context providing tolerance and validation settings.</param>
    /// <returns>Result containing principal axis vector (typically ZAxis of local frame) or error if extraction fails.</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Vector3d> ExtractPrincipalAxis<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        ExtractSourcePlane(geometry, context).Map(plane => plane.ZAxis);

    /// <summary>Computes canonical transform for world plane alignment or centroid-based positioning using PlaneToPlane or Translation.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to compute transform for.</param>
    /// <param name="mode">Canonical positioning mode (WorldXY/YZ/XZ, AreaCentroid, VolumeCentroid).</param>
    /// <param name="context">Geometry context providing tolerance and validation settings.</param>
    /// <returns>Result containing canonical transform or error if computation fails.</returns>
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

    /// <summary>Computes rotation transform to align source vector with target direction, handling parallel/antiparallel edge cases.</summary>
    /// <param name="source">Source direction vector to rotate from.</param>
    /// <param name="target">Target direction vector to rotate toward.</param>
    /// <param name="center">Center point for rotation pivot.</param>
    /// <param name="context">Geometry context providing tolerance for vector validation.</param>
    /// <returns>Result containing rotation transform, identity for parallel vectors, or error for zero-length/antiparallel vectors.</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeVectorAlignment(Vector3d source, Vector3d target, Point3d center, IGeometryContext context) =>
        (source.Length, target.Length, source * target / (source.Length * target.Length)) switch {
            (double sLen, double tLen, _) when sLen < OrientConfig.ToleranceDefaults.MinVectorLength || tLen < OrientConfig.ToleranceDefaults.MinVectorLength =>
                ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors.WithContext("Zero-length vector")),
            (_, _, double cosAngle) when cosAngle >= OrientConfig.ToleranceDefaults.ParallelCosineThreshold =>
                ResultFactory.Create(value: Transform.Identity),
            (_, _, double cosAngle) when cosAngle <= OrientConfig.ToleranceDefaults.AntiparallelCosineThreshold =>
                ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment.WithContext("Antiparallel vectors require reference plane")),
            _ =>
                ResultFactory.Create(value: Transform.Rotation(source, target, center)),
        };

    /// <summary>Flips geometry direction using type-specific in-place mutation with explicit null safety checks.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to flip (must be duplicated by caller).</param>
    /// <param name="context">Geometry context (unused but maintained for API consistency).</param>
    /// <returns>Result containing flipped geometry or error if flip operation fails or type unsupported.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipGeometryDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        geometry switch {
            Curve curve when curve.Reverse() =>
                ResultFactory.Create(value: (T)(object)curve),
            Curve _ =>
                ResultFactory.Create<T>(error: E.Geometry.TransformFailed.WithContext("Curve.Reverse failed")),
            Brep brep => (brep.Flip(), brep) switch {
                (_, Brep flipped) => ResultFactory.Create(value: (T)(object)flipped),
            },
            Mesh mesh when mesh.Flip(flipVertexNormals: true, flipFaceNormals: true, flipFaceOrientation: true) =>
                ResultFactory.Create(value: (T)(object)mesh),
            Mesh _ =>
                ResultFactory.Create<T>(error: E.Geometry.TransformFailed.WithContext("Mesh.Flip failed")),
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext($"Type: {geometry.GetType().Name}")),
        };
}
