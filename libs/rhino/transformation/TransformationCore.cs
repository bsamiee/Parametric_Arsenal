using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Orchestration layer for transformation operations via UnifiedOperation.</summary>
internal static class TransformationCore {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteTransform<T>(
        T geometry,
        Transformation.TransformRequest request,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        !TransformationConfig.Transformations.TryGetValue(request.GetType(), out TransformationConfig.TransformMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTransformSpec)
            : BuildTransform(request: request, context: context, meta: meta)
                .Bind(xform => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                        ApplyTransform(item: item, transform: xform)),
                    config: new OperationConfig<T, T> {
                        Context = context,
                        ValidationMode = meta.ValidationMode | TransformationConfig.GetGeometryValidation(typeof(T)),
                        OperationName = meta.OperationName,
                        EnableDiagnostics = enableDiagnostics,
                    }))
                .Map(static r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ExecuteArray<T>(
        T geometry,
        Transformation.ArrayRequest request,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        !TransformationConfig.Arrays.TryGetValue(request.GetType(), out TransformationConfig.ArrayMetadata? meta)
            ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode)
            : BuildArrayTransforms(request: request, context: context)
                .Bind(transforms => ApplyArrayTransforms(
                    geometry: geometry,
                    transforms: transforms,
                    meta: meta,
                    context: context,
                    enableDiagnostics: enableDiagnostics));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteMorph<T>(
        T geometry,
        Transformation.MorphRequest request,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        !TransformationConfig.Morphs.TryGetValue(request.GetType(), out TransformationConfig.MorphMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation)
            : UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    ExecuteMorphInternal(item: item, request: request, context: context)
                        .Map(result => (IReadOnlyList<T>)[result,])),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = meta.ValidationMode | TransformationConfig.GetGeometryValidation(typeof(T)),
                    OperationName = meta.OperationName,
                    EnableDiagnostics = enableDiagnostics,
                })
                .Map(static r => r[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> BuildTransform(
        Transformation.TransformRequest request,
        IGeometryContext context,
        TransformationConfig.TransformMetadata meta) =>
        request switch {
            Transformation.MatrixTransform matrix =>
                matrix.Matrix.IsValid && Math.Abs(matrix.Matrix.Determinant) > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: matrix.Matrix)
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Valid: {matrix.Matrix.IsValid}, Det: {matrix.Matrix.Determinant:F6}"))),
            Transformation.UniformScaleTransform uniform =>
                uniform.Factor is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                    ? ResultFactory.Create(value: Transform.Scale(uniform.Anchor, uniform.Factor))
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Factor: {uniform.Factor:F6}"))),
            Transformation.NonUniformScaleTransform nonUniform =>
                nonUniform.Plane.IsValid
                && nonUniform.XScale is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                && nonUniform.YScale is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                && nonUniform.ZScale is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                    ? ResultFactory.Create(value: Transform.Scale(
                        plane: nonUniform.Plane,
                        xScale: nonUniform.XScale,
                        yScale: nonUniform.YScale,
                        zScale: nonUniform.ZScale))
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Plane: {nonUniform.Plane.IsValid}, X: {nonUniform.XScale:F6}, Y: {nonUniform.YScale:F6}, Z: {nonUniform.ZScale:F6}"))),
            Transformation.AxisRotationTransform rotation =>
                rotation.Axis.Length > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: Transform.Rotation(
                        angleRadians: rotation.AngleRadians,
                        rotationAxis: rotation.Axis,
                        rotationCenter: rotation.Center))
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Axis: {rotation.Axis.Length:F6}"))),
            Transformation.DirectionRotationTransform vectors =>
                vectors.Start.Length > context.AbsoluteTolerance && vectors.End.Length > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: Transform.Rotation(
                        startDirection: vectors.Start,
                        endDirection: vectors.End,
                        rotationCenter: vectors.Center))
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Start: {vectors.Start.Length:F6}, End: {vectors.End.Length:F6}"))),
            Transformation.MirrorTransform mirror =>
                mirror.Plane.IsValid
                    ? ResultFactory.Create(value: Transform.Mirror(mirror.Plane))
                    : ResultFactory.Create<Transform>(error: meta.Error),
            Transformation.TranslationTransform translation =>
                ResultFactory.Create(value: Transform.Translation(translation.Motion)),
            Transformation.ShearTransform shear =>
                shear.Plane.IsValid
                && shear.Direction.Length > context.AbsoluteTolerance
                && shear.Plane.ZAxis.IsParallelTo(
                    other: shear.Direction,
                    angleTolerance: context.AngleToleranceRadians * TransformationConfig.AngleToleranceMultiplier) == 0
                    ? ResultFactory.Create(value: Transform.Shear(
                        plane: shear.Plane,
                        xShear: shear.Direction * Math.Tan(shear.AngleRadians),
                        yShear: Vector3d.Zero,
                        zShear: Vector3d.Zero))
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Plane: {shear.Plane.IsValid}, Dir: {shear.Direction.Length:F6}"))),
            Transformation.ProjectionTransform projection =>
                projection.Plane.IsValid
                    ? ResultFactory.Create(value: Transform.PlanarProjection(projection.Plane))
                    : ResultFactory.Create<Transform>(error: meta.Error),
            Transformation.ChangeBasisTransform basis =>
                basis.From.IsValid && basis.To.IsValid
                    ? ResultFactory.Create(value: Transform.ChangeBasis(basis.From, basis.To))
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"From: {basis.From.IsValid}, To: {basis.To.IsValid}"))),
            Transformation.PlaneToPlaneTransform planeToPlane =>
                planeToPlane.From.IsValid && planeToPlane.To.IsValid
                    ? ResultFactory.Create(value: Transform.PlaneToPlane(planeToPlane.From, planeToPlane.To))
                    : ResultFactory.Create<Transform>(error: meta.Error.WithContext(string.Create(
                        CultureInfo.InvariantCulture,
                        $"From: {planeToPlane.From.IsValid}, To: {planeToPlane.To.IsValid}"))),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformSpec),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ApplyArrayTransforms<T>(
        T geometry,
        IReadOnlyList<Transform> transforms,
        TransformationConfig.ArrayMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                ApplyTransform(item: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                AccumulateErrors = false,
                OperationName = meta.OperationName,
                EnableDiagnostics = enableDiagnostics,
            });

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Transform>> BuildArrayTransforms(
        Transformation.ArrayRequest request,
        IGeometryContext context) =>
        request switch {
            Transformation.RectangularArray rectangular => TransformationCompute.BuildRectangularTransforms(
                xCount: rectangular.XCount,
                yCount: rectangular.YCount,
                zCount: rectangular.ZCount,
                xSpacing: rectangular.XSpacing,
                ySpacing: rectangular.YSpacing,
                zSpacing: rectangular.ZSpacing,
                tolerance: context.AbsoluteTolerance),
            Transformation.PolarArray polar => TransformationCompute.BuildPolarTransforms(
                center: polar.Center,
                axis: polar.Axis,
                count: polar.Count,
                totalAngle: polar.TotalAngle,
                tolerance: context.AbsoluteTolerance),
            Transformation.LinearArray linear => TransformationCompute.BuildLinearTransforms(
                direction: linear.Direction,
                count: linear.Count,
                spacing: linear.Spacing,
                tolerance: context.AbsoluteTolerance),
            Transformation.PathArray path => TransformationCompute.BuildPathTransforms(
                path: path.Path,
                count: path.Count,
                orientToPath: path.OrientToPath,
                tolerance: context.AbsoluteTolerance),
            _ => ResultFactory.Create<IReadOnlyList<Transform>>(error: E.Geometry.Transformation.InvalidArrayMode),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ExecuteMorphInternal<T>(
        T item,
        Transformation.MorphRequest request,
        IGeometryContext context) where T : GeometryBase =>
        request switch {
            Transformation.FlowMorph flow => TransformationCompute.Flow(
                geometry: item,
                baseCurve: flow.BaseCurve,
                targetCurve: flow.TargetCurve,
                preserveStructure: flow.PreserveStructure,
                context: context),
            Transformation.TwistMorph twist => TransformationCompute.Twist(
                geometry: item,
                axis: twist.Axis,
                angleRadians: twist.AngleRadians,
                infinite: twist.Infinite,
                context: context),
            Transformation.BendMorph bend => TransformationCompute.Bend(
                geometry: item,
                spine: bend.Spine,
                angle: bend.AngleRadians,
                context: context),
            Transformation.TaperMorph taper => TransformationCompute.Taper(
                geometry: item,
                axis: taper.Axis,
                startWidth: taper.StartWidth,
                endWidth: taper.EndWidth,
                context: context),
            Transformation.StretchMorph stretch => TransformationCompute.Stretch(
                geometry: item,
                axis: stretch.Axis,
                context: context),
            Transformation.SplopMorph splop => TransformationCompute.Splop(
                geometry: item,
                basePlane: splop.BasePlane,
                targetSurface: splop.TargetSurface,
                targetPoint: splop.TargetPoint,
                context: context),
            Transformation.SporphMorph sporph => TransformationCompute.Sporph(
                geometry: item,
                sourceSurface: sporph.SourceSurface,
                targetSurface: sporph.TargetSurface,
                preserveStructure: sporph.PreserveStructure,
                context: context),
            Transformation.MaelstromMorph maelstrom => TransformationCompute.Maelstrom(
                geometry: item,
                center: maelstrom.Center,
                axis: maelstrom.Axis,
                radius: maelstrom.Radius,
                angle: maelstrom.AngleRadians,
                context: context),
            _ => ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(
        T item,
        Transform transform) where T : GeometryBase {
        GeometryBase normalized = item is Extrusion extrusion
            ? extrusion.ToBrep(splitKinkyFaces: true)
            : item;
        T duplicate = (T)normalized.Duplicate();
        Result<IReadOnlyList<T>> result = duplicate.Transform(transform)
            ? ResultFactory.Create<IReadOnlyList<T>>(value: [duplicate,])
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.TransformApplicationFailed);

        (item is Extrusion ? normalized : null)?.Dispose();
        (!result.IsSuccess ? duplicate : null)?.Dispose();

        return result;
    }
}
