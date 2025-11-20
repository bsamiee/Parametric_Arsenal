using System;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Rhino.Geometry;
using Rhino.Geometry.Morphs;

namespace Arsenal.Rhino.Transformation;

/// <summary>SpaceMorph deformation operations and array transform generation.</summary>
[Pure]
internal static class TransformationCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<IReadOnlyList<T>> ApplyTransform<T>(
        T item,
        Transform transform) where T : GeometryBase {
        GeometryBase normalized = item is Extrusion extrusion
            ? extrusion.ToBrep(splitKinkyFaces: true)
            : item;
        T duplicate = (T)normalized.Duplicate();
        bool success = duplicate.Transform(transform);

        (item is Extrusion ? normalized : null)?.Dispose();
        (!success ? duplicate : null)?.Dispose();

        return success
            ? new TransformationConfig.ComputeOutcome<IReadOnlyList<T>>(true, [duplicate,], string.Empty)
            : new TransformationConfig.ComputeOutcome<IReadOnlyList<T>>(false, Array.Empty<T>(), "Transform application failed");
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<Transform[]> RectangularArray(
        Transformation.RectangularArray request,
        IGeometryContext context) {
        _ = context;
        int totalCount = request.XCount * request.YCount * request.ZCount;
        Transform[] transforms = new Transform[totalCount];
        int index = 0;

        for (int i = 0; i < request.XCount; i++) {
            double dx = i * request.XSpacing;
            for (int j = 0; j < request.YCount; j++) {
                double dy = j * request.YSpacing;
                for (int k = 0; k < request.ZCount; k++) {
                    double dz = k * request.ZSpacing;
                    transforms[index++] = Transform.Translation(dx: dx, dy: dy, dz: dz);
                }
            }
        }

        return new TransformationConfig.ComputeOutcome<Transform[]>(true, transforms, string.Empty);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<Transform[]> PolarArray(
        Transformation.PolarArray request,
        IGeometryContext context) {
        _ = context;
        Transform[] transforms = new Transform[request.Count];
        double angleStep = request.TotalAngle / request.Count;

        for (int i = 0; i < request.Count; i++) {
            transforms[i] = Transform.Rotation(
                angleRadians: angleStep * i,
                rotationAxis: request.Axis,
                rotationCenter: request.Center);
        }

        return new TransformationConfig.ComputeOutcome<Transform[]>(true, transforms, string.Empty);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<Transform[]> LinearArray(
        Transformation.LinearArray request,
        IGeometryContext context) {
        _ = context;
        Transform[] transforms = new Transform[request.Count];
        double dirLength = request.Direction.Length;
        Vector3d step = (request.Direction / dirLength) * request.Spacing;

        for (int i = 0; i < request.Count; i++) {
            transforms[i] = Transform.Translation(step * i);
        }

        return new TransformationConfig.ComputeOutcome<Transform[]>(true, transforms, string.Empty);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<Transform[]> PathArray(
        Transformation.PathArray request,
        IGeometryContext context) {
        _ = context;
        double curveLength = request.Path.GetLength();
        double[] parameters = request.Count == 1
            ? [request.Path.LengthParameter(curveLength * 0.5, out double singleParameter) ? singleParameter : request.Path.Domain.ParameterAt(0.5),]
            : request.Path.DivideByCount(request.Count - 1, includeEnds: true) is double[] divideParams && divideParams.Length == request.Count
                ? divideParams
                : [.. Enumerable.Range(0, request.Count)
                    .Select(i => {
                        double targetLength = curveLength * i / (request.Count - 1);
                        return request.Path.LengthParameter(targetLength, out double tParam)
                            ? tParam
                            : request.Path.Domain.ParameterAt(Math.Clamp(targetLength / curveLength, 0.0, 1.0));
                    }),
                ];

        Transform[] transforms = new Transform[request.Count];
        for (int i = 0; i < request.Count; i++) {
            double t = parameters[i];
            Point3d pt = request.Path.PointAt(t);

            transforms[i] = request.OrientToPath && request.Path.FrameAt(t, out Plane frame) && frame.IsValid
                ? Transform.PlaneToPlane(Plane.WorldXY, frame)
                : Transform.Translation(pt - Point3d.Origin);
        }

        return new TransformationConfig.ComputeOutcome<Transform[]>(true, transforms, string.Empty);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Flow(
        GeometryBase geometry,
        Transformation.FlowMorph request,
        IGeometryContext context) =>
        ApplyMorph(
            morph: new FlowSpaceMorph(
                curve0: request.BaseCurve,
                curve1: request.TargetCurve,
                preventStretching: !request.PreserveStructure) {
                PreserveStructure = request.PreserveStructure,
                Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                QuickPreview = false,
            },
            geometry: geometry,
            context: context,
            description: "Flow");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Twist(
        GeometryBase geometry,
        Transformation.TwistMorph request,
        IGeometryContext context) =>
        ApplyMorph(
            morph: new TwistSpaceMorph {
                TwistAxis = request.Axis,
                TwistAngleRadians = request.AngleRadians,
                InfiniteTwist = request.Infinite,
                PreserveStructure = false,
                Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                QuickPreview = false,
            },
            geometry: geometry,
            context: context,
            description: "Twist");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Bend(
        GeometryBase geometry,
        Transformation.BendMorph request,
        IGeometryContext context) =>
        ApplyMorph(
            morph: new BendSpaceMorph(
                start: request.Axis.From,
                end: request.Axis.To,
                point: request.Axis.PointAt(0.5),
                angle: request.AngleRadians,
                straight: false,
                symmetric: false) {
                PreserveStructure = false,
                Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                QuickPreview = false,
            },
            geometry: geometry,
            context: context,
            description: "Bend");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Taper(
        GeometryBase geometry,
        Transformation.TaperMorph request,
        IGeometryContext context) =>
        ApplyMorph(
            morph: new TaperSpaceMorph(
                start: request.Axis.From,
                end: request.Axis.To,
                startRadius: request.StartWidth,
                endRadius: request.EndWidth,
                bFlat: false,
                infiniteTaper: false) {
                PreserveStructure = false,
                Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                QuickPreview = false,
            },
            geometry: geometry,
            context: context,
            description: "Taper");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Stretch(
        GeometryBase geometry,
        Transformation.StretchMorph request,
        IGeometryContext context) =>
        ApplyMorph(
            morph: new StretchSpaceMorph(
                start: request.Axis.From,
                end: request.Axis.To,
                length: request.Axis.Length * 2.0) {
                PreserveStructure = false,
                Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                QuickPreview = false,
            },
            geometry: geometry,
            context: context,
            description: "Stretch");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Splop(
        GeometryBase geometry,
        Transformation.SplopMorph request,
        IGeometryContext context) =>
        request.TargetSurface.ClosestPoint(request.TargetPoint, out double u, out double v)
            ? ApplyMorph(
                morph: new SplopSpaceMorph(
                    plane: request.BasePlane,
                    surface: request.TargetSurface,
                    surfaceParam: new Point2d(u, v)) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry,
                context: context,
                description: "Splop")
            : new TransformationConfig.ComputeOutcome<GeometryBase>(false, geometry, "Surface closest point failed");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Sporph(
        GeometryBase geometry,
        Transformation.SporphMorph request,
        IGeometryContext context) =>
        ApplyMorph(
            morph: new SporphSpaceMorph(
                surface0: request.SourceSurface,
                surface1: request.TargetSurface) {
                PreserveStructure = request.PreserveStructure,
                Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                QuickPreview = false,
            },
            geometry: geometry,
            context: context,
            description: "Sporph");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TransformationConfig.ComputeOutcome<GeometryBase> Maelstrom(
        GeometryBase geometry,
        Transformation.MaelstromMorph request,
        IGeometryContext context) =>
        ApplyMorph(
            morph: new MaelstromSpaceMorph(
                plane: new Plane(origin: request.Center, normal: request.Axis),
                radius0: 0.0,
                radius1: request.Radius,
                angle: request.AngleRadians) {
                PreserveStructure = false,
                Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                QuickPreview = false,
            },
            geometry: geometry,
            context: context,
            description: "Maelstrom");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TransformationConfig.ComputeOutcome<GeometryBase> ApplyMorph<TMorph>(
        TMorph morph,
        GeometryBase geometry,
        IGeometryContext context,
        string description) where TMorph : SpaceMorph {
        using (morph as IDisposable) {
            GeometryBase duplicate = geometry.Duplicate();
            bool success = morph.Morph(duplicate);
            (!success ? duplicate : null)?.Dispose();

            return success
                ? new TransformationConfig.ComputeOutcome<GeometryBase>(true, duplicate, string.Empty)
                : new TransformationConfig.ComputeOutcome<GeometryBase>(false, geometry, description);
        }
    }
}
