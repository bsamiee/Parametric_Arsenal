using System;
using System.Collections.Frozen;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Validation-driven orientation dispatch with minimal strategy surface.</summary>
internal static class OrientCore {
    private static readonly FrozenDictionary<Type, (byte Kind, V PlaneMode, bool SupportsBestFit)> TypeMetadata = new Dictionary<Type, (byte Kind, V PlaneMode, bool SupportsBestFit)> {
        [typeof(Curve)] = (1, V.None, false),
        [typeof(NurbsCurve)] = (1, V.None, false),
        [typeof(LineCurve)] = (1, V.None, false),
        [typeof(ArcCurve)] = (1, V.None, false),
        [typeof(PolyCurve)] = (1, V.None, false),
        [typeof(PolylineCurve)] = (1, V.None, false),
        [typeof(Surface)] = (2, V.None, false),
        [typeof(NurbsSurface)] = (2, V.None, false),
        [typeof(PlaneSurface)] = (2, V.None, false),
        [typeof(Brep)] = (3, V.MassProperties | V.BoundingBox, false),
        [typeof(Extrusion)] = (4, V.MassProperties | V.BoundingBox, false),
        [typeof(Mesh)] = (5, V.MassProperties | V.BoundingBox, true),
        [typeof(PointCloud)] = (6, V.None, true),
    }.ToFrozenDictionary();

    private static Result<(byte Kind, V PlaneMode, bool SupportsBestFit)> ResolveKind(GeometryBase geometry) =>
        geometry is null
            ? ResultFactory.Create<(byte Kind, V PlaneMode, bool SupportsBestFit)>(error: E.Geometry.UnsupportedOrientationType.WithContext("null"))
            : TypeMetadata.TryGetValue(geometry.GetType(), out (byte Kind, V PlaneMode, bool SupportsBestFit) direct)
                ? ResultFactory.Create(value: direct)
                : ((Func<Result<(byte Kind, V PlaneMode, bool SupportsBestFit)>>)(() => {
                    KeyValuePair<Type, (byte Kind, V PlaneMode, bool SupportsBestFit)> match = TypeMetadata.FirstOrDefault(pair => pair.Key.IsInstanceOfType(geometry));
                    return match.Key is not null
                        ? ResultFactory.Create(value: match.Value)
                        : ResultFactory.Create<(byte Kind, V PlaneMode, bool SupportsBestFit)>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name));
                }))();

    internal static Result<Point3d> ExtractCentroid(GeometryBase geometry, bool useMassProperties, IGeometryContext context) =>
        ResolveKind(geometry)
            .Bind(_ => {
                Type runtimeType = geometry.GetType();
                V configuredMode;
                V baseMode = OrientConfig.ValidationModes.TryGetValue(runtimeType, out configuredMode) ? configuredMode : V.Standard;
                V validationMode = baseMode | (useMassProperties ? V.MassProperties : V.BoundingBox);

                return ResultFactory.Create(value: geometry)
                    .Validate(args: [context, validationMode,])
                    .Bind(valid => (useMassProperties, valid) switch {
                        (true, Curve curve) => ((Func<Result<Point3d>>)(() => {
                            using AreaMassProperties? properties = AreaMassProperties.Compute(curve);
                            return properties?.Centroid is Point3d centroid
                                ? ResultFactory.Create(value: centroid)
                                : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                        }))(),
                        (true, Surface surface) => ((Func<Result<Point3d>>)(() => {
                            using AreaMassProperties? properties = AreaMassProperties.Compute(surface);
                            return properties?.Centroid is Point3d centroid
                                ? ResultFactory.Create(value: centroid)
                                : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                        }))(),
                        (true, Brep brep) => (brep.IsSolid
                            ? (Func<Result<Point3d>>)(() => {
                                using VolumeMassProperties? properties = VolumeMassProperties.Compute(brep);
                                return properties?.Centroid is Point3d centroid
                                    ? ResultFactory.Create(value: centroid)
                                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                            })
                            : (Func<Result<Point3d>>)(() => {
                                using AreaMassProperties? properties = AreaMassProperties.Compute(brep);
                                return properties?.Centroid is Point3d centroid
                                    ? ResultFactory.Create(value: centroid)
                                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                            }))(),
                        (true, Extrusion extrusion) => (extrusion.IsSolid
                            ? (Func<Result<Point3d>>)(() => {
                                using VolumeMassProperties? properties = VolumeMassProperties.Compute(extrusion);
                                return properties?.Centroid is Point3d centroid
                                    ? ResultFactory.Create(value: centroid)
                                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                            })
                            : extrusion.IsClosed(0) && extrusion.IsClosed(1)
                                ? (Func<Result<Point3d>>)(() => {
                                    using AreaMassProperties? properties = AreaMassProperties.Compute(extrusion);
                                    return properties?.Centroid is Point3d centroid
                                        ? ResultFactory.Create(value: centroid)
                                        : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                                })
                                : (Func<Result<Point3d>>)(() => extrusion.GetBoundingBox(accurate: true) switch {
                                    BoundingBox box when box.IsValid => ResultFactory.Create(value: box.Center),
                                    _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                                }))(),
                        (true, Mesh mesh) => (mesh.IsClosed
                            ? (Func<Result<Point3d>>)(() => {
                                using VolumeMassProperties? properties = VolumeMassProperties.Compute(mesh);
                                return properties?.Centroid is Point3d centroid
                                    ? ResultFactory.Create(value: centroid)
                                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                            })
                            : (Func<Result<Point3d>>)(() => {
                                using AreaMassProperties? properties = AreaMassProperties.Compute(mesh);
                                return properties?.Centroid is Point3d centroid
                                    ? ResultFactory.Create(value: centroid)
                                    : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed);
                            }))(),
                        (_, PointCloud cloud) => cloud.GetBoundingBox(accurate: true) switch {
                            BoundingBox box when box.IsValid => ResultFactory.Create(value: box.Center),
                            _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        (false, GeometryBase geometryBase) => geometryBase.GetBoundingBox(accurate: true) switch {
                            BoundingBox box when box.IsValid => ResultFactory.Create(value: box.Center),
                            _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.UnsupportedOrientationType.WithContext(runtimeType.Name)),
                    });
            });

    internal static Result<Plane> ExtractPlane(GeometryBase geometry, IGeometryContext context) =>
        ResolveKind(geometry)
            .Bind(metadata => {
                Type runtimeType = geometry.GetType();
                V configuredMode;
                V baseMode = OrientConfig.ValidationModes.TryGetValue(runtimeType, out configuredMode) ? configuredMode : V.Standard;
                V validationMode = baseMode | metadata.PlaneMode;

                return ResultFactory.Create(value: geometry)
                    .Validate(args: [context, validationMode,])
                    .Bind(valid => valid switch {
                        Curve curve => curve.FrameAt(curve.Domain.Mid, out Plane frame) && frame.IsValid
                            ? ResultFactory.Create(value: frame)
                            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                        Surface surface => surface.FrameAt(surface.Domain(0).Mid, surface.Domain(1).Mid, out Plane frame) && frame.IsValid
                            ? ResultFactory.Create(value: frame)
                            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                        Brep brep => ((Func<Result<Plane>>)(() => {
                            Vector3d normal = brep.Faces.Count > 0 ? brep.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis;
                            bool solid = brep.IsSolid;
                            bool oriented = brep.SolidOrientation != BrepSolidOrientation.None;
                            return (solid
                                ? (Func<Result<Plane>>)(() => {
                                    using VolumeMassProperties? properties = VolumeMassProperties.Compute(brep);
                                    return properties?.Centroid is Point3d centroid
                                        ? ResultFactory.Create(value: new Plane(centroid, normal))
                                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                })
                                : oriented
                                    ? (Func<Result<Plane>>)(() => {
                                        using AreaMassProperties? properties = AreaMassProperties.Compute(brep);
                                        return properties?.Centroid is Point3d centroid
                                            ? ResultFactory.Create(value: new Plane(centroid, normal))
                                            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                    })
                                    : (Func<Result<Plane>>)(() => {
                                        BoundingBox box = brep.GetBoundingBox(accurate: true);
                                        return box.IsValid
                                            ? ResultFactory.Create(value: new Plane(box.Center, normal))
                                            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                    }))();
                        }))(),
                        Extrusion extrusion => ((Func<Result<Plane>>)(() => {
                            using LineCurve path = extrusion.PathLineCurve();
                            Vector3d tangent = path.TangentAtStart;
                            bool solid = extrusion.IsSolid;
                            bool closed = extrusion.IsClosed(0) && extrusion.IsClosed(1);
                            return (solid
                                ? (Func<Result<Plane>>)(() => {
                                    using VolumeMassProperties? properties = VolumeMassProperties.Compute(extrusion);
                                    return properties?.Centroid is Point3d centroid
                                        ? ResultFactory.Create(value: new Plane(centroid, tangent))
                                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                })
                                : closed
                                    ? (Func<Result<Plane>>)(() => {
                                        using AreaMassProperties? properties = AreaMassProperties.Compute(extrusion);
                                        return properties?.Centroid is Point3d centroid
                                            ? ResultFactory.Create(value: new Plane(centroid, tangent))
                                            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                    })
                                    : (Func<Result<Plane>>)(() => {
                                        BoundingBox box = extrusion.GetBoundingBox(accurate: true);
                                        return box.IsValid
                                            ? ResultFactory.Create(value: new Plane(box.Center, tangent))
                                            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                    }))();
                        }))(),
                        Mesh mesh => ((Func<Result<Plane>>)(() => {
                            Vector3d normal = mesh.Normals.Count > 0 ? mesh.Normals[0] : Vector3d.ZAxis;
                            bool solid = mesh.IsClosed;
                            return (solid
                                ? (Func<Result<Plane>>)(() => {
                                    using VolumeMassProperties? properties = VolumeMassProperties.Compute(mesh);
                                    return properties?.Centroid is Point3d centroid
                                        ? ResultFactory.Create(value: new Plane(centroid, normal))
                                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                })
                                : (Func<Result<Plane>>)(() => {
                                    using AreaMassProperties? properties = AreaMassProperties.Compute(mesh);
                                    return properties?.Centroid is Point3d centroid
                                        ? ResultFactory.Create(value: new Plane(centroid, normal))
                                        : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed);
                                }))();
                        }))(),
                        PointCloud cloud => cloud.Count > 0
                            ? ResultFactory.Create(value: new Plane(cloud[0].Location, Vector3d.ZAxis))
                            : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                        _ => ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(runtimeType.Name)),
                    });
            });

    internal static Result<Plane> ExtractBestFitPlane(GeometryBase geometry, IGeometryContext context) =>
        ResolveKind(geometry)
            .Bind(metadata => metadata.SupportsBestFit
                ? ((Func<Result<Plane>>)(() => {
                    Type runtimeType = geometry.GetType();
                    V configuredMode;
                    V baseMode = OrientConfig.ValidationModes.TryGetValue(runtimeType, out configuredMode) ? configuredMode : V.Standard;
                    V validationMode = baseMode | V.Degeneracy;

                    return ResultFactory.Create(value: geometry)
                        .Validate(args: [context, validationMode,])
                        .Bind(valid => valid switch {
                            Mesh mesh => mesh.Vertices.Count >= OrientConfig.BestFitMinPoints && Plane.FitPlaneToPoints(mesh.Vertices.ToPoint3dArray(), out Plane plane) == PlaneFitResult.Success
                                ? ResultFactory.Create(value: plane)
                                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                            PointCloud cloud => cloud.Count >= OrientConfig.BestFitMinPoints && Plane.FitPlaneToPoints(cloud.GetPoints(), out Plane plane) == PlaneFitResult.Success
                                ? ResultFactory.Create(value: plane)
                                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                            _ => ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(runtimeType.Name)),
                        });
                }))()
                : ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(T geometry, Transform transform) where T : GeometryBase =>
        ResolveKind(geometry)
            .Bind(_ => {
                GeometryBase? duplicate = geometry switch {
                    Curve curve => (GeometryBase?)curve.DuplicateCurve(),
                    Surface surface => surface.DuplicateSurface(),
                    Brep brep => brep.DuplicateBrep(),
                    Extrusion extrusion => extrusion.Duplicate(),
                    Mesh mesh => mesh.DuplicateMesh(),
                    PointCloud cloud => cloud.Duplicate(),
                    _ => null,
                };

                return duplicate is null
                    ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed)
                    : duplicate.Transform(transform)
                        ? duplicate is T typed
                            ? ResultFactory.Create(value: (IReadOnlyList<T>)[typed,])
                            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.UnsupportedOrientationType.WithContext(typeof(T).Name))
                        : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed);
            });

    internal static Result<T> FlipGeometry<T>(T geometry) where T : GeometryBase =>
        ResolveKind(geometry)
            .Bind(_ => geometry switch {
                Curve curve => ResultFactory.Create(value: curve.DuplicateCurve())
                    .Bind(duplicate => duplicate is null
                        ? ResultFactory.Create<T>(error: E.Geometry.TransformFailed)
                        : duplicate.Reverse()
                            ? duplicate is T typed
                                ? ResultFactory.Create(value: typed)
                                : ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(typeof(T).Name))
                            : ResultFactory.Create<T>(error: E.Geometry.TransformFailed)),
                Brep brep => ResultFactory.Create(value: brep.DuplicateBrep())
                    .Bind(duplicate => duplicate is null
                        ? ResultFactory.Create<T>(error: E.Geometry.TransformFailed)
                        : ((Func<Result<T>>)(() => {
                            duplicate.Flip();
                            return duplicate is T typed
                                ? ResultFactory.Create(value: typed)
                                : ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(typeof(T).Name));
                        }))()),
                Extrusion extrusion => ResultFactory.Create(value: extrusion.ToBrep())
                    .Bind(brep => brep is null
                        ? ResultFactory.Create<T>(error: E.Geometry.TransformFailed)
                        : ((Func<Result<T>>)(() => {
                            brep.Flip();
                            return Extrusion.TryGetExtrusion(brep, out Extrusion? extracted) && extracted is not null
                                ? extracted is T typed
                                    ? ResultFactory.Create(value: typed)
                                    : ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(typeof(T).Name))
                                : ResultFactory.Create<T>(error: E.Geometry.TransformFailed);
                        }))()),
                Mesh mesh => ResultFactory.Create(value: mesh.DuplicateMesh())
                    .Bind(duplicate => duplicate is null
                        ? ResultFactory.Create<T>(error: E.Geometry.TransformFailed)
                        : ((Func<Result<T>>)(() => {
                            duplicate.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true);
                            return duplicate is T typed
                                ? ResultFactory.Create(value: typed)
                                : ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(typeof(T).Name));
                        }))()),
                PointCloud cloud => ResultFactory.Create(value: cloud.Duplicate())
                    .Bind(duplicate => duplicate is null
                        ? ResultFactory.Create<T>(error: E.Geometry.TransformFailed)
                        : duplicate is T typed
                            ? ResultFactory.Create(value: typed)
                            : ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(typeof(T).Name))),
                _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext(typeof(T).Name)),
            });
}
