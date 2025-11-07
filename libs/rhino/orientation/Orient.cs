using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation with canonical positioning, alignment, and transformation.</summary>
public static class Orient {
    public readonly struct Canonical(byte mode) {
        internal readonly byte Mode = mode;
        public static readonly Canonical WorldXY = new(1);
        public static readonly Canonical WorldYZ = new(2);
        public static readonly Canonical WorldXZ = new(3);
        public static readonly Canonical AreaCentroid = new(4);
        public static readonly Canonical VolumeCentroid = new(5);
    }

    public readonly record struct OrientSpec {
        public Plane? TargetPlane { get; init; }
        public Point3d? TargetPoint { get; init; }
        public Vector3d? TargetVector { get; init; }
        public Curve? TargetCurve { get; init; }
        public Surface? TargetSurface { get; init; }
        public double CurveParameter { get; init; }
        public (double u, double v) SurfaceUV { get; init; }

        public static OrientSpec Plane(Plane p) => new() { TargetPlane = p };
        public static OrientSpec Point(Point3d p) => new() { TargetPoint = p };
        public static OrientSpec Vector(Vector3d v) => new() { TargetVector = v };
        public static OrientSpec Curve(Curve c, double t) => new() { TargetCurve = c, CurveParameter = t };
        public static OrientSpec Surface(Surface s, double u, double v) => new() { TargetSurface = s, SurfaceUV = (u, v) };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(T geometry, Plane target, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (OrientCore.PlaneExtractors.TryGetValue(item.GetType(), out Func<object, Result<Plane>>? ex)
                    ? ex(item)
                    : OrientCore.PlaneExtractors
                        .Where(kv => kv.Key.IsInstanceOfType(item))
                        .OrderByDescending(kv => kv.Key, Comparer<Type>.Create((a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                        .Select(kv => kv.Value(item))
                        .DefaultIfEmpty(ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name)))
                        .First())
                .Bind(src => target.IsValid
                    ? ResultFactory.Create(value: Transform.PlaneToPlane(src, target))
                    : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane))
                .Bind(xform => (T)item.Duplicate() switch {
                    T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V m) ? m : V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item => {
                Result<Transform> xformResult = (mode.Mode, item.GetBoundingBox(accurate: true)) switch {
                    (1, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(b.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY)),
                    (2, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(b.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ)),
                    (3, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(b.Center, Vector3d.XAxis, Vector3d.ZAxis), new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis))),
                    (4, _) => (item, item.GetBoundingBox(accurate: true)) switch {
                        (GeometryBase g, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.Translation(Point3d.Origin - b.Center)),
                        _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (5, _) => item switch {
                        Brep brep when brep.IsSolid => VolumeMassProperties.Compute(brep) switch {
                            VolumeMassProperties vmp => ((Func<Result<Transform>>)(() => { Point3d c = vmp.Centroid; vmp.Dispose(); return ResultFactory.Create(value: Transform.Translation(Point3d.Origin - c)); }))(),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        Brep brep when brep.SolidOrientation != BrepSolidOrientation.None => AreaMassProperties.Compute(brep) switch {
                            AreaMassProperties amp => ((Func<Result<Transform>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: Transform.Translation(Point3d.Origin - c)); }))(),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        Extrusion ext when ext.IsSolid => VolumeMassProperties.Compute(ext) switch {
                            VolumeMassProperties vmp => ((Func<Result<Transform>>)(() => { Point3d c = vmp.Centroid; vmp.Dispose(); return ResultFactory.Create(value: Transform.Translation(Point3d.Origin - c)); }))(),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        Extrusion ext when ext.IsClosed(0) && ext.IsClosed(1) => AreaMassProperties.Compute(ext) switch {
                            AreaMassProperties amp => ((Func<Result<Transform>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: Transform.Translation(Point3d.Origin - c)); }))(),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        Mesh mesh when mesh.IsClosed => VolumeMassProperties.Compute(mesh) switch {
                            VolumeMassProperties vmp => ((Func<Result<Transform>>)(() => { Point3d c = vmp.Centroid; vmp.Dispose(); return ResultFactory.Create(value: Transform.Translation(Point3d.Origin - c)); }))(),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        Mesh mesh => AreaMassProperties.Compute(mesh) switch {
                            AreaMassProperties amp => ((Func<Result<Transform>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: Transform.Translation(Point3d.Origin - c)); }))(),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        Curve curve when curve.IsClosed => AreaMassProperties.Compute(curve) switch {
                            AreaMassProperties amp => ((Func<Result<Transform>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: Transform.Translation(Point3d.Origin - c)); }))(),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                        },
                        _ => ResultFactory.Create<Transform>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (_, BoundingBox b) when !b.IsValid => ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
                };
                return xformResult.Bind(xform => (T)item.Duplicate() switch {
                    T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                });
            }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = mode.Mode switch {
                    1 or 2 or 3 => V.Standard | V.BoundingBox,
                    4 => V.Standard | V.BoundingBox,
                    5 => V.Standard | V.MassProperties,
                    _ => V.Standard,
                },
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d target, bool useMass, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item => {
                Result<Point3d> centroidResult = (item, useMass) switch {
                    (Brep brep, true) when brep.IsSolid => VolumeMassProperties.Compute(brep) switch {
                        VolumeMassProperties vmp => ((Func<Result<Point3d>>)(() => { Point3d c = vmp.Centroid; vmp.Dispose(); return ResultFactory.Create(value: c); }))(),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (Brep brep, true) when brep.SolidOrientation != BrepSolidOrientation.None => AreaMassProperties.Compute(brep) switch {
                        AreaMassProperties amp => ((Func<Result<Point3d>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: c); }))(),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (Extrusion ext, true) when ext.IsSolid => VolumeMassProperties.Compute(ext) switch {
                        VolumeMassProperties vmp => ((Func<Result<Point3d>>)(() => { Point3d c = vmp.Centroid; vmp.Dispose(); return ResultFactory.Create(value: c); }))(),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (Extrusion ext, true) when ext.IsClosed(0) && ext.IsClosed(1) => AreaMassProperties.Compute(ext) switch {
                        AreaMassProperties amp => ((Func<Result<Point3d>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: c); }))(),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (Mesh mesh, true) when mesh.IsClosed => VolumeMassProperties.Compute(mesh) switch {
                        VolumeMassProperties vmp => ((Func<Result<Point3d>>)(() => { Point3d c = vmp.Centroid; vmp.Dispose(); return ResultFactory.Create(value: c); }))(),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (Mesh mesh, true) => AreaMassProperties.Compute(mesh) switch {
                        AreaMassProperties amp => ((Func<Result<Point3d>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: c); }))(),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (Curve curve, true) when curve.IsClosed => AreaMassProperties.Compute(curve) switch {
                        AreaMassProperties amp => ((Func<Result<Point3d>>)(() => { Point3d c = amp.Centroid; amp.Dispose(); return ResultFactory.Create(value: c); }))(),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    (GeometryBase g, false) => g.GetBoundingBox(accurate: true) switch {
                        BoundingBox b when b.IsValid => ResultFactory.Create(value: b.Center),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    },
                    _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                };
                return centroidResult
                    .Map(c => Transform.Translation(target - c))
                    .Bind(xform => (T)item.Duplicate() switch {
                        T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    });
            }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = useMass ? V.Standard | V.MassProperties : V.Standard | V.BoundingBox,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d target, Vector3d? source, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item => {
                Result<Transform> xformResult = (item.GetBoundingBox(accurate: true), source ?? Vector3d.ZAxis, target) switch {
                    (BoundingBox b, Vector3d s, Vector3d t) when b.IsValid && s.Length > OrientConfig.MinVectorLength && t.Length > OrientConfig.MinVectorLength =>
                        (new Vector3d(s), new Vector3d(t)) switch {
                            (Vector3d su, Vector3d tu) when su.Unitize() && tu.Unitize() =>
                                (Vector3d.CrossProduct(su, tu).Length < OrientConfig.ParallelThreshold)
                                    ? (Math.Abs((su * tu) + 1.0) < OrientConfig.ParallelThreshold)
                                        ? ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment)
                                        : ResultFactory.Create(value: Transform.Identity)
                                    : ResultFactory.Create(value: Transform.Rotation(su, tu, b.Center)),
                            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
                        },
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
                };
                return xformResult.Bind(xform => (T)item.Duplicate() switch {
                    T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                });
            }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(T geometry, Plane plane, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                plane.IsValid
                    ? ResultFactory.Create(value: Transform.Mirror(plane))
                        .Bind(xform => (T)item.Duplicate() switch {
                            T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                        })
                    : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane)),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V m) ? m : V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                item.Duplicate() switch {
                    Curve c when c.Reverse() => ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)c,]),
                    Brep b => ((Func<Result<IReadOnlyList<T>>>)(() => { b.Flip(); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)b,]); }))(),
                    Extrusion e => e.ToBrep() switch {
                        Brep br => ((Func<Result<IReadOnlyList<T>>>)(() => { br.Flip(); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)br,]); }))(),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    },
                    Mesh m => ((Func<Result<IReadOnlyList<T>>>)(() => { m.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)m,]); }))(),
                    null => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name)),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V m) ? m : V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        (spec.TargetPlane, spec.TargetPoint, spec.TargetVector, spec.TargetCurve, spec.TargetSurface) switch {
            (null, null, null, null, null) => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode),
            (Plane p, null, null, null, null) when p != default => ToPlane(geometry, p, context),
            (null, Point3d pt, null, null, null) when pt != default => ToPoint(geometry, pt, useMass: false, context),
            (null, null, Vector3d v, null, null) when v != default => ToVector(geometry, v, source: null, context),
            (null, null, null, Curve c, null) => c.FrameAt(spec.CurveParameter, out Plane f) && f.IsValid
                ? ToPlane(geometry, f, context)
                : ResultFactory.Create<T>(error: E.Geometry.InvalidCurveParameter),
            (null, null, null, null, Surface s) => s.FrameAt(spec.SurfaceUV.u, spec.SurfaceUV.v, out Plane f) && f.IsValid
                ? ToPlane(geometry, f, context)
                : ResultFactory.Create<T>(error: E.Geometry.InvalidSurfaceUV),
            _ => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode),
        };
}
