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
    /// <summary>Aligns geometry to target plane using PlaneToPlane transformation.</summary>
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
                    .Bind(xform => (T)item.Duplicate() switch {
                        T duplicate when duplicate.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

    /// <summary>Positions geometry using canonical world plane alignment or centroid positioning.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ComputeCanonicalTransform(item, mode, context)
                    .Bind(xform => (T)item.Duplicate() switch {
                        T duplicate when duplicate.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

    /// <summary>Aligns geometry center to target point using translation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d targetPoint, bool useMassCentroid, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassCentroid, context)
                    .Bind(centroid => targetPoint.IsValid switch {
                        false => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane.WithContext("Invalid target point")),
                        _ => ResultFactory.Create(value: Transform.Translation(targetPoint - centroid)),
                    })
                    .Bind(xform => (T)item.Duplicate() switch {
                        T duplicate when duplicate.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

    /// <summary>Rotates geometry to align source axis with target direction.</summary>
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
                    .Bind(xform => (T)item.Duplicate() switch {
                        T duplicate when duplicate.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[duplicate,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

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
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
            }).Bind(results => results.Count switch {
                1 => ResultFactory.Create(value: results[0]),
                _ => ResultFactory.Create<T>(error: E.Geometry.TransformFailed),
            });

    /// <summary>Reverses curve direction or flips surface/brep/mesh normals.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.FlipGeometryDirection((T)item.Duplicate(), context)
                    .Map(flipped => (IReadOnlyList<T>)[flipped,])),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V mode)
                    ? mode
                    : V.Standard,
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

/// <summary>Semantic marker for canonical positioning modes.</summary>
public readonly struct Canonical(byte mode) {
    internal readonly byte Mode = mode;

    public static readonly Canonical WorldXY = new(1);
    public static readonly Canonical WorldYZ = new(2);
    public static readonly Canonical WorldXZ = new(3);
    public static readonly Canonical AreaCentroid = new(4);
    public static readonly Canonical VolumeCentroid = new(5);
}

/// <summary>Polymorphic specification for orientation target discrimination.</summary>
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
