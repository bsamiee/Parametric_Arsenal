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
    /// <summary>Execute affine transformation operation on geometry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteTransform<T>(T geometry, Transformation.TransformOperation operation, IGeometryContext context) where T : GeometryBase =>
        !TransformationConfig.TransformOperations.TryGetValue(operation.GetType(), out TransformationConfig.TransformOperationMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTransformSpec.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : BuildTransform(operation: operation, context: context)
                .Bind(xform => UnifiedOperation.Apply(
                    input: geometry,
                    operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                        ApplyTransform(item: item, transform: xform)),
                    config: new OperationConfig<T, T> {
                        Context = context,
                        ValidationMode = TransformationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                        OperationName = meta.OperationName,
                    }))
                .Map(static r => r[0]);

    /// <summary>Execute array operation on geometry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ExecuteArray<T>(T geometry, Transformation.ArrayOperation operation, IGeometryContext context) where T : GeometryBase =>
        !TransformationConfig.ArrayOperations.TryGetValue(operation.GetType(), out TransformationConfig.ArrayOperationMetadata? meta)
            ? ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : operation switch {
                Transformation.RectangularArray rect => ExecuteRectangularArray(geometry: geometry, operation: rect, meta: meta, context: context),
                Transformation.PolarArray polar => ExecutePolarArray(geometry: geometry, operation: polar, meta: meta, context: context),
                Transformation.LinearArray linear => ExecuteLinearArray(geometry: geometry, operation: linear, meta: meta, context: context),
                Transformation.PathArray path => ExecutePathArray(geometry: geometry, operation: path, meta: meta, context: context),
                _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode.WithContext($"Unhandled operation: {operation.GetType().Name}")),
            };

    /// <summary>Execute SpaceMorph operation on geometry.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteMorph<T>(T geometry, Transformation.MorphOperation operation, IGeometryContext context) where T : GeometryBase =>
        !TransformationConfig.MorphOperations.TryGetValue(operation.GetType(), out TransformationConfig.MorphOperationMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    operation switch {
                        Transformation.Flow flow => TransformationCompute.Flow(
                            geometry: item,
                            baseCurve: flow.BaseCurve,
                            targetCurve: flow.TargetCurve,
                            preserveStructure: flow.PreserveStructure,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        Transformation.Twist twist => TransformationCompute.Twist(
                            geometry: item,
                            axis: twist.Axis,
                            angleRadians: twist.AngleRadians,
                            infinite: twist.Infinite,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        Transformation.Bend bend => TransformationCompute.Bend(
                            geometry: item,
                            spine: bend.Spine,
                            angle: bend.AngleRadians,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        Transformation.Taper taper => TransformationCompute.Taper(
                            geometry: item,
                            axis: taper.Axis,
                            startWidth: taper.StartWidth,
                            endWidth: taper.EndWidth,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        Transformation.Stretch stretch => TransformationCompute.Stretch(
                            geometry: item,
                            axis: stretch.Axis,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        Transformation.Splop splop => TransformationCompute.Splop(
                            geometry: item,
                            basePlane: splop.BasePlane,
                            targetSurface: splop.TargetSurface,
                            targetPoint: splop.TargetPoint,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        Transformation.Sporph sporph => TransformationCompute.Sporph(
                            geometry: item,
                            sourceSurface: sporph.SourceSurface,
                            targetSurface: sporph.TargetSurface,
                            preserveStructure: sporph.PreserveStructure,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        Transformation.Maelstrom maelstrom => TransformationCompute.Maelstrom(
                            geometry: item,
                            center: maelstrom.Center,
                            axis: new Line(maelstrom.Center, maelstrom.Axis),
                            radius: maelstrom.Radius,
                            angle: maelstrom.AngleRadians,
                            context: context).Map(r => (IReadOnlyList<T>)[r,]),
                        _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidMorphOperation.WithContext($"Unhandled operation: {operation.GetType().Name}")),
                    }),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = TransformationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                    OperationName = meta.OperationName,
                }).Map(static r => r[0]);

    /// <summary>Build Rhino Transform from algebraic operation type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> BuildTransform(Transformation.TransformOperation operation, IGeometryContext context) =>
        operation switch {
            Transformation.Matrix m => ValidateMatrix(xform: m.Value, context: context),
            Transformation.UniformScale s => ValidateScale(factor: s.Factor)
                .Map(_ => Transform.Scale(s.Anchor, s.Factor)),
            Transformation.NonUniformScale ns => ValidatePlane(plane: ns.Plane)
                .Bind(_ => ValidateScale(factor: ns.XScale))
                .Bind(_ => ValidateScale(factor: ns.YScale))
                .Bind(_ => ValidateScale(factor: ns.ZScale))
                .Map(_ => Transform.Scale(ns.Plane, ns.XScale, ns.YScale, ns.ZScale)),
            Transformation.Rotation rot => ValidateVector(vector: rot.Axis, context: context, errorContext: "Rotation axis")
                .Map(_ => Transform.Rotation(rot.AngleRadians, rot.Axis, rot.Center)),
            Transformation.RotationVectors rv => ValidateVector(vector: rv.Start, context: context, errorContext: "Start vector")
                .Bind(_ => ValidateVector(vector: rv.End, context: context, errorContext: "End vector"))
                .Map(_ => Transform.Rotation(rv.Start, rv.End, rv.Center)),
            Transformation.Mirror mir => ValidatePlane(plane: mir.ReflectionPlane)
                .Map(_ => Transform.Mirror(mir.ReflectionPlane)),
            Transformation.Translation trans => ResultFactory.Create(value: Transform.Translation(trans.Motion)),
            Transformation.Shear shear => ValidatePlane(plane: shear.Plane)
                .Bind(_ => ValidateVector(vector: shear.Direction, context: context, errorContext: "Shear direction"))
                .Bind(_ => shear.Plane.ZAxis.IsParallelTo(shear.Direction, context.AngleToleranceRadians * TransformationConfig.AngleToleranceMultiplier) == 0
                    ? ResultFactory.Create(value: Transform.Shear(shear.Plane, shear.Direction * Math.Tan(shear.Angle), Vector3d.Zero, Vector3d.Zero))
                    : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidShearParameters.WithContext("Shear direction must not be parallel to plane normal"))),
            Transformation.Projection proj => ValidatePlane(plane: proj.TargetPlane)
                .Map(_ => Transform.PlanarProjection(proj.TargetPlane)),
            Transformation.ChangeBasis cb => ValidatePlane(plane: cb.FromPlane)
                .Bind(_ => ValidatePlane(plane: cb.ToPlane))
                .Map(_ => Transform.ChangeBasis(cb.FromPlane, cb.ToPlane)),
            Transformation.PlaneToPlane ptp => ValidatePlane(plane: ptp.FromPlane)
                .Bind(_ => ValidatePlane(plane: ptp.ToPlane))
                .Map(_ => Transform.PlaneToPlane(ptp.FromPlane, ptp.ToPlane)),
            _ => ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformSpec.WithContext($"Unhandled operation: {operation.GetType().Name}")),
        };

    /// <summary>Apply transform to geometry with Extrusion conversion.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(T item, Transform transform) where T : GeometryBase {
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

    /// <summary>Execute rectangular array operation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ExecuteRectangularArray<T>(T geometry, Transformation.RectangularArray operation, TransformationConfig.ArrayOperationMetadata meta, IGeometryContext context) where T : GeometryBase {
        int totalCount = operation.XCount * operation.YCount * operation.ZCount;
        if (operation.XCount <= 0 || operation.YCount <= 0 || operation.ZCount <= 0
            || totalCount > meta.MaxCount
            || Math.Abs(operation.XSpacing) <= context.AbsoluteTolerance
            || Math.Abs(operation.YSpacing) <= context.AbsoluteTolerance
            || (operation.ZCount > 1 && Math.Abs(operation.ZSpacing) <= context.AbsoluteTolerance)) {
            return ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"XCount: {operation.XCount}, YCount: {operation.YCount}, ZCount: {operation.ZCount}, Total: {totalCount}"));
        }

        Transform[] transforms = new Transform[totalCount];
        int index = 0;
        for (int i = 0; i < operation.XCount; i++) {
            double dx = i * operation.XSpacing;
            for (int j = 0; j < operation.YCount; j++) {
                double dy = j * operation.YSpacing;
                for (int k = 0; k < operation.ZCount; k++) {
                    transforms[index++] = Transform.Translation(dx: dx, dy: dy, dz: k * operation.ZSpacing);
                }
            }
        }

        return UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                ApplyTransform(item: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = TransformationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                OperationName = meta.OperationName,
            });
    }

    /// <summary>Execute polar array operation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ExecutePolarArray<T>(T geometry, Transformation.PolarArray operation, TransformationConfig.ArrayOperationMetadata meta, IGeometryContext context) where T : GeometryBase {
        if (operation.Count <= 0 || operation.Count > meta.MaxCount
            || operation.Axis.Length <= context.AbsoluteTolerance
            || operation.TotalAngleRadians <= 0.0 || operation.TotalAngleRadians > RhinoMath.TwoPI) {
            return ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"Count: {operation.Count}, Axis: {operation.Axis.Length}, Angle: {operation.TotalAngleRadians}"));
        }

        Transform[] transforms = new Transform[operation.Count];
        double angleStep = operation.TotalAngleRadians / operation.Count;
        for (int i = 0; i < operation.Count; i++) {
            transforms[i] = Transform.Rotation(angleRadians: angleStep * i, rotationAxis: operation.Axis, rotationCenter: operation.Center);
        }

        return UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                ApplyTransform(item: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = TransformationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                OperationName = meta.OperationName,
            });
    }

    /// <summary>Execute linear array operation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ExecuteLinearArray<T>(T geometry, Transformation.LinearArray operation, TransformationConfig.ArrayOperationMetadata meta, IGeometryContext context) where T : GeometryBase {
        double dirLength = operation.Direction.Length;
        if (operation.Count <= 0 || operation.Count > meta.MaxCount
            || dirLength <= context.AbsoluteTolerance
            || Math.Abs(operation.Spacing) <= context.AbsoluteTolerance) {
            return ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"Count: {operation.Count}, Direction: {dirLength}, Spacing: {operation.Spacing}"));
        }

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
                ValidationMode = TransformationConfig.GeometryValidation.GetValueOrDefault(typeof(T), meta.ValidationMode),
                OperationName = meta.OperationName,
            });
    }

    /// <summary>Execute path array operation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> ExecutePathArray<T>(T geometry, Transformation.PathArray operation, TransformationConfig.ArrayOperationMetadata meta, IGeometryContext context) where T : GeometryBase =>
        TransformationCompute.PathArray(geometry: geometry, path: operation.PathCurve, count: operation.Count, orientToPath: operation.OrientToPath, context: context, enableDiagnostics: false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transform> ValidateMatrix(Transform xform, IGeometryContext context) =>
        xform.IsValid && Math.Abs(xform.Determinant) > context.AbsoluteTolerance
            ? ResultFactory.Create(value: xform)
            : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformMatrix.WithContext($"Valid: {xform.IsValid}, Det: {xform.Determinant}"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double> ValidateScale(double factor) =>
        factor is >= TransformationConfig.MinScaleFactor and <= TransformationConfig.MaxScaleFactor
            ? ResultFactory.Create(value: factor)
            : ResultFactory.Create<double>(error: E.Geometry.Transformation.InvalidScaleFactor.WithContext($"Factor: {factor}"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Plane> ValidatePlane(Plane plane) =>
        plane.IsValid
            ? ResultFactory.Create(value: plane)
            : ResultFactory.Create<Plane>(error: E.Geometry.Transformation.InvalidBasisPlanes.WithContext("Plane is invalid"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Vector3d> ValidateVector(Vector3d vector, IGeometryContext context, string errorContext) =>
        vector.Length > context.AbsoluteTolerance
            ? ResultFactory.Create(value: vector)
            : ResultFactory.Create<Vector3d>(error: E.Geometry.Transformation.InvalidRotationAxis.WithContext($"{errorContext}: {vector.Length}"));
}
