using System.Collections.Frozen;
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

/// <summary>Orientation orchestration layer with metadata dispatch and UnifiedOperation integration.</summary>
[Pure]
internal static class OrientCore {
    /// <summary>Plane extractor dispatch by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, Result<Plane>>> PlaneExtractors =
        new Dictionary<Type, Func<object, Result<Plane>>> {
            [typeof(Curve)] = g => ((Curve)g).FrameAt(((Curve)g).Domain.Mid, out Plane f) && f.IsValid
                ? ResultFactory.Create(value: f)
                : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            [typeof(Surface)] = g => ((Surface)g) switch {
                Surface s when s.FrameAt(s.Domain(0).Mid, s.Domain(1).Mid, out Plane f) && f.IsValid => ResultFactory.Create(value: f),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
            [typeof(Brep)] = g => ((Brep)g) switch {
                Brep b when b.IsSolid => ((Func<Result<Plane>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(b); return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Brep b when b.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Plane>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(b); return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Brep b => ResultFactory.Create(value: new Plane(b.GetBoundingBox(accurate: false).Center, b.Faces.Count > 0 ? b.Faces[0].NormalAt(0.5, 0.5) : Vector3d.ZAxis)),
            },
            [typeof(Extrusion)] = g => ((Extrusion)g) switch {
                Extrusion e when e.IsSolid => ((Func<Result<Plane>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(e); using LineCurve path = e.PathLineCurve(); return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, path.TangentAtStart)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Extrusion e when e.IsClosed(0) && e.IsClosed(1) => ((Func<Result<Plane>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(e); using LineCurve path = e.PathLineCurve(); return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, path.TangentAtStart)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Extrusion e => ((Func<Result<Plane>>)(() => { using LineCurve path = e.PathLineCurve(); return ResultFactory.Create(value: new Plane(e.GetBoundingBox(accurate: false).Center, path.TangentAtStart)); }))(),
            },
            [typeof(Mesh)] = g => ((Mesh)g) switch {
                Mesh m when m.IsClosed => ((Func<Result<Plane>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(m); return vmp is not null ? ResultFactory.Create(value: new Plane(vmp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
                Mesh m => ((Func<Result<Plane>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(m); return amp is not null ? ResultFactory.Create(value: new Plane(amp.Centroid, m.Normals.Count > 0 ? m.Normals[0] : Vector3d.ZAxis)) : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed); }))(),
            },
            [typeof(Point3d)] = g => ResultFactory.Create(value: new Plane((Point3d)g, Vector3d.ZAxis)),
            [typeof(PointCloud)] = g => (PointCloud)g switch {
                PointCloud pc when pc.Count > 0 => ResultFactory.Create(value: new Plane(pc[0].Location, Vector3d.ZAxis)),
                _ => ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            },
        }.ToFrozenDictionary();

    /// <summary>Extract plane from geometry using type-specific dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractPlane(GeometryBase geometry) =>
        PlaneExtractors.TryGetValue(geometry.GetType(), out Func<object, Result<Plane>>? extractor)
            ? extractor(geometry)
            : PlaneExtractors.FirstOrDefault(kv => kv.Key.IsInstanceOfType(geometry)).Value?.Invoke(geometry)
                ?? ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name));

    /// <summary>Centroid extraction via mass properties or bounding box.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d> ExtractCentroid(GeometryBase geometry, bool useMassProperties) =>
        (geometry, useMassProperties) switch {
            (Brep brep, true) when brep.IsSolid => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Brep brep, true) when brep.SolidOrientation != BrepSolidOrientation.None => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(brep); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Extrusion ext, true) when ext.IsSolid => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(ext); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Extrusion ext, true) when ext.IsClosed(0) && ext.IsClosed(1) => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(ext); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Mesh mesh, true) when mesh.IsClosed => ((Func<Result<Point3d>>)(() => { using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh); return vmp is not null ? ResultFactory.Create(value: vmp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Mesh mesh, true) => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(mesh); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (Curve curve, true) => ((Func<Result<Point3d>>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(curve); return amp is not null ? ResultFactory.Create(value: amp.Centroid) : ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed); }))(),
            (GeometryBase g, false) => g.GetBoundingBox(accurate: true) switch {
                BoundingBox b when b.IsValid => ResultFactory.Create(value: b.Center),
                _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
            },
            _ => ResultFactory.Create<Point3d>(error: E.Geometry.CentroidExtractionFailed),
        };

    /// <summary>Transform application with duplication.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(T geometry, Transform xform) where T : GeometryBase =>
        (T)geometry.Duplicate() switch {
            T dup when dup.Transform(xform) => ResultFactory.Create(value: (IReadOnlyList<T>)[dup,]),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
        };

    /// <summary>Canonical mode orientation with metadata dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> OrientToCanonical<T>(T geometry, Orient.CanonicalMode mode, IGeometryContext context) where T : GeometryBase =>
        OrientConfig.CanonicalModes.TryGetValue(mode.GetType(), out OrientConfig.OrientOperationMetadata? metadata)
            ? UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    ComputeCanonicalTransform(item, mode)
                        .Bind(xform => ApplyTransform(item, xform))),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                }).Map(r => r[0])
            : ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode);

    /// <summary>Compute canonical transform based on mode type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> ComputeCanonicalTransform<T>(T geometry, Orient.CanonicalMode mode) where T : GeometryBase =>
        (mode, geometry.GetBoundingBox(accurate: true)) switch {
            (Orient.VolumeCentroidMode, _) => ExtractCentroid(geometry, useMassProperties: true).Map(c => Transform.Translation(Point3d.Origin - c)),
            (_, BoundingBox box) when !box.IsValid => ResultFactory.Create<Transform>(error: E.Validation.BoundingBoxInvalid),
            (Orient.WorldXYMode, BoundingBox box) => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.YAxis), Plane.WorldXY)),
            (Orient.WorldYZMode, BoundingBox box) => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.YAxis, Vector3d.ZAxis), Plane.WorldYZ)),
            (Orient.WorldXZMode, BoundingBox box) => ResultFactory.Create(value: Transform.PlaneToPlane(new Plane(box.Center, Vector3d.XAxis, Vector3d.ZAxis), new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis))),
            (Orient.AreaCentroidMode, BoundingBox box) => ResultFactory.Create(value: Transform.Translation(Point3d.Origin - box.Center)),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
        };

    /// <summary>Orientation target application with metadata dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> OrientToTarget<T>(T geometry, Orient.OrientationTarget target, IGeometryContext context) where T : GeometryBase =>
        OrientConfig.OrientTargets.TryGetValue(target.GetType(), out OrientConfig.OrientOperationMetadata? metadata)
            ? UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    ComputeTargetTransform(item, target, context)
                        .Bind(xform => ApplyTransform(item, xform))),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                }).Map(r => r[0])
            : ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode);

    /// <summary>Compute target transform based on target type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> ComputeTargetTransform<T>(T geometry, Orient.OrientationTarget target, IGeometryContext context) where T : GeometryBase =>
        target switch {
            Orient.PlaneTarget pt => !pt.Target.IsValid
                ? ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationPlane)
                : ExtractPlane(geometry).Bind(src => ResultFactory.Create(value: Transform.PlaneToPlane(src, pt.Target))),
            Orient.PointTarget pt => ExtractCentroid(geometry, useMassProperties: false).Map(c => Transform.Translation(pt.Target - c)),
            Orient.MassPointTarget mpt => ExtractCentroid(geometry, useMassProperties: true).Map(c => Transform.Translation(mpt.Target - c)),
            Orient.VectorTarget vt => ComputeVectorTransform(geometry, vt),
            Orient.CurveFrameTarget cft => cft.Curve.FrameAt(cft.Parameter, out Plane frame) && frame.IsValid
                ? ExtractPlane(geometry).Bind(src => ResultFactory.Create(value: Transform.PlaneToPlane(src, frame)))
                : ResultFactory.Create<Transform>(error: E.Geometry.InvalidCurveParameter),
            Orient.SurfaceFrameTarget sft => sft.Surface.FrameAt(sft.U, sft.V, out Plane frame) && frame.IsValid
                ? ExtractPlane(geometry).Bind(src => ResultFactory.Create(value: Transform.PlaneToPlane(src, frame)))
                : ResultFactory.Create<Transform>(error: E.Geometry.InvalidSurfaceUV),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationMode),
        };

    /// <summary>Compute vector rotation transform.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> ComputeVectorTransform<T>(T geometry, Orient.VectorTarget vt) where T : GeometryBase =>
        geometry.GetBoundingBox(accurate: true) switch {
            BoundingBox box when box.IsValid && vt.Source.Length > RhinoMath.ZeroTolerance && vt.Target.Length > RhinoMath.ZeroTolerance =>
                ((Func<Result<Transform>>)(() => {
                    Vector3d su = new(vt.Source);
                    Vector3d tu = new(vt.Target);
                    _ = su.Unitize();
                    _ = tu.Unitize();
                    Point3d pt = vt.Anchor ?? box.Center;
                    return Vector3d.CrossProduct(su, tu).Length < RhinoMath.SqrtEpsilon
                        ? Math.Abs((su * tu) - 1.0) < RhinoMath.SqrtEpsilon
                            ? ResultFactory.Create(value: Transform.Identity)
                            : Math.Abs((su * tu) + 1.0) < RhinoMath.SqrtEpsilon
                                ? ((Func<Result<Transform>>)(() => {
                                    Vector3d axisCandidate = Math.Abs(su * Vector3d.XAxis) < 0.95 ? Vector3d.CrossProduct(su, Vector3d.XAxis) : Vector3d.CrossProduct(su, Vector3d.YAxis);
                                    bool normalized = axisCandidate.Unitize();
                                    return normalized
                                        ? ResultFactory.Create(value: Transform.Rotation(Math.PI, axisCandidate, pt))
                                        : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors);
                                }))()
                                : ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors)
                        : ResultFactory.Create(value: Transform.Rotation(su, tu, pt));
                }))(),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.InvalidOrientationVectors),
        };

    /// <summary>Best-fit plane extraction from point cloud or mesh.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Plane> ExtractBestFitPlane(GeometryBase geometry) =>
        geometry switch {
            PointCloud pc when pc.Count >= OrientConfig.BestFitMinPoints => pc.GetPoints() is Point3d[] pts
                && Plane.FitPlaneToPoints(pts, out Plane plane) == PlaneFitResult.Success
                && pts.Aggregate(0.0, (sum, p) => sum + (plane.DistanceTo(p) * plane.DistanceTo(p))) / pts.Length is double variance
                && Math.Sqrt(variance) <= OrientConfig.BestFitResidualThreshold
                    ? ResultFactory.Create(value: plane)
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            PointCloud pc => ResultFactory.Create<Plane>(error: E.Geometry.InsufficientParameters.WithContext($"Best-fit plane requires {OrientConfig.BestFitMinPoints} points, got {pc.Count}")),
            Mesh m when m.Vertices.Count >= OrientConfig.BestFitMinPoints => m.Vertices.ToPoint3dArray() is Point3d[] pts
                && Plane.FitPlaneToPoints(pts, out Plane plane) == PlaneFitResult.Success
                && pts.Aggregate(0.0, (sum, p) => sum + (plane.DistanceTo(p) * plane.DistanceTo(p))) / pts.Length is double variance
                && Math.Sqrt(variance) <= OrientConfig.BestFitResidualThreshold
                    ? ResultFactory.Create(value: plane)
                    : ResultFactory.Create<Plane>(error: E.Geometry.FrameExtractionFailed),
            Mesh m => ResultFactory.Create<Plane>(error: E.Geometry.InsufficientParameters.WithContext($"Best-fit plane requires {OrientConfig.BestFitMinPoints} points, got {m.Vertices.Count}")),
            _ => ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(geometry.GetType().Name)),
        };

    /// <summary>Best-fit orientation with metadata dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> OrientToBestFit<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                ExtractBestFitPlane(item)
                    .Bind(plane => ApplyTransform(item, Transform.PlaneToPlane(plane, Plane.WorldXY)))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.GeometryOperations.TryGetValue((geometry.GetType(), OrientConfig.OpType.BestFit), out OrientConfig.OrientOperationMetadata? m) ? m.ValidationMode : V.Standard,
                OperationName = OrientConfig.GeometryOperations.TryGetValue((geometry.GetType(), OrientConfig.OpType.BestFit), out OrientConfig.OrientOperationMetadata? n) ? n.OperationName : "Orient.BestFit",
            }).Map(r => r[0]);

    /// <summary>Mirror operation with metadata dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Mirror<T>(T geometry, Plane plane, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                plane.IsValid
                    ? ApplyTransform(item, Transform.Mirror(plane))
                    : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane)),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.GeometryOperations.TryGetValue((geometry.GetType(), OrientConfig.OpType.Mirror), out OrientConfig.OrientOperationMetadata? m)
                    ? m.ValidationMode
                    : OrientConfig.DefaultValidationModes.GetValueOrDefault(geometry.GetType(), V.Standard),
                OperationName = OrientConfig.GeometryOperations.TryGetValue((geometry.GetType(), OrientConfig.OpType.Mirror), out OrientConfig.OrientOperationMetadata? n)
                    ? n.OperationName
                    : "Orient.Mirror",
            }).Map(r => r[0]);

    /// <summary>Flip direction operation with metadata dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> FlipDirection<T>(T geometry, IGeometryContext context) where T : GeometryBase =>
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
                    Mesh mesh => ((Func<Result<IReadOnlyList<T>>>)(() => { mesh.Flip(vertexNormals: true, faceNormals: true, faceOrientation: true); return ResultFactory.Create(value: (IReadOnlyList<T>)[(T)(GeometryBase)mesh,]); }))(),
                    null => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.TransformFailed),
                    _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name)),
                }),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientConfig.GeometryOperations.TryGetValue((geometry.GetType(), OrientConfig.OpType.FlipDirection), out OrientConfig.OrientOperationMetadata? m)
                    ? m.ValidationMode
                    : OrientConfig.DefaultValidationModes.GetValueOrDefault(geometry.GetType(), V.Standard),
                OperationName = OrientConfig.GeometryOperations.TryGetValue((geometry.GetType(), OrientConfig.OpType.FlipDirection), out OrientConfig.OrientOperationMetadata? n)
                    ? n.OperationName
                    : "Orient.Flip",
            }).Map(r => r[0]);

    /// <summary>Optimization orientation with metadata dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orient.OptimizationResult> OptimizeOrientation(
        Brep brep,
        Orient.OptimizationCriteria criteria,
        IGeometryContext context) =>
        OrientConfig.OptimizationCriteria.TryGetValue(criteria.GetType(), out OrientConfig.OrientOperationMetadata? metadata)
            ? UnifiedOperation.Apply(
                input: brep,
                operation: (Func<Brep, Result<IReadOnlyList<Orient.OptimizationResult>>>)(item =>
                    OrientCompute.OptimizeOrientation(item, criteria, context.AbsoluteTolerance)
                        .Map(r => (IReadOnlyList<Orient.OptimizationResult>)[r,])),
                config: new OperationConfig<Brep, Orient.OptimizationResult> {
                    Context = context,
                    ValidationMode = metadata.ValidationMode,
                    OperationName = metadata.OperationName,
                }).Map(r => r[0])
            : ResultFactory.Create<Orient.OptimizationResult>(error: E.Geometry.InvalidOrientationMode);

    /// <summary>Relative orientation computation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orient.RelativeOrientationResult> ComputeRelative(
        GeometryBase geometryA,
        GeometryBase geometryB,
        IGeometryContext context) =>
        OrientCompute.ComputeRelative(geometryA, geometryB, context);

    /// <summary>Pattern detection and alignment.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orient.PatternResult> DetectPattern(
        GeometryBase[] geometries,
        IGeometryContext context) =>
        OrientCompute.DetectPattern(geometries, context);
}
