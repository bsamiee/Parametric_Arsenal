using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transform;

/// <summary>Transform matrix construction, validation, and application with disposal patterns.</summary>
internal static class TransformCore {
    /// <summary>Build transform matrix from specification via pattern matching dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<global::Rhino.Geometry.Transform> BuildTransform(
        Transform.TransformSpec spec,
        IGeometryContext context) =>
        spec switch {
            { Matrix: global::Rhino.Geometry.Transform m } =>
                ValidateTransform(transform: m, context: context),

            { UniformScale: (Point3d anchor, double factor) } =>
                factor >= TransformConfig.MinScaleFactor && factor <= TransformConfig.MaxScaleFactor
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.Scale(anchor, factor))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidScaleFactor.WithContext($"Factor: {factor:F6}")),

            { NonUniformScale: (Plane plane, double x, double y, double z) } =>
                plane.IsValid
                && x >= TransformConfig.MinScaleFactor && x <= TransformConfig.MaxScaleFactor
                && y >= TransformConfig.MinScaleFactor && y <= TransformConfig.MaxScaleFactor
                && z >= TransformConfig.MinScaleFactor && z <= TransformConfig.MaxScaleFactor
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.Scale(plane, x, y, z))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidScaleFactor.WithContext($"Plane valid: {plane.IsValid}, X: {x:F6}, Y: {y:F6}, Z: {z:F6}")),

            { Rotation: (double angle, Vector3d axis, Point3d center) } =>
                axis.Length > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.Rotation(angle, axis, center))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidRotationAxis.WithContext($"Axis length: {axis.Length:F6}")),

            { RotationVectors: (Vector3d start, Vector3d end, Point3d center) } =>
                start.Length > context.AbsoluteTolerance && end.Length > context.AbsoluteTolerance
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.Rotation(start, end, center))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidRotationAxis.WithContext($"Start: {start.Length:F6}, End: {end.Length:F6}")),

            { MirrorPlane: Plane plane } =>
                plane.IsValid
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.Mirror(plane))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidMirrorPlane),

            { Translation: Vector3d motion } =>
                ResultFactory.Create(value: global::Rhino.Geometry.Transform.Translation(motion)),

            { Shear: (Plane plane, Vector3d direction, double angle) } =>
                plane.IsValid && direction.Length > context.AbsoluteTolerance
                && !plane.ZAxis.IsParallelTo(direction, context.AngleTolerance * TransformConfig.AngleToleranceMultiplier)
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.Shear(plane, direction, angle))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidShearParameters.WithContext($"Plane: {plane.IsValid}, Direction: {direction.Length:F6}")),

            { ProjectionPlane: Plane plane } =>
                plane.IsValid
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.PlanarProjection(plane))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidProjectionPlane),

            { ChangeBasis: (Plane from, Plane to) } =>
                from.IsValid && to.IsValid
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.ChangeBasis(from, to))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidBasisPlanes.WithContext($"From: {from.IsValid}, To: {to.IsValid}")),

            { PlaneToPlane: (Plane from, Plane to) } =>
                from.IsValid && to.IsValid
                    ? ResultFactory.Create(value: global::Rhino.Geometry.Transform.PlaneToPlane(from, to))
                    : ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidBasisPlanes.WithContext($"From: {from.IsValid}, To: {to.IsValid}")),

            _ => ResultFactory.Create<global::Rhino.Geometry.Transform>(error: E.Transform.InvalidTransformSpec),
        };

    /// <summary>Validate transform matrix for validity and non-singularity.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<global::Rhino.Geometry.Transform> ValidateTransform(
        global::Rhino.Geometry.Transform transform,
        IGeometryContext context) =>
        transform.IsValid && Math.Abs(transform.Determinant) > context.AbsoluteTolerance
            ? ResultFactory.Create(value: transform)
            : ResultFactory.Create<global::Rhino.Geometry.Transform>(
                error: E.Transform.InvalidTransformMatrix.WithContext($"Valid: {transform.IsValid}, Det: {transform.Determinant:F6}"));

    /// <summary>Apply transform to geometry with Extrusion conversion and disposal.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(
        T item,
        global::Rhino.Geometry.Transform transform,
        IGeometryContext context) where T : GeometryBase {
        (GeometryBase normalized, bool shouldDispose) = item switch {
            Extrusion ext => (ext.ToBrep(splitKinkyFaces: true), true),
            GeometryBase g => (g, false),
        };

        try {
            T duplicate = (T)normalized.Duplicate();
            return duplicate.Transform(transform)
                ? ResultFactory.Create<IReadOnlyList<T>>(value: [duplicate,])
                : ResultFactory.Create<IReadOnlyList<T>>(error: E.Transform.TransformApplicationFailed);
        } finally {
            (shouldDispose ? normalized as IDisposable : null)?.Dispose();
        }
    }

    /// <summary>Generate rectangular grid array transforms via nested loops.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> RectangularArray<T>(
        T geometry,
        int xCount,
        int yCount,
        int zCount,
        double xSpacing,
        double ySpacing,
        double zSpacing,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        xCount > 0 && yCount > 0 && zCount > 0
        && xCount * yCount * zCount <= TransformConfig.MaxArrayCount
        && Math.Abs(xSpacing) > context.AbsoluteTolerance
        && Math.Abs(ySpacing) > context.AbsoluteTolerance
        && (zCount is 1 || Math.Abs(zSpacing) > context.AbsoluteTolerance)
            ? ((Func<Result<IReadOnlyList<T>>>)(() => {
                int totalCount = xCount * yCount * zCount;
                global::Rhino.Geometry.Transform[] transforms = new global::Rhino.Geometry.Transform[totalCount];
                int index = 0;
                
                for (int i = 0; i < xCount; i++) {
                    double dx = i * xSpacing;
                    for (int j = 0; j < yCount; j++) {
                        double dy = j * ySpacing;
                        for (int k = 0; k < zCount; k++) {
                            double dz = k * zSpacing;
                            transforms[index++] = global::Rhino.Geometry.Transform.Translation(dx: dx, dy: dy, dz: dz);
                        }
                    }
                }

                return UnifiedOperation.Apply(
                    input: transforms,
                    operation: (Func<global::Rhino.Geometry.Transform, Result<IReadOnlyList<T>>>)(xform =>
                        ApplyTransform(item: geometry, transform: xform, context: context)),
                    config: new OperationConfig<global::Rhino.Geometry.Transform, T> {
                        Context = context,
                        ValidationMode = V.None,
                        AccumulateErrors = false,
                        OperationName = "Transform.RectangularArray",
                        EnableDiagnostics = enableDiagnostics,
                    }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]);
            }))()
            : ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Transform.InvalidArrayParameters.WithContext($"XCount: {xCount}, YCount: {yCount}, ZCount: {zCount}, Total: {xCount * yCount * zCount}"));

    /// <summary>Generate polar array transforms via angular stepping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> PolarArray<T>(
        T geometry,
        Point3d center,
        Vector3d axis,
        int count,
        double totalAngle,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        count > 0 && count <= TransformConfig.MaxArrayCount
        && axis.Length > context.AbsoluteTolerance
        && totalAngle > 0.0 && totalAngle <= RhinoMath.TwoPI
            ? ((Func<Result<IReadOnlyList<T>>>)(() => {
                global::Rhino.Geometry.Transform[] transforms = new global::Rhino.Geometry.Transform[count];
                double angleStep = totalAngle / count;

                for (int i = 0; i < count; i++) {
                    double angle = angleStep * i;
                    transforms[i] = global::Rhino.Geometry.Transform.Rotation(
                        angleRadians: angle,
                        rotationAxis: axis,
                        rotationCenter: center);
                }

                return UnifiedOperation.Apply(
                    input: transforms,
                    operation: (Func<global::Rhino.Geometry.Transform, Result<IReadOnlyList<T>>>)(xform =>
                        ApplyTransform(item: geometry, transform: xform, context: context)),
                    config: new OperationConfig<global::Rhino.Geometry.Transform, T> {
                        Context = context,
                        ValidationMode = V.None,
                        AccumulateErrors = false,
                        OperationName = "Transform.PolarArray",
                        EnableDiagnostics = enableDiagnostics,
                    }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]);
            }))()
            : ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Transform.InvalidArrayParameters.WithContext($"Count: {count}, Axis: {axis.Length:F6}, Angle: {totalAngle:F6}"));

    /// <summary>Generate linear array transforms via directional stepping.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> LinearArray<T>(
        T geometry,
        Vector3d direction,
        int count,
        double spacing,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        count > 0 && count <= TransformConfig.MaxArrayCount
        && direction.Length > context.AbsoluteTolerance
        && Math.Abs(spacing) > context.AbsoluteTolerance
            ? ((Func<Result<IReadOnlyList<T>>>)(() => {
                global::Rhino.Geometry.Transform[] transforms = new global::Rhino.Geometry.Transform[count];
                Vector3d unitDirection = direction / direction.Length;

                for (int i = 0; i < count; i++) {
                    Vector3d motion = unitDirection * (spacing * i);
                    transforms[i] = global::Rhino.Geometry.Transform.Translation(motion);
                }

                return UnifiedOperation.Apply(
                    input: transforms,
                    operation: (Func<global::Rhino.Geometry.Transform, Result<IReadOnlyList<T>>>)(xform =>
                        ApplyTransform(item: geometry, transform: xform, context: context)),
                    config: new OperationConfig<global::Rhino.Geometry.Transform, T> {
                        Context = context,
                        ValidationMode = V.None,
                        AccumulateErrors = false,
                        OperationName = "Transform.LinearArray",
                        EnableDiagnostics = enableDiagnostics,
                    }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]);
            }))()
            : ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Transform.InvalidArrayParameters.WithContext($"Count: {count}, Direction: {direction.Length:F6}, Spacing: {spacing:F6}"));
}
