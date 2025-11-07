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
    /// <summary>FrozenDictionary dispatch for plane extraction from various geometry types.</summary>
    private static readonly FrozenDictionary<Type, Func<object, IGeometryContext, Result<Plane>>> _planeExtractors =
        new Dictionary<Type, Func<object, IGeometryContext, Result<Plane>>> {
            [typeof(Curve)] = (g, _) => ((Curve)g) switch {
                Curve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsCurve)] = (g, _) => ((NurbsCurve)g) switch {
                NurbsCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(LineCurve)] = (g, _) => ((LineCurve)g) switch {
                LineCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(ArcCurve)] = (g, _) => ((ArcCurve)g) switch {
                ArcCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PolyCurve)] = (g, _) => ((PolyCurve)g) switch {
                PolyCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PolylineCurve)] = (g, _) => ((PolylineCurve)g) switch {
                PolylineCurve c when c.FrameAt(c.Domain.Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Surface)] = (g, _) => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(NurbsSurface)] = (g, _) => ((NurbsSurface)g) switch {
                NurbsSurface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(PlaneSurface)] = (g, _) => ((PlaneSurface)g) switch {
                PlaneSurface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane frame) && frame.IsValid =>
                    ResultFactory.Create(value: frame),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = (g, ctx) => ExtractBrepPlane((Brep)g, ctx),
            [typeof(Mesh)] = (g, ctx) => ExtractMeshPlane((Mesh)g, ctx),
            [typeof(Point)] = (g, _) =>
                ResultFactory.Create(value: new Plane(((Point)g).Location, Vector3d.ZAxis)),
            [typeof(PointCloud)] = (g, _) => ((PointCloud)g) switch {
                PointCloud pc when pc.Count > 0 =>
                    ResultFactory.Create(value: new Plane(pc[0].Location, Vector3d.ZAxis)),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();

    /// <summary>Extracts source plane from geometry using type-based dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractSourcePlane<T>(T geometry, IGeometryContext context) where T : GeometryBase {
        Func<object, IGeometryContext, Result<Plane>>? extractor = _planeExtractors.TryGetValue(geometry.GetType(), out Func<object, IGeometryContext, Result<Plane>>? exact)
            ? exact
            : _planeExtractors
                .Where(kv => kv.Key.IsInstanceOfType(geometry))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create((a, b) =>
                    a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
                .Select(kv => kv.Value)
                .FirstOrDefault();
        return extractor is not null
            ? extractor(geometry, context)
            : ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name));
    }

    /// <summary>Extracts centroid from geometry using mass properties or bounding box.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d> ExtractCentroid<T>(T geometry, bool useMassProperties) where T : GeometryBase =>
        (geometry, useMassProperties) switch {
            (Brep { IsSolid: true } brep, true) => VolumeMassProperties.Compute(brep) switch {
                VolumeMassProperties props => ((Func<Result<Point3d>>)(() => {
                    Point3d centroid = props.Centroid;
                    props.Dispose();
                    return ResultFactory.Create(value: centroid);
                }))(),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Brep brep, false) when brep.SolidOrientation != BrepSolidOrientation.None =>
                AreaMassProperties.Compute(brep) switch {
                    AreaMassProperties props => ((Func<Result<Point3d>>)(() => {
                        Point3d centroid = props.Centroid;
                        props.Dispose();
                        return ResultFactory.Create(value: centroid);
                    }))(),
                    _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                },
            (Mesh { IsClosed: true } mesh, true) => VolumeMassProperties.Compute(mesh) switch {
                VolumeMassProperties props => ((Func<Result<Point3d>>)(() => {
                    Point3d centroid = props.Centroid;
                    props.Dispose();
                    return ResultFactory.Create(value: centroid);
                }))(),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Mesh mesh, false) => AreaMassProperties.Compute(mesh) switch {
                AreaMassProperties props => ((Func<Result<Point3d>>)(() => {
                    Point3d centroid = props.Centroid;
                    props.Dispose();
                    return ResultFactory.Create(value: centroid);
                }))(),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (Curve { IsClosed: true } curve, _) => AreaMassProperties.Compute(curve) switch {
                AreaMassProperties props => ((Func<Result<Point3d>>)(() => {
                    Point3d centroid = props.Centroid;
                    props.Dispose();
                    return ResultFactory.Create(value: centroid);
                }))(),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            (GeometryBase g, _) => g.GetBoundingBox(accurate: true) switch {
                BoundingBox { IsValid: true } bbox => ResultFactory.Create(value: bbox.Center),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
        };

    /// <summary>Computes canonical transform for positioning geometry to world planes or origin.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeCanonicalTransform<T>(T geometry, Orient.Canonical mode) where T : GeometryBase =>
        (mode.Mode, geometry.GetBoundingBox(accurate: true)) switch {
            (1, BoundingBox { IsValid: true } bbox) =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    new Plane(bbox.Center, Vector3d.XAxis, Vector3d.YAxis),
                    Plane.WorldXY)),
            (2, BoundingBox { IsValid: true } bbox) =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    new Plane(bbox.Center, Vector3d.YAxis, Vector3d.ZAxis),
                    Plane.WorldYZ)),
            (3, BoundingBox { IsValid: true } bbox) =>
                ResultFactory.Create(value: Transform.PlaneToPlane(
                    new Plane(bbox.Center, Vector3d.XAxis, Vector3d.ZAxis),
                    new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis))),
            (4, _) =>
                ExtractCentroid(geometry, useMassProperties: false)
                    .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (5, _) =>
                ExtractCentroid(geometry, useMassProperties: true)
                    .Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
            (_, BoundingBox { IsValid: false }) =>
                ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
        };

    /// <summary>Computes rotation transform to align source vector with target vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComputeVectorAlignment(Vector3d source, Vector3d target, Point3d center, IGeometryContext context) =>
        (source.Length > context.AbsoluteTolerance, target.Length > context.AbsoluteTolerance) switch {
            (false, _) or (_, false) =>
                ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
            (true, true) when Math.Abs(Vector3d.VectorAngle(source, target)) < OrientConfig.Tolerance.ParallelAngleThreshold =>
                ResultFactory.Create(value: Transform.Identity),
            (true, true) when Math.Abs(Vector3d.VectorAngle(source, target) - Math.PI) < OrientConfig.Tolerance.ParallelAngleThreshold =>
                ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment),
            (true, true) =>
                ResultFactory.Create(value: Transform.Rotation(source, target, center)),
        };

    /// <summary>Flips geometry direction based on type-specific operations.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipGeometryDirection<T>(T geometry) where T : GeometryBase =>
        geometry switch {
            Curve c => c.Reverse() switch {
                true => ResultFactory.Create(value: (T)(object)c),
                false => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            },
            Brep b => FlipBrep<T>(b),
            Mesh m => FlipMesh<T>(m),
            _ => ResultFactory.Create<T>(
                error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    /// <summary>Extracts plane from Brep using mass properties and face normal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Plane> ExtractBrepPlane(Brep brep, IGeometryContext context) {
        VolumeMassProperties? volumeProps = brep.IsSolid ? VolumeMassProperties.Compute(brep) : null;
        AreaMassProperties? areaProps = volumeProps is null && brep.SolidOrientation != BrepSolidOrientation.None
            ? AreaMassProperties.Compute(brep)
            : null;
        Point3d centroid = volumeProps?.Centroid
            ?? areaProps?.Centroid
            ?? brep.GetBoundingBox(accurate: false).Center;
        Vector3d normal = brep.Faces.Count > 0
            ? brep.Faces[0].NormalAt(brep.Faces[0].Domain(0).Mid, brep.Faces[0].Domain(1).Mid)
            : Vector3d.ZAxis;
        volumeProps?.Dispose();
        areaProps?.Dispose();
        return normal.Length > context.AbsoluteTolerance
            ? ResultFactory.Create(value: new Plane(centroid, normal))
            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
    }

    /// <summary>Extracts plane from Mesh using mass properties and vertex normal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Plane> ExtractMeshPlane(Mesh mesh, IGeometryContext context) {
        VolumeMassProperties? volumeProps = mesh.IsClosed ? VolumeMassProperties.Compute(mesh) : null;
        AreaMassProperties? areaProps = volumeProps is null ? AreaMassProperties.Compute(mesh) : null;
        Point3d centroid = volumeProps?.Centroid
            ?? areaProps?.Centroid
            ?? mesh.GetBoundingBox(accurate: false).Center;
        Vector3d normal = mesh.Normals.Count > 0
            ? mesh.Normals[0]
            : Vector3d.ZAxis;
        volumeProps?.Dispose();
        areaProps?.Dispose();
        return normal.Length > context.AbsoluteTolerance
            ? ResultFactory.Create(value: new Plane(centroid, normal))
            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
    }

    /// <summary>Flips Brep normals in-place and returns as Result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> FlipBrep<T>(Brep brep) where T : GeometryBase {
        brep.Flip();
        return ResultFactory.Create(value: (T)(object)brep);
    }

    /// <summary>Flips Mesh normals in-place and returns as Result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> FlipMesh<T>(Mesh mesh) where T : GeometryBase {
        mesh.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true);
        return ResultFactory.Create(value: (T)(object)mesh);
    }
}
