using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Affine transforms, arrays, and deformations with unified polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Transformation is the primary API entry point for the Transformation namespace")]
public static class Transformation {
    /// <summary>Base type for transform operations.</summary>
    public abstract record TransformOperation;

    /// <summary>Direct transform matrix application.</summary>
    public sealed record MatrixTransform(Transform Matrix) : TransformOperation;

    /// <summary>Uniform scale about anchor point.</summary>
    public sealed record UniformScale(Point3d Anchor, double Factor) : TransformOperation;

    /// <summary>Non-uniform scale along plane axes.</summary>
    public sealed record NonUniformScale(Plane Plane, double XScale, double YScale, double ZScale) : TransformOperation;

    /// <summary>Rotation around axis by angle in radians.</summary>
    public sealed record AxisRotation(double AngleRadians, Vector3d Axis, Point3d Center) : TransformOperation;

    /// <summary>Rotation from start direction to end direction.</summary>
    public sealed record VectorRotation(Vector3d StartDirection, Vector3d EndDirection, Point3d Center) : TransformOperation;

    /// <summary>Mirror reflection across plane.</summary>
    public sealed record MirrorTransform(Plane MirrorPlane) : TransformOperation;

    /// <summary>Translation by motion vector.</summary>
    public sealed record Translation(Vector3d Motion) : TransformOperation;

    /// <summary>Shear parallel to plane in given direction.</summary>
    public sealed record Shear(Plane Plane, Vector3d Direction, double Angle) : TransformOperation;

    /// <summary>Orthogonal projection to plane.</summary>
    public sealed record Projection(Plane ProjectionPlane) : TransformOperation;

    /// <summary>Change of basis transformation.</summary>
    public sealed record ChangeBasis(Plane FromPlane, Plane ToPlane) : TransformOperation;

    /// <summary>Plane-to-plane orientation transform.</summary>
    public sealed record PlaneToPlane(Plane FromPlane, Plane ToPlane) : TransformOperation;

    /// <summary>Base type for array operations.</summary>
    public abstract record ArrayOperation;

    /// <summary>Rectangular grid array.</summary>
    public sealed record RectangularArray(int XCount, int YCount, int ZCount, double XSpacing, double YSpacing, double ZSpacing) : ArrayOperation;

    /// <summary>Polar array around axis.</summary>
    public sealed record PolarArray(Point3d Center, Vector3d Axis, int Count, double TotalAngleRadians) : ArrayOperation;

    /// <summary>Linear array along direction.</summary>
    public sealed record LinearArray(Vector3d Direction, int Count, double Spacing) : ArrayOperation;

    /// <summary>Array along path curve with optional orientation.</summary>
    public sealed record PathArray(Curve PathCurve, int Count, bool OrientToPath) : ArrayOperation;

    /// <summary>Base type for morph operations.</summary>
    public abstract record MorphOperation;

    /// <summary>Flow geometry from base curve to target curve.</summary>
    public sealed record FlowMorph(Curve BaseCurve, Curve TargetCurve, bool PreserveStructure) : MorphOperation;

    /// <summary>Twist geometry around axis.</summary>
    public sealed record TwistMorph(Line Axis, double AngleRadians, bool Infinite) : MorphOperation;

    /// <summary>Bend geometry along spine.</summary>
    public sealed record BendMorph(Line Spine, double Angle) : MorphOperation;

    /// <summary>Taper geometry along axis.</summary>
    public sealed record TaperMorph(Line Axis, double StartWidth, double EndWidth) : MorphOperation;

    /// <summary>Stretch geometry along axis.</summary>
    public sealed record StretchMorph(Line Axis) : MorphOperation;

    /// <summary>Splop geometry from base plane to surface.</summary>
    public sealed record SplopMorph(Plane BasePlane, Surface TargetSurface, Point3d TargetPoint) : MorphOperation;

    /// <summary>Sporph geometry from source surface to target surface.</summary>
    public sealed record SporphMorph(Surface SourceSurface, Surface TargetSurface, bool PreserveStructure) : MorphOperation;

    /// <summary>Maelstrom vortex deformation.</summary>
    public sealed record MaelstromMorph(Point3d Center, Vector3d Axis, double Radius, double Angle) : MorphOperation;

    /// <summary>Apply transform operation to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(
        T geometry,
        TransformOperation operation,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.Execute(geometry: geometry, operation: operation, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Apply array transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> ArrayTransform<T>(
        T geometry,
        ArrayOperation operation,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ExecuteArray(geometry: geometry, operation: operation, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Apply SpaceMorph deformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Morph<T>(
        T geometry,
        MorphOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        TransformationCore.ExecuteMorph(geometry: geometry, operation: operation, context: context);

    /// <summary>Scale geometry uniformly about anchor point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Point3d anchor,
        double factor,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new UniformScale(Anchor: anchor, Factor: factor), context: context);

    /// <summary>Scale geometry non-uniformly along plane axes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Plane plane,
        double xScale,
        double yScale,
        double zScale,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new NonUniformScale(Plane: plane, XScale: xScale, YScale: yScale, ZScale: zScale), context: context);

    /// <summary>Rotate geometry around axis by angle in radians.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        double angleRadians,
        Vector3d axis,
        Point3d center,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new AxisRotation(AngleRadians: angleRadians, Axis: axis, Center: center), context: context);

    /// <summary>Rotate geometry from start direction to end direction around center.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        Vector3d startDirection,
        Vector3d endDirection,
        Point3d center,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new VectorRotation(StartDirection: startDirection, EndDirection: endDirection, Center: center), context: context);

    /// <summary>Mirror geometry across plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(
        T geometry,
        Plane plane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new MirrorTransform(MirrorPlane: plane), context: context);

    /// <summary>Translate geometry by motion vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Vector3d motion,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new Translation(Motion: motion), context: context);

    /// <summary>Translate geometry from start point to end point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Point3d start,
        Point3d end,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new Translation(Motion: end - start), context: context);

    /// <summary>Shear geometry parallel to plane in given direction by angle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Shear<T>(
        T geometry,
        Plane plane,
        Vector3d direction,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new Shear(Plane: plane, Direction: direction, Angle: angle), context: context);

    /// <summary>Project geometry orthogonally to plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Project<T>(
        T geometry,
        Plane plane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new Projection(ProjectionPlane: plane), context: context);

    /// <summary>Change coordinate system from one plane basis to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ChangeBasis<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new ChangeBasis(FromPlane: fromPlane, ToPlane: toPlane), context: context);

    /// <summary>Transform geometry from one plane orientation to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> PlaneToPlane<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(geometry: geometry, operation: new PlaneToPlane(FromPlane: fromPlane, ToPlane: toPlane), context: context);
}
