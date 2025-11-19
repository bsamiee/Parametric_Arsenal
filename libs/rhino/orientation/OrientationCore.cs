using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Orientation;

/// <summary>Orchestration layer for orientation operations via UnifiedOperation.</summary>
[Pure]
internal static class OrientationCore {
    /// <summary>Execute orientation operation on geometry with unified dispatch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Execute<T>(T geometry, Orientation.Operation operation, IGeometryContext context) where T : GeometryBase =>
        !OrientationConfig.Operations.TryGetValue(operation.GetType(), out OrientationConfig.OrientationOperationMetadata? opMeta)
            ? ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : operation switch {
                Orientation.ToPlane toPlane => ExecuteToPlane(geometry: geometry, target: toPlane.Target, meta: opMeta, context: context),
                Orientation.ToCanonical toCanonical => ExecuteToCanonical(geometry: geometry, mode: toCanonical.Mode, meta: opMeta, context: context),
                Orientation.ToPoint toPoint => ExecuteToPoint(geometry: geometry, target: toPoint.Target, centroidMode: toPoint.CentroidType, meta: opMeta, context: context),
                Orientation.ToVector toVector => ExecuteToVector(geometry: geometry, target: toVector.Target, source: toVector.Source, anchor: toVector.Anchor, meta: opMeta, context: context),
                Orientation.ToBestFit => ExecuteToBestFit(geometry: geometry, meta: opMeta, context: context),
                Orientation.Mirror mirror => ExecuteMirror(geometry: geometry, plane: mirror.MirrorPlane, meta: opMeta, context: context),
                Orientation.FlipDirection => ExecuteFlipDirection(geometry: geometry, meta: opMeta, context: context),
                Orientation.ToCurveFrame toCurve => ExecuteToCurveFrame(geometry: geometry, curve: toCurve.Curve, parameter: toCurve.Parameter, meta: opMeta, context: context),
                Orientation.ToSurfaceFrame toSurface => ExecuteToSurfaceFrame(geometry: geometry, surface: toSurface.Surface, u: toSurface.U, v: toSurface.V, meta: opMeta, context: context),
                _ => ResultFactory.Create<T>(error: E.Geometry.InvalidOrientationMode.WithContext($"Unhandled operation: {operation.GetType().Name}")),
            };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteToPlane<T>(T geometry, Plane target, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                (OrientationConfig.PlaneExtractors.TryGetValue(item.GetType(), out Func<GeometryBase, Result<Plane>>? extractor)
                    ? extractor(item)
                    : OrientationConfig.PlaneExtractors.FirstOrDefault(kv => kv.Key.IsInstanceOfType(item)).Value?.Invoke(item)
                        ?? ResultFactory.Create<Plane>(error: E.Geometry.UnsupportedOrientationType.WithContext(item.GetType().Name)))
                .Bind(src => !target.IsValid
                    ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane)
                    : OrientationCompute.ApplyTransform(geometry: item, transform: Transform.PlaneToPlane(src, target)))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteToCanonical<T>(T geometry, Orientation.CanonicalMode mode, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientationCompute.ComputeCanonicalTransform(geometry: item, mode: mode)
                    .Bind(xform => OrientationCompute.ApplyTransform(geometry: item, transform: xform))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientationConfig.CanonicalModeValidation.GetValueOrDefault(mode.GetType(), meta.ValidationMode),
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteToPoint<T>(T geometry, Point3d target, Orientation.CentroidMode centroidMode, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientationCompute.ExtractCentroid(geometry: item, useMassProperties: centroidMode is Orientation.MassCentroid)
                    .Map(c => Transform.Translation(target - c))
                    .Bind(xform => OrientationCompute.ApplyTransform(geometry: item, transform: xform))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientationConfig.CentroidModeValidation.GetValueOrDefault(centroidMode.GetType(), meta.ValidationMode),
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteToVector<T>(T geometry, Vector3d target, Vector3d? source, Point3d? anchor, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientationCompute.ComputeVectorRotation(geometry: item, target: target, source: source, anchor: anchor)
                    .Bind(xform => OrientationCompute.ApplyTransform(geometry: item, transform: xform))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteToBestFit<T>(T geometry, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientationCompute.ExtractBestFitPlane(geometry: item)
                    .Bind(plane => OrientationCompute.ApplyTransform(geometry: item, transform: Transform.PlaneToPlane(plane, Plane.WorldXY)))),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteMirror<T>(T geometry, Plane plane, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                plane.IsValid
                    ? OrientationCompute.ApplyTransform(geometry: item, transform: Transform.Mirror(plane))
                    : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.InvalidOrientationPlane)),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteFlipDirection<T>(T geometry, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                OrientationCompute.FlipDirection(geometry: item)),
            config: new OperationConfig<T, T> {
                Context = context,
                ValidationMode = OrientationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                OperationName = meta.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteToCurveFrame<T>(T geometry, Curve curve, double parameter, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        curve.FrameAt(parameter, out Plane frame) && frame.IsValid
            ? ExecuteToPlane(geometry: geometry, target: frame, meta: meta, context: context)
            : ResultFactory.Create<T>(error: E.Geometry.InvalidCurveParameter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteToSurfaceFrame<T>(T geometry, Surface surface, double u, double v, OrientationConfig.OrientationOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        surface.FrameAt(u, v, out Plane frame) && frame.IsValid
            ? ExecuteToPlane(geometry: geometry, target: frame, meta: meta, context: context)
            : ResultFactory.Create<T>(error: E.Geometry.InvalidSurfaceUV);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orientation.OptimizationResult> ExecuteOptimization(Brep brep, Orientation.OptimizationCriteria criteria, IGeometryContext context) =>
        OrientationConfig.Operations[typeof(Orientation.Optimize)] is OrientationConfig.OrientationOperationMetadata meta
            ? UnifiedOperation.Apply(
                input: brep,
                operation: (Func<Brep, Result<IReadOnlyList<Orientation.OptimizationResult>>>)(item =>
                    OrientationCompute.OptimizeOrientation(brep: item, criteria: criteria, tolerance: context.AbsoluteTolerance)
                        .Map(r => (IReadOnlyList<Orientation.OptimizationResult>)[r,])),
                config: new OperationConfig<Brep, Orientation.OptimizationResult> {
                    Context = context,
                    ValidationMode = meta.ValidationMode,
                    OperationName = meta.OperationName,
                }).Map(static r => r[0])
            : ResultFactory.Create<Orientation.OptimizationResult>(error: E.Geometry.InvalidOrientationMode);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orientation.RelativeOrientationResult> ExecuteComputeRelative(GeometryBase geometryA, GeometryBase geometryB, IGeometryContext context) =>
        OrientationCompute.ComputeRelative(
            geometryA: geometryA,
            geometryB: geometryB,
            symmetryTolerance: context.AbsoluteTolerance,
            angleTolerance: context.AngleToleranceRadians);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Orientation.PatternDetectionResult> ExecuteDetectPattern(GeometryBase[] geometries, IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(static g => g.All(static item => item?.IsValid == true), error: E.Validation.GeometryInvalid)
            .Bind(validGeometries => OrientationCompute.DetectPattern(
                geometries: validGeometries,
                absoluteTolerance: context.AbsoluteTolerance,
                angleTolerance: context.AngleToleranceRadians));
}
