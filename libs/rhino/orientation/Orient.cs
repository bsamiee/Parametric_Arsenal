using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation with canonical positioning and alignment operations.</summary>
public static class Orient {
    /// <summary>Semantic marker for canonical positioning modes.</summary>
    public readonly struct Canonical(byte mode) {
        internal readonly byte Mode = mode;

        /// <summary>Align bounding box center to World XY plane origin with XY axes.</summary>
        public static readonly Canonical WorldXY = new(1);

        /// <summary>Align bounding box center to World YZ plane origin with YZ axes.</summary>
        public static readonly Canonical WorldYZ = new(2);

        /// <summary>Align bounding box center to World XZ plane origin with XZ axes.</summary>
        public static readonly Canonical WorldXZ = new(3);

        /// <summary>Translate area centroid to world origin.</summary>
        public static readonly Canonical AreaCentroid = new(4);

        /// <summary>Translate volume centroid to world origin.</summary>
        public static readonly Canonical VolumeCentroid = new(5);
    }

    /// <summary>Polymorphic target specification for orientation operations.</summary>
    public readonly record struct OrientSpec {
        /// <summary>Target object for orientation operation.</summary>
        public required object Target { get; init; }

        /// <summary>Target plane when Target is Plane.</summary>
        public Plane? TargetPlane { get; init; }

        /// <summary>Target point when Target is Point3d.</summary>
        public Point3d? TargetPoint { get; init; }

        /// <summary>Target vector when Target is Vector3d.</summary>
        public Vector3d? TargetVector { get; init; }

        /// <summary>Target curve when Target is Curve.</summary>
        public Curve? TargetCurve { get; init; }

        /// <summary>Target surface when Target is Surface.</summary>
        public Surface? TargetSurface { get; init; }

        /// <summary>Curve parameter for frame extraction when using Curve target.</summary>
        public double CurveParameter { get; init; }

        /// <summary>Surface UV parameters for frame extraction when using Surface target.</summary>
        public (double u, double v) SurfaceUV { get; init; }

        /// <summary>Creates orientation specification targeting a plane.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Plane(Plane plane) => new() { Target = plane, TargetPlane = plane, };

        /// <summary>Creates orientation specification targeting a point.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Point(Point3d point) => new() { Target = point, TargetPoint = point, };

        /// <summary>Creates orientation specification targeting a vector direction.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Vector(Vector3d vector) => new() { Target = vector, TargetVector = vector, };

        /// <summary>Creates orientation specification targeting a curve frame at parameter t.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Curve(Curve curve, double t) => new() {
            Target = curve,
            TargetCurve = curve,
            CurveParameter = t,
        };

        /// <summary>Creates orientation specification targeting a surface frame at UV coordinates.</summary>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Surface(Surface surface, double u, double v) => new() {
            Target = surface,
            TargetSurface = surface,
            SurfaceUV = (u, v),
        };
    }

    /// <summary>Aligns geometry to target plane using PlaneToPlane transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(T geometry, Plane targetPlane, IGeometryContext context) where T : GeometryBase =>
        targetPlane.IsValid switch {
            false => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationPlane),
            true => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    OrientCore.ExtractSourcePlane(item, context)
                        .Map(sourcePlane => Transform.PlaneToPlane(sourcePlane, targetPlane))
                        .Bind(xform => ApplyTransform(item, xform, context))),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                        ? mode
                        : V.Standard,
                }).Map(results => results[0]),
        };

    /// <summary>Positions geometry using canonical world plane alignment or centroid positioning.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ComputeCanonicalTransform(item, mode)
                    .Bind(xform => ApplyTransform(item, xform, context))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V validationMode)
                    ? validationMode
                    : V.Standard,
            }).Map(results => results[0]);

    /// <summary>Translates geometry center to target point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d target, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassCentroid)
                    .Map(centroid => Transform.Translation(target - centroid))
                    .Bind(xform => ApplyTransform(item, xform, context))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Map(results => results[0]);

    /// <summary>Rotates geometry to align source axis with target direction.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d targetDirection, Vector3d? sourceAxis, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassProperties: false)
                    .Bind(center => OrientCore.ComputeVectorAlignment(
                        sourceAxis ?? Vector3d.ZAxis,
                        targetDirection,
                        center,
                        context))
                    .Bind(xform => ApplyTransform(item, xform, context))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Map(results => results[0]);

    /// <summary>Mirrors geometry across specified plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(T geometry, Plane mirrorPlane, IGeometryContext context) where T : GeometryBase =>
        mirrorPlane.IsValid switch {
            false => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationPlane),
            true => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    ApplyTransform(item, Transform.Mirror(mirrorPlane), context)),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                        ? mode
                        : V.Standard,
                }).Map(results => results[0]),
        };

    /// <summary>Reverses geometry direction for curves or flips normals for surfaces/meshes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item => {
                T duplicate = (T)item.Duplicate();
                return OrientCore.FlipGeometryDirection(duplicate)
                    .Map(flipped => (IReadOnlyList<T>)[flipped,]);
            }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Map(results => results[0]);

    /// <summary>Applies polymorphic orientation specification to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        spec.Target switch {
            Plane p => ToPlane(geometry, p, context),
            Point3d pt => ToPoint(geometry, pt, useMassCentroid: false, context),
            Vector3d v => ToVector(geometry, v, sourceAxis: null, context),
            Curve c when spec.TargetCurve is not null => c.FrameAt(spec.CurveParameter, out Plane frame) switch {
                true when frame.IsValid => ToPlane(geometry, frame, context),
                _ => ResultFactory.Create<T>(error: E.Geometry.InvalidCurveParameter),
            },
            Surface s when spec.TargetSurface is not null => s.FrameAt(spec.SurfaceUV.u, spec.SurfaceUV.v, out Plane frame) switch {
                true when frame.IsValid => ToPlane(geometry, frame, context),
                _ => ResultFactory.Create<T>(error: E.Geometry.InvalidSurfaceUV),
            },
            _ => ResultFactory.Create<T>(
                error: E.Geometry.UnsupportedOrientationType.WithContext(spec.Target.GetType().Name)),
        };

    /// <summary>Applies transformation to geometry and returns result as single-item list.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ApplyTransform<T>(T geometry, Transform xform, IGeometryContext context) where T : GeometryBase {
        T duplicate = (T)geometry.Duplicate();
        return (xform.IsValid && Math.Abs(xform.Determinant) > context.AbsoluteTolerance) switch {
            false => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
            true => duplicate.Transform(xform) switch {
                true => ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                false => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
            },
        };
    }
}
