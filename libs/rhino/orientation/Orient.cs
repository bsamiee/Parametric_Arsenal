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
    /// <summary>Unified operation wrapper eliminating boilerplate duplication across all orientation operations.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteOrientation<T>(
        T geometry,
        Func<T, IGeometryContext, Result<Transform>> transformBuilder,
        IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                transformBuilder(item, context)
                    .Bind(xform => (T)item.Duplicate() switch {
                        T duplicate when duplicate.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

    /// <summary>Aligns geometry to target plane using PlaneToPlane transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(T geometry, Plane targetPlane, IGeometryContext context) where T : GeometryBase =>
        ExecuteOrientation(geometry, (item, ctx) =>
            OrientCore.ExtractSourcePlane(item, ctx)
                .Bind(sourcePlane => targetPlane.IsValid switch {
                    false => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane),
                    _ => ResultFactory.Create(value: Transform.PlaneToPlane(sourcePlane, targetPlane)),
                }), context);

    /// <summary>Positions geometry using canonical world plane alignment or centroid positioning.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        ExecuteOrientation(geometry, (item, ctx) => OrientCore.ComputeCanonicalTransform(item, mode, ctx), context);

    /// <summary>Aligns geometry center to target point using translation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d targetPoint, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        ExecuteOrientation(geometry, (item, ctx) =>
            OrientCore.ExtractCentroid(item, useMassCentroid, ctx)
                .Bind(centroid => targetPoint.IsValid switch {
                    false => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane.WithContext("Invalid target point")),
                    _ => ResultFactory.Create(value: Transform.Translation(targetPoint - centroid)),
                }), context);

    /// <summary>Rotates geometry to align source axis with target direction.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d targetDirection, Vector3d? sourceAxis, IGeometryContext context) where T : GeometryBase =>
        ExecuteOrientation(geometry, (item, ctx) =>
            OrientCore.ExtractCentroid(item, useMassCentroid: false, ctx)
                .Bind(center => OrientCore.ComputeVectorAlignment(
                    sourceAxis ?? Vector3d.ZAxis,
                    targetDirection,
                    center,
                    ctx)), context);

    /// <summary>Mirrors geometry across reflection plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(T geometry, Plane mirrorPlane, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                mirrorPlane.IsValid switch {
                    false => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane.WithContext("Invalid mirror plane")),
                    _ => (T)item.Duplicate() switch {
                        T duplicate when duplicate.Transform(Transform.Mirror(mirrorPlane)) =>
                            ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    },
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

    /// <summary>Reverses curve direction or flips surface/brep/mesh normals.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.FlipGeometryDirection((T)item.Duplicate(), context)
                    .Map(flipped => (IReadOnlyList<T>)[flipped,])),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode) ? mode : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

    /// <summary>Applies polymorphic orientation using specification-based dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        spec.Target switch {
            Plane plane => ToPlane(geometry, plane, context),
            Point3d point => ToPoint(geometry, point, useMassCentroid: false, context),
            Vector3d vector => ToVector(geometry, vector, sourceAxis: null, context),
            Curve curve when spec.TargetCurve is not null => curve.FrameAt(spec.CurveParameter, out Plane frame) && frame.IsValid switch {
                true => ToPlane(geometry, frame, context),
                false => ResultFactory.Create<T>(error: E.Geometry.InvalidCurveParameter.WithContext($"Parameter: {spec.CurveParameter}")),
            },
            Surface surface when spec.TargetSurface is not null => surface.FrameAt(spec.SurfaceUV.u, spec.SurfaceUV.v, out Plane frame) && frame.IsValid switch {
                true => ToPlane(geometry, frame, context),
                false => ResultFactory.Create<T>(error: E.Geometry.InvalidSurfaceUV.WithContext($"UV: ({spec.SurfaceUV.u}, {spec.SurfaceUV.v})")),
            },
            _ => ResultFactory.Create<T>(error: E.Geometry.UnsupportedOrientationType.WithContext($"Target type: {spec.Target.GetType().Name}")),
        };
}
