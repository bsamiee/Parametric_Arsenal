#pragma warning disable IDISP001, IDISP007 // SpaceMorph objects are disposed by ApplyMorph helper

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
using RhinoTransform = global::Rhino.Geometry.Transform;

namespace Arsenal.Rhino.Transform;

/// <summary>SpaceMorph deformation operations and curve-based array transformations.</summary>
internal static class TransformCompute {
    /// <summary>Flow geometry from base curve to target curve using FlowSpaceMorph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Flow<T>(
        T geometry,
        Curve baseCurve,
        Curve targetCurve,
        bool preserveStructure,
        IGeometryContext context) where T : GeometryBase =>
        baseCurve.IsValid && targetCurve.IsValid && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                FlowSpaceMorph morph = new(
                    curve0: baseCurve,
                    curve1: targetCurve,
                    preventStretching: !preserveStructure) {
                    PreserveStructure = preserveStructure,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidFlowCurves.WithContext($"Base: {baseCurve.IsValid}, Target: {targetCurve.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Twist geometry around axis by angle using TwistSpaceMorph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Twist<T>(
        T geometry,
        Line axis,
        double angleRadians,
        bool infinite,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid && Math.Abs(angleRadians) <= TransformConfig.MaxTwistAngle && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                TwistSpaceMorph morph = new() {
                    TwistAxis = axis,
                    TwistAngleRadians = angleRadians,
                    InfiniteTwist = infinite,
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidTwistParameters.WithContext($"Axis: {axis.IsValid}, Angle: {angleRadians.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Bend geometry along spine using BendSpaceMorph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Bend<T>(
        T geometry,
        Line spine,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        spine.IsValid && Math.Abs(angle) <= TransformConfig.MaxBendAngle && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                BendSpaceMorph morph = new(
                    start: spine.From,
                    end: spine.To,
                    point: spine.From + (spine.Direction * 0.5 * spine.Length),
                    angle: angle,
                    straight: false,
                    symmetric: false) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidBendParameters.WithContext($"Spine: {spine.IsValid}, Angle: {angle.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

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
            ? ((Func<Result<T>>)(() => {
                TaperSpaceMorph morph = new(
                    start: axis.From,
                    end: axis.To,
                    startRadius: startWidth,
                    endRadius: endWidth,
                    bFlat: false,
                    infiniteTaper: false) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidTaperParameters.WithContext($"Axis: {axis.IsValid}, Start: {startWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, End: {endWidth.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Stretch geometry along axis using StretchSpaceMorph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Stretch<T>(
        T geometry,
        Line axis,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                StretchSpaceMorph morph = new(
                    start: axis.From,
                    end: axis.To,
                    length: axis.Length * 2.0) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidStretchParameters.WithContext($"Axis: {axis.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Splop geometry from base plane to point on target surface.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Splop<T>(
        T geometry,
        Plane basePlane,
        Surface targetSurface,
        Point3d targetPoint,
        IGeometryContext context) where T : GeometryBase =>
        basePlane.IsValid && targetSurface.IsValid && targetPoint.IsValid && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                double u = 0.0;
                double v = 0.0;
                return targetSurface.ClosestPoint(targetPoint, out u, out v)
                    ? ((Func<Result<T>>)(() => {
                        SplopSpaceMorph morph = new(
                            plane: basePlane,
                            surface: targetSurface,
                            surfaceParam: new Point2d(u, v)) {
                            PreserveStructure = false,
                            Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                            QuickPreview = false,
                        };
                        return ApplyMorph(morph: morph, geometry: geometry);
                    }))()
                    : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidSplopParameters.WithContext("Surface closest point failed"));
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidSplopParameters.WithContext($"Plane: {basePlane.IsValid}, Surface: {targetSurface.IsValid}, Point: {targetPoint.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Sporph geometry from source surface to target surface.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Sporph<T>(
        T geometry,
        Surface sourceSurface,
        Surface targetSurface,
        bool preserveStructure,
        IGeometryContext context) where T : GeometryBase =>
        sourceSurface.IsValid && targetSurface.IsValid && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                SporphSpaceMorph morph = new(
                    surface0: sourceSurface,
                    surface1: targetSurface) {
                    PreserveStructure = preserveStructure,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidSporphParameters.WithContext($"Source: {sourceSurface.IsValid}, Target: {targetSurface.IsValid}, Geometry: {geometry.IsValid}"));

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
            ? ((Func<Result<T>>)(() => {
                Plane plane = new(origin: center, normal: axis.Direction);
                MaelstromSpaceMorph morph = new(
                    plane: plane,
                    radius0: 0.0,
                    radius1: radius,
                    angle: angle) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.InvalidMaelstromParameters.WithContext($"Center: {center.IsValid}, Axis: {axis.IsValid}, Radius: {radius.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, Geometry: {geometry.IsValid}"));

    /// <summary>Array geometry along path curve with optional frame orientation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<T>> PathArray<T>(
        T geometry,
        Curve path,
        int count,
        bool orientToPath,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        count > 0 && count <= TransformConfig.MaxArrayCount && path.IsValid && geometry.IsValid
            ? ((Func<Result<IReadOnlyList<T>>>)(() => {
                RhinoTransform[] transforms = new RhinoTransform[count];
                Interval domain = path.Domain;
                double step = count > 1 ? (domain.Max - domain.Min) / (count - 1) : 0.0;

                for (int i = 0; i < count; i++) {
                    double t = count > 1 ? domain.Min + (step * i) : domain.Mid;
                    Point3d pt = path.PointAt(t);

                    transforms[i] = orientToPath && path.FrameAt(t, out Plane frame) && frame.IsValid
                        ? RhinoTransform.PlaneToPlane(Plane.WorldXY, frame)
                        : RhinoTransform.Translation(pt - Point3d.Origin);
                }

                return UnifiedOperation.Apply(
                    input: transforms,
                    operation: (Func<RhinoTransform, Result<IReadOnlyList<T>>>)(xform =>
                        TransformCore.ApplyTransform(item: geometry, transform: xform)),
                    config: new OperationConfig<RhinoTransform, T> {
                        Context = context,
                        ValidationMode = V.None,
                        AccumulateErrors = false,
                        OperationName = "Transform.PathArray",
                        EnableDiagnostics = enableDiagnostics,
                    }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]);
            }))()
            : ResultFactory.Create<IReadOnlyList<T>>(
                error: global::Arsenal.Core.Errors.E.Transform.InvalidArrayParameters.WithContext($"Count: {count.ToString(System.Globalization.CultureInfo.InvariantCulture)}, Path: {path?.IsValid ?? false}, Geometry: {geometry.IsValid}"));

    /// <summary>Apply SpaceMorph to geometry with duplication and validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyMorph<TMorph, T>(
        TMorph morph,
        T geometry) where TMorph : SpaceMorph where T : GeometryBase {
        try {
            return SpaceMorph.IsMorphable(geometry)
                ? ((Func<Result<T>>)(() => {
                    T duplicate = (T)geometry.Duplicate();
                    return morph.Morph(duplicate)
                        ? ResultFactory.Create(value: duplicate)
                        : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.MorphApplicationFailed.WithContext($"Morph type: {typeof(TMorph).Name}"));
                }))()
                : ResultFactory.Create<T>(error: global::Arsenal.Core.Errors.E.Transform.GeometryNotMorphable.WithContext($"Geometry: {typeof(T).Name}, Morph: {typeof(TMorph).Name}"));
        } finally {
            (morph as IDisposable)?.Dispose();
        }
    }
}
