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
                OrientCore.DispatchByType(item, OrientCore.PlaneExtractors, E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name))
                    .Bind(src => target.IsValid
                        ? ResultFactory.Create(value: Transform.PlaneToPlane(src, target))
                        : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane))
                    .Bind(xform => OrientCore.ApplyTransform(item, xform).Map(g => (IReadOnlyList<T>)[(T)g,]))),
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
                    (4, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.Translation(Point3d.Origin - b.Center)),
                    (4, _) => ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
                    (5, _) => OrientCore.CentroidExtractor(item, true).Map(c => Transform.Translation(Point3d.Origin - c)),
                    (_, BoundingBox b) when !b.IsValid => ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
                };
                return xformResult.Bind(xform => OrientCore.ApplyTransform(item, xform).Map(g => (IReadOnlyList<T>)[(T)g,]));
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
                OrientCore.CentroidExtractor(item, useMass)
                    .Map(c => Transform.Translation(target - c))
                    .Bind(xform => OrientCore.ApplyTransform(item, xform).Map(g => (IReadOnlyList<T>)[(T)g,]))),
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
                return xformResult.Bind(xform => OrientCore.ApplyTransform(item, xform).Map(g => (IReadOnlyList<T>)[(T)g,]));
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
                        .Bind(xform => OrientCore.ApplyTransform(item, xform).Map(g => (IReadOnlyList<T>)[(T)g,]))
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
                OrientCore.DispatchByType(item, OrientCore.FlipOperations, E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name))
                    .Map(g => (IReadOnlyList<T>)[(T)g,])),
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
