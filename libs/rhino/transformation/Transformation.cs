using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Transformation;

/// <summary>Affine transforms, arrays, and deformations with unified polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Transformation is the primary API entry point for the Transformation namespace")]
public static class Transformation {
    /// <summary>Transform operation algebraic hierarchy.</summary>
    public abstract record TransformOperation;

    /// <summary>Array operation algebraic hierarchy.</summary>
    public abstract record ArrayOperation;

    /// <summary>SpaceMorph operation algebraic hierarchy.</summary>
    public abstract record MorphOperation;

    /// <summary>Mirror reflection across plane.</summary>
    public sealed record Mirror(Plane Plane) : TransformOperation;

    /// <summary>Translation by motion vector.</summary>
    public sealed record Translation(Vector3d Motion) : TransformOperation;

    /// <summary>Orthogonal projection to plane.</summary>
    public sealed record Projection(Plane Plane) : TransformOperation;

    /// <summary>Change of coordinate basis.</summary>
    public sealed record BasisChange(Plane From, Plane To) : TransformOperation;

    /// <summary>Plane-to-plane orientation transform.</summary>
    public sealed record PlaneToPlane(Plane From, Plane To) : TransformOperation;

    /// <summary>Direct transform matrix application.</summary>
    public sealed record Matrix(Transform Value) : TransformOperation;

    /// <summary>Uniform scale from anchor point.</summary>
    public sealed record UniformScale(Point3d Anchor, double Factor) : TransformOperation;

    /// <summary>Rotation from start vector to end vector.</summary>
    public sealed record VectorRotation(Vector3d Start, Vector3d End, Point3d Center) : TransformOperation;

    /// <summary>Linear array along direction.</summary>
    public sealed record LinearArray(Vector3d Direction, int Count, double Spacing) : ArrayOperation;

    /// <summary>Stretch deformation along axis.</summary>
    public sealed record StretchMorph(Line Axis) : MorphOperation;

    /// <summary>Bend deformation along axis.</summary>
    public sealed record BendMorph(Line Axis, double AngleRadians) : MorphOperation;

    /// <summary>Array along path curve with optional orientation.</summary>
    public sealed record PathArray(Curve Path, int Count, bool OrientToPath) : ArrayOperation;

    /// <summary>Rotation around axis by angle.</summary>
    public sealed record AxisRotation(double AngleRadians, Vector3d Axis, Point3d Center) : TransformOperation;

    /// <summary>Twist deformation around axis.</summary>
    public sealed record TwistMorph(Line Axis, double AngleRadians, bool Infinite) : MorphOperation;

    /// <summary>Polar array around axis.</summary>
    public sealed record PolarArray(Point3d Center, Vector3d Axis, int Count, double TotalAngleRadians) : ArrayOperation;

    /// <summary>Taper deformation along axis.</summary>
    public sealed record TaperMorph(Line Axis, double StartWidth, double EndWidth) : MorphOperation;

    /// <summary>Non-uniform scale along plane axes.</summary>
    public sealed record NonUniformScale(Plane Plane, double XScale, double YScale, double ZScale) : TransformOperation;

    /// <summary>Shear deformation.</summary>
    public sealed record Shear(Plane Plane, Vector3d Direction, double AngleRadians) : TransformOperation;

    /// <summary>Flow geometry from base curve to target curve.</summary>
    public sealed record FlowMorph(Curve BaseCurve, Curve TargetCurve, bool PreserveStructure) : MorphOperation;

    /// <summary>Splop geometry from plane to surface point.</summary>
    public sealed record SplopMorph(Plane BasePlane, Surface TargetSurface, Point3d TargetPoint) : MorphOperation;

    /// <summary>Maelstrom vortex deformation.</summary>
    public sealed record MaelstromMorph(Line Axis, double Radius, double AngleRadians) : MorphOperation;

    /// <summary>Sporph geometry from source surface to target surface.</summary>
    public sealed record SporphMorph(Surface SourceSurface, Surface TargetSurface, bool PreserveStructure) : MorphOperation;

    /// <summary>Rectangular grid array in XYZ directions.</summary>
    public sealed record RectangularArray(int XCount, int YCount, int ZCount, double XSpacing, double YSpacing, double ZSpacing) : ArrayOperation;

    /// <summary>Sequential composition of multiple transforms.</summary>
    public sealed record Compound(TransformOperation[] Operations) : TransformOperation;

    /// <summary>Weighted blend between two transforms.</summary>
    public sealed record Blended(TransformOperation First, TransformOperation Second, double BlendFactor) : TransformOperation;

    /// <summary>Interpolated transform between start and end with parameter t ∈ [0,1].</summary>
    public sealed record Interpolated(TransformOperation Start, TransformOperation End, double Parameter) : TransformOperation;

    /// <summary>Result of transform decomposition into TRS components.</summary>
    public sealed record DecomposedTransform(
        Vector3d Translation,
        Quaternion Rotation,
        Vector3d Scale,
        Transform Residual,
        bool IsOrthogonal,
        double OrthogonalityError);

    /// <summary>Apply SpaceMorph deformation operation to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Morph<T>(
        T geometry,
        MorphOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        TransformationCompute.ExecuteMorph(
            geometry: geometry,
            operation: operation,
            context: context);

    /// <summary>Apply transform operation to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Apply<T>(
        T geometry,
        TransformOperation operation,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ExecuteTransform(
            geometry: geometry,
            operation: operation,
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Apply array operation to geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<T>> ApplyArray<T>(
        T geometry,
        ArrayOperation operation,
        IGeometryContext context,
        bool enableDiagnostics = false) where T : GeometryBase =>
        TransformationCore.ExecuteArray(
            geometry: geometry,
            operation: operation,
            context: context,
            enableDiagnostics: enableDiagnostics);

    /// <summary>Decompose transform matrix into translation, rotation, scale components (TRS).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<DecomposedTransform> Decompose(
        Transform matrix,
        IGeometryContext context) =>
        TransformationCompute.DecomposeTransform(matrix: matrix, context: context);
}
