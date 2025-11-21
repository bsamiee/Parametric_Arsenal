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
}
