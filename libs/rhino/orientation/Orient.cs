using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation engine providing canonical positioning, alignment, mirroring, and directional corrections.</summary>
public static class Orient {
    /// <summary>Aligns geometry to target plane using PlaneToPlane transform with source frame extraction.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to align.</param>
    /// <param name="targetPlane">Target plane for alignment.</param>
    /// <param name="context">Geometry context providing validation and tolerance settings.</param>
    /// <returns>Result containing aligned geometry or errors if alignment fails.</returns>
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
                    .Bind(xform => ValidateAndApplyTransform(item, xform, errorContext: "Invalid transform"))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            })
        .Map(results => results[0]);

    /// <summary>Positions geometry using canonical world plane alignment or centroid positioning.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to position.</param>
    /// <param name="mode">Canonical positioning mode (WorldXY/YZ/XZ, AreaCentroid, VolumeCentroid).</param>
    /// <param name="context">Geometry context providing validation and tolerance settings.</param>
    /// <returns>Result containing positioned geometry or errors if positioning fails.</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ComputeCanonicalTransform(item, mode, context)
                    .Bind(xform => ValidateAndApplyTransform(item, xform, errorContext: "Invalid canonical transform"))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = mode.Mode switch {
                    1 or 2 or 3 => V.Standard | V.BoundingBox,
                    4 => V.Standard | V.MassProperties,
                    5 => V.Standard | V.MassProperties,
                    _ => V.Standard,
                },
            })
        .Map(results => results[0]);

    /// <summary>Aligns geometry center to target point using translation transform.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to translate.</param>
    /// <param name="target">Target point for centroid alignment.</param>
    /// <param name="useMassCentroid">When true, uses mass properties centroid; when false, uses bounding box center.</param>
    /// <param name="context">Geometry context providing validation and tolerance settings.</param>
    /// <returns>Result containing translated geometry or errors if translation fails.</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d target, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassCentroid, context)
                    .Map(centroid => Transform.Translation(target - centroid))
                    .Bind(xform => ValidateAndApplyTransform(item, xform, errorContext: "Invalid translation transform"))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = useMassCentroid ? V.Standard | V.MassProperties : V.Standard | V.BoundingBox,
            })
        .Map(results => results[0]);

    /// <summary>Rotates geometry to align source axis with target direction vector using geometry's local coordinate frame.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to rotate.</param>
    /// <param name="targetDirection">Target direction vector to align toward.</param>
    /// <param name="sourceAxis">Optional source axis to rotate from; when null, extracts principal axis from geometry's local frame.</param>
    /// <param name="context">Geometry context providing validation and tolerance settings.</param>
    /// <returns>Result containing rotated geometry or errors if rotation fails (zero-length vectors, antiparallel alignment, or invalid geometry).</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d targetDirection, Vector3d? sourceAxis, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (sourceAxis.HasValue switch {
                    true => ResultFactory.Create(value: sourceAxis.Value),
                    false => OrientCore.ExtractPrincipalAxis(item, context),
                })
                .Bind(source => OrientCore.ExtractCentroid(item, useMassCentroid: false, context)
                    .Bind(center => OrientCore.ComputeVectorAlignment(source, targetDirection, center, context)))
                .Bind(xform => ValidateAndApplyTransform(item, xform, errorContext: "Invalid rotation transform"))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            })
        .Map(results => results[0]);

    /// <summary>Mirrors geometry across plane using reflection transform.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to mirror.</param>
    /// <param name="mirrorPlane">Mirror plane for reflection operation.</param>
    /// <param name="context">Geometry context providing validation settings.</param>
    /// <returns>Result containing mirrored geometry or errors if mirroring fails.</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(T geometry, Plane mirrorPlane, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                mirrorPlane.IsValid switch {
                    false => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane.WithContext("Mirror plane invalid")),
                    _ => ValidateAndApplyTransform(item, Transform.Mirror(mirrorPlane), errorContext: "Mirror transform application failed"),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            })
        .Map(results => results[0]);

    /// <summary>Flips geometry direction using type-specific in-place mutation (Curve.Reverse, Brep.Flip, Mesh.Flip).</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to flip.</param>
    /// <param name="context">Geometry context providing validation settings.</param>
    /// <returns>Result containing flipped geometry or errors if flip operation fails or type unsupported.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (item.Duplicate() as T) switch {
                    T duplicated => OrientCore.FlipGeometryDirection(duplicated, context)
                        .Map(flipped => (IReadOnlyList<T>)[flipped,]),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed.WithContext("Duplication failed")),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            })
        .Map(results => results[0]);

    /// <summary>Applies polymorphic orientation specification to geometry with type-based dispatch.</summary>
    /// <typeparam name="T">Geometry type constrained to GeometryBase.</typeparam>
    /// <param name="geometry">Geometry instance to orient.</param>
    /// <param name="spec">Orientation specification discriminating target type (Plane, Point, Vector, Curve, Surface).</param>
    /// <param name="context">Geometry context providing validation and tolerance settings.</param>
    /// <returns>Result containing oriented geometry or errors if orientation fails.</returns>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        (spec.Target, spec.TargetPlane, spec.TargetPoint, spec.TargetVector, spec.TargetCurve, spec.TargetSurface) switch {
            (_, Plane plane, null, null, null, null) when plane.IsValid =>
                ToPlane(geometry, plane, context),
            (_, null, Point3d point, null, null, null) =>
                ToPoint(geometry, point, useMassCentroid: true, context),
            (_, null, null, Vector3d vector, null, null) when vector.Length > OrientConfig.ToleranceDefaults.MinVectorLength =>
                ToVector(geometry, vector, sourceAxis: null, context),
            (_, null, null, null, Curve curve, null) when curve.FrameAt(spec.CurveParameter, out Plane curveFrame) && curveFrame.IsValid =>
                ToPlane(geometry, curveFrame, context),
            (_, null, null, null, Curve _, null) =>
                ResultFactory.Create<T>(error: E.Geometry.InvalidCurveParameter.WithContext($"t={spec.CurveParameter}")),
            (_, null, null, null, null, Surface surface) when surface.FrameAt(spec.SurfaceUV.u, spec.SurfaceUV.v, out Plane surfaceFrame) && surfaceFrame.IsValid =>
                ToPlane(geometry, surfaceFrame, context),
            (_, null, null, null, null, Surface _) =>
                ResultFactory.Create<T>(error: E.Geometry.InvalidSurfaceUV.WithContext($"u={spec.SurfaceUV.u}, v={spec.SurfaceUV.v}")),
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext($"Target: {spec.Target.GetType().Name}")),
        };

    /// <summary>Validates transform and applies to duplicated geometry with determinant checking.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ValidateAndApplyTransform<T>(T geometry, Transform xform, string errorContext) where T : GeometryBase =>
        (xform.IsValid, Math.Abs(xform.Determinant) > OrientConfig.ToleranceDefaults.MinDeterminant) switch {
            (false, _) or (_, false) => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed.WithContext(errorContext)),
            _ => (geometry.Duplicate(), xform) switch {
                (T duplicated, Transform transform) when duplicated.Transform(transform) =>
                    ResultFactory.Create(value: (IReadOnlyList<T>)[duplicated,]),
                _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed.WithContext($"{errorContext} - application failed")),
            },
        };

    /// <summary>Semantic marker for canonical positioning operations with world plane alignment modes.</summary>
    public readonly struct Canonical(byte mode) {
        /// <summary>Internal mode byte for canonical operation dispatch.</summary>
        internal readonly byte Mode = mode;

        /// <summary>Align bounding box to World XY plane (Z-up orientation).</summary>
        public static readonly Canonical WorldXY = new(1);

        /// <summary>Align bounding box to World YZ plane (X-up orientation).</summary>
        public static readonly Canonical WorldYZ = new(2);

        /// <summary>Align bounding box to World XZ plane (Y-up orientation).</summary>
        public static readonly Canonical WorldXZ = new(3);

        /// <summary>Translate geometry to origin using area centroid for closed geometry.</summary>
        public static readonly Canonical AreaCentroid = new(4);

        /// <summary>Translate geometry to origin using volume centroid for solid geometry.</summary>
        public static readonly Canonical VolumeCentroid = new(5);
    }

    /// <summary>Polymorphic orientation specification for alignment target discrimination with type-based dispatch.</summary>
    public readonly record struct OrientSpec {
        /// <summary>Target object for orientation operation (runtime type discrimination).</summary>
        public required object Target { get; init; }

        /// <summary>Target plane for PlaneToPlane alignment.</summary>
        public Plane? TargetPlane { get; init; }

        /// <summary>Target point for translation alignment.</summary>
        public Point3d? TargetPoint { get; init; }

        /// <summary>Target direction vector for rotation alignment.</summary>
        public Vector3d? TargetVector { get; init; }

        /// <summary>Target curve for frame extraction at parameter.</summary>
        public Curve? TargetCurve { get; init; }

        /// <summary>Target surface for frame extraction at UV coordinates.</summary>
        public Surface? TargetSurface { get; init; }

        /// <summary>Curve parameter for frame extraction (normalized [0,1] or domain parameter).</summary>
        public double CurveParameter { get; init; }

        /// <summary>Surface UV coordinates for frame extraction.</summary>
        public (double u, double v) SurfaceUV { get; init; }

        /// <summary>Creates plane-based orientation specification for PlaneToPlane alignment.</summary>
        /// <param name="plane">Target plane for alignment operation.</param>
        /// <returns>OrientSpec configured for plane-based orientation.</returns>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Plane(Plane plane) =>
            new() {
                Target = plane,
                TargetPlane = plane,
            };

        /// <summary>Creates point-based translation specification for centroid alignment.</summary>
        /// <param name="point">Target point for translation operation.</param>
        /// <returns>OrientSpec configured for point-based translation.</returns>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Point(Point3d point) =>
            new() {
                Target = point,
                TargetPoint = point,
            };

        /// <summary>Creates vector-based rotation specification for directional alignment.</summary>
        /// <param name="vector">Target direction vector for rotation operation.</param>
        /// <returns>OrientSpec configured for vector-based rotation.</returns>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Vector(Vector3d vector) =>
            new() {
                Target = vector,
                TargetVector = vector,
            };

        /// <summary>Creates curve-based frame extraction specification for alignment to curve frame at parameter.</summary>
        /// <param name="curve">Target curve for frame extraction.</param>
        /// <param name="t">Curve parameter (normalized [0,1] or domain value) for frame extraction point.</param>
        /// <returns>OrientSpec configured for curve-based frame orientation.</returns>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Curve(Curve curve, double t) =>
            new() {
                Target = curve,
                TargetCurve = curve,
                CurveParameter = t,
            };

        /// <summary>Creates surface-based frame extraction specification for alignment to surface frame at UV coordinates.</summary>
        /// <param name="surface">Target surface for frame extraction.</param>
        /// <param name="u">Surface U parameter for frame extraction point.</param>
        /// <param name="v">Surface V parameter for frame extraction point.</param>
        /// <returns>OrientSpec configured for surface-based frame orientation.</returns>
        [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OrientSpec Surface(Surface surface, double u, double v) =>
            new() {
                Target = surface,
                TargetSurface = surface,
                SurfaceUV = (u, v),
            };
    }
}
