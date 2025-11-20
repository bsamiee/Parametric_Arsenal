using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Morphs;

namespace Arsenal.Rhino.Transformation;

/// <summary>SpaceMorph deformation operations and transform generation.</summary>
internal static class TransformationCompute {
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
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidFlowCurves.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Base: {baseCurve.IsValid}, Target: {targetCurve.IsValid}, Geometry: {geometry.IsValid}")));

    /// <summary>Twist geometry around axis by angle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Twist<T>(
        T geometry,
        Line axis,
        double angleRadians,
        bool infinite,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid && Math.Abs(angleRadians) <= TransformationConfig.MaxTwistAngle && geometry.IsValid
            ? ApplyMorph(
                morph: new TwistSpaceMorph {
                    TwistAxis = axis,
                    TwistAngleRadians = angleRadians,
                    InfiniteTwist = infinite,
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTwistParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Axis: {axis.IsValid}, Angle: {angleRadians:F6}, Geometry: {geometry.IsValid}")));

    /// <summary>Bend geometry along spine.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Bend<T>(
        T geometry,
        Line spine,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        spine.IsValid && Math.Abs(angle) <= TransformationConfig.MaxBendAngle && geometry.IsValid
            ? ApplyMorph(
                morph: new BendSpaceMorph(
                    start: spine.From,
                    end: spine.To,
                    point: spine.PointAt(0.5),
                    angle: angle,
                    straight: false,
                    symmetric: false) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidBendParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Spine: {spine.IsValid}, Angle: {angle:F6}, Geometry: {geometry.IsValid}")));

    /// <summary>Taper geometry along axis from start width to end width.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<T> Taper<T>(
        T geometry,
        Line axis,
        double startWidth,
        double endWidth,
        IGeometryContext context) where T : GeometryBase =>
        axis.IsValid
        && startWidth >= TransformationConfig.MinScaleFactor
        && endWidth >= TransformationConfig.MinScaleFactor
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
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidTaperParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Axis: {axis.IsValid}, Start: {startWidth:F6}, End: {endWidth:F6}, Geometry: {geometry.IsValid}")));

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
                    point: axis.PointAt(0.5)) {
                    PreserveStructure = false,
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidStretchParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Axis: {axis.IsValid}, Geometry: {geometry.IsValid}")));

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
                        Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                        QuickPreview = false,
                    },
                    geometry: geometry)
                : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSplopParameters.WithContext("Surface closest point failed"))
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSplopParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Plane: {basePlane.IsValid}, Surface: {targetSurface.IsValid}, Point: {targetPoint.IsValid}, Geometry: {geometry.IsValid}")));

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
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidSporphParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Source: {sourceSurface.IsValid}, Target: {targetSurface.IsValid}, Geometry: {geometry.IsValid}")));

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
                    Tolerance = Math.Max(context.AbsoluteTolerance, TransformationConfig.DefaultMorphTolerance),
                    QuickPreview = false,
                },
                geometry: geometry)
            : ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMaelstromParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Center: {center.IsValid}, Axis: {axis.IsValid}, Radius: {radius:F6}, Geometry: {geometry.IsValid}")));

    /// <summary>Generate rectangular grid array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Transform>> BuildRectangularTransforms(
        int xCount,
        int yCount,
        int zCount,
        double xSpacing,
        double ySpacing,
        double zSpacing,
        double tolerance) {
        int totalCount = xCount * yCount * zCount;
        if (xCount <= 0 || yCount <= 0 || zCount <= 0
            || totalCount > TransformationConfig.MaxArrayCount
            || Math.Abs(xSpacing) <= tolerance
            || Math.Abs(ySpacing) <= tolerance
            || (zCount > 1 && Math.Abs(zSpacing) <= tolerance)) {
            return ResultFactory.Create<IReadOnlyList<Transform>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"XCount: {xCount}, YCount: {yCount}, ZCount: {zCount}, Total: {totalCount}")));
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

        return ResultFactory.Create<IReadOnlyList<Transform>>(value: transforms);
    }

    /// <summary>Generate polar array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Transform>> BuildPolarTransforms(
        Point3d center,
        Vector3d axis,
        int count,
        double totalAngle,
        double tolerance) {
        if (count <= 0 || count > TransformationConfig.MaxArrayCount
            || axis.Length <= tolerance
            || totalAngle <= 0.0 || totalAngle > RhinoMath.TwoPI) {
            return ResultFactory.Create<IReadOnlyList<Transform>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Count: {count}, Axis: {axis.Length:F6}, Angle: {totalAngle:F6}")));
        }

        Transform[] transforms = new Transform[count];
        double angleStep = totalAngle / count;

        for (int i = 0; i < count; i++) {
            transforms[i] = Transform.Rotation(
                angleRadians: angleStep * i,
                rotationAxis: axis,
                rotationCenter: center);
        }

        return ResultFactory.Create<IReadOnlyList<Transform>>(value: transforms);
    }

    /// <summary>Generate linear array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Transform>> BuildLinearTransforms(
        Vector3d direction,
        int count,
        double spacing,
        double tolerance) {
        double dirLength = direction.Length;
        if (count <= 0 || count > TransformationConfig.MaxArrayCount
            || dirLength <= tolerance
            || Math.Abs(spacing) <= tolerance) {
            return ResultFactory.Create<IReadOnlyList<Transform>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Count: {count}, Direction: {dirLength:F6}, Spacing: {spacing:F6}")));
        }

        Transform[] transforms = new Transform[count];
        Vector3d step = (direction / dirLength) * spacing;

        for (int i = 0; i < count; i++) {
            transforms[i] = Transform.Translation(step * i);
        }

        return ResultFactory.Create<IReadOnlyList<Transform>>(value: transforms);
    }

    /// <summary>Generate path array transforms with optional orientation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Transform>> BuildPathTransforms(
        Curve path,
        int count,
        bool orientToPath,
        double tolerance) {
        if (count <= 0 || count > TransformationConfig.MaxArrayCount || path?.IsValid != true) {
            return ResultFactory.Create<IReadOnlyList<Transform>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Count: {count}, Path: {path?.IsValid ?? false}")));
        }

        double curveLength = path.GetLength();
        if (curveLength <= tolerance) {
            return ResultFactory.Create<IReadOnlyList<Transform>>(error: E.Geometry.Transformation.InvalidArrayParameters.WithContext(string.Create(
                CultureInfo.InvariantCulture,
                $"Count: {count}, PathLength: {curveLength:F6}")));
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
                    }),
                ];

        Transform[] transforms = new Transform[count];
        for (int i = 0; i < count; i++) {
            double t = parameters[i];
            Point3d pt = path.PointAt(t);

            transforms[i] = orientToPath && path.FrameAt(t, out Plane frame) && frame.IsValid
                ? Transform.PlaneToPlane(Plane.WorldXY, frame)
                : Transform.Translation(pt - Point3d.Origin);
        }

        return ResultFactory.Create<IReadOnlyList<Transform>>(value: transforms);
    }

    /// <summary>Apply SpaceMorph to geometry with duplication.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<T> ApplyMorph<TMorph, T>(
        TMorph morph,
        T geometry) where TMorph : SpaceMorph where T : GeometryBase {
        using (morph as IDisposable) {
            if (!SpaceMorph.IsMorphable(geometry)) {
                return ResultFactory.Create<T>(error: E.Geometry.Transformation.GeometryNotMorphable.WithContext(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Geometry: {typeof(T).Name}, Morph: {typeof(TMorph).Name}")));
            }

            T duplicate = (T)geometry.Duplicate();
            return morph.Morph(duplicate)
                ? ResultFactory.Create(value: duplicate)
                : ResultFactory.Create<T>(error: E.Geometry.Transformation.MorphApplicationFailed.WithContext(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Morph type: {typeof(TMorph).Name}")));
        }
    }
}
