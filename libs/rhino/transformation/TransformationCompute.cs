using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
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
        return morph.Morph(duplicate)
            ? ResultFactory.Create(value: duplicate)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.MorphApplicationFailed.WithContext($"Morph type: {typeof(TMorph).Name}"));
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
                    preventStretching: !operation.PreserveStructure) {
                    PreserveStructure = operation.PreserveStructure,
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
                    length: operation.Axis.Length * 2.0) {
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
                    plane: new Plane(origin: operation.Axis.From, normal: operation.Axis.Direction),
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
                    Vector3d translation = firstDecomp.Translation * (1.0 - t) + secondDecomp.Translation * t;
                    Vector3d scale = firstDecomp.Scale * (1.0 - t) + secondDecomp.Scale * t;
                    Quaternion rotation = Quaternion.Slerp(firstDecomp.Rotation, secondDecomp.Rotation, t);

                    Transform translationMatrix = Transform.Translation(motion: translation);
                    Transform rotationMatrix = Transform.Rotation(quaternion: rotation, rotationCenter: Point3d.Origin);
                    Transform scaleMatrix = Transform.Scale(plane: Plane.WorldXY, xScaleFactor: scale.X, yScaleFactor: scale.Y, zScaleFactor: scale.Z);
                    Transform result = translationMatrix * rotationMatrix * scaleMatrix;

                    return result.IsValid && Math.Abs(result.Determinant) > context.AbsoluteTolerance
                        ? ResultFactory.Create(value: result)
                        : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformMatrix.WithContext($"Blend produced invalid matrix, det={result.Determinant.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"));
                }));

    /// <summary>Interpolate between two transforms using SLERP for rotation and LERP for translation/scale.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Transform> InterpolateTransforms(
        Transform start,
        Transform end,
        double t,
        IGeometryContext context) =>
        DecomposeTransformInternal(matrix: start, context: context)
            .Bind(startDecomp => DecomposeTransformInternal(matrix: end, context: context)
                .Bind(endDecomp => {
                    double clampedT = Math.Clamp(t, 0.0, 1.0);
                    Vector3d translation = startDecomp.Translation * (1.0 - clampedT) + endDecomp.Translation * clampedT;
                    Vector3d scale = startDecomp.Scale * (1.0 - clampedT) + endDecomp.Scale * clampedT;
                    Quaternion rotation = Quaternion.Slerp(startDecomp.Rotation, endDecomp.Rotation, clampedT);

                    Transform translationMatrix = Transform.Translation(motion: translation);
                    Transform rotationMatrix = Transform.Rotation(quaternion: rotation, rotationCenter: Point3d.Origin);
                    Transform scaleMatrix = Transform.Scale(plane: Plane.WorldXY, xScaleFactor: scale.X, yScaleFactor: scale.Y, zScaleFactor: scale.Z);
                    Transform result = translationMatrix * rotationMatrix * scaleMatrix;

                    return result.IsValid && Math.Abs(result.Determinant) > context.AbsoluteTolerance
                        ? ResultFactory.Create(value: result)
                        : ResultFactory.Create<Transform>(error: E.Geometry.Transformation.InvalidTransformMatrix.WithContext("Interpolation produced invalid matrix"));
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

    /// <summary>Extract TRS components using column norm-based polar decomposition.</summary>
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

        double scaleX = Math.Sqrt(m00 * m00 + m10 * m10 + m20 * m20);
        double scaleY = Math.Sqrt(m01 * m01 + m11 * m11 + m21 * m21);
        double scaleZ = Math.Sqrt(m02 * m02 + m12 * m12 + m22 * m22);

        Vector3d scale = new(
            scaleX > context.AbsoluteTolerance ? scaleX : 1.0,
            scaleY > context.AbsoluteTolerance ? scaleY : 1.0,
            scaleZ > context.AbsoluteTolerance ? scaleZ : 1.0);

        double r00 = scaleX > context.AbsoluteTolerance ? m00 / scaleX : 1.0;
        double r01 = scaleY > context.AbsoluteTolerance ? m01 / scaleY : 0.0;
        double r02 = scaleZ > context.AbsoluteTolerance ? m02 / scaleZ : 0.0;
        double r10 = scaleX > context.AbsoluteTolerance ? m10 / scaleX : 0.0;
        double r11 = scaleY > context.AbsoluteTolerance ? m11 / scaleY : 1.0;
        double r12 = scaleZ > context.AbsoluteTolerance ? m12 / scaleZ : 0.0;
        double r20 = scaleX > context.AbsoluteTolerance ? m20 / scaleX : 0.0;
        double r21 = scaleY > context.AbsoluteTolerance ? m21 / scaleY : 0.0;
        double r22 = scaleZ > context.AbsoluteTolerance ? m22 / scaleZ : 1.0;

        double trace = r00 + r11 + r22;
        Quaternion rotation = trace > 0.0
            ? QuaternionFromPositiveTrace(r00: r00, r11: r11, r22: r22, r01: r01, r02: r02, r10: r10, r12: r12, r20: r20, r21: r21, trace: trace)
            : QuaternionFromNegativeTrace(r00: r00, r11: r11, r22: r22, r01: r01, r02: r02, r10: r10, r12: r12, r20: r20, r21: r21);

        double dot0 = r00 * r00 + r10 * r10 + r20 * r20;
        double dot1 = r01 * r01 + r11 * r11 + r21 * r21;
        double dot2 = r02 * r02 + r12 * r12 + r22 * r22;
        double cross01 = r00 * r01 + r10 * r11 + r20 * r21;
        double cross02 = r00 * r02 + r10 * r12 + r20 * r22;
        double cross12 = r01 * r02 + r11 * r12 + r21 * r22;

        double orthogonalityError = Math.Max(
            Math.Max(Math.Abs(dot0 - 1.0), Math.Abs(dot1 - 1.0)),
            Math.Max(Math.Abs(dot2 - 1.0), Math.Max(Math.Abs(cross01), Math.Max(Math.Abs(cross02), Math.Abs(cross12)))));

        bool isOrthogonal = orthogonalityError < TransformationConfig.OrthogonalityTolerance;

        Transform reconstructed = Transform.Translation(motion: translation)
            * Transform.Rotation(quaternion: rotation, rotationCenter: Point3d.Origin)
            * Transform.Scale(plane: Plane.WorldXY, xScaleFactor: scale.X, yScaleFactor: scale.Y, zScaleFactor: scale.Z);

        bool hasInverse = reconstructed.TryGetInverse(out Transform inv);
        Transform residual = matrix * (hasInverse ? inv : Transform.Identity);

        return ResultFactory.Create(value: new Transformation.DecomposedTransform(
            Translation: translation,
            Rotation: rotation,
            Scale: scale,
            Residual: residual,
            IsOrthogonal: isOrthogonal,
            OrthogonalityError: orthogonalityError));
    }

    /// <summary>Construct quaternion from rotation matrix with positive trace.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion QuaternionFromPositiveTrace(
        double r00,
        double r11,
        double r22,
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
