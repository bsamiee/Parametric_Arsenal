using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Affine transforms, array generation, and space morphs using algebraic requests.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Transformation is the primary API entry point for the Transformation namespace")]
public static class Transformation {
    /// <summary>Base type for affine transform requests.</summary>
    public abstract record TransformRequest;

    /// <summary>Apply an explicit transform matrix.</summary>
    public sealed record MatrixTransform(Transform Matrix) : TransformRequest;

    /// <summary>Uniform scale about an anchor point.</summary>
    public sealed record UniformScaleTransform(Point3d Anchor, double Factor) : TransformRequest;

    /// <summary>Non-uniform scale in a basis plane.</summary>
    public sealed record NonUniformScaleTransform(Plane Plane, double XScale, double YScale, double ZScale) : TransformRequest;

    /// <summary>Rotation around an axis and center.</summary>
    public sealed record AxisRotationTransform(double AngleRadians, Vector3d Axis, Point3d Center) : TransformRequest;

    /// <summary>Rotation from a start vector to an end vector.</summary>
    public sealed record DirectionRotationTransform(Vector3d Start, Vector3d End, Point3d Center) : TransformRequest;

    /// <summary>Mirror across a plane.</summary>
    public sealed record MirrorTransform(Plane Plane) : TransformRequest;

    /// <summary>Translation by a motion vector.</summary>
    public sealed record TranslationTransform(Vector3d Motion) : TransformRequest;

    /// <summary>Shear parallel to a plane in a direction.</summary>
    public sealed record ShearTransform(Plane Plane, Vector3d Direction, double AngleRadians) : TransformRequest;

    /// <summary>Orthogonal projection to a plane.</summary>
    public sealed record ProjectionTransform(Plane Plane) : TransformRequest;

    /// <summary>Change of basis between planes.</summary>
    public sealed record ChangeBasisTransform(Plane From, Plane To) : TransformRequest;

    /// <summary>Plane-to-plane transform.</summary>
    public sealed record PlaneToPlaneTransform(Plane From, Plane To) : TransformRequest;

    /// <summary>Base type for array generation requests.</summary>
    public abstract record ArrayRequest;

    /// <summary>Rectangular array parameters.</summary>
    public sealed record RectangularArray(int XCount, int YCount, int ZCount, double XSpacing, double YSpacing, double ZSpacing) : ArrayRequest;

    /// <summary>Polar array parameters.</summary>
    public sealed record PolarArray(Point3d Center, Vector3d Axis, int Count, double TotalAngle) : ArrayRequest;

    /// <summary>Linear array parameters.</summary>
    public sealed record LinearArray(Vector3d Direction, int Count, double Spacing) : ArrayRequest;

    /// <summary>Path array parameters.</summary>
    public sealed record PathArray(Curve Path, int Count, bool OrientToPath) : ArrayRequest;

    /// <summary>Base type for space morph requests.</summary>
    public abstract record MorphRequest;

    /// <summary>Flow geometry from base to target curve.</summary>
    public sealed record FlowMorph(Curve BaseCurve, Curve TargetCurve, bool PreserveStructure) : MorphRequest;

    /// <summary>Twist geometry around an axis.</summary>
    public sealed record TwistMorph(Line Axis, double AngleRadians, bool Infinite) : MorphRequest;

    /// <summary>Bend geometry along a spine.</summary>
    public sealed record BendMorph(Line Spine, double AngleRadians) : MorphRequest;

    /// <summary>Taper geometry along an axis.</summary>
    public sealed record TaperMorph(Line Axis, double StartWidth, double EndWidth) : MorphRequest;

    /// <summary>Stretch geometry along an axis.</summary>
    public sealed record StretchMorph(Line Axis) : MorphRequest;

    /// <summary>Splop geometry from a plane to a target surface point.</summary>
    public sealed record SplopMorph(Plane BasePlane, Surface TargetSurface, Point3d TargetPoint) : MorphRequest;

    /// <summary>Sporph geometry from source to target surface.</summary>
    public sealed record SporphMorph(Surface SourceSurface, Surface TargetSurface, bool PreserveStructure) : MorphRequest;

    /// <summary>Maelstrom vortex deformation.</summary>
    public sealed record MaelstromMorph(Point3d Center, Line Axis, double Radius, double AngleRadians) : MorphRequest;

    /// <summary>Apply transform request to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(
        T geometry,
        TransformRequest request,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ExecuteTransform(
            geometry: geometry,
            request: request,
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Generate and apply array transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> ArrayTransform<T>(
        T geometry,
        ArrayRequest request,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ExecuteArray(
            geometry: geometry,
            request: request,
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Apply space morph to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Morph<T>(
        T geometry,
        MorphRequest request,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ExecuteMorph(
            geometry: geometry,
            request: request,
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Scale geometry uniformly about anchor point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Point3d anchor,
        double factor,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new UniformScaleTransform(Anchor: anchor, Factor: factor),
            context: context);

    /// <summary>Scale geometry non-uniformly along plane axes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Plane plane,
        double xScale,
        double yScale,
        double zScale,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new NonUniformScaleTransform(Plane: plane, XScale: xScale, YScale: yScale, ZScale: zScale),
            context: context);

    /// <summary>Rotate geometry around axis by angle in radians.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        double angleRadians,
        Vector3d axis,
        Point3d center,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new AxisRotationTransform(AngleRadians: angleRadians, Axis: axis, Center: center),
            context: context);

    /// <summary>Rotate geometry from start direction to end direction around center.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        Vector3d startDirection,
        Vector3d endDirection,
        Point3d center,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new DirectionRotationTransform(Start: startDirection, End: endDirection, Center: center),
            context: context);

    /// <summary>Mirror geometry across plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(
        T geometry,
        Plane plane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new MirrorTransform(Plane: plane),
            context: context);

    /// <summary>Translate geometry by motion vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Vector3d motion,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new TranslationTransform(Motion: motion),
            context: context);

    /// <summary>Translate geometry from start point to end point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Point3d start,
        Point3d end,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new TranslationTransform(Motion: end - start),
            context: context);

    /// <summary>Shear geometry parallel to plane in given direction by angle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Shear<T>(
        T geometry,
        Plane plane,
        Vector3d direction,
        double angle,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new ShearTransform(Plane: plane, Direction: direction, AngleRadians: angle),
            context: context);

    /// <summary>Project geometry orthogonally to plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Project<T>(
        T geometry,
        Plane plane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new ProjectionTransform(Plane: plane),
            context: context);

    /// <summary>Change coordinate system from one plane basis to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ChangeBasis<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new ChangeBasisTransform(From: fromPlane, To: toPlane),
            context: context);

    /// <summary>Transform geometry from one plane orientation to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> PlaneToPlane<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new PlaneToPlaneTransform(From: fromPlane, To: toPlane),
            context: context);
}
