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

namespace Arsenal.Rhino.Transformation;

/// <summary>Transform matrix construction, validation, and application.</summary>
internal static class TransformCore {
    private const string DoubleFormat = "F6";

    private static string Fmt(double value) => value.ToString(DoubleFormat, System.Globalization.CultureInfo.InvariantCulture);

    private static readonly FrozenDictionary<byte, (Func<Transforms.TransformSpec, IGeometryContext, (bool Valid, string Context)> Validate, Func<Transforms.TransformSpec, Transform> Build, SystemError Error)> _builders =
        new Dictionary<byte, (Func<Transforms.TransformSpec, IGeometryContext, (bool, string)>, Func<Transforms.TransformSpec, Transform>, SystemError)> {
            [1] = ((s, c) => (s.Matrix is Transform m && m.IsValid && Math.Abs(m.Determinant) > c.AbsoluteTolerance, $"Valid: {s.Matrix?.IsValid ?? false}, Det: {Fmt(s.Matrix?.Determinant ?? 0)}"),
                s => s.Matrix!.Value, E.Geometry.Transformation.InvalidTransformMatrix),
            [2] = ((s, _) => (s.UniformScale is (Point3d, double f) && f is >= TransformConfig.MinScaleFactor and <= TransformConfig.MaxScaleFactor, $"Factor: {Fmt(s.UniformScale?.Factor ?? 0)}"),
                s => Transform.Scale(s.UniformScale!.Value.Anchor, s.UniformScale.Value.Factor), E.Geometry.Transformation.InvalidScaleFactor),
            [3] = ((s, _) => (s.NonUniformScale is (Plane p, double x, double y, double z) && p.IsValid && x is >= TransformConfig.MinScaleFactor and <= TransformConfig.MaxScaleFactor && y is >= TransformConfig.MinScaleFactor and <= TransformConfig.MaxScaleFactor && z is >= TransformConfig.MinScaleFactor and <= TransformConfig.MaxScaleFactor, $"Plane: {s.NonUniformScale?.Plane.IsValid ?? false}, X: {Fmt(s.NonUniformScale?.X ?? 0)}, Y: {Fmt(s.NonUniformScale?.Y ?? 0)}, Z: {Fmt(s.NonUniformScale?.Z ?? 0)}"),
                s => Transform.Scale(s.NonUniformScale!.Value.Plane, s.NonUniformScale.Value.X, s.NonUniformScale.Value.Y, s.NonUniformScale.Value.Z), E.Geometry.Transformation.InvalidScaleFactor),
            [4] = ((s, c) => (s.Rotation is (double, Vector3d a, Point3d) && a.Length > c.AbsoluteTolerance, $"Axis: {Fmt(s.Rotation?.Axis.Length ?? 0)}"),
                s => Transform.Rotation(s.Rotation!.Value.Angle, s.Rotation.Value.Axis, s.Rotation.Value.Center), E.Geometry.Transformation.InvalidRotationAxis),
            [5] = ((s, c) => (s.RotationVectors is (Vector3d st, Vector3d en, Point3d) && st.Length > c.AbsoluteTolerance && en.Length > c.AbsoluteTolerance, $"Start: {Fmt(s.RotationVectors?.Start.Length ?? 0)}, End: {Fmt(s.RotationVectors?.End.Length ?? 0)}"),
                s => Transform.Rotation(s.RotationVectors!.Value.Start, s.RotationVectors.Value.End, s.RotationVectors.Value.Center), E.Geometry.Transformation.InvalidRotationAxis),
            [6] = ((s, _) => (s.MirrorPlane is Plane p && p.IsValid, string.Empty),
                s => Transform.Mirror(s.MirrorPlane!.Value), E.Geometry.Transformation.InvalidMirrorPlane),
            [7] = ((s, _) => (s.Translation is Vector3d, string.Empty),
                s => Transform.Translation(s.Translation!.Value), E.Geometry.Transformation.InvalidTransformSpec),
            [8] = ((s, c) => (s.Shear is (Plane p, Vector3d d, double) && p.IsValid && d.Length > c.AbsoluteTolerance && p.ZAxis.IsParallelTo(d, c.AngleToleranceRadians * TransformConfig.AngleToleranceMultiplier) == 0, $"Plane: {s.Shear?.Plane.IsValid ?? false}, Dir: {Fmt(s.Shear?.Direction.Length ?? 0)}"),
                s => Transform.Shear(s.Shear!.Value.Plane, s.Shear.Value.Direction * Math.Tan(s.Shear.Value.Angle), Vector3d.Zero, Vector3d.Zero), E.Geometry.Transformation.InvalidShearParameters),
            [9] = ((s, _) => (s.ProjectionPlane is Plane p && p.IsValid, string.Empty),
                s => Transform.PlanarProjection(s.ProjectionPlane!.Value), E.Geometry.Transformation.InvalidProjectionPlane),
            [10] = ((s, _) => (s.ChangeBasis is (Plane f, Plane t) && f.IsValid && t.IsValid, $"From: {s.ChangeBasis?.From.IsValid ?? false}, To: {s.ChangeBasis?.To.IsValid ?? false}"),
                s => Transform.ChangeBasis(s.ChangeBasis!.Value.From, s.ChangeBasis.Value.To), E.Geometry.Transformation.InvalidBasisPlanes),
            [11] = ((s, _) => (s.PlaneToPlane is (Plane f, Plane t) && f.IsValid && t.IsValid, $"From: {s.PlaneToPlane?.From.IsValid ?? false}, To: {s.PlaneToPlane?.To.IsValid ?? false}"),
                s => Transform.PlaneToPlane(s.PlaneToPlane!.Value.From, s.PlaneToPlane.Value.To), E.Geometry.Transformation.InvalidBasisPlanes),
        }.ToFrozenDictionary();

    private static byte DetectMode(Transforms.TransformSpec spec) =>
        spec switch {
            { Matrix: not null } => 1,
            { UniformScale: not null } => 2,
            { NonUniformScale: not null } => 3,
            { Rotation: not null } => 4,
            { RotationVectors: not null } => 5,
            { MirrorPlane: not null } => 6,
            { Translation: not null } => 7,
            { Shear: not null } => 8,
            { ProjectionPlane: not null } => 9,
            { ChangeBasis: not null } => 10,
            { PlaneToPlane: not null } => 11,
            _ => 0,
        };

    /// <summary>Build transform matrix from specification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> BuildTransform(
        Transforms.TransformSpec spec,
        IGeometryContext context) =>
        DetectMode(spec) is byte mode and > 0 && _builders.TryGetValue(mode, out (Func<Transforms.TransformSpec, IGeometryContext, (bool Valid, string Context)> validate, Func<Transforms.TransformSpec, Transform> build, SystemError error) entry)
            ? entry.validate(spec, context) is (true, _)
                ? ResultFactory.Create(value: entry.build(spec))
                : ResultFactory.Create<Transform>(error: entry.error.WithContext(entry.validate(spec, context).Context))
            : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformSpec);

    /// <summary>Apply transform to geometry with Extrusion conversion.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> ApplyTransform<T>(
        T item,
        Transform transform) where T : GeometryBase {
        bool isExtrusion = item is Extrusion;
        GeometryBase normalized = isExtrusion ? ((Extrusion)(object)item).ToBrep(splitKinkyFaces: true) : item;
        bool shouldDispose = isExtrusion;

        try {
            T duplicate = (T)normalized.Duplicate();
            return duplicate.Transform(transform)
                ? ResultFactory.Create<IReadOnlyList<T>>(value: [duplicate,])
                : ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.TransformApplicationFailed);
        } finally {
            if (shouldDispose) {
                (normalized as IDisposable)?.Dispose();
            }
        }
    }

    /// <summary>Generate rectangular grid array transforms.</summary>
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
        bool enableDiagnostics) where T : GeometryBase {
        int totalCount = xCount * yCount * zCount;
        if (xCount <= 0 || yCount <= 0 || zCount <= 0
            || totalCount > TransformConfig.MaxArrayCount
            || Math.Abs(xSpacing) <= context.AbsoluteTolerance
            || Math.Abs(ySpacing) <= context.AbsoluteTolerance
            || (zCount > 1 && Math.Abs(zSpacing) <= context.AbsoluteTolerance)) {
            return ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"XCount: {xCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, YCount: {yCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, ZCount: {zCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, Total: {totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        }

        Transform[] transforms = new Transform[totalCount];
        int index = 0;

        for (int i = 0; i < xCount; i++) {
            double dx = i * xSpacing;
            for (int j = 0; j < yCount; j++) {
                double dy = j * ySpacing;
                for (int k = 0; k < zCount; k++) {
                    double dz = k * zSpacing;
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
                ValidationMode = V.None,
                AccumulateErrors = false,
                OperationName = "Transforms.RectangularArray",
                EnableDiagnostics = enableDiagnostics,
            });
    }

    /// <summary>Generate polar array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> PolarArray<T>(
        T geometry,
        Point3d center,
        Vector3d axis,
        int count,
        double totalAngle,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        if (count <= 0 || count > TransformConfig.MaxArrayCount
            || axis.Length <= context.AbsoluteTolerance
            || totalAngle <= 0.0 || totalAngle > RhinoMath.TwoPI) {
            return ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"Count: {count}, Axis: {Fmt(axis.Length)}, Angle: {Fmt(totalAngle)}"));
        }

        Transform[] transforms = new Transform[count];
        double angleStep = totalAngle / count;

        for (int i = 0; i < count; i++) {
            transforms[i] = Transform.Rotation(
                angleRadians: angleStep * i,
                rotationAxis: axis,
                rotationCenter: center);
        }

        return UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                ApplyTransform(item: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = V.None,
                AccumulateErrors = false,
                OperationName = "Transforms.PolarArray",
                EnableDiagnostics = enableDiagnostics,
            });
    }

    /// <summary>Generate linear array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> LinearArray<T>(
        T geometry,
        Vector3d direction,
        int count,
        double spacing,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        if (count <= 0 || count > TransformConfig.MaxArrayCount
            || direction.Length <= context.AbsoluteTolerance
            || Math.Abs(spacing) <= context.AbsoluteTolerance) {
            return ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext($"Count: {count}, Direction: {Fmt(direction.Length)}, Spacing: {Fmt(spacing)}"));
        }

        Transform[] transforms = new Transform[count];
        Vector3d step = (direction / direction.Length) * spacing;

        for (int i = 0; i < count; i++) {
            transforms[i] = Transform.Translation(step * i);
        }

        return UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                ApplyTransform(item: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = V.None,
                AccumulateErrors = false,
                OperationName = "Transforms.LinearArray",
                EnableDiagnostics = enableDiagnostics,
            });
    }
}
