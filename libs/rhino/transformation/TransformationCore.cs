using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Orchestration layer for transformation operations via UnifiedOperation.</summary>
[Pure]
internal static class TransformationCore {
    /// <summary>Execute transform operation on geometry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Execute<T>(
        T geometry,
        Transformation.TransformOperation operation,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        !TransformationConfig.TransformOperations.TryGetValue(operation.GetType(), out TransformationConfig.TransformOperationMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTransformSpec.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : BuildTransformMatrix(operation: operation, context: context)
                .Bind(xform => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                        ApplyTransformToGeometry(geometry: item, transform: xform)),
                    config: new OperationConfig<T, T> {
                        Context = context,
                        ValidationMode = TransformationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                        OperationName = meta.OperationName,
                        EnableDiagnostics = enableDiagnostics,
                    }))
                .Map(static r => r[0]);

    /// <summary>Execute array operation on geometry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ExecuteArray<T>(
        T geometry,
        Transformation.ArrayOperation operation,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        !TransformationConfig.ArrayOperations.TryGetValue(operation.GetType(), out TransformationConfig.ArrayOperationMetadata? meta)
            ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : operation switch {
                Transformation.RectangularArray rect => GenerateRectangularTransforms(
                    xCount: rect.XCount,
                    yCount: rect.YCount,
                    zCount: rect.ZCount,
                    xSpacing: rect.XSpacing,
                    ySpacing: rect.YSpacing,
                    zSpacing: rect.ZSpacing,
                    context: context)
                    .Bind(xforms => ApplyTransforms(geometry: geometry, transforms: xforms, meta: meta, context: context, enableDiagnostics: enableDiagnostics)),
                Transformation.PolarArray polar => GeneratePolarTransforms(
                    center: polar.Center,
                    axis: polar.Axis,
                    count: polar.Count,
                    totalAngle: polar.TotalAngleRadians,
                    context: context)
                    .Bind(xforms => ApplyTransforms(geometry: geometry, transforms: xforms, meta: meta, context: context, enableDiagnostics: enableDiagnostics)),
                Transformation.LinearArray linear => GenerateLinearTransforms(
                    direction: linear.Direction,
                    count: linear.Count,
                    spacing: linear.Spacing,
                    context: context)
                    .Bind(xforms => ApplyTransforms(geometry: geometry, transforms: xforms, meta: meta, context: context, enableDiagnostics: enableDiagnostics)),
                Transformation.PathArray path => TransformationCompute.PathArray(
                    geometry: geometry,
                    path: path.PathCurve,
                    count: path.Count,
                    orientToPath: path.OrientToPath,
                    context: context,
                    enableDiagnostics: enableDiagnostics),
                _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode.WithContext($"Unhandled operation: {operation.GetType().Name}")),
            };

    /// <summary>Execute morph operation on geometry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteMorph<T>(
        T geometry,
        Transformation.MorphOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        !TransformationConfig.MorphOperations.TryGetValue(operation.GetType(), out TransformationConfig.MorphOperationMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : operation switch {
                Transformation.FlowMorph flow => TransformationCompute.Flow(
                    geometry: geometry,
                    baseCurve: flow.BaseCurve,
                    targetCurve: flow.TargetCurve,
                    preserveStructure: flow.PreserveStructure,
                    context: context,
                    tolerance: meta.Tolerance),
                Transformation.TwistMorph twist => TransformationCompute.Twist(
                    geometry: geometry,
                    axis: twist.Axis,
                    angleRadians: twist.AngleRadians,
                    infinite: twist.Infinite,
                    context: context,
                    tolerance: meta.Tolerance),
                Transformation.BendMorph bend => TransformationCompute.Bend(
                    geometry: geometry,
                    spine: bend.Spine,
                    angle: bend.Angle,
                    context: context,
                    tolerance: meta.Tolerance),
                Transformation.TaperMorph taper => TransformationCompute.Taper(
                    geometry: geometry,
                    axis: taper.Axis,
                    startWidth: taper.StartWidth,
                    endWidth: taper.EndWidth,
                    context: context,
                    tolerance: meta.Tolerance),
                Transformation.StretchMorph stretch => TransformationCompute.Stretch(
                    geometry: geometry,
                    axis: stretch.Axis,
                    context: context,
                    tolerance: meta.Tolerance),
                Transformation.SplopMorph splop => TransformationCompute.Splop(
                    geometry: geometry,
                    basePlane: splop.BasePlane,
                    targetSurface: splop.TargetSurface,
                    targetPoint: splop.TargetPoint,
                    context: context,
                    tolerance: meta.Tolerance),
                Transformation.SporphMorph sporph => TransformationCompute.Sporph(
                    geometry: geometry,
                    sourceSurface: sporph.SourceSurface,
                    targetSurface: sporph.TargetSurface,
                    preserveStructure: sporph.PreserveStructure,
                    context: context,
                    tolerance: meta.Tolerance),
                Transformation.MaelstromMorph maelstrom => TransformationCompute.Maelstrom(
                    geometry: geometry,
                    center: maelstrom.Center,
                    axis: maelstrom.Axis,
                    radius: maelstrom.Radius,
                    angle: maelstrom.Angle,
                    context: context,
                    tolerance: meta.Tolerance),
                _ => ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation.WithContext($"Unhandled operation: {operation.GetType().Name}")),
            };

    /// <summary>Build transform matrix from algebraic operation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> BuildTransformMatrix(
        Transformation.TransformOperation operation,
        IGeometryContext context) =>
        operation switch {
            Transformation.MatrixTransform matrix =>
                matrix.Matrix.IsValid && Math.Abs(matrix.Matrix.Determinant) > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: matrix.Matrix)
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformMatrix.WithContext($"Valid: {matrix.Matrix.IsValid}, Det: {matrix.Matrix.Determinant:F6}")),
            Transformation.UniformScale scale =>
                scale.Factor is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                    ? ResultFactory.Create(value: Transform.Scale(scale.Anchor, scale.Factor))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidScaleFactor.WithContext($"Factor: {scale.Factor:F6}")),
            Transformation.NonUniformScale scale =>
                scale.Plane.IsValid
                && scale.XScale is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                && scale.YScale is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                && scale.ZScale is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
                    ? ResultFactory.Create(value: Transform.Scale(scale.Plane, scale.XScale, scale.YScale, scale.ZScale))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidScaleFactor.WithContext($"Plane: {scale.Plane.IsValid}, X: {scale.XScale:F6}, Y: {scale.YScale:F6}, Z: {scale.ZScale:F6}")),
            Transformation.AxisRotation rotation =>
                rotation.Axis.Length > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: Transform.Rotation(rotation.AngleRadians, rotation.Axis, rotation.Center))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidRotationAxis.WithContext($"Axis length: {rotation.Axis.Length:F6}")),
            Transformation.VectorRotation rotation =>
                rotation.StartDirection.Length > context.AbsoluteTolerance && rotation.EndDirection.Length > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: Transform.Rotation(rotation.StartDirection, rotation.EndDirection, rotation.Center))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidRotationAxis.WithContext($"Start: {rotation.StartDirection.Length:F6}, End: {rotation.EndDirection.Length:F6}")),
            Transformation.MirrorTransform mirror =>
                mirror.MirrorPlane.IsValid
                    ? ResultFactory.Create(value: Transform.Mirror(mirror.MirrorPlane))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidMirrorPlane),
            Transformation.Translation translation =>
                ResultFactory.Create(value: Transform.Translation(translation.Motion)),
            Transformation.Shear shear =>
                shear.Plane.IsValid
                && shear.Direction.Length > context.AbsoluteTolerance
                && shear.Plane.ZAxis.IsParallelTo(shear.Direction, context.AngleToleranceRadians * TransformationConfig.AngleToleranceMultiplier) == 0
                    ? ResultFactory.Create(value: Transform.Shear(shear.Plane, shear.Direction * Math.Tan(shear.Angle), Vector3d.Zero, Vector3d.Zero))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidShearParameters.WithContext($"Plane: {shear.Plane.IsValid}, Dir: {shear.Direction.Length:F6}")),
            Transformation.Projection projection =>
                projection.ProjectionPlane.IsValid
                    ? ResultFactory.Create(value: Transform.PlanarProjection(projection.ProjectionPlane))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidProjectionPlane),
            Transformation.ChangeBasis basis =>
                basis.FromPlane.IsValid && basis.ToPlane.IsValid
                    ? ResultFactory.Create(value: Transform.ChangeBasis(basis.FromPlane, basis.ToPlane))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidBasisPlanes.WithContext($"From: {basis.FromPlane.IsValid}, To: {basis.ToPlane.IsValid}")),
            Transformation.PlaneToPlane planes =>
                planes.FromPlane.IsValid && planes.ToPlane.IsValid
                    ? ResultFactory.Create(value: Transform.PlaneToPlane(planes.FromPlane, planes.ToPlane))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidBasisPlanes.WithContext($"From: {planes.FromPlane.IsValid}, To: {planes.ToPlane.IsValid}")),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformSpec.WithContext($"Unhandled operation: {operation.GetType().Name}")),
        };

    /// <summary>Apply transform to geometry with Extrusion conversion.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ApplyTransformToGeometry<T>(
        T geometry,
        Transform transform) where T : GeometryBase {
        GeometryBase normalized = geometry is Extrusion extrusion
            ? extrusion.ToBrep(splitKinkyFaces: true)
            : geometry;
        T duplicate = (T)normalized.Duplicate();
        Result<IReadOnlyList<T>> result = duplicate.Transform(transform)
            ? ResultFactory.Create<IReadOnlyList<T>>(value: [duplicate,])
            : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.TransformApplicationFailed);

        (geometry is Extrusion ? normalized : null)?.Dispose();
        (!result.IsSuccess ? duplicate : null)?.Dispose();

        return result;
    }

    /// <summary>Apply array of transforms to geometry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ApplyTransforms<T>(
        T geometry,
        Transform[] transforms,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                ApplyTransformToGeometry(geometry: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                AccumulateErrors = false,
                OperationName = meta.OperationName,
                EnableDiagnostics = enableDiagnostics,
            });

    /// <summary>Generate rectangular grid transforms.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform[]> GenerateRectangularTransforms(
        int xCount,
        int yCount,
        int zCount,
        double xSpacing,
        double ySpacing,
        double zSpacing,
        IGeometryContext context) {
        int totalCount = xCount * yCount * zCount;
        return xCount <= 0 || yCount <= 0 || zCount <= 0
            || totalCount > TransformationConfig.MaxArrayCount
            || Math.Abs(xSpacing) <= context.AbsoluteTolerance
            || Math.Abs(ySpacing) <= context.AbsoluteTolerance
            || (zCount > 1 && Math.Abs(zSpacing) <= context.AbsoluteTolerance)
            ? ResultFactory.Create<Transform[]>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"XCount: {xCount}, YCount: {yCount}, ZCount: {zCount}, Total: {totalCount}"))
            : ResultFactory.Create(value: [.. Enumerable.Range(0, xCount)
                .SelectMany(i => Enumerable.Range(0, yCount)
                    .SelectMany(j => Enumerable.Range(0, zCount)
                        .Select(k => Transform.Translation(dx: i * xSpacing, dy: j * ySpacing, dz: k * zSpacing)))),
            ]);
    }

    /// <summary>Generate polar array transforms.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform[]> GeneratePolarTransforms(
        Point3d center,
        Vector3d axis,
        int count,
        double totalAngle,
        IGeometryContext context) =>
        count <= 0 || count > TransformationConfig.MaxArrayCount
        || axis.Length <= context.AbsoluteTolerance
        || totalAngle <= 0.0 || totalAngle > RhinoMath.TwoPI
            ? ResultFactory.Create<Transform[]>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"Count: {count}, Axis: {axis.Length:F6}, Angle: {totalAngle:F6}"))
            : ResultFactory.Create(value: [.. Enumerable.Range(0, count)
                .Select(i => Transform.Rotation(
                    angleRadians: totalAngle * i / count,
                    rotationAxis: axis,
                    rotationCenter: center)),
            ]);

    /// <summary>Generate linear array transforms.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform[]> GenerateLinearTransforms(
        Vector3d direction,
        int count,
        double spacing,
        IGeometryContext context) {
        double dirLength = direction.Length;
        return count <= 0 || count > TransformationConfig.MaxArrayCount
            || dirLength <= context.AbsoluteTolerance
            || Math.Abs(spacing) <= context.AbsoluteTolerance
            ? ResultFactory.Create<Transform[]>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"Count: {count}, Direction: {dirLength:F6}, Spacing: {spacing:F6}"))
            : ResultFactory.Create(value: [.. Enumerable.Range(0, count)
                .Select(i => Transform.Translation((direction / dirLength) * spacing * i)),
            ]);
    }
}
