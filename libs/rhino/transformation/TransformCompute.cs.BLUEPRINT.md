# TransformCompute.cs Blueprint

## File Purpose
Advanced transformations: SpaceMorph operations (flow, twist, bend, etc.) and path array with curve frame alignment.

## Complete File Code

```csharp
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
                FlowSpaceMorph morph = new() {
                    BaseCurve = baseCurve,
                    TargetCurve = targetCurve,
                    PreserveStructure = preserveStructure,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidFlowCurves.WithContext($"Base: {baseCurve.IsValid}, Target: {targetCurve.IsValid}, Geometry: {geometry.IsValid}"));

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
            : ResultFactory.Create<T>(error: E.Transform.InvalidTwistParameters.WithContext($"Axis: {axis.IsValid}, Angle: {angleRadians:F6}, Geometry: {geometry.IsValid}"));

    /// <summary>Bend geometry along spine using BendSpaceMorph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Bend<T>(
        T geometry,
        Line spine,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        spine.IsValid && Math.Abs(angle) <= TransformConfig.MaxBendAngle && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                BendSpaceMorph morph = new() {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                morph.SetBendLine(spine.From, spine.To);
                morph.BendAngleRadians = angle;
                morph.Symmetric = false;
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidBendParameters.WithContext($"Spine: {spine.IsValid}, Angle: {angle:F6}, Geometry: {geometry.IsValid}"));

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
                TaperSpaceMorph morph = new() {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                    InfiniteTaper = false,
                    Flat = false,
                };
                morph.SetTaperLine(axis.From, axis.To);
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidTaperParameters.WithContext($"Axis: {axis.IsValid}, Start: {startWidth:F6}, End: {endWidth:F6}, Geometry: {geometry.IsValid}"));

    /// <summary>Stretch geometry along axis using StretchSpaceMorph.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Stretch<T>(
        T geometry,
        Line axis,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                StretchSpaceMorph morph = new() {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidStretchParameters.WithContext($"Axis: {axis.IsValid}, Geometry: {geometry.IsValid}"));

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
                SplopSpaceMorph morph = new() {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                morph.SetBasePlane(basePlane);
                double u = 0.0;
                double v = 0.0;
                return targetSurface.ClosestPoint(targetPoint, out u, out v)
                    ? ((Func<Result<T>>)(() => {
                        morph.SetSurfacePoint(targetSurface, u, v);
                        return ApplyMorph(morph: morph, geometry: geometry);
                    }))()
                    : ResultFactory.Create<T>(error: E.Transform.InvalidSplopParameters.WithContext("Surface closest point failed"));
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidSplopParameters.WithContext($"Plane: {basePlane.IsValid}, Surface: {targetSurface.IsValid}, Point: {targetPoint.IsValid}, Geometry: {geometry.IsValid}"));

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
                SporphSpaceMorph morph = new() {
                    PreserveStructure = preserveStructure,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                morph.SetSourceAndTarget(sourceSurface, targetSurface);
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidSporphParameters.WithContext($"Source: {sourceSurface.IsValid}, Target: {targetSurface.IsValid}, Geometry: {geometry.IsValid}"));

    /// <summary>Maelstrom vortex deformation around axis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Maelstrom<T>(
        T geometry,
        Point3d center,
        Line axis,
        double radius,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        center.IsValid && axis.IsValid && radius > context.AbsoluteTolerance && geometry.IsValid
            ? ((Func<Result<T>>)(() => {
                MaelstromSpaceMorph morph = new() {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                };
                return ApplyMorph(morph: morph, geometry: geometry);
            }))()
            : ResultFactory.Create<T>(error: E.Transform.InvalidMaelstromParameters.WithContext($"Center: {center.IsValid}, Axis: {axis.IsValid}, Radius: {radius:F6}, Geometry: {geometry.IsValid}"));

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
                Rhino.Geometry.Transform[] transforms = new Rhino.Geometry.Transform[count];
                Interval domain = path.Domain;
                double step = count > 1 ? (domain.Max - domain.Min) / (count - 1) : 0.0;

                for (int i = 0; i < count; i++) {
                    double t = count > 1 ? domain.Min + (step * i) : domain.Mid;
                    Point3d pt = path.PointAt(t);

                    transforms[i] = orientToPath && path.FrameAt(t, out Plane frame) && frame.IsValid
                        ? Rhino.Geometry.Transform.PlaneToPlane(Plane.WorldXY, frame)
                        : Rhino.Geometry.Transform.Translation(pt - Point3d.Origin);
                }

                return UnifiedOperation.Apply(
                    input: transforms,
                    operation: (Func<Rhino.Geometry.Transform, Result<IReadOnlyList<T>>>)(xform =>
                        TransformCore.ApplyTransform(item: geometry, transform: xform, context: context)),
                    config: new OperationConfig<Rhino.Geometry.Transform, T> {
                        Context = context,
                        ValidationMode = V.None,
                        AccumulateErrors = false,
                        OperationName = "Transform.PathArray",
                        EnableDiagnostics = enableDiagnostics,
                    }).Map(results => (IReadOnlyList<T>)[.. results.SelectMany(static r => r),]);
            }))()
            : ResultFactory.Create<IReadOnlyList<T>>(
                error: E.Transform.InvalidArrayParameters.WithContext($"Count: {count}, Path: {path?.IsValid ?? false}, Geometry: {geometry.IsValid}"));

    /// <summary>Apply SpaceMorph to geometry with duplication and validation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyMorph<TMorph, T>(
        TMorph morph,
        T geometry) where TMorph : SpaceMorph where T : GeometryBase =>
        morph.IsMorphable(geometry)
            ? ((Func<Result<T>>)(() => {
                T duplicate = (T)geometry.Duplicate();
                return morph.Morph(duplicate)
                    ? ResultFactory.Create(value: duplicate)
                    : ResultFactory.Create<T>(error: E.Transform.MorphApplicationFailed.WithContext($"Morph type: {typeof(TMorph).Name}"));
            }))()
            : ResultFactory.Create<T>(error: E.Transform.GeometryNotMorphable.WithContext($"Geometry: {typeof(T).Name}, Morph: {typeof(TMorph).Name}"));
}
```

## Notes
- All SpaceMorph operations follow same pattern: create morph, configure, apply
- Flow uses FlowSpaceMorph with base and target curves
- Twist uses TwistSpaceMorph with axis and angle validation
- Bend uses BendSpaceMorph with SetBendLine configuration
- Taper uses TaperSpaceMorph with SetTaperLine
- Stretch uses StretchSpaceMorph for directional deformation
- Splop uses SplopSpaceMorph with SetBasePlane and SetSurfacePoint
- Sporph uses SporphSpaceMorph with SetSourceAndTarget
- Maelstrom uses MaelstromSpaceMorph for vortex effect
- PathArray pre-computes transforms along curve with FrameAt
- ApplyMorph generic helper handles IsMorphable check and duplication
- All methods use inline lambda execution pattern
- Comprehensive error context for debugging
- RhinoCommon SDK morph classes used directly
- Tolerance uses max of context and default morph tolerance
- PreserveStructure and QuickPreview configured per operation type
