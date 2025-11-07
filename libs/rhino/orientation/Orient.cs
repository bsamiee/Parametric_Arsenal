using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation engine with canonical positioning, alignment, and transformation operations.</summary>
public static class Orient {
    /// <summary>Semantic marker for canonical positioning modes.</summary>
    public readonly struct Canonical(byte mode) {
        internal readonly byte Mode = mode;

        public static readonly Canonical WorldXY = new(1);
        public static readonly Canonical WorldYZ = new(2);
        public static readonly Canonical WorldXZ = new(3);
        public static readonly Canonical AreaCentroid = new(4);
        public static readonly Canonical VolumeCentroid = new(5);
    }

    /// <summary>Polymorphic specification for orientation alignment targets.</summary>
    public readonly record struct OrientSpec {
        public required object Target { get; init; }
        public Plane? TargetPlane { get; init; }
        public Point3d? TargetPoint { get; init; }
        public Vector3d? TargetVector { get; init; }
        public Curve? TargetCurve { get; init; }
        public Surface? TargetSurface { get; init; }
        public double CurveParameter { get; init; }
        public (double u, double v) SurfaceUV { get; init; }

        public static OrientSpec Plane(Plane plane) => new() { Target = plane, TargetPlane = plane, };
        public static OrientSpec Point(Point3d point) => new() { Target = point, TargetPoint = point, };
        public static OrientSpec Vector(Vector3d vector) => new() { Target = vector, TargetVector = vector, };
        public static OrientSpec Curve(Curve curve, double t) => new() { Target = curve, TargetCurve = curve, CurveParameter = t, };
        public static OrientSpec Surface(Surface surface, double u, double v) => new() { Target = surface, TargetSurface = surface, SurfaceUV = (u, v), };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(T geometry, Plane targetPlane, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractSourcePlane(item, context)
                    .Bind(sourcePlane => targetPlane.IsValid switch {
                        false => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane),
                        _ => ResultFactory.Create(value: Transform.PlaneToPlane(sourcePlane, targetPlane)),
                    })
                    .Map(xform => {
                        T result = (T)item.Duplicate();
                        return result.Transform(xform) switch {
                            true => (IReadOnlyList<T>)[result,],
                            false => throw new InvalidOperationException(E.Geometry.TransformFailed.Message),
                        };
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ComputeCanonicalTransform(item, mode, context)
                    .Map(xform => {
                        T result = (T)item.Duplicate();
                        return result.Transform(xform) switch {
                            true => (IReadOnlyList<T>)[result,],
                            false => throw new InvalidOperationException(E.Geometry.TransformFailed.Message),
                        };
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = mode.Mode switch {
                    1 or 2 or 3 => V.Standard | V.BoundingBox,
                    4 => V.Standard | V.AreaCentroid,
                    5 => V.Standard | V.MassProperties,
                    _ => V.Standard,
                },
            }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d target, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassCentroid, context)
                    .Map(centroid => Transform.Translation(target - centroid))
                    .Map(xform => {
                        T result = (T)item.Duplicate();
                        return result.Transform(xform) switch {
                            true => (IReadOnlyList<T>)[result,],
                            false => throw new InvalidOperationException(E.Geometry.TransformFailed.Message),
                        };
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = useMassCentroid ? V.Standard | V.MassProperties : V.Standard | V.BoundingBox,
            }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d targetDirection, Vector3d? sourceAxis, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassCentroid: false, context)
                    .Bind(center => OrientCore.ComputeVectorAlignment(
                        sourceAxis ?? Vector3d.ZAxis,
                        targetDirection,
                        center,
                        context))
                    .Map(xform => {
                        T result = (T)item.Duplicate();
                        return result.Transform(xform) switch {
                            true => (IReadOnlyList<T>)[result,],
                            false => throw new InvalidOperationException(E.Geometry.TransformFailed.Message),
                        };
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = V.Standard,
            }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(T geometry, Plane mirrorPlane, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                mirrorPlane.IsValid switch {
                    false => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane),
                    _ => ResultFactory.Create(value: Transform.Mirror(mirrorPlane))
                        .Map(xform => {
                            T result = (T)item.Duplicate();
                            return result.Transform(xform) switch {
                                true => (IReadOnlyList<T>)[result,],
                                false => throw new InvalidOperationException(E.Geometry.TransformFailed.Message),
                            };
                        }),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.FlipGeometryDirection(item, context)
                    .Map(flipped => (IReadOnlyList<T>)[flipped,])),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            }).Map(results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (spec.TargetPlane, spec.TargetPoint, spec.TargetVector, spec.TargetCurve, spec.TargetSurface) switch {
                    (Plane p, null, null, null, null) => ToPlane(item, p, context).Map(r => (IReadOnlyList<T>)[r,]),
                    (null, Point3d pt, null, null, null) => ToPoint(item, pt, useMassCentroid: false, context).Map(r => (IReadOnlyList<T>)[r,]),
                    (null, null, Vector3d v, null, null) => ToVector(item, v, sourceAxis: null, context).Map(r => (IReadOnlyList<T>)[r,]),
                    (null, null, null, Curve c, null) => c.FrameAt(spec.CurveParameter, out Plane frame) switch {
                        true when frame.IsValid => ToPlane(item, frame, context).Map(r => (IReadOnlyList<T>)[r,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidCurveParameter),
                    },
                    (null, null, null, null, Surface s) => s.FrameAt(spec.SurfaceUV.u, spec.SurfaceUV.v, out Plane frame) switch {
                        true when frame.IsValid => ToPlane(item, frame, context).Map(r => (IReadOnlyList<T>)[r,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidSurfaceUV),
                    },
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationMode),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            }).Map(results => results[0]);
}
