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
    /// <summary>Canonical orientation mode specifier for standard world plane alignments.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Canonical(byte mode) {
        internal readonly byte Mode = mode;
        /// <summary>Align to world XY plane with origin at bounding box center.</summary>
        public static readonly Canonical WorldXY = new(1);
        /// <summary>Align to world YZ plane with origin at bounding box center.</summary>
        public static readonly Canonical WorldYZ = new(2);
        /// <summary>Align to world XZ plane with origin at bounding box center.</summary>
        public static readonly Canonical WorldXZ = new(3);
        /// <summary>Translate centroid to origin using area mass properties.</summary>
        public static readonly Canonical AreaCentroid = new(4);
        /// <summary>Translate centroid to origin using volume mass properties.</summary>
        public static readonly Canonical VolumeCentroid = new(5);
    }

    /// <summary>Polymorphic orientation target specification with plane, point, vector, or geometry-based references.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct OrientSpec {
        /// <summary>Target plane for plane-to-plane transformation.</summary>
        public Plane? TargetPlane { get; init; }
        /// <summary>Target point for translation-only transformation.</summary>
        public Point3d? TargetPoint { get; init; }
        /// <summary>Target vector for rotation-only transformation.</summary>
        public Vector3d? TargetVector { get; init; }
        /// <summary>Target curve for frame-based transformation.</summary>
        public Curve? TargetCurve { get; init; }
        /// <summary>Target surface for frame-based transformation.</summary>
        public Surface? TargetSurface { get; init; }
        /// <summary>Parameter for curve frame evaluation.</summary>
        public double CurveParameter { get; init; }
        /// <summary>UV coordinates for surface frame evaluation.</summary>
        public (double u, double v) SurfaceUV { get; init; }

        /// <summary>Creates plane-to-plane orientation specification.</summary>
        public static OrientSpec Plane(Plane p) => new() { TargetPlane = p };
        /// <summary>Creates point-to-point orientation specification.</summary>
        public static OrientSpec Point(Point3d p) => new() { TargetPoint = p };
        /// <summary>Creates vector-to-vector orientation specification.</summary>
        public static OrientSpec Vector(Vector3d v) => new() { TargetVector = v };
        /// <summary>Creates curve frame orientation specification.</summary>
        public static OrientSpec Curve(Curve c, double t) => new() { TargetCurve = c, CurveParameter = t };
        /// <summary>Creates surface frame orientation specification.</summary>
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
                    ? OrientCore.ApplyTransform(item, Transform.PlaneToPlane(src, target))
                    : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V m) ? m : V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (mode.Mode, item.GetBoundingBox(accurate: true)) switch {
                    (1, BoundingBox b) when b.IsValid => OrientCore.ApplyTransform(item, Transform.PlaneToPlane(new Plane(b.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY)),
                    (2, BoundingBox b) when b.IsValid => OrientCore.ApplyTransform(item, Transform.PlaneToPlane(new Plane(b.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ)),
                    (3, BoundingBox b) when b.IsValid => OrientCore.ApplyTransform(item, Transform.PlaneToPlane(new Plane(b.Center, Vector3d.XAxis, Vector3d.ZAxis), new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis))),
                    (4, BoundingBox b) when b.IsValid => OrientCore.ApplyTransform(item, Transform.Translation(Point3d.Origin - b.Center)),
                    (5, _) => OrientCore.ExtractCentroid(item, useMassProperties: true).Map(c => Transform.Translation(Point3d.Origin - c)).Bind(x => OrientCore.ApplyTransform(item, x)),
                    (_, BoundingBox b) when !b.IsValid => ResultFactory.Create<IReadOnlyList<T>>(error: E.Validation.BoundingBoxInvalid),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationMode),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = mode.Mode switch {
                    1 or 2 or 3 or 4 => V.Standard | V.BoundingBox,
                    5 => V.Standard | V.MassProperties,
                    _ => V.Standard,
                },
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d target, bool useMass, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassProperties: useMass)
                    .Map(c => Transform.Translation(target - c))
                    .Bind(x => OrientCore.ApplyTransform(item, x))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = useMass ? V.Standard | V.MassProperties : V.Standard | V.BoundingBox,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d target, Vector3d? source, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (item.GetBoundingBox(accurate: true), source ?? Vector3d.ZAxis, target) switch {
                    (BoundingBox b, Vector3d s, Vector3d t) when b.IsValid && s.Length > OrientConfig.MinVectorLength && t.Length > OrientConfig.MinVectorLength =>
                        (new Vector3d(s), new Vector3d(t)) switch {
                            (Vector3d su, Vector3d tu) when su.Unitize() && tu.Unitize() =>
                                (Vector3d.CrossProduct(su, tu).Length < OrientConfig.ParallelThreshold)
                                    ? (Math.Abs((su * tu) + 1.0) < OrientConfig.ParallelThreshold)
                                        ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.ParallelVectorAlignment)
                                        : OrientCore.ApplyTransform(item, Transform.Identity)
                                    : OrientCore.ApplyTransform(item, Transform.Rotation(su, tu, b.Center)),
                            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationVectors),
                        },
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationVectors),
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
                    ? OrientCore.ApplyTransform(item, Transform.Mirror(plane))
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
                    Brep b => ((Func<Result<IReadOnlyList<T>>>)(() => { b.Flip(onlyReversedFaces: false); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)b,]); }))(),
                    Extrusion e => e.ToBrep() switch {
                        Brep br => ((Func<Result<IReadOnlyList<T>>>)(() => { br.Flip(onlyReversedFaces: false); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)br,]); }))(),
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
