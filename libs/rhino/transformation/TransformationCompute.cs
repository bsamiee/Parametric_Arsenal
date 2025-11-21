using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Morphs;

namespace Arsenal.Rhino.Transformation;

/// <summary>SpaceMorph deformation operations and curve-based array transformations.</summary>
internal static class TransformationCompute {
    /// <summary>Core morph application after morphability check.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyMorphCore<TMorph, T>(
        TMorph morph,
        T geometry) where TMorph : SpaceMorph where T : GeometryBase {
        T duplicate = (T)geometry.Duplicate();
        bool success = morph.Morph(duplicate);
        Result<T> result = success
            ? ResultFactory.Create(value: duplicate)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.MorphApplicationFailed.WithContext($"Morph type: {typeof(TMorph).Name}"));

        (result.IsSuccess ? null : duplicate)?.Dispose();

        return result;
    }

    /// <summary>Apply SpaceMorph to geometry with duplication.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyMorph<TMorph, T>(
        TMorph morph,
        T geometry) where TMorph : SpaceMorph where T : GeometryBase {
        using (morph as IDisposable) {
            return !SpaceMorph.IsMorphable(geometry)
                ? ResultFactory.Create<T>(error: E.Geometry.Transformation.GeometryNotMorphable.WithContext($"Geometry: {typeof(T).Name}, Morph: {typeof(TMorph).Name}"))
                : ApplyMorphCore(morph: morph, geometry: geometry);
        }
    }

    /// <summary>Execute morph operation on geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> ExecuteMorph<T>(
        T geometry,
        Transformation.MorphOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        !TransformationConfig.MorphOperations.TryGetValue(operation.GetType(), out TransformationConfig.MorphOperationMetadata? meta)
            ? ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation)
            : operation switch {
                Transformation.FlowMorph f => Flow(geometry: geometry, operation: f, meta: meta, context: context),
                Transformation.TwistMorph tw => Twist(geometry: geometry, operation: tw, meta: meta, context: context),
                Transformation.BendMorph b => Bend(geometry: geometry, operation: b, meta: meta, context: context),
                Transformation.TaperMorph ta => Taper(geometry: geometry, operation: ta, meta: meta, context: context),
                Transformation.StretchMorph s => Stretch(geometry: geometry, operation: s, meta: meta, context: context),
                Transformation.SplopMorph sp => Splop(geometry: geometry, operation: sp, meta: meta, context: context),
                Transformation.SporphMorph sr => Sporph(geometry: geometry, operation: sr, meta: meta, context: context),
                Transformation.MaelstromMorph m => Maelstrom(geometry: geometry, operation: m, meta: meta, context: context),
                _ => ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation),
            };

    /// <summary>Flow geometry along base curve to target curve.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Flow<T>(
        T geometry,
        Transformation.FlowMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.BaseCurve.IsValid && operation.TargetCurve.IsValid && geometry.IsValid
            ? ApplyMorph(
                morph: new FlowSpaceMorph(
                    curve0: operation.BaseCurve,
                    curve1: operation.TargetCurve,
                    preventStretching: operation.PreserveStructure) {
                    Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidFlowCurves.WithContext($"Base: {operation.BaseCurve.IsValid}, Target: {operation.TargetCurve.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Sporph geometry from source surface to target surface.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Sporph<T>(
        T geometry,
        Transformation.SporphMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.SourceSurface.IsValid && operation.TargetSurface.IsValid && geometry.IsValid
            ? ApplyMorph(
                morph: new SporphSpaceMorph(
                    surface0: operation.SourceSurface,
                    surface1: operation.TargetSurface) {
                    PreserveStructure = operation.PreserveStructure,
                    Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSporphParameters.WithContext($"Source: {operation.SourceSurface.IsValid}, Target: {operation.TargetSurface.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Twist geometry around axis by angle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Twist<T>(
        T geometry,
        Transformation.TwistMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.Axis.IsValid && Math.Abs(operation.AngleRadians) <= TransformationConfig.MaxTwistAngle && geometry.IsValid
            ? ApplyMorph(
                morph: new TwistSpaceMorph {
                    TwistAxis = operation.Axis,
                    TwistAngleRadians = operation.AngleRadians,
                    InfiniteTwist = operation.Infinite,
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTwistParameters.WithContext($"Axis: {operation.Axis.IsValid}, Angle: {operation.AngleRadians.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Stretch geometry along axis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Stretch<T>(
        T geometry,
        Transformation.StretchMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.Axis.IsValid && geometry.IsValid
            ? ApplyMorph(
                morph: new StretchSpaceMorph(
                    start: operation.Axis.From,
                    end: operation.Axis.To,
                    length: operation.Axis.Length) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidStretchParameters.WithContext($"Axis: {operation.Axis.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Array geometry along path curve with optional orientation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> PathArray<T>(
        T geometry,
        Transformation.PathArray operation,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        operation.Count <= 0 || operation.Count > meta.MaxCount || operation.Path?.IsValid != true || !geometry.IsValid
            ? ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Count: {operation.Count}, Path: {operation.Path?.IsValid ?? false}, Geometry: {geometry.IsValid}")))
            : PathArrayCore(
                geometry: geometry,
                operation: operation,
                meta: meta,
                context: context,
                enableDiagnostics: enableDiagnostics);

    /// <summary>Maelstrom vortex deformation around axis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Maelstrom<T>(
        T geometry,
        Transformation.MaelstromMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.Axis.IsValid && operation.Radius > context.AbsoluteTolerance && geometry.IsValid && Math.Abs(operation.AngleRadians) <= RhinoMath.TwoPI
            ? ApplyMorph(
                morph: new MaelstromSpaceMorph(
                    plane: new Plane(origin: operation.Axis.From, normal: operation.Axis.UnitTangent),
                    radius0: 0.0,
                    radius1: operation.Radius,
                    angle: operation.AngleRadians) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMaelstromParameters.WithContext($"Axis: {operation.Axis.IsValid}, Radius: {operation.Radius.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Splop geometry from base plane to target surface point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Splop<T>(
        T geometry,
        Transformation.SplopMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.BasePlane.IsValid && operation.TargetSurface.IsValid && operation.TargetPoint.IsValid && geometry.IsValid
            ? operation.TargetSurface.ClosestPoint(operation.TargetPoint, out double u, out double v)
                ? ApplyMorph(
                    morph: new SplopSpaceMorph(
                        plane: operation.BasePlane,
                        surface: operation.TargetSurface,
                        surfaceParam: new Point2d(u, v)) {
                        PreserveStructure = false,
                        Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                        QuickPreview = false,
                    },
                    geometry: geometry)
                : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSplopParameters.WithContext("Surface closest point failed"))
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSplopParameters.WithContext($"Plane: {operation.BasePlane.IsValid}, Surface: {operation.TargetSurface.IsValid}, Point: {operation.TargetPoint.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Bend geometry along axis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Bend<T>(
        T geometry,
        Transformation.BendMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.Axis.IsValid && Math.Abs(operation.AngleRadians) <= TransformationConfig.MaxBendAngle && geometry.IsValid
            ? ApplyMorph(
                morph: new BendSpaceMorph(
                    start: operation.Axis.From,
                    end: operation.Axis.To,
                    point: operation.Axis.PointAt(0.5),
                    angle: operation.AngleRadians,
                    straight: false,
                    symmetric: false) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidBendParameters.WithContext($"Axis: {operation.Axis.IsValid}, Angle: {operation.AngleRadians.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Core path array implementation after validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> PathArrayCore<T>(
        T geometry,
        Transformation.PathArray operation,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        double curveLength = operation.Path.GetLength();
        return curveLength <= context.AbsoluteTolerance
            ? ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Count: {operation.Count}, PathLength: {curveLength.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}")))
            : PathArrayTransforms(
                geometry: geometry,
                operation: operation,
                curveLength: curveLength,
                meta: meta,
                context: context,
                enableDiagnostics: enableDiagnostics);
    }

    /// <summary>Taper geometry along axis from start width to end width.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> Taper<T>(
        T geometry,
        Transformation.TaperMorph operation,
        TransformationConfig.MorphOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase =>
        operation.Axis.IsValid
        && operation.StartWidth >= TransformationConfig.MinScaleFactor
        && operation.EndWidth >= TransformationConfig.MinScaleFactor
        && geometry.IsValid
            ? ApplyMorph(
                morph: new TaperSpaceMorph(
                    start: operation.Axis.From,
                    end: operation.Axis.To,
                    startRadius: operation.StartWidth,
                    endRadius: operation.EndWidth,
                    bFlat: false,
                    infiniteTaper: false) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, meta.Tolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTaperParameters.WithContext($"Axis: {operation.Axis.IsValid}, Start: {operation.StartWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, End: {operation.EndWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Generate path array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<T>> PathArrayTransforms<T>(
        T geometry,
        Transformation.PathArray operation,
        double curveLength,
        TransformationConfig.ArrayOperationMetadata meta,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        double[] parameters = operation.Count == 1
            ? [operation.Path.LengthParameter(curveLength * 0.5, out double singleParameter) ? singleParameter : operation.Path.Domain.ParameterAt(0.5),]
            : operation.Path.DivideByCount(operation.Count - 1, includeEnds: true) is double[] divideParams && divideParams.Length == operation.Count
                ? divideParams
                : [.. Enumerable.Range(0, operation.Count)
                    .Select(i => {
                        double targetLength = curveLength * i / (operation.Count - 1);
                        return operation.Path.LengthParameter(targetLength, out double tParam)
                            ? tParam
                            : operation.Path.Domain.ParameterAt(Math.Clamp(targetLength / curveLength, 0.0, 1.0));
                    }),
                ];

        Transform[] transforms = new Transform[operation.Count];
        for (int i = 0; i < operation.Count; i++) {
            double t = parameters[i];
            Point3d pt = operation.Path.PointAt(t);

            transforms[i] = operation.OrientToPath && operation.Path.FrameAt(t, out Plane frame) && frame.IsValid
                ? Transform.PlaneToPlane(Plane.WorldXY, frame)
                : Transform.Translation(pt - Point3d.Origin);
        }

        return UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                TransformationCore.ApplyTransform(item: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                AccumulateErrors = false,
                OperationName = meta.OperationName,
                EnableDiagnostics = enableDiagnostics,
            });
    }

    /// <summary>Compose multiple transforms into single matrix via sequential multiplication.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> ComposeTransforms(
        Transformation.TransformOperation[] operations,
        IGeometryContext context) =>
        operations.Aggregate(
            seed: ResultFactory.Create(value: Transform.Identity),
            func: (accumResult, operation) => accumResult.IsSuccess
                ? TransformationCore.BuildTransformMatrix(operation: operation, context: context)
                    .Map(matrix => accumResult.Value * matrix)
                : accumResult);

    /// <summary>Blend two transforms with TRS decomposition and quaternion interpolation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> BlendTransforms(
        Transform first,
        Transform second,
        double factor,
        IGeometryContext context) =>
        DecomposeTransformInternal(matrix: first, context: context)
            .Bind(firstDecomp => DecomposeTransformInternal(matrix: second, context: context)
                .Bind(secondDecomp => {
                    double t = Math.Clamp(factor, 0.0, 1.0);
                    Vector3d translation = (firstDecomp.Translation * (1.0 - t)) + (secondDecomp.Translation * t);
                    Vector3d scale = (firstDecomp.Scale * (1.0 - t)) + (secondDecomp.Scale * t);
                    Quaternion rotation = Quaternion.Slerp(firstDecomp.Rotation, secondDecomp.Rotation, t);

                    double w = rotation.A;
                    double x = rotation.B;
                    double y = rotation.C;
                    double z = rotation.D;
                    double xx = x * x;
                    double yy = y * y;
                    double zz = z * z;
                    double xy = x * y;
                    double xz = x * z;
                    double yz = y * z;
                    double wx = w * x;
                    double wy = w * y;
                    double wz = w * z;

                    Transform result = Transform.Identity;
                    result.M00 = 1.0 - (2.0 * (yy + zz));
                    result.M01 = 2.0 * (xy - wz);
                    result.M02 = 2.0 * (xz + wy);
                    result.M03 = translation.X;
                    result.M10 = 2.0 * (xy + wz);
                    result.M11 = 1.0 - (2.0 * (xx + zz));
                    result.M12 = 2.0 * (yz - wx);
                    result.M13 = translation.Y;
                    result.M20 = 2.0 * (xz - wy);
                    result.M21 = 2.0 * (yz + wx);
                    result.M22 = 1.0 - (2.0 * (xx + yy));
                    result.M23 = translation.Z;
                    result.M00 *= scale.X;
                    result.M10 *= scale.X;
                    result.M20 *= scale.X;
                    result.M01 *= scale.Y;
                    result.M11 *= scale.Y;
                    result.M21 *= scale.Y;
                    result.M02 *= scale.Z;
                    result.M12 *= scale.Z;
                    result.M22 *= scale.Z;

                    return result.IsValid && result.Determinant > context.AbsoluteTolerance
                        ? ResultFactory.Create(value: result)
                        : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformMatrix.WithContext($"Blend produced invalid matrix, det={result.Determinant.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));
                }));

    /// <summary>Decompose transform matrix into TRS components using polar decomposition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transformation.DecomposedTransform> DecomposeTransform(
        Transform matrix,
        IGeometryContext context) =>
        DecomposeTransformInternal(matrix: matrix, context: context);

    /// <summary>Internal decomposition helper with result type matching internal needs.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transformation.DecomposedTransform> DecomposeTransformInternal(
        Transform matrix,
        IGeometryContext context) {
        return !matrix.IsValid
            ? ResultFactory.Create<Transformation.DecomposedTransform>(error: E.Geometry.Transformation.InvalidTransformMatrix)
            : ExtractTransformComponents(matrix: matrix, context: context);
    }

    /// <summary>Extract TRS components using hybrid polar decomposition: column norm for orthogonal, iterative refinement for shear.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transformation.DecomposedTransform> ExtractTransformComponents(
        Transform matrix,
        IGeometryContext context) {
        Vector3d translation = new(matrix.M03, matrix.M13, matrix.M23);

        double m00 = matrix.M00;
        double m01 = matrix.M01;
        double m02 = matrix.M02;
        double m10 = matrix.M10;
        double m11 = matrix.M11;
        double m12 = matrix.M12;
        double m20 = matrix.M20;
        double m21 = matrix.M21;
        double m22 = matrix.M22;

        double col0DotCol1 = (m00 * m01) + (m10 * m11) + (m20 * m21);
        double col0DotCol2 = (m00 * m02) + (m10 * m12) + (m20 * m22);
        double col1DotCol2 = (m01 * m02) + (m11 * m12) + (m21 * m22);
        double maxCrossTerm = Math.Max(Math.Abs(col0DotCol1), Math.Max(Math.Abs(col0DotCol2), Math.Abs(col1DotCol2)));

        double scaleX = Math.Sqrt((m00 * m00) + (m10 * m10) + (m20 * m20));
        double scaleY = Math.Sqrt((m01 * m01) + (m11 * m11) + (m21 * m21));
        double scaleZ = Math.Sqrt((m02 * m02) + (m12 * m12) + (m22 * m22));
        double minScale = Math.Min(scaleX, Math.Min(scaleY, scaleZ));

        bool hasShear = maxCrossTerm > Math.Max(minScale * TransformationConfig.ShearDetectionThreshold, context.AbsoluteTolerance);

        return hasShear
            ? PolarDecompositionWithShear(m00: m00, m01: m01, m02: m02, m10: m10, m11: m11, m12: m12, m20: m20, m21: m21, m22: m22, translation: translation, context: context)
            : PolarDecompositionOrthogonal(m00: m00, m01: m01, m02: m02, m10: m10, m11: m11, m12: m12, m20: m20, m21: m21, m22: m22, scaleX: scaleX, scaleY: scaleY, scaleZ: scaleZ, translation: translation, context: context);
    }

    /// <summary>Fast column norm decomposition for orthogonal transforms (no significant shear).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transformation.DecomposedTransform> PolarDecompositionOrthogonal(
        double m00, double m01, double m02,
        double m10, double m11, double m12,
        double m20, double m21, double m22,
        double scaleX, double scaleY, double scaleZ,
        Vector3d translation,
        IGeometryContext context) {
        double safeScaleX = scaleX > context.AbsoluteTolerance ? scaleX : 1.0;
        double safeScaleY = scaleY > context.AbsoluteTolerance ? scaleY : 1.0;
        double safeScaleZ = scaleZ > context.AbsoluteTolerance ? scaleZ : 1.0;

        double r00 = scaleX > context.AbsoluteTolerance ? m00 / scaleX : 1.0;
        double r01 = scaleY > context.AbsoluteTolerance ? m01 / scaleY : 0.0;
        double r02 = scaleZ > context.AbsoluteTolerance ? m02 / scaleZ : 0.0;
        double r10 = scaleX > context.AbsoluteTolerance ? m10 / scaleX : 0.0;
        double r11 = scaleY > context.AbsoluteTolerance ? m11 / scaleY : 1.0;
        double r12 = scaleZ > context.AbsoluteTolerance ? m12 / scaleZ : 0.0;
        double r20 = scaleX > context.AbsoluteTolerance ? m20 / scaleX : 0.0;
        double r21 = scaleY > context.AbsoluteTolerance ? m21 / scaleY : 0.0;
        double r22 = scaleZ > context.AbsoluteTolerance ? m22 / scaleZ : 1.0;

        double rotationDeterminant = (r00 * ((r11 * r22) - (r12 * r21))) - (r01 * ((r10 * r22) - (r12 * r20))) + (r02 * ((r10 * r21) - (r11 * r20)));
        bool requiresReflectionFix = rotationDeterminant < 0.0;
        double absScaleX = Math.Abs(safeScaleX);
        double absScaleY = Math.Abs(safeScaleY);
        double absScaleZ = Math.Abs(safeScaleZ);
        int reflectionAxis = !requiresReflectionFix
            ? -1
            : absScaleX <= absScaleY && absScaleX <= absScaleZ
                ? 0
                : absScaleY <= absScaleZ
                    ? 1
                    : 2;

        double signX = reflectionAxis == 0 ? -1.0 : 1.0;
        double signY = reflectionAxis == 1 ? -1.0 : 1.0;
        double signZ = reflectionAxis == 2 ? -1.0 : 1.0;

        Vector3d scale = new(safeScaleX * signX, safeScaleY * signY, safeScaleZ * signZ);

        r00 *= signX;
        r01 *= signY;
        r02 *= signZ;
        r10 *= signX;
        r11 *= signY;
        r12 *= signZ;
        r20 *= signX;
        r21 *= signY;
        r22 *= signZ;

        double trace = r00 + r11 + r22;
        Quaternion rotation = trace > 0.0
            ? QuaternionFromPositiveTrace(r01: r01, r02: r02, r10: r10, r12: r12, r20: r20, r21: r21, trace: trace)
            : QuaternionFromNegativeTrace(r00: r00, r11: r11, r22: r22, r01: r01, r02: r02, r10: r10, r12: r12, r20: r20, r21: r21);

        double dot0 = (r00 * r00) + (r10 * r10) + (r20 * r20);
        double dot1 = (r01 * r01) + (r11 * r11) + (r21 * r21);
        double dot2 = (r02 * r02) + (r12 * r12) + (r22 * r22);
        double cross01 = (r00 * r01) + (r10 * r11) + (r20 * r21);
        double cross02 = (r00 * r02) + (r10 * r12) + (r20 * r22);
        double cross12 = (r01 * r02) + (r11 * r12) + (r21 * r22);

        double orthogonalityError = Math.Max(
            Math.Max(Math.Abs(dot0 - 1.0), Math.Abs(dot1 - 1.0)),
            Math.Max(Math.Abs(dot2 - 1.0), Math.Max(Math.Abs(cross01), Math.Max(Math.Abs(cross02), Math.Abs(cross12)))));

        double w = rotation.A;
        double x = rotation.B;
        double y = rotation.C;
        double z = rotation.D;
        double xx = x * x;
        double yy = y * y;
        double zz = z * z;
        double xy = x * y;
        double xz = x * z;
        double yz = y * z;
        double wx = w * x;
        double wy = w * y;
        double wz = w * z;

        Transform reconstructed = Transform.Identity;
        reconstructed.M00 = (1.0 - (2.0 * (yy + zz))) * scale.X;
        reconstructed.M01 = (2.0 * (xy - wz)) * scale.Y;
        reconstructed.M02 = (2.0 * (xz + wy)) * scale.Z;
        reconstructed.M03 = translation.X;
        reconstructed.M10 = (2.0 * (xy + wz)) * scale.X;
        reconstructed.M11 = (1.0 - (2.0 * (xx + zz))) * scale.Y;
        reconstructed.M12 = (2.0 * (yz - wx)) * scale.Z;
        reconstructed.M13 = translation.Y;
        reconstructed.M20 = (2.0 * (xz - wy)) * scale.X;
        reconstructed.M21 = (2.0 * (yz + wx)) * scale.Y;
        reconstructed.M22 = (1.0 - (2.0 * (xx + yy))) * scale.Z;
        reconstructed.M23 = translation.Z;

        bool hasInverse = reconstructed.TryGetInverse(out Transform inv);
        Transform originalMatrix = Transform.Identity;
        originalMatrix.M00 = m00;
        originalMatrix.M01 = m01;
        originalMatrix.M02 = m02;
        originalMatrix.M03 = translation.X;
        originalMatrix.M10 = m10;
        originalMatrix.M11 = m11;
        originalMatrix.M12 = m12;
        originalMatrix.M13 = translation.Y;
        originalMatrix.M20 = m20;
        originalMatrix.M21 = m21;
        originalMatrix.M22 = m22;
        originalMatrix.M23 = translation.Z;
        Transform residual = originalMatrix * (hasInverse ? inv : Transform.Identity);

        return ResultFactory.Create(value: new Transformation.DecomposedTransform(
            Translation: translation,
            Rotation: rotation,
            Scale: scale,
            Residual: residual,
            IsOrthogonal: orthogonalityError < TransformationConfig.OrthogonalityTolerance,
            OrthogonalityError: orthogonalityError));
    }

    /// <summary>Iterative polar decomposition for transforms with shear using Newton-Schulz algorithm.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transformation.DecomposedTransform> PolarDecompositionWithShear(
        double m00, double m01, double m02,
        double m10, double m11, double m12,
        double m20, double m21, double m22,
        Vector3d translation,
        IGeometryContext context) =>
        Enumerable.Range(0, TransformationConfig.MaxNewtonSchulzIterations).Aggregate(
            seed: (q00: m00, q01: m01, q02: m02, q10: m10, q11: m11, q12: m12, q20: m20, q21: m21, q22: m22, converged: false),
            func: (state, _) => state.converged ? state : IterateNewtonSchulz(state.q00, state.q01, state.q02, state.q10, state.q11, state.q12, state.q20, state.q21, state.q22, context: context),
            resultSelector: final => ExtractScaleAndBuildResult(
                m00: m00, m01: m01, m02: m02, m10: m10, m11: m11, m12: m12, m20: m20, m21: m21, m22: m22,
                q00: final.q00, q01: final.q01, q02: final.q02, q10: final.q10, q11: final.q11, q12: final.q12, q20: final.q20, q21: final.q21, q22: final.q22,
                translation: translation, context: context));

    /// <summary>Single Newton-Schulz iteration: Q_next = 0.5(Q + Q^(-1)).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double q00, double q01, double q02, double q10, double q11, double q12, double q20, double q21, double q22, bool converged) IterateNewtonSchulz(
        double q00, double q01, double q02,
        double q10, double q11, double q12,
        double q20, double q21, double q22,
        IGeometryContext context) {
        double det = (q00 * ((q11 * q22) - (q12 * q21))) - (q01 * ((q10 * q22) - (q12 * q20))) + (q02 * ((q10 * q21) - (q11 * q20)));
        return Math.Abs(det) < context.AbsoluteTolerance
            ? (q00, q01, q02, q10, q11, q12, q20, q21, q22, true)
            : IterateNewtonSchulzCore(q00: q00, q01: q01, q02: q02, q10: q10, q11: q11, q12: q12, q20: q20, q21: q21, q22: q22, det: det, context: context);
    }

    /// <summary>Core Newton-Schulz iteration after determinant check.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double q00, double q01, double q02, double q10, double q11, double q12, double q20, double q21, double q22, bool converged) IterateNewtonSchulzCore(
        double q00, double q01, double q02,
        double q10, double q11, double q12,
        double q20, double q21, double q22,
        double det,
        IGeometryContext context) {
        double invDet = 1.0 / det;
        double inv00 = ((q11 * q22) - (q12 * q21)) * invDet;
        double inv01 = ((q02 * q21) - (q01 * q22)) * invDet;
        double inv02 = ((q01 * q12) - (q02 * q11)) * invDet;
        double inv10 = ((q12 * q20) - (q10 * q22)) * invDet;
        double inv11 = ((q00 * q22) - (q02 * q20)) * invDet;
        double inv12 = ((q02 * q10) - (q00 * q12)) * invDet;
        double inv20 = ((q10 * q21) - (q11 * q20)) * invDet;
        double inv21 = ((q01 * q20) - (q00 * q21)) * invDet;
        double inv22 = ((q00 * q11) - (q01 * q10)) * invDet;

        double next00 = 0.5 * (q00 + inv00);
        double next01 = 0.5 * (q01 + inv01);
        double next02 = 0.5 * (q02 + inv02);
        double next10 = 0.5 * (q10 + inv10);
        double next11 = 0.5 * (q11 + inv11);
        double next12 = 0.5 * (q12 + inv12);
        double next20 = 0.5 * (q20 + inv20);
        double next21 = 0.5 * (q21 + inv21);
        double next22 = 0.5 * (q22 + inv22);

        double maxDiff = Math.Max(
            Math.Max(Math.Abs(next00 - q00), Math.Max(Math.Abs(next01 - q01), Math.Abs(next02 - q02))),
            Math.Max(Math.Max(Math.Abs(next10 - q10), Math.Max(Math.Abs(next11 - q11), Math.Abs(next12 - q12))),
                Math.Max(Math.Abs(next20 - q20), Math.Max(Math.Abs(next21 - q21), Math.Abs(next22 - q22)))));

        return (next00, next01, next02, next10, next11, next12, next20, next21, next22, maxDiff < context.AbsoluteTolerance * TransformationConfig.NewtonSchulzToleranceMultiplier);
    }

    /// <summary>Extract scale from M = Q·S and build final decomposition result.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Transformation.DecomposedTransform> ExtractScaleAndBuildResult(
        double m00, double m01, double m02,
        double m10, double m11, double m12,
        double m20, double m21, double m22,
        double q00, double q01, double q02,
        double q10, double q11, double q12,
        double q20, double q21, double q22,
        Vector3d translation,
        IGeometryContext context) {
        double s00 = (q00 * m00) + (q10 * m10) + (q20 * m20);
        double s11 = (q01 * m01) + (q11 * m11) + (q21 * m21);
        double s22 = (q02 * m02) + (q12 * m12) + (q22 * m22);

        double safeScaleX = Math.Abs(s00) > context.AbsoluteTolerance
            ? s00
            : s00 < 0.0
                ? -1.0
                : 1.0;
        double safeScaleY = Math.Abs(s11) > context.AbsoluteTolerance
            ? s11
            : s11 < 0.0
                ? -1.0
                : 1.0;
        double safeScaleZ = Math.Abs(s22) > context.AbsoluteTolerance
            ? s22
            : s22 < 0.0
                ? -1.0
                : 1.0;

        double r00 = q00;
        double r01 = q01;
        double r02 = q02;
        double r10 = q10;
        double r11 = q11;
        double r12 = q12;
        double r20 = q20;
        double r21 = q21;
        double r22 = q22;

        double rotationDeterminant = (r00 * ((r11 * r22) - (r12 * r21))) - (r01 * ((r10 * r22) - (r12 * r20))) + (r02 * ((r10 * r21) - (r11 * r20)));
        bool requiresReflectionFix = rotationDeterminant < 0.0;
        double absScaleX = Math.Abs(safeScaleX);
        double absScaleY = Math.Abs(safeScaleY);
        double absScaleZ = Math.Abs(safeScaleZ);
        int reflectionAxis = !requiresReflectionFix
            ? -1
            : absScaleX <= absScaleY && absScaleX <= absScaleZ
                ? 0
                : absScaleY <= absScaleZ
                    ? 1
                    : 2;

        double signX = reflectionAxis == 0 ? -1.0 : 1.0;
        double signY = reflectionAxis == 1 ? -1.0 : 1.0;
        double signZ = reflectionAxis == 2 ? -1.0 : 1.0;

        Vector3d scale = new(safeScaleX * signX, safeScaleY * signY, safeScaleZ * signZ);

        r00 *= signX;
        r01 *= signY;
        r02 *= signZ;
        r10 *= signX;
        r11 *= signY;
        r12 *= signZ;
        r20 *= signX;
        r21 *= signY;
        r22 *= signZ;

        double trace = r00 + r11 + r22;
        Quaternion rotation = trace > 0.0
            ? QuaternionFromPositiveTrace(r01: r01, r02: r02, r10: r10, r12: r12, r20: r20, r21: r21, trace: trace)
            : QuaternionFromNegativeTrace(r00: r00, r11: r11, r22: r22, r01: r01, r02: r02, r10: r10, r12: r12, r20: r20, r21: r21);

        double dot0 = (r00 * r00) + (r10 * r10) + (r20 * r20);
        double dot1 = (r01 * r01) + (r11 * r11) + (r21 * r21);
        double dot2 = (r02 * r02) + (r12 * r12) + (r22 * r22);
        double cross01 = (r00 * r01) + (r10 * r11) + (r20 * r21);
        double cross02 = (r00 * r02) + (r10 * r12) + (r20 * r22);
        double cross12 = (r01 * r02) + (r11 * r12) + (r21 * r22);

        double orthogonalityError = Math.Max(
            Math.Max(Math.Abs(dot0 - 1.0), Math.Abs(dot1 - 1.0)),
            Math.Max(Math.Abs(dot2 - 1.0), Math.Max(Math.Abs(cross01), Math.Max(Math.Abs(cross02), Math.Abs(cross12)))));

        double w = rotation.A;
        double x = rotation.B;
        double y = rotation.C;
        double z = rotation.D;
        double xx = x * x;
        double yy = y * y;
        double zz = z * z;
        double xy = x * y;
        double xz = x * z;
        double yz = y * z;
        double wx = w * x;
        double wy = w * y;
        double wz = w * z;

        Transform reconstructed = Transform.Identity;
        reconstructed.M00 = (1.0 - (2.0 * (yy + zz))) * scale.X;
        reconstructed.M01 = (2.0 * (xy - wz)) * scale.Y;
        reconstructed.M02 = (2.0 * (xz + wy)) * scale.Z;
        reconstructed.M03 = translation.X;
        reconstructed.M10 = (2.0 * (xy + wz)) * scale.X;
        reconstructed.M11 = (1.0 - (2.0 * (xx + zz))) * scale.Y;
        reconstructed.M12 = (2.0 * (yz - wx)) * scale.Z;
        reconstructed.M13 = translation.Y;
        reconstructed.M20 = (2.0 * (xz - wy)) * scale.X;
        reconstructed.M21 = (2.0 * (yz + wx)) * scale.Y;
        reconstructed.M22 = (1.0 - (2.0 * (xx + yy))) * scale.Z;
        reconstructed.M23 = translation.Z;

        bool hasInverse = reconstructed.TryGetInverse(out Transform inv);
        Transform originalMatrix = Transform.Identity;
        originalMatrix.M00 = m00;
        originalMatrix.M01 = m01;
        originalMatrix.M02 = m02;
        originalMatrix.M03 = translation.X;
        originalMatrix.M10 = m10;
        originalMatrix.M11 = m11;
        originalMatrix.M12 = m12;
        originalMatrix.M13 = translation.Y;
        originalMatrix.M20 = m20;
        originalMatrix.M21 = m21;
        originalMatrix.M22 = m22;
        originalMatrix.M23 = translation.Z;
        Transform residual = originalMatrix * (hasInverse ? inv : Transform.Identity);

        return ResultFactory.Create(value: new Transformation.DecomposedTransform(
            Translation: translation,
            Rotation: rotation,
            Scale: scale,
            Residual: residual,
            IsOrthogonal: orthogonalityError < TransformationConfig.OrthogonalityTolerance,
            OrthogonalityError: orthogonalityError));
    }

    /// <summary>Construct quaternion from rotation matrix with positive trace.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion QuaternionFromPositiveTrace(
        double r01,
        double r02,
        double r10,
        double r12,
        double r20,
        double r21,
        double trace) {
        double s = Math.Sqrt(trace + 1.0) * 2.0;
        double w = 0.25 * s;
        double x = (r21 - r12) / s;
        double y = (r02 - r20) / s;
        double z = (r10 - r01) / s;
        return new Quaternion(a: w, b: x, c: y, d: z);
    }

    /// <summary>Construct quaternion from rotation matrix with negative trace.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion QuaternionFromNegativeTrace(
        double r00,
        double r11,
        double r22,
        double r01,
        double r02,
        double r10,
        double r12,
        double r20,
        double r21) =>
        r00 > r11 && r00 > r22
            ? QuaternionFromXDominant(r00: r00, r11: r11, r22: r22, r01: r01, r02: r02, r10: r10, r12: r12, r20: r20, r21: r21)
            : r11 > r22
                ? QuaternionFromYDominant(r00: r00, r01: r01, r02: r02, r10: r10, r11: r11, r12: r12, r20: r20, r21: r21, r22: r22)
                : QuaternionFromZDominant(r00: r00, r01: r01, r02: r02, r10: r10, r11: r11, r12: r12, r20: r20, r21: r21, r22: r22);

    /// <summary>Construct quaternion when X component is dominant.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion QuaternionFromXDominant(
        double r00,
        double r11,
        double r22,
        double r01,
        double r02,
        double r10,
        double r12,
        double r20,
        double r21) {
        double s = Math.Sqrt(1.0 + r00 - r11 - r22) * 2.0;
        double w = (r21 - r12) / s;
        double x = 0.25 * s;
        double y = (r01 + r10) / s;
        double z = (r02 + r20) / s;
        return new Quaternion(a: w, b: x, c: y, d: z);
    }

    /// <summary>Construct quaternion when Y component is dominant.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion QuaternionFromYDominant(
        double r00,
        double r01,
        double r02,
        double r10,
        double r11,
        double r12,
        double r20,
        double r21,
        double r22) {
        double s = Math.Sqrt(1.0 + r11 - r00 - r22) * 2.0;
        double w = (r02 - r20) / s;
        double x = (r01 + r10) / s;
        double y = 0.25 * s;
        double z = (r12 + r21) / s;
        return new Quaternion(a: w, b: x, c: y, d: z);
    }

    /// <summary>Construct quaternion when Z component is dominant.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion QuaternionFromZDominant(
        double r00,
        double r01,
        double r02,
        double r10,
        double r11,
        double r12,
        double r20,
        double r21,
        double r22) {
        double s = Math.Sqrt(1.0 + r22 - r00 - r11) * 2.0;
        double w = (r10 - r01) / s;
        double x = (r02 + r20) / s;
        double y = (r12 + r21) / s;
        double z = 0.25 * s;
        return new Quaternion(a: w, b: x, c: y, d: z);
    }
}
