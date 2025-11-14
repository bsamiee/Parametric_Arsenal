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

/// <summary>Polymorphic geometry orientation and canonical alignment.</summary>
public static class Orient {
    /// <summary>Canonical orientation modes for world plane alignment operations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Canonical(byte mode) {
        internal readonly byte Mode = mode;
        /// <summary>Align bounding box center to world XY plane.</summary>
        public static readonly Canonical WorldXY = new(1);
        /// <summary>Align bounding box center to world YZ plane.</summary>
        public static readonly Canonical WorldYZ = new(2);
        /// <summary>Align bounding box center to world XZ plane.</summary>
        public static readonly Canonical WorldXZ = new(3);
        /// <summary>Translate area centroid to world origin.</summary>
        public static readonly Canonical AreaCentroid = new(4);
        /// <summary>Translate volume centroid to world origin.</summary>
        public static readonly Canonical VolumeCentroid = new(5);
    }

    /// <summary>Orientation specification for plane, point, vector, or curve/surface frame targets.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct OrientSpec {
        /// <summary>Target plane for plane-to-plane alignment.</summary>
        public Plane? TargetPlane { get; init; }
        /// <summary>Target point for translation operations.</summary>
        public Point3d? TargetPoint { get; init; }
        /// <summary>Target vector for rotation alignment.</summary>
        public Vector3d? TargetVector { get; init; }
        /// <summary>Target curve for frame-based orientation.</summary>
        public Curve? TargetCurve { get; init; }
        /// <summary>Target surface for frame-based orientation.</summary>
        public Surface? TargetSurface { get; init; }
        /// <summary>Curve parameter for frame evaluation at specific location.</summary>
        public double CurveParameter { get; init; }
        /// <summary>Surface UV coordinates for frame evaluation.</summary>
        public (double u, double v) SurfaceUV { get; init; }

        /// <summary>Create plane-to-plane orientation specification.</summary>
        public static OrientSpec Plane(Plane p) => new() { TargetPlane = p };
        /// <summary>Create point-to-point translation specification.</summary>
        public static OrientSpec Point(Point3d p) => new() { TargetPoint = p };
        /// <summary>Create vector-to-vector rotation specification.</summary>
        public static OrientSpec Vector(Vector3d v) => new() { TargetVector = v };
        /// <summary>Create curve frame orientation at parameter t.</summary>
        public static OrientSpec Curve(Curve c, double t) => new() { TargetCurve = c, CurveParameter = t };
        /// <summary>Create surface frame orientation at UV coordinates.</summary>
        public static OrientSpec Surface(Surface s, double u, double v) => new() { TargetSurface = s, SurfaceUV = (u, v) };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToPlane<T>(T geometry, Plane target, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (OrientCore.PlaneExtractors.TryGetValue(item.GetType(), out Func<object, Result<Plane>>? extractor) ? extractor(item)
                    : OrientCore.PlaneExtractors.FirstOrDefault(kv => kv.Key.IsInstanceOfType(item)).Value?.Invoke(item)
                    ?? ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name)))
                .Bind(src => !target.IsValid
                    ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane)
                    : OrientCore.ApplyTransform(item, Transform.PlaneToPlane(src, target)))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.GetValueOrDefault(typeof(T), V.Standard),
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToCanonical<T>(T geometry, Canonical mode, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                ((mode.Mode, item.GetBoundingBox(accurate: true)) switch {
                    (_, BoundingBox box) when !box.IsValid && mode.Mode != 5 => ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
                    (1, BoundingBox box) => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY)),
                    (2, BoundingBox box) => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ)),
                    (3, BoundingBox box) => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis), new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis))),
                    (4, BoundingBox box) => ResultFactory.Create(value: Transform.Translation(Point3d.Origin - box.Center)),
                    (5, _) => OrientCore.ExtractCentroid(item, useMassProperties: true).Map(c => Transform.Translation(Point3d.Origin - c)),
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
                }).Bind(xform => OrientCore.ApplyTransform(item, xform))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = mode.Mode is (>= 1 and <= 4) ? V.Standard | V.BoundingBox : mode.Mode is 5 ? V.Standard | V.MassProperties : V.Standard,
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
    public static Result<T> ToVector<T>(T geometry, Vector3d target, Vector3d? source, Point3d? anchor, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                ((item.GetBoundingBox(accurate: true), source ?? Vector3d.ZAxis, target) switch {
                    (BoundingBox box, Vector3d s, Vector3d t) when box.IsValid && s.Length > RhinoMath.ZeroTolerance && t.Length > RhinoMath.ZeroTolerance =>
                        ((Func<Result<Transform>>)(() => {
                            Vector3d su = new(s);
                            Vector3d tu = new(t);
                            // The outer 'when' clause guarantees Unitize will succeed.
                            _ = su.Unitize();
                            _ = tu.Unitize();
                            Point3d pt = anchor ?? box.Center;

                            return Vector3d.CrossProduct(su, tu).Length < RhinoMath.SqrtEpsilon
                                ? Math.Abs((su * tu) - 1.0) < RhinoMath.SqrtEpsilon
                                    ? ResultFactory.Create(value: Transform.Identity)
                                    : Math.Abs((su * tu) + 1.0) < RhinoMath.SqrtEpsilon
                                        ? ((Func<Result<Transform>>)(() => {
                                            Vector3d axisCandidate = Math.Abs(su * Vector3d.XAxis) < 0.95 ? Vector3d.CrossProduct(su, Vector3d.XAxis) : Vector3d.CrossProduct(su, Vector3d.YAxis);
                                            bool normalized = axisCandidate.Unitize();
                                            return normalized
                                                ? ResultFactory.Create(value: Transform.Rotation(RhinoMath.PI, axisCandidate, pt))
                                                : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors);
                                        }))()
                                        : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors)
                                : ResultFactory.Create(value: Transform.Rotation(su, tu, pt));
                        }))(),
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
                }).Bind(xform => OrientCore.ApplyTransform(item, xform))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToBestFit<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientCore.ExtractBestFitPlane(item)
                    .Bind(plane => OrientCore.ApplyTransform(item, Transform.PlaneToPlane(plane, Plane.WorldXY)))),
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
                ValidationMode = OrientConfig.ValidationModes.GetValueOrDefault(typeof(T), V.Standard),
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
                ValidationMode = OrientConfig.ValidationModes.GetValueOrDefault(typeof(T), V.Standard),
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        (spec.TargetPlane, spec.TargetPoint, spec.TargetVector, spec.TargetCurve, spec.TargetSurface) switch {
            (null, null, null, null, null) => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode),
            (Plane p, null, null, null, null) when p != default => ToPlane(geometry, p, context),
            (null, Point3d pt, null, null, null) when pt != default => ToPoint(geometry, pt, useMass: false, context),
            (null, null, Vector3d v, null, null) when v != default => ToVector(geometry, v, source: null, anchor: null, context),
            (null, null, null, Curve c, null) => c.FrameAt(spec.CurveParameter, out Plane f) && f.IsValid
                ? ToPlane(geometry, f, context)
                : ResultFactory.Create<T>(error: E.Geometry.InvalidCurveParameter),
            (null, null, null, null, Surface s) => s.FrameAt(spec.SurfaceUV.u, spec.SurfaceUV.v, out Plane f) && f.IsValid
                ? ToPlane(geometry, f, context)
                : ResultFactory.Create<T>(error: E.Geometry.InvalidSurfaceUV),
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
