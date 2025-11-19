using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Affine transforms, arrays, and deformations with unified polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Transformation is the primary API entry point for the Transformation namespace")]
public static class Transformation {
    /// <summary>Transform specification discriminated union.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct TransformSpec {
        /// <summary>Direct transform matrix application.</summary>
        public Transform? Matrix { get; init; }
        /// <summary>Uniform scale: (anchor, factor).</summary>
        public (Point3d Anchor, double Factor)? UniformScale { get; init; }
        /// <summary>Non-uniform scale: (plane, xScale, yScale, zScale).</summary>
        public (Plane Plane, double X, double Y, double Z)? NonUniformScale { get; init; }
        /// <summary>Rotation: (angle radians, axis, center).</summary>
        public (double Angle, Vector3d Axis, Point3d Center)? Rotation { get; init; }
        /// <summary>Rotation from start direction to end direction: (start, end, center).</summary>
        public (Vector3d Start, Vector3d End, Point3d Center)? RotationVectors { get; init; }
        /// <summary>Mirror plane for reflection.</summary>
        public Plane? MirrorPlane { get; init; }
        /// <summary>Translation motion vector.</summary>
        public Vector3d? Translation { get; init; }
        /// <summary>Shear: (plane, direction, angle).</summary>
        public (Plane Plane, Vector3d Direction, double Angle)? Shear { get; init; }
        /// <summary>Projection: plane for orthogonal projection.</summary>
        public Plane? ProjectionPlane { get; init; }
        /// <summary>Change of basis: (from plane, to plane).</summary>
        public (Plane From, Plane To)? ChangeBasis { get; init; }
        /// <summary>Plane-to-plane transform: (from, to).</summary>
        public (Plane From, Plane To)? PlaneToPlane { get; init; }

        /// <summary>Create matrix transform specification.</summary>
        [Pure]
        public static TransformSpec FromMatrix(Transform xform) => new() { Matrix = xform };
        /// <summary>Create uniform scale specification.</summary>
        [Pure]
        public static TransformSpec FromScale(Point3d anchor, double factor) => new() { UniformScale = (anchor, factor) };
        /// <summary>Create non-uniform scale specification.</summary>
        [Pure]
        public static TransformSpec FromScale(Plane plane, double x, double y, double z) => new() { NonUniformScale = (plane, x, y, z) };
        /// <summary>Create rotation around axis specification.</summary>
        [Pure]
        public static TransformSpec FromRotation(double angle, Vector3d axis, Point3d center) => new() { Rotation = (angle, axis, center) };
        /// <summary>Create rotation from vector to vector specification.</summary>
        [Pure]
        public static TransformSpec FromRotation(Vector3d start, Vector3d end, Point3d center) => new() { RotationVectors = (start, end, center) };
        /// <summary>Create mirror reflection specification.</summary>
        [Pure]
        public static TransformSpec FromMirror(Plane plane) => new() { MirrorPlane = plane };
        /// <summary>Create translation specification.</summary>
        [Pure]
        public static TransformSpec FromTranslation(Vector3d motion) => new() { Translation = motion };
        /// <summary>Create shear specification.</summary>
        [Pure]
        public static TransformSpec FromShear(Plane plane, Vector3d direction, double angle) => new() { Shear = (plane, direction, angle) };
        /// <summary>Create projection specification.</summary>
        [Pure]
        public static TransformSpec FromProjection(Plane plane) => new() { ProjectionPlane = plane };
        /// <summary>Create change of basis specification.</summary>
        [Pure]
        public static TransformSpec FromChangeBasis(Plane from, Plane to) => new() { ChangeBasis = (from, to) };
        /// <summary>Create plane-to-plane specification.</summary>
        [Pure]
        public static TransformSpec FromPlaneToPlane(Plane from, Plane to) => new() { PlaneToPlane = (from, to) };
    }

    /// <summary>Array transformation specification.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct ArraySpec {
        /// <summary>Array mode: 1=Rectangular, 2=Polar, 3=Linear, 4=Path.</summary>
        public byte Mode { get; init; }
        /// <summary>Total count for polar/linear/path arrays.</summary>
        public int Count { get; init; }

        /// <summary>Rectangular: X count.</summary>
        public int XCount { get; init; }
        /// <summary>Rectangular: Y count.</summary>
        public int YCount { get; init; }
        /// <summary>Rectangular: Z count (optional).</summary>
        public int? ZCount { get; init; }
        /// <summary>Rectangular: X spacing.</summary>
        public double XSpacing { get; init; }
        /// <summary>Rectangular: Y spacing.</summary>
        public double YSpacing { get; init; }
        /// <summary>Rectangular: Z spacing (optional).</summary>
        public double? ZSpacing { get; init; }

        /// <summary>Polar: center point.</summary>
        public Point3d? Center { get; init; }
        /// <summary>Polar: rotation axis.</summary>
        public Vector3d? Axis { get; init; }
        /// <summary>Polar: total angle in radians (default 2π).</summary>
        public double? TotalAngle { get; init; }

        /// <summary>Linear: direction vector.</summary>
        public Vector3d? Direction { get; init; }
        /// <summary>Linear/Path: spacing between instances.</summary>
        public double Spacing { get; init; }

        /// <summary>Path: curve to follow.</summary>
        public Curve? PathCurve { get; init; }
        /// <summary>Path: orient geometry to curve frames.</summary>
        public bool OrientToPath { get; init; }

        /// <summary>Create rectangular array specification.</summary>
        [Pure]
        public static ArraySpec Rectangular(int xCount, int yCount, int zCount, double xSpace, double ySpace, double zSpace) =>
            new() { Mode = 1, XCount = xCount, YCount = yCount, ZCount = zCount, XSpacing = xSpace, YSpacing = ySpace, ZSpacing = zSpace };
        /// <summary>Create polar array specification.</summary>
        [Pure]
        public static ArraySpec Polar(Point3d center, Vector3d axis, int count, double totalAngle) =>
            new() { Mode = 2, Center = center, Axis = axis, Count = count, TotalAngle = totalAngle };
        /// <summary>Create linear array specification.</summary>
        [Pure]
        public static ArraySpec Linear(Vector3d direction, int count, double spacing) =>
            new() { Mode = 3, Direction = direction, Count = count, Spacing = spacing };
        /// <summary>Create path array specification.</summary>
        [Pure]
        public static ArraySpec Path(Curve path, int count, bool orient) =>
            new() { Mode = 4, PathCurve = path, Count = count, OrientToPath = orient };
    }

    /// <summary>SpaceMorph operation specification.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct MorphSpec {
        /// <summary>Morph operation: 1=Flow, 2=Twist, 3=Bend, 4=Taper, 5=Stretch, 6=Splop, 7=Sporph, 8=Maelstrom.</summary>
        public byte Operation { get; init; }
        /// <summary>Flow: base curve.</summary>
        public Curve? BaseCurve { get; init; }
        /// <summary>Flow: target curve.</summary>
        public Curve? TargetCurve { get; init; }
        /// <summary>Twist/Bend/Taper/Stretch: axis line.</summary>
        public Line? Axis { get; init; }
        /// <summary>Twist/Bend: rotation angle in radians.</summary>
        public double Angle { get; init; }
        /// <summary>Twist: infinite twist flag.</summary>
        public bool Infinite { get; init; }
        /// <summary>All morphs: preserve NURBS structure flag.</summary>
        public bool PreserveStructure { get; init; }
        /// <summary>Taper: start width.</summary>
        public double? StartWidth { get; init; }
        /// <summary>Taper: end width.</summary>
        public double? EndWidth { get; init; }
        /// <summary>Splop: base plane.</summary>
        public Plane? BasePlane { get; init; }
        /// <summary>Splop/Sporph: target surface.</summary>
        public Surface? TargetSurface { get; init; }
        /// <summary>Sporph: source surface.</summary>
        public Surface? SourceSurface { get; init; }
        /// <summary>Splop: target point on surface.</summary>
        public Point3d? TargetPoint { get; init; }
        /// <summary>Maelstrom: vortex center.</summary>
        public Point3d? Center { get; init; }
        /// <summary>Maelstrom: vortex radius.</summary>
        public double? Radius { get; init; }

        /// <summary>Create flow morph specification.</summary>
        [Pure]
        public static MorphSpec Flow(Curve baseCurve, Curve targetCurve, bool preserve) =>
            new() { Operation = 1, BaseCurve = baseCurve, TargetCurve = targetCurve, PreserveStructure = preserve };
        /// <summary>Create twist morph specification.</summary>
        [Pure]
        public static MorphSpec Twist(Line axis, double angle, bool infinite) =>
            new() { Operation = 2, Axis = axis, Angle = angle, Infinite = infinite };
        /// <summary>Create bend morph specification.</summary>
        [Pure]
        public static MorphSpec Bend(Line axis, double angle) =>
            new() { Operation = 3, Axis = axis, Angle = angle };
        /// <summary>Create taper morph specification.</summary>
        [Pure]
        public static MorphSpec Taper(Line axis, double startWidth, double endWidth) =>
            new() { Operation = 4, Axis = axis, StartWidth = startWidth, EndWidth = endWidth };
        /// <summary>Create stretch morph specification.</summary>
        [Pure]
        public static MorphSpec Stretch(Line axis) =>
            new() { Operation = 5, Axis = axis };
        /// <summary>Create splop morph specification.</summary>
        [Pure]
        public static MorphSpec Splop(Plane basePlane, Surface targetSurface, Point3d targetPoint) =>
            new() { Operation = 6, BasePlane = basePlane, TargetSurface = targetSurface, TargetPoint = targetPoint };
        /// <summary>Create sporph morph specification.</summary>
        [Pure]
        public static MorphSpec Sporph(Surface sourceSurface, Surface targetSurface, bool preserve) =>
            new() { Operation = 7, SourceSurface = sourceSurface, TargetSurface = targetSurface, PreserveStructure = preserve };
        /// <summary>Create maelstrom morph specification.</summary>
        [Pure]
        public static MorphSpec Maelstrom(Point3d center, Vector3d axis, double radius, double angle) =>
            new() { Operation = 8, Center = center, Axis = new Line(center, axis), Radius = radius, Angle = angle };
    }

    /// <summary>Apply transform specification to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(
        T geometry,
        TransformSpec spec,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.BuildTransform(spec: spec, context: context)
            .Bind(xform => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<T>>>)(item =>
                    TransformationCore.ApplyTransform(item: item, transform: xform)),
                config: new OperationConfig<T, T> {
                    Context = context,
                    ValidationMode = TransformationConfig.GetValidationMode(typeof(T)),
                    OperationName = "Transformation.Apply",
                    EnableDiagnostics = enableDiagnostics,
                }))
            .Map(r => r[0]);

    /// <summary>Apply array transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> ArrayTransform<T>(
        T geometry,
        ArraySpec spec,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        spec.Mode switch {
            1 => TransformationCore.RectangularArray(
                geometry: geometry,
                xCount: spec.XCount,
                yCount: spec.YCount,
                zCount: spec.ZCount ?? 1,
                xSpacing: spec.XSpacing,
                ySpacing: spec.YSpacing,
                zSpacing: spec.ZSpacing ?? 0.0,
                context: context,
                enableDiagnostics: enableDiagnostics),
            2 => TransformationCore.PolarArray(
                geometry: geometry,
                center: spec.Center!.Value,
                axis: spec.Axis!.Value,
                count: spec.Count,
                totalAngle: spec.TotalAngle ?? RhinoMath.TwoPI,
                context: context,
                enableDiagnostics: enableDiagnostics),
            3 => TransformationCore.LinearArray(
                geometry: geometry,
                direction: spec.Direction!.Value,
                count: spec.Count,
                spacing: spec.Spacing,
                context: context,
                enableDiagnostics: enableDiagnostics),
            4 => TransformationCompute.PathArray(
                geometry: geometry,
                path: spec.PathCurve!,
                count: spec.Count,
                orientToPath: spec.OrientToPath,
                context: context,
                enableDiagnostics: enableDiagnostics),
            _ => ResultFactory.Create<IReadOnlyList<T>>(error: E.Geometry.Transformation.InvalidArrayMode),
        };

    /// <summary>Apply SpaceMorph deformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Morph<T>(
        T geometry,
        MorphSpec spec,
        IGeometryContext context) where T : GeometryBase =>
        spec.Operation switch {
            1 => TransformationCompute.Flow(
                geometry: geometry,
                baseCurve: spec.BaseCurve!,
                targetCurve: spec.TargetCurve!,
                preserveStructure: spec.PreserveStructure,
                context: context),
            2 => TransformationCompute.Twist(
                geometry: geometry,
                axis: spec.Axis!.Value,
                angleRadians: spec.Angle,
                infinite: spec.Infinite,
                context: context),
            3 => TransformationCompute.Bend(
                geometry: geometry,
                spine: spec.Axis!.Value,
                angle: spec.Angle,
                context: context),
            4 => TransformationCompute.Taper(
                geometry: geometry,
                axis: spec.Axis!.Value,
                startWidth: spec.StartWidth!.Value,
                endWidth: spec.EndWidth!.Value,
                context: context),
            5 => TransformationCompute.Stretch(
                geometry: geometry,
                axis: spec.Axis!.Value,
                context: context),
            6 => TransformationCompute.Splop(
                geometry: geometry,
                basePlane: spec.BasePlane!.Value,
                targetSurface: spec.TargetSurface!,
                targetPoint: spec.TargetPoint!.Value,
                context: context),
            7 => TransformationCompute.Sporph(
                geometry: geometry,
                sourceSurface: spec.SourceSurface!,
                targetSurface: spec.TargetSurface!,
                preserveStructure: spec.PreserveStructure,
                context: context),
            8 => TransformationCompute.Maelstrom(
                geometry: geometry,
                center: spec.Center!.Value,
                axis: spec.Axis!.Value,
                radius: spec.Radius!.Value,
                angle: spec.Angle,
                context: context),
            _ => ResultFactory.Create<T>(error: E.Geometry.Transformation.InvalidMorphOperation),
        };

    /// <summary>Scale geometry uniformly about anchor point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Point3d anchor,
        double factor,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromScale(anchor, factor), context: context);

    /// <summary>Scale geometry non-uniformly along plane axes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Plane plane,
        double xScale,
        double yScale,
        double zScale,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromScale(plane, xScale, yScale, zScale), context: context);

    /// <summary>Rotate geometry around axis by angle in radians.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        double angleRadians,
        Vector3d axis,
        Point3d center,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromRotation(angleRadians, axis, center), context: context);

    /// <summary>Rotate geometry from start direction to end direction around center.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        Vector3d startDirection,
        Vector3d endDirection,
        Point3d center,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromRotation(startDirection, endDirection, center), context: context);

    /// <summary>Mirror geometry across plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(
        T geometry,
        Plane plane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromMirror(plane), context: context);

    /// <summary>Translate geometry by motion vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Vector3d motion,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromTranslation(motion), context: context);

    /// <summary>Translate geometry from start point to end point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Point3d start,
        Point3d end,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromTranslation(end - start), context: context);

    /// <summary>Shear geometry parallel to plane in given direction by angle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Shear<T>(
        T geometry,
        Plane plane,
        Vector3d direction,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromShear(plane, direction, angle), context: context);

    /// <summary>Project geometry orthogonally to plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Project<T>(
        T geometry,
        Plane plane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromProjection(plane), context: context);

    /// <summary>Change coordinate system from one plane basis to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ChangeBasis<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromChangeBasis(fromPlane, toPlane), context: context);

    /// <summary>Transform geometry from one plane orientation to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> PlaneToPlane<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, spec: TransformSpec.FromPlaneToPlane(fromPlane, toPlane), context: context);
}
