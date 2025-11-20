using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Affine transforms, arrays, and deformations with unified polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Transformation is the primary API entry point for the Transformation namespace")]
public static class Transformation {
    /// <summary>Affine transform request.</summary>
    public abstract record TransformRequest;

    /// <summary>Direct matrix transform.</summary>
    public sealed record MatrixTransform(Transform Matrix) : TransformRequest;

    /// <summary>Uniform scale about an anchor point.</summary>
    public sealed record UniformScaleTransform(Point3d Anchor, double Factor) : TransformRequest;

    /// <summary>Non-uniform scale within a plane basis.</summary>
    public sealed record NonUniformScaleTransform(Plane Plane, double XScale, double YScale, double ZScale) : TransformRequest;

    /// <summary>Rotation about an axis through a center point.</summary>
    public sealed record AxisRotationTransform(double AngleRadians, Vector3d Axis, Point3d Center) : TransformRequest;

    /// <summary>Rotation from start to end direction around a center point.</summary>
    public sealed record VectorRotationTransform(Vector3d StartDirection, Vector3d EndDirection, Point3d Center) : TransformRequest;

    /// <summary>Mirror reflection across a plane.</summary>
    public sealed record MirrorTransform(Plane Plane) : TransformRequest;

    /// <summary>Translation by a motion vector.</summary>
    public sealed record TranslationTransform(Vector3d Motion) : TransformRequest;

    /// <summary>Shear relative to a plane and direction.</summary>
    public sealed record ShearTransform(Plane Plane, Vector3d Direction, double AngleRadians) : TransformRequest;

    /// <summary>Orthogonal projection to a plane.</summary>
    public sealed record ProjectionTransform(Plane Plane) : TransformRequest;

    /// <summary>Change of basis between planes.</summary>
    public sealed record ChangeBasisTransform(Plane From, Plane To) : TransformRequest;

    /// <summary>Transform geometry from one plane orientation to another.</summary>
    public sealed record PlaneToPlaneTransform(Plane From, Plane To) : TransformRequest;

    /// <summary>Array transformation request.</summary>
    public abstract record ArrayRequest;

    /// <summary>Rectangular grid array.</summary>
    public sealed record RectangularArray(int XCount, int YCount, int ZCount, double XSpacing, double YSpacing, double ZSpacing) : ArrayRequest;

    /// <summary>Polar array around an axis.</summary>
    public sealed record PolarArray(Point3d Center, Vector3d Axis, int Count, double TotalAngle) : ArrayRequest;

    /// <summary>Linear array along a direction.</summary>
    public sealed record LinearArray(Vector3d Direction, int Count, double Spacing) : ArrayRequest;

    /// <summary>Path array following a curve.</summary>
    public sealed record PathArray(Curve Path, int Count, bool OrientToPath) : ArrayRequest;

    /// <summary>SpaceMorph deformation request.</summary>
    public abstract record MorphRequest;

    /// <summary>Flow geometry from a base curve to a target curve.</summary>
    public sealed record FlowMorph(Curve BaseCurve, Curve TargetCurve, bool PreserveStructure) : MorphRequest;

    /// <summary>Twist geometry around an axis.</summary>
    public sealed record TwistMorph(Line Axis, double AngleRadians, bool Infinite) : MorphRequest;

    /// <summary>Bend geometry along an axis.</summary>
    public sealed record BendMorph(Line Axis, double AngleRadians) : MorphRequest;

    /// <summary>Taper geometry along an axis.</summary>
    public sealed record TaperMorph(Line Axis, double StartWidth, double EndWidth) : MorphRequest;

    /// <summary>Stretch geometry along an axis.</summary>
    public sealed record StretchMorph(Line Axis) : MorphRequest;

    /// <summary>Splop geometry from a plane to a target surface point.</summary>
    public sealed record SplopMorph(Plane BasePlane, Surface TargetSurface, Point3d TargetPoint) : MorphRequest;

    /// <summary>Sporph geometry from source surface to target surface.</summary>
    public sealed record SporphMorph(Surface SourceSurface, Surface TargetSurface, bool PreserveStructure) : MorphRequest;

    /// <summary>Maelstrom vortex deformation around an axis.</summary>
    public sealed record MaelstromMorph(Point3d Center, Vector3d Axis, double Radius, double AngleRadians) : MorphRequest;

    /// <summary>Apply transform request to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(
        T geometry,
        TransformRequest request,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ApplyTransform(
            geometry: geometry,
            request: request,
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Apply array transformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> ArrayTransform<T>(
        T geometry,
        ArrayRequest request,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ArrayTransform(
            geometry: geometry,
            request: request,
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Apply SpaceMorph deformation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Morph<T>(
        T geometry,
        MorphRequest request,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.Morph(
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
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new UniformScaleTransform(anchor, factor),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Scale geometry non-uniformly along plane axes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Scale<T>(
        T geometry,
        Plane plane,
        double xScale,
        double yScale,
        double zScale,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new NonUniformScaleTransform(plane, xScale, yScale, zScale),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Rotate geometry around axis by angle in radians.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        double angleRadians,
        Vector3d axis,
        Point3d center,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new AxisRotationTransform(angleRadians, axis, center),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Rotate geometry from start direction to end direction around center.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Rotate<T>(
        T geometry,
        Vector3d startDirection,
        Vector3d endDirection,
        Point3d center,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new VectorRotationTransform(startDirection, endDirection, center),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Mirror geometry across plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Mirror<T>(
        T geometry,
        Plane plane,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new MirrorTransform(plane),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Translate geometry by motion vector.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Vector3d motion,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new TranslationTransform(motion),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Translate geometry from start point to end point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Translate<T>(
        T geometry,
        Point3d start,
        Point3d end,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new TranslationTransform(end - start),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Shear geometry parallel to plane in given direction by angle.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Shear<T>(
        T geometry,
        Plane plane,
        Vector3d direction,
        double angle,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new ShearTransform(plane, direction, angle),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Project geometry orthogonally to plane.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Project<T>(
        T geometry,
        Plane plane,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new ProjectionTransform(plane),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Change coordinate system from one plane basis to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> ChangeBasis<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new ChangeBasisTransform(fromPlane, toPlane),
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Transform geometry from one plane orientation to another.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> PlaneToPlane<T>(
        T geometry,
        Plane fromPlane,
        Plane toPlane,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        Apply(
            geometry: geometry,
            request: new PlaneToPlaneTransform(fromPlane, toPlane),
            context: context,
            enableDiagnostics: enableDiagnostics);
}
