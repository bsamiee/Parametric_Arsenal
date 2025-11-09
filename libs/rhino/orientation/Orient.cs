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

    /// <summary>Polymorphic orientation target specification using discriminated union pattern.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct OrientSpec {
        internal readonly object? Target;
        internal readonly (double, double) Parameters;

        private OrientSpec(object? target, (double, double) parameters) {
            this.Target = target;
            this.Parameters = parameters;
        }

        /// <summary>Creates plane-to-plane orientation specification.</summary>
        public static OrientSpec Plane(Plane p) => new(p, default);
        /// <summary>Creates point-to-point orientation specification.</summary>
        public static OrientSpec Point(Point3d p) => new(p, default);
        /// <summary>Creates vector-to-vector orientation specification.</summary>
        public static OrientSpec Vector(Vector3d v) => new(v, default);
        /// <summary>Creates curve frame orientation specification.</summary>
        public static OrientSpec Curve(Curve c, double t) => new(c, (t, 0));
        /// <summary>Creates surface frame orientation specification.</summary>
        public static OrientSpec Surface(Surface s, double u, double v) => new(s, (u, v));

        internal Result<Plane> ToPlane() =>
            this.Target switch {
                Plane p => ResultFactory.Create(value: p),
                global::Rhino.Geometry.Curve c when c.FrameAt(this.Parameters.Item1, out Plane f) && f.IsValid => ResultFactory.Create(value: f),
                global::Rhino.Geometry.Curve => ResultFactory.Create<Plane>(error: E.Geometry.InvalidCurveParameter),
                global::Rhino.Geometry.Surface s when s.FrameAt(this.Parameters.Item1, this.Parameters.Item2, out Plane f) && f.IsValid => ResultFactory.Create(value: f),
                global::Rhino.Geometry.Surface => ResultFactory.Create<Plane>(error: E.Geometry.InvalidSurfaceUV),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.InvalidOrientationMode),
            };
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
                        .OrderBy(kv => kv.Key, Comparer<Type>.Create((a, b) => b.IsAssignableFrom(a) ? -1 : a.IsAssignableFrom(b) ? 1 : 0))
                        .Select(kv => kv.Value(item))
                        .FirstOrDefault(ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name))))
                .Bind(src => target.IsValid
                    ? item.Duplicate() switch {
                        T dup when dup.Transform(Transform.PlaneToPlane(src, target)) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    }
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
                ((mode.Mode, item.GetBoundingBox(accurate: true)) switch {
                    (1, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(b.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY)),
                    (2, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(b.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ)),
                    (3, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(b.Center, Vector3d.XAxis, Vector3d.ZAxis), new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis))),
                    (4, BoundingBox b) when b.IsValid => ResultFactory.Create(value: Transform.Translation(Point3d.Origin - b.Center)),
                    (5, _) => ((item, item.GetBoundingBox(accurate: true)) switch {
                        (Brep b, _) when b.IsSolid => OrientCore.ComputeMass(() => VolumeMassProperties.Compute(b), vmp => vmp.Centroid),
                        (Brep b, _) when b.SolidOrientation != BrepSolidOrientation.None => OrientCore.ComputeMass(() => AreaMassProperties.Compute(b), amp => amp.Centroid),
                        (Extrusion e, _) when e.IsSolid => OrientCore.ComputeMass(() => VolumeMassProperties.Compute(e), vmp => vmp.Centroid),
                        (Extrusion e, _) when e.IsClosed(0) && e.IsClosed(1) => OrientCore.ComputeMass(() => AreaMassProperties.Compute(e), amp => amp.Centroid),
                        (Mesh m, _) when m.IsClosed => OrientCore.ComputeMass(() => VolumeMassProperties.Compute(m), vmp => vmp.Centroid),
                        (Mesh m, _) => OrientCore.ComputeMass(() => AreaMassProperties.Compute(m), amp => amp.Centroid),
                        (Curve c, _) => OrientCore.ComputeMass(() => AreaMassProperties.Compute(c), amp => amp.Centroid),
                        (GeometryBase, BoundingBox b) when b.IsValid => ResultFactory.Create(value: b.Center),
                        _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                    }).Map(c => Transform.Translation(Point3d.Origin - c)),
                    (_, BoundingBox b) when !b.IsValid => ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
                    _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
                }).Bind(xform => item.Duplicate() switch {
                    T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                })),
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
                ((item, useMass, item.GetBoundingBox(accurate: true)) switch {
                    (Brep b, true, _) when b.IsSolid => OrientCore.ComputeMass(() => VolumeMassProperties.Compute(b), vmp => vmp.Centroid),
                    (Brep b, true, _) when b.SolidOrientation != BrepSolidOrientation.None => OrientCore.ComputeMass(() => AreaMassProperties.Compute(b), amp => amp.Centroid),
                    (Extrusion e, true, _) when e.IsSolid => OrientCore.ComputeMass(() => VolumeMassProperties.Compute(e), vmp => vmp.Centroid),
                    (Extrusion e, true, _) when e.IsClosed(0) && e.IsClosed(1) => OrientCore.ComputeMass(() => AreaMassProperties.Compute(e), amp => amp.Centroid),
                    (Mesh m, true, _) when m.IsClosed => OrientCore.ComputeMass(() => VolumeMassProperties.Compute(m), vmp => vmp.Centroid),
                    (Mesh m, true, _) => OrientCore.ComputeMass(() => AreaMassProperties.Compute(m), amp => amp.Centroid),
                    (Curve c, true, _) => OrientCore.ComputeMass(() => AreaMassProperties.Compute(c), amp => amp.Centroid),
                    (GeometryBase, false, BoundingBox b) when b.IsValid => ResultFactory.Create(value: b.Center),
                    _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
                }).Map(c => Transform.Translation(target - c))
                .Bind(xform => item.Duplicate() switch {
                    T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                })),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = useMass ? V.Standard | V.MassProperties : V.Standard | V.BoundingBox,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToVector<T>(T geometry, Vector3d target, Vector3d? source, Point3d? anchor, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (item.GetBoundingBox(accurate: true), source ?? Vector3d.ZAxis, target) switch {
                    (BoundingBox b, Vector3d s, Vector3d t) when b.IsValid && s.Length > OrientConfig.MinVectorLength && t.Length > OrientConfig.MinVectorLength =>
                        (new Vector3d(s), new Vector3d(t), anchor ?? b.Center) switch {
                            (Vector3d su, Vector3d tu, Point3d pt) when su.Unitize() && tu.Unitize() =>
                                (Vector3d.CrossProduct(su, tu).Length < OrientConfig.ParallelThreshold
                                    ? Math.Abs((su * tu) + 1.0) < OrientConfig.ParallelThreshold
                                        ? ResultFactory.Create<Transform>(error: E.Geometry.ParallelVectorAlignment)
                                        : ResultFactory.Create(value: Transform.Identity)
                                    : ResultFactory.Create(value: Transform.Rotation(su, tu, pt)))
                                .Bind(xform => item.Duplicate() switch {
                                    T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                                }),
                            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationVectors),
                        },
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationVectors),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ToBestFit<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (item switch {
                    PointCloud pc when pc.Count > 0 && Plane.FitPlaneToPoints(pc.GetPoints(), out Plane plane) == PlaneFitResult.Success => ResultFactory.Create(value: plane),
                    Mesh m when m.Vertices.Count > 0 && Plane.FitPlaneToPoints(m.Vertices.ToPoint3dArray(), out Plane plane) == PlaneFitResult.Success => ResultFactory.Create(value: plane),
                    PointCloud or Mesh => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
                    _ => ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name)),
                }).Bind(plane => item.Duplicate() switch {
                    T dup when dup.Transform(Transform.PlaneToPlane(plane, Plane.WorldXY)) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                })),
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
                    ? item.Duplicate() switch {
                        T dup when dup.Transform(Transform.Mirror(plane)) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    }
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
                    Brep b => ((Func<IReadOnlyList<T>>)(() => { b.Flip(); return [(T)(GeometryBase)b,]; }))() switch {
                        IReadOnlyList<T> result => ResultFactory.Create(value: result),
                    },
                    Extrusion e => e.ToBrep() switch {
                        Brep br => ((Func<IReadOnlyList<T>>)(() => { br.Flip(); return [(T)(GeometryBase)br,]; }))() switch {
                            IReadOnlyList<T> result => ResultFactory.Create(value: result),
                        },
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    },
                    Mesh m => ((Func<IReadOnlyList<T>>)(() => { m.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true); return [(T)(GeometryBase)m,]; }))() switch {
                        IReadOnlyList<T> result => ResultFactory.Create(value: result),
                    },
                    null => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name)),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.ValidationModes.TryGetValue(typeof(T), out V m) ? m : V.Standard,
            }).Map(r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(T geometry, OrientSpec spec, IGeometryContext context) where T : GeometryBase =>
        spec.Target switch {
            null => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode),
            Plane p => ToPlane(geometry, p, context),
            Point3d pt => ToPoint(geometry, pt, useMass: false, context),
            Vector3d v => ToVector(geometry, v, source: null, anchor: null, context),
            Curve or Surface => spec.ToPlane().Bind(plane => ToPlane(geometry, plane, context)),
            _ => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode),
        };
}
