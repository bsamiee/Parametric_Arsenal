using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Transform matrix construction, validation, and orchestration via UnifiedOperation.</summary>
internal static class TransformationCore {
    /// <summary>Execute transform operation on geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteTransform<T>(
        T geometry,
        Transformation.TransformOperation operation,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        !TransformationConfig.TransformOperations.TryGetValue(operation.GetType(), out TransformationConfig.TransformOperationMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTransformSpec)
            : BuildTransformMatrix(operation: operation, context: context)
                .Bind(xform => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<T>>>)(item => ApplyTransform(item: item, transform: xform)),
                    config: new OperationConfig<T, T> {
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                        Context = context,
                        EnableDiagnostics = enableDiagnostics,
                    }))
                .Map(r => r[0]);

    /// <summary>Execute array operation on geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ExecuteArray<T>(
        T geometry,
        Transformation.ArrayOperation operation,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        !TransformationConfig.ArrayOperations.TryGetValue(operation.GetType(), out TransformationConfig.ArrayOperationMetadata? meta)
            ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode)
            : operation switch {
                Transformation.RectangularArray r => RectangularArray(
                    geometry: geometry,
                    operation: r,
                    meta: meta,
                    context: context,
                    enableDiagnostics: enableDiagnostics),
                Transformation.PolarArray p => PolarArray(
                    geometry: geometry,
                    operation: p,
                    meta: meta,
                    context: context,
                    enableDiagnostics: enableDiagnostics),
                Transformation.LinearArray l => LinearArray(
                    geometry: geometry,
                    operation: l,
                    meta: meta,
                    context: context,
                    enableDiagnostics: enableDiagnostics),
                Transformation.PathArray pa => TransformationCompute.PathArray(
                    geometry: geometry,
                    operation: pa,
                    meta: meta,
                    context: context,
                    enableDiagnostics: enableDiagnostics),
                _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode),
            };

    /// <summary>Build transform matrix from operation type.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> BuildTransformMatrix(
        Transformation.TransformOperation operation,
        IGeometryContext context) =>
        operation switch {
            Transformation.MatrixTransform m => ValidateMatrix(matrix: m.Value, context: context),
            Transformation.UniformScale s => ValidateScaleFactor(factor: s.Factor)
                .Map(_ => Transform.Scale(anchor: s.Anchor, scaleFactor: s.Factor)),
            Transformation.NonUniformScale ns => ValidateScaleFactors(x: ns.XScale, y: ns.YScale, z: ns.ZScale)
                .Ensure(_ => ns.Plane.IsValid, error: E.Geometry.Transformation.InvalidBasisPlanes)
                .Map(_ => Transform.Scale(plane: ns.Plane, xScaleFactor: ns.XScale, yScaleFactor: ns.YScale, zScaleFactor: ns.ZScale)),
            Transformation.AxisRotation ar => ValidateAxis(axis: ar.Axis, context: context)
                .Map(_ => Transform.Rotation(angleRadians: ar.AngleRadians, rotationAxis: ar.Axis, rotationCenter: ar.Center)),
            Transformation.VectorRotation vr => ValidateRotationVectors(start: vr.Start, end: vr.End, context: context)
                .Map(_ => Transform.Rotation(startDirection: vr.Start, endDirection: vr.End, rotationCenter: vr.Center)),
            Transformation.MirrorTransform mt => ValidatePlane(plane: mt.Plane)
                .Map(_ => Transform.Mirror(mirrorPlane: mt.Plane)),
            Transformation.Translation t => ResultFactory.Create(value: Transform.Translation(motion: t.Motion)),
            Transformation.ShearTransform sh => ValidateShear(plane: sh.Plane, direction: sh.Direction, context: context)
                .Map(_ => Transform.Shear(plane: sh.Plane, x: sh.Direction * Math.Tan(sh.AngleRadians), y: Vector3d.Zero, z: Vector3d.Zero)),
            Transformation.ProjectionTransform p => ValidatePlane(plane: p.Plane)
                .Map(_ => Transform.PlanarProjection(plane: p.Plane)),
            Transformation.BasisChange cb => ValidatePlanes(from: cb.From, to: cb.To)
                .Map(_ => Transform.ChangeBasis(plane0: cb.From, plane1: cb.To)),
            Transformation.PlaneTransform ptp => ValidatePlanes(from: ptp.From, to: ptp.To)
                .Map(_ => Transform.PlaneToPlane(ptp.From, ptp.To)),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformSpec),
        };

    /// <summary>Validate transform matrix determinant.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> ValidateMatrix(Transform matrix, IGeometryContext context) =>
        matrix.IsValid && Math.Abs(matrix.Determinant) > context.AbsoluteTolerance
            ? ResultFactory.Create(value: matrix)
            : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformMatrix.WithContext($"Valid: {matrix.IsValid}, Det: {matrix.Determinant.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary>Validate scale factor bounds.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double> ValidateScaleFactor(double factor) =>
        factor is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
            ? ResultFactory.Create(value: factor)
            : ResultFactory.Create<double>(error: E.Geometry.Transformation.InvalidScaleFactor.WithContext($"Factor: {factor.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary>Validate all three scale factors.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(double, double, double)> ValidateScaleFactors(double x, double y, double z) =>
        x is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
        && y is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
        && z is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
            ? ResultFactory.Create(value: (x, y, z))
            : ResultFactory.Create<(double, double, double)>(error: E.Geometry.Transformation.InvalidScaleFactor.WithContext($"X: {x.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Y: {y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Z: {z.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary>Validate rotation axis is non-degenerate.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Vector3d> ValidateAxis(Vector3d axis, IGeometryContext context) =>
        axis.Length > context.AbsoluteTolerance
            ? ResultFactory.Create(value: axis)
            : ResultFactory.Create<Vector3d>(error: E.Geometry.Transformation.InvalidRotationAxis.WithContext($"Length: {axis.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary>Validate rotation vectors are non-degenerate.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Vector3d, Vector3d)> ValidateRotationVectors(Vector3d start, Vector3d end, IGeometryContext context) =>
        start.Length > context.AbsoluteTolerance && end.Length > context.AbsoluteTolerance
            ? ResultFactory.Create(value: (start, end))
            : ResultFactory.Create<(Vector3d, Vector3d)>(error: E.Geometry.Transformation.InvalidRotationAxis.WithContext($"Start: {start.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, End: {end.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary>Validate plane is valid.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Plane> ValidatePlane(Plane plane) =>
        plane.IsValid
            ? ResultFactory.Create(value: plane)
            : ResultFactory.Create<Plane>(error: E.Geometry.Transformation.InvalidMirrorPlane);

    /// <summary>Validate both planes are valid.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Plane, Plane)> ValidatePlanes(Plane from, Plane to) =>
        from.IsValid && to.IsValid
            ? ResultFactory.Create(value: (from, to))
            : ResultFactory.Create<(Plane, Plane)>(error: E.Geometry.Transformation.InvalidBasisPlanes.WithContext($"From: {from.IsValid}, To: {to.IsValid}"));

    /// <summary>Validate shear parameters.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(Plane, Vector3d)> ValidateShear(Plane plane, Vector3d direction, IGeometryContext context) =>
        plane.IsValid
        && direction.Length > context.AbsoluteTolerance
        && plane.ZAxis.IsParallelTo(direction, context.AngleToleranceRadians * TransformationConfig.AngleToleranceMultiplier) == 0
            ? ResultFactory.Create(value: (plane, direction))
            : ResultFactory.Create<(Plane, Vector3d)>(error: E.Geometry.Transformation.InvalidShearParameters.WithContext($"Plane: {plane.IsValid}, Dir: {direction.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary>Apply transform to geometry with Extrusion conversion.</summary>
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

    /// <summary>Generate rectangular grid array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> RectangularArray<T>(
        T geometry,
        Transformation.RectangularArray operation,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        int totalCount = operation.XCount * operation.YCount * operation.ZCount;
        return operation.XCount <= 0 || operation.YCount <= 0 || operation.ZCount <= 0
            || totalCount > meta.MaxCount
            || Math.Abs(operation.XSpacing) <= context.AbsoluteTolerance
            || Math.Abs(operation.YSpacing) <= context.AbsoluteTolerance
            || (operation.ZCount > 1 && Math.Abs(operation.ZSpacing) <= context.AbsoluteTolerance)
            ? ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"XCount: {operation.XCount}, YCount: {operation.YCount}, ZCount: {operation.ZCount}, Total: {totalCount}")))
            : RectangularArrayCore(
                geometry: geometry,
                operation: operation,
                totalCount: totalCount,
                meta: meta,
                context: context,
                enableDiagnostics: enableDiagnostics);
    }

    /// <summary>Core rectangular array implementation after validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> RectangularArrayCore<T>(
        T geometry,
        Transformation.RectangularArray operation,
        int totalCount,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        Transform[] transforms = new Transform[totalCount];
        int index = 0;

        for (int i = 0; i < operation.XCount; i++) {
            double dx = i * operation.XSpacing;
            for (int j = 0; j < operation.YCount; j++) {
                double dy = j * operation.YSpacing;
                for (int k = 0; k < operation.ZCount; k++) {
                    double dz = k * operation.ZSpacing;
                    transforms[index++] = Transform.Translation(dx: dx, dy: dy, dz: dz);
                }
            }
        }

        return UnifiedOperation.Apply(
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
    }

    /// <summary>Generate polar array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> PolarArray<T>(
        T geometry,
        Transformation.PolarArray operation,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        operation.Count <= 0 || operation.Count > meta.MaxCount
            || operation.Axis.Length <= context.AbsoluteTolerance
            || operation.TotalAngleRadians <= 0.0 || operation.TotalAngleRadians > RhinoMath.TwoPI
            ? ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Count: {operation.Count}, Axis: {operation.Axis.Length.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Angle: {operation.TotalAngleRadians.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}")))
            : PolarArrayCore(
                geometry: geometry,
                operation: operation,
                meta: meta,
                context: context,
                enableDiagnostics: enableDiagnostics);

    /// <summary>Core polar array implementation after validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> PolarArrayCore<T>(
        T geometry,
        Transformation.PolarArray operation,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        Transform[] transforms = new Transform[operation.Count];
        double angleStep = operation.TotalAngleRadians / operation.Count;

        for (int i = 0; i < operation.Count; i++) {
            transforms[i] = Transform.Rotation(
                angleRadians: angleStep * i,
                rotationAxis: operation.Axis,
                rotationCenter: operation.Center);
        }

        return UnifiedOperation.Apply(
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
    }

    /// <summary>Generate linear array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> LinearArray<T>(
        T geometry,
        Transformation.LinearArray operation,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        double dirLength = operation.Direction.Length;
        return operation.Count <= 0 || operation.Count > meta.MaxCount
            || dirLength <= context.AbsoluteTolerance
            || Math.Abs(operation.Spacing) <= context.AbsoluteTolerance
            ? ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Count: {operation.Count}, Direction: {dirLength.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Spacing: {operation.Spacing.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}")))
            : LinearArrayCore(
                geometry: geometry,
                operation: operation,
                dirLength: dirLength,
                meta: meta,
                context: context,
                enableDiagnostics: enableDiagnostics);
    }

    /// <summary>Core linear array implementation after validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> LinearArrayCore<T>(
        T geometry,
        Transformation.LinearArray operation,
        double dirLength,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        Transform[] transforms = new Transform[operation.Count];
        Vector3d step = (operation.Direction / dirLength) * operation.Spacing;

        for (int i = 0; i < operation.Count; i++) {
            transforms[i] = Transform.Translation(step * i);
        }

        return UnifiedOperation.Apply(
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
    }
}
