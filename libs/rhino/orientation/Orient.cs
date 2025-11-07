using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation engine for canonical positioning and arbitrary target alignment.</summary>
public static class Orient {
    /// <summary>Aligns geometry to target plane using PlaneToPlane rigid body transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(
        T geometry,
        Plane targetPlane,
        IGeometryContext context) where T : GeometryBase =>
        targetPlane.IsValid switch {
            false => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationPlane),
            true => OrientCore.ExtractSourcePlane(geometry, context)
                .Map(sourcePlane => Transform.PlaneToPlane(from: sourcePlane, to: targetPlane))
                .Bind(xform => ApplyTransform(geometry, xform, context)),
        };

    /// <summary>Positions geometry canonically to world planes or centroid-aligned origins.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(
        T geometry,
        Canonical mode,
        IGeometryContext context) where T : GeometryBase =>
        OrientCore.ComputeCanonicalTransform(geometry, mode.Mode, context)
            .Bind(xform => ApplyTransform(geometry, xform, context));

    /// <summary>Translates geometry center to target point using mass centroid or bounding box center.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(
        T geometry,
        Point3d targetPoint,
        bool useMassCentroid,
        IGeometryContext context) where T : GeometryBase =>
        OrientCore.ExtractCentroid(geometry, useMassCentroid, context)
            .Map(centroid => Transform.Translation(targetPoint - centroid))
            .Bind(xform => ApplyTransform(geometry, xform, context));

    /// <summary>Rotates geometry to align source axis with target direction vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(
        T geometry,
        Vector3d targetDirection,
        Vector3d? sourceAxis,
        IGeometryContext context) where T : GeometryBase =>
        (sourceAxis ?? Vector3d.ZAxis, geometry.GetBoundingBox(accurate: true)) switch {
            (Vector3d src, BoundingBox bbox) when bbox.IsValid =>
                OrientCore.ComputeVectorAlignment(src, targetDirection, bbox.Center, context)
                    .Bind(xform => ApplyTransform(geometry, xform, context)),
            _ => ResultFactory.Create<T>(error: E.Validation.BoundingBoxInvalid),
        };

    /// <summary>Mirrors geometry across plane using reflection transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(
        T geometry,
        Plane mirrorPlane,
        IGeometryContext context) where T : GeometryBase =>
        mirrorPlane.IsValid switch {
            false => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationPlane),
            true => ApplyTransform(geometry, Transform.Mirror(mirrorPlane: mirrorPlane), context),
        };

    /// <summary>Flips geometry direction using type-specific reversal operations.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(
        T geometry,
        IGeometryContext context) where T : GeometryBase => (
            OrientConfig.ValidationModes.TryGetValue(geometry.GetType(), out V validationMode) ? validationMode : V.Standard,
            UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    OrientCore.FlipGeometryDirection(item, context)
                        .Map(flipped => (IReadOnlyList<T>)[flipped,])),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = validationMode,
                })) switch {
            (_, Result<IReadOnlyList<T>> result) => result.Map(results => results[0]),
        };

    /// <summary>Applies orientation using polymorphic OrientSpec target specification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(
        T geometry,
        OrientSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        (spec.TargetPlane, spec.TargetPoint, spec.TargetVector, spec.TargetCurve, spec.TargetSurface) switch {
            (Plane p, null, null, null, null) => ToPlane(geometry, p, context),
            (null, Point3d pt, null, null, null) => ToPoint(geometry, pt, useMassCentroid: false, context),
            (null, null, Vector3d v, null, null) => ToVector(geometry, v, sourceAxis: null, context),
            (null, null, null, Curve c, null) =>
                OrientCore.ExtractTargetPlane(c, spec.SurfaceUV, spec.CurveParameter, context)
                    .Bind(targetPlane => ToPlane(geometry, targetPlane, context)),
            (null, null, null, null, Surface s) =>
                OrientCore.ExtractTargetPlane(s, spec.SurfaceUV, spec.CurveParameter, context)
                    .Bind(targetPlane => ToPlane(geometry, targetPlane, context)),
            _ => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode.WithContext("Ambiguous OrientSpec")),
        };

    /// <summary>Applies transform to geometry with validation and duplicate handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyTransform<T>(T geometry, Transform xform, IGeometryContext context) where T : GeometryBase => (
            OrientConfig.ValidationModes.TryGetValue(geometry.GetType(), out V validationMode) ? validationMode : V.Standard,
            UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item => (xform.IsValid, Math.Abs(xform.Determinant)) switch {
                    (false, _) => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed.WithContext("Invalid transform")),
                    (true, double det) when det < OrientConfig.ToleranceDefaults.MinDeterminant =>
                        ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed.WithContext($"Degenerate transform: det={det}")),
                    (true, _) => item.Duplicate() switch {
                        T duplicate when duplicate.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed.WithContext($"Transform failed on {item.GetType().Name}")),
                    },
                }),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = validationMode,
                })) switch {
            (_, Result<IReadOnlyList<T>> result) => result.Map(results => results[0]),
        };
}

/// <summary>Semantic marker for canonical positioning modes using byte-encoded dispatch.</summary>
public readonly struct Canonical(byte mode) {
    internal readonly byte Mode = mode;

    /// <summary>Aligns bounding box to World XY plane with center at origin.</summary>
    public static readonly Canonical WorldXY = new(1);

    /// <summary>Aligns bounding box to World YZ plane with center at origin.</summary>
    public static readonly Canonical WorldYZ = new(2);

    /// <summary>Aligns bounding box to World XZ plane with center at origin.</summary>
    public static readonly Canonical WorldXZ = new(3);

    /// <summary>Translates geometry centroid to origin using bounding box center for open geometry or area centroid for closed curves/surfaces.</summary>
    public static readonly Canonical BoundingCentroid = new(4);

    /// <summary>Translates mass centroid to origin using volume centroid for solids or area centroid for closed surfaces.</summary>
    public static readonly Canonical MassCentroid = new(5);
}

/// <summary>Polymorphic orientation target specification with discriminated union semantics.</summary>
public readonly record struct OrientSpec {
    /// <summary>Target object for orientation dispatch (Plane, Point3d, Vector3d, Curve, Surface).</summary>
    public required object Target { get; init; }

    /// <summary>Resolved Plane target when Target is Plane.</summary>
    public Plane? TargetPlane { get; init; }

    /// <summary>Resolved Point3d target when Target is Point3d.</summary>
    public Point3d? TargetPoint { get; init; }

    /// <summary>Resolved Vector3d target when Target is Vector3d.</summary>
    public Vector3d? TargetVector { get; init; }

    /// <summary>Resolved Curve target when Target is Curve.</summary>
    public Curve? TargetCurve { get; init; }

    /// <summary>Resolved Surface target when Target is Surface.</summary>
    public Surface? TargetSurface { get; init; }

    /// <summary>Curve parameter for frame extraction when Target is Curve.</summary>
    public double CurveParameter { get; init; }

    /// <summary>Surface UV parameters for frame extraction when Target is Surface.</summary>
    public (double u, double v) SurfaceUV { get; init; }

    /// <summary>Creates OrientSpec targeting Plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OrientSpec Plane(Plane plane) => new() { Target = plane, TargetPlane = plane, };

    /// <summary>Creates OrientSpec targeting Point3d.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OrientSpec Point(Point3d point) => new() { Target = point, TargetPoint = point, };

    /// <summary>Creates OrientSpec targeting Vector3d.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OrientSpec Vector(Vector3d vector) => new() { Target = vector, TargetVector = vector, };

    /// <summary>Creates OrientSpec targeting Curve frame at parameter t.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OrientSpec Curve(Curve curve, double t) => new() { Target = curve, TargetCurve = curve, CurveParameter = t, };

    /// <summary>Creates OrientSpec targeting Surface frame at UV coordinates.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OrientSpec Surface(Surface surface, double u, double v) => new() { Target = surface, TargetSurface = surface, SurfaceUV = (u, v), };
}
