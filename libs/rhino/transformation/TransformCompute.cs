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
internal static class TransformCompute {
    /// <summary>Flow geometry along base curve to target curve.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Flow<T>(
        T geometry,
        Curve baseCurve,
        Curve targetCurve,
        bool preserveStructure,
        IGeometryContext context) where T : GeometryBase =>
        baseCurve.IsValid && targetCurve.IsValid && geometry.IsValid
            ? ApplyMorph(
                morph: new FlowSpaceMorph(
                    curve0: baseCurve,
                    curve1: targetCurve,
                    preventStretching: !preserveStructure) {
                    PreserveStructure = preserveStructure,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidFlowCurves.WithContext($"Base: {baseCurve.IsValid}, Target: {targetCurve.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Twist geometry around axis by angle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Twist<T>(
        T geometry,
        Line axis,
        double angleRadians,
        bool infinite,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid && Math.Abs(angleRadians) <= TransformConfig.MaxTwistAngle && geometry.IsValid
            ? ApplyMorph(
                morph: new TwistSpaceMorph {
                    TwistAxis = axis,
                    TwistAngleRadians = angleRadians,
                    InfiniteTwist = infinite,
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTwistParameters.WithContext($"Axis: {axis.IsValid}, Angle: {angleRadians.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Bend geometry along spine.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Bend<T>(
        T geometry,
        Line spine,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        spine.IsValid && Math.Abs(angle) <= TransformConfig.MaxBendAngle && geometry.IsValid
            ? ApplyMorph(
                morph: new BendSpaceMorph(
                    start: spine.From,
                    end: spine.To,
                    point: spine.PointAt(0.5),
                    angle: angle,
                    straight: false,
                    symmetric: false) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidBendParameters.WithContext($"Spine: {spine.IsValid}, Angle: {angle.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Taper geometry along axis from start width to end width.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Taper<T>(
        T geometry,
        Line axis,
        double startWidth,
        double endWidth,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid
        && startWidth >= TransformConfig.MinScaleFactor
        && endWidth >= TransformConfig.MinScaleFactor
        && geometry.IsValid
            ? ApplyMorph(
                morph: new TaperSpaceMorph(
                    start: axis.From,
                    end: axis.To,
                    startRadius: startWidth,
                    endRadius: endWidth,
                    bFlat: false,
                    infiniteTaper: false) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTaperParameters.WithContext($"Axis: {axis.IsValid}, Start: {startWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, End: {endWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Stretch geometry along axis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Stretch<T>(
        T geometry,
        Line axis,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid && geometry.IsValid
            ? ApplyMorph(
                morph: new StretchSpaceMorph(
                    start: axis.From,
                    end: axis.To,
                    length: axis.Length * 2.0) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidStretchParameters.WithContext($"Axis: {axis.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Splop geometry from base plane to target surface point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Splop<T>(
        T geometry,
        Plane basePlane,
        Surface targetSurface,
        Point3d targetPoint,
        IGeometryContext context) where T : GeometryBase =>
        basePlane.IsValid && targetSurface.IsValid && targetPoint.IsValid && geometry.IsValid
            ? targetSurface.ClosestPoint(targetPoint, out double u, out double v)
                ? ApplyMorph(
                    morph: new SplopSpaceMorph(
                        plane: basePlane,
                        surface: targetSurface,
                        surfaceParam: new Point2d(u, v)) {
                        PreserveStructure = false,
                        Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                        QuickPreview = false,
                    },
                    geometry: geometry)
                : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSplopParameters.WithContext("Surface closest point failed"))
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSplopParameters.WithContext($"Plane: {basePlane.IsValid}, Surface: {targetSurface.IsValid}, Point: {targetPoint.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Sporph geometry from source surface to target surface.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Sporph<T>(
        T geometry,
        Surface sourceSurface,
        Surface targetSurface,
        bool preserveStructure,
        IGeometryContext context) where T : GeometryBase =>
        sourceSurface.IsValid && targetSurface.IsValid && geometry.IsValid
            ? ApplyMorph(
                morph: new SporphSpaceMorph(
                    surface0: sourceSurface,
                    surface1: targetSurface) {
                    PreserveStructure = preserveStructure,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSporphParameters.WithContext($"Source: {sourceSurface.IsValid}, Target: {targetSurface.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Maelstrom vortex deformation around axis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Maelstrom<T>(
        T geometry,
        Point3d center,
        Line axis,
        double radius,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        center.IsValid && axis.IsValid && radius > context.AbsoluteTolerance && geometry.IsValid && Math.Abs(angle) <= RhinoMath.TwoPI
            ? ApplyMorph(
                morph: new MaelstromSpaceMorph(
                    plane: new Plane(origin: center, normal: axis.Direction),
                    radius0: 0.0,
                    radius1: radius,
                    angle: angle) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMaelstromParameters.WithContext($"Center: {center.IsValid}, Axis: {axis.IsValid}, Radius: {radius.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Array geometry along path curve with optional orientation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> PathArray<T>(
        T geometry,
        Curve path,
        int count,
        bool orientToPath,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase {
        if (count <= 0 || count > TransformConfig.MaxArrayCount || path?.IsValid != true || !geometry.IsValid) {
            return ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Count: {count}, Path: {path?.IsValid ?? false}, Geometry: {geometry.IsValid}")));
        }

        double curveLength = path.GetLength();
        if (curveLength <= context.AbsoluteTolerance) {
            return ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Count: {count}, PathLength: {curveLength.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}")));
        }

        double[] parameters = count == 1
            ? [path.LengthParameter(curveLength * 0.5, out double singleParameter) ? singleParameter : path.Domain.ParameterAt(0.5),]
            : path.DivideByCount(count - 1, includeEnds: true) is double[] divideParams && divideParams.Length == count
                ? divideParams
                : [.. Enumerable.Range(0, count)
                    .Select(i => {
                        double targetLength = curveLength * i / (count - 1);
                        return path.LengthParameter(targetLength, out double tParam)
                            ? tParam
                            : path.Domain.ParameterAt(Math.Clamp(targetLength / curveLength, 0.0, 1.0));
                    }),];

        Transform[] transforms = new Transform[count];
        for (int i = 0; i < count; i++) {
            double t = parameters[i];
            Point3d pt = path.PointAt(t);

            transforms[i] = orientToPath && path.FrameAt(t, out Plane frame) && frame.IsValid
                ? Transform.PlaneToPlane(Plane.WorldXY, frame)
                : Transform.Translation(pt - Point3d.Origin);
        }

        return UnifiedOperation.Apply(
            input: transforms,
            operation: (Func<Transform, Result<IReadOnlyList<T>>>)(xform =>
                TransformCore.ApplyTransform(item: geometry, transform: xform)),
            config: new OperationConfig<IReadOnlyList<Transform>, T> {
                Context = context,
                ValidationMode = V.None,
                AccumulateErrors = false,
                OperationName = "Transforms.PathArray",
                EnableDiagnostics = enableDiagnostics,
            });
    }

    /// <summary>Apply SpaceMorph to geometry with duplication.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyMorph<TMorph, T>(
        TMorph morph,
        T geometry) where TMorph : SpaceMorph where T : GeometryBase {
        using (morph as IDisposable) {
            if (!SpaceMorph.IsMorphable(geometry)) {
                return ResultFactory.Create<T>(error: E.Geometry.Transformation.GeometryNotMorphable.WithContext($"Geometry: {typeof(T).Name}, Morph: {typeof(TMorph).Name}"));
            }

            T duplicate = (T)geometry.Duplicate();
            return morph.Morph(duplicate)
                ? ResultFactory.Create(value: duplicate)
                : ResultFactory.Create<T>(error: E.Geometry.Transformation.MorphApplicationFailed.WithContext($"Morph type: {typeof(TMorph).Name}"));
        }
    }
}
