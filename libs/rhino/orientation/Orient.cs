using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Polymorphic geometry orientation and canonical alignment.</summary>
public static class Orient {
    /// <summary>Canonical orientation mode for world plane alignments.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Canonical(byte mode) {
        internal readonly byte Mode = mode;
        /// <summary>Align to world XY at bounding box center.</summary>
        public static readonly Canonical WorldXY = new(1);
        /// <summary>Align to world YZ at bounding box center.</summary>
        public static readonly Canonical WorldYZ = new(2);
        /// <summary>Align to world XZ at bounding box center.</summary>
        public static readonly Canonical WorldXZ = new(3);
        /// <summary>Translate area centroid to origin.</summary>
        public static readonly Canonical AreaCentroid = new(4);
        /// <summary>Translate volume centroid to origin.</summary>
        public static readonly Canonical VolumeCentroid = new(5);
    }

    /// <summary>Orientation target: plane, point, vector, or geometry frame.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct OrientSpec {
        /// <summary>Target plane for plane-to-plane transformation.</summary>
        public Plane? TargetPlane { get; init; }
        /// <summary>Target point for translation.</summary>
        public Point3d? TargetPoint { get; init; }
        /// <summary>Target vector for rotation.</summary>
        public Vector3d? TargetVector { get; init; }
        /// <summary>Target curve for frame-based transformation.</summary>
        public Curve? TargetCurve { get; init; }
        /// <summary>Target surface for frame-based transformation.</summary>
        public Surface? TargetSurface { get; init; }
        /// <summary>Curve parameter for frame evaluation.</summary>
        public double CurveParameter { get; init; }
        /// <summary>Surface UV for frame evaluation.</summary>
        public (double u, double v) SurfaceUV { get; init; }

        /// <summary>Plane-to-plane orientation.</summary>
        public static OrientSpec Plane(Plane p) => new() { TargetPlane = p };
        /// <summary>Point-to-point orientation.</summary>
        public static OrientSpec Point(Point3d p) => new() { TargetPoint = p };
        /// <summary>Vector-to-vector orientation.</summary>
        public static OrientSpec Vector(Vector3d v) => new() { TargetVector = v };
        /// <summary>Curve frame orientation.</summary>
        public static OrientSpec Curve(Curve c, double t) => new() { TargetCurve = c, CurveParameter = t };
        /// <summary>Surface frame orientation.</summary>
        public static OrientSpec Surface(Surface s, double u, double v) => new() { TargetSurface = s, SurfaceUV = (u, v) };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(T geometry, Plane target, IGeometryContext context) where T : GeometryBase {
        Result<Plane> validatedTarget = ResultFactory.Create(value: target)
            .Ensure(plane => plane.IsValid, error: E.Geometry.InvalidOrientationPlane);

        V configuredMode;
        V baseMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out configuredMode) ? configuredMode : V.Standard;

        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                validatedTarget.Bind(targetPlane =>
                    OrientCore.ExtractPlane(item, context)
                        .Bind(source => OrientCore.ApplyTransform(item, Transform.PlaneToPlane(source, targetPlane))))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = baseMode,
            }).Map(result => result[0]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase {
        V configuredMode;
        V baseMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out configuredMode) ? configuredMode : V.Standard;

        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item => ((Func<Result<IReadOnlyList<T>>>)(() => {
                Type runtimeType = item.GetType();
                V runtimeConfigured;
                V runtimeMode = OrientConfig.ValidationModes.TryGetValue(runtimeType, out runtimeConfigured) ? runtimeConfigured : V.Standard;
                Result<BoundingBox> bounding = ResultFactory.Create(value: item)
                    .Validate(args: [context, runtimeMode | V.BoundingBox,])
                    .Map(valid => valid.GetBoundingBox(accurate: true))
                    .Bind(box => box.IsValid
                        ? ResultFactory.Create(value: box)
                        : ResultFactory.Create<BoundingBox>(error: E.Validation.BoundingBoxInvalid));

                Result<Transform> transform = mode.Mode switch {
                    1 => bounding.Bind(box => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY))),
                    2 => bounding.Bind(box => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ))),
                    3 => bounding.Bind(box => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis), new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis)))),
                    4 => bounding.Bind(box => ResultFactory.Create(value: Transform.Translation(Point3d.Origin - box.Center))),
                    5 => OrientCore.ExtractCentroid(item, useMassProperties: true, context).Map(centroid => Transform.Translation(Point3d.Origin - centroid)),
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
                };

                return transform.Bind(xform => OrientCore.ApplyTransform(item, xform));
            }))()),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = baseMode | (mode.Mode is 5 ? V.MassProperties : V.BoundingBox),
            }).Map(result => result[0]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPoint<T>(T geometry, Point3d target, bool useMass, IGeometryContext context) where T : GeometryBase {
        V configuredMode;
        V baseMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out configuredMode) ? configuredMode : V.Standard;

        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractCentroid(item, useMassProperties: useMass, context)
                    .Map(centroid => Transform.Translation(target - centroid))
                    .Bind(transform => OrientCore.ApplyTransform(item, transform))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = baseMode | (useMass ? V.MassProperties : V.BoundingBox),
            }).Map(result => result[0]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d target, Vector3d? source, Point3d? anchor, IGeometryContext context) where T : GeometryBase {
        Result<Vector3d> resolvedSource = ResultFactory.Create(value: source ?? Vector3d.ZAxis)
            .Ensure(vector => vector.IsValid, error: E.Geometry.InvalidOrientationVectors)
            .Ensure(vector => vector.Length > OrientConfig.MinVectorLength, error: E.Geometry.InvalidOrientationVectors)
            .Bind(vector => {
                Vector3d normalized = vector;
                return normalized.Unitize()
                    ? ResultFactory.Create(value: normalized)
                    : ResultFactory.Create<Vector3d>(error: E.Geometry.InvalidOrientationVectors);
            });

        Result<Vector3d> resolvedTarget = ResultFactory.Create(value: target)
            .Ensure(vector => vector.IsValid, error: E.Geometry.InvalidOrientationVectors)
            .Ensure(vector => vector.Length > OrientConfig.MinVectorLength, error: E.Geometry.InvalidOrientationVectors)
            .Bind(vector => {
                Vector3d normalized = vector;
                return normalized.Unitize()
                    ? ResultFactory.Create(value: normalized)
                    : ResultFactory.Create<Vector3d>(error: E.Geometry.InvalidOrientationVectors);
            });

        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item => ((Func<Result<IReadOnlyList<T>>>)(() => {
                Type runtimeType = item.GetType();
                V runtimeConfigured;
                V runtimeMode = OrientConfig.ValidationModes.TryGetValue(runtimeType, out runtimeConfigured) ? runtimeConfigured : V.Standard;
                Result<Point3d> anchorPoint = anchor.HasValue
                    ? ResultFactory.Create(value: anchor.Value)
                    : ResultFactory.Create(value: item)
                        .Validate(args: [context, runtimeMode | V.BoundingBox,])
                        .Map(valid => valid.GetBoundingBox(accurate: true))
                        .Bind(box => box.IsValid
                            ? ResultFactory.Create(value: box.Center)
                            : ResultFactory.Create<Point3d>(error: E.Validation.BoundingBoxInvalid));

                Result<Transform> transform = resolvedSource.Bind(sourceVector =>
                    resolvedTarget.Bind(targetVector =>
                        anchorPoint.Bind(anchorValue => Vector3d.CrossProduct(sourceVector, targetVector).Length < OrientConfig.ParallelThreshold
                            ? Math.Abs((sourceVector * targetVector) + 1.0) < OrientConfig.ParallelThreshold
                                ? ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment)
                                : ResultFactory.Create(value: Transform.Identity)
                            : ResultFactory.Create(value: Transform.Rotation(sourceVector, targetVector, anchorValue)))));

                return transform.Bind(xform => OrientCore.ApplyTransform(item, xform));
            }))()),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V configuredMode) ? configuredMode : V.Standard,
            }).Map(result => result[0]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToBestFit<T>(T geometry, IGeometryContext context) where T : GeometryBase {
        V configuredMode;
        V baseMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out configuredMode) ? configuredMode : V.Standard;

        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractBestFitPlane(item, context)
                    .Bind(plane => OrientCore.ApplyTransform(item, Transform.PlaneToPlane(plane, Plane.WorldXY)))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = baseMode | V.Degeneracy,
            }).Map(result => result[0]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(T geometry, Plane plane, IGeometryContext context) where T : GeometryBase {
        Result<Plane> validatedPlane = ResultFactory.Create(value: plane)
            .Ensure(p => p.IsValid, error: E.Geometry.InvalidOrientationPlane);

        V configuredMode;
        V baseMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out configuredMode) ? configuredMode : V.Standard;

        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                validatedPlane.Bind(targetPlane => OrientCore.ApplyTransform(item, Transform.Mirror(targetPlane)))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = baseMode,
            }).Map(result => result[0]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase {
        V configuredMode;
        V baseMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out configuredMode) ? configuredMode : V.Standard;

        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.FlipGeometry(item)
                    .Map(result => (IReadOnlyList<T>)[result,])),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = baseMode,
            }).Map(result => result[0]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        (spec.TargetPlane, spec.TargetPoint, spec.TargetVector, spec.TargetCurve, spec.TargetSurface) switch {
            (null, null, null, null, null) => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode),
            (Plane targetPlane, null, null, null, null) => ToPlane(geometry, targetPlane, context),
            (null, Point3d targetPoint, null, null, null) => ToPoint(geometry, targetPoint, useMass: false, context),
            (null, null, Vector3d targetVector, null, null) => ToVector(geometry, targetVector, source: null, anchor: null, context),
            (null, null, null, Curve curve, null) => ResultFactory.Create(value: curve)
                .Validate(args: [context, OrientConfig.ValidationModes.TryGetValue(curve.GetType(), out V curveMode) ? curveMode : V.Standard,])
                .Bind(validCurve => validCurve.FrameAt(spec.CurveParameter, out Plane frame) && frame.IsValid
                    ? ToPlane(geometry, frame, context)
                    : ResultFactory.Create<T>(error: E.Geometry.InvalidCurveParameter)),
            (null, null, null, null, Surface surface) => ResultFactory.Create(value: surface)
                .Validate(args: [context, OrientConfig.ValidationModes.TryGetValue(surface.GetType(), out V surfaceMode) ? surfaceMode : V.Standard,])
                .Bind(validSurface => validSurface.FrameAt(spec.SurfaceUV.u, spec.SurfaceUV.v, out Plane frame) && frame.IsValid
                    ? ToPlane(geometry, frame, context)
                    : ResultFactory.Create<T>(error: E.Geometry.InvalidSurfaceUV)),
            _ => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Transform OptimalTransform, double Score, byte[] CriteriaMet)> OptimizeOrientation(
        Brep brep,
        byte criteria,
        IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: brep,
            operation: (Func<Brep, Result<IReadOnlyList<(Transform, double, byte[])>>>)(item =>
                OrientCompute.OptimizeOrientation(item, criteria, context.AbsoluteTolerance, context)
                    .Map(r => (IReadOnlyList<(Transform, double, byte[])>)[r,])),
            config: new OperationConfig<Brep, (Transform, double, byte[])> {
                Context = context,
                ValidationMode = V.Standard | V.Topology,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Transform RelativeTransform, double Twist, double Tilt, byte SymmetryType, byte Relationship)> ComputeRelativeOrientation(
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        OrientCompute.ComputeRelative(geometryA, geometryB, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(byte PatternType, Transform[] IdealTransforms, int[] Anomalies, double Deviation)> DetectAndAlign(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        OrientCompute.DetectPattern(geometries, context);
}
