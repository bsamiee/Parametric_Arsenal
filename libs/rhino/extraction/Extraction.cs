using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point and curve extraction from geometry.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0104:Type name should not collide", Justification = "Different namespace")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match namespace", Justification = "Intentional API design")]
public static class Extraction {
    /// <summary>Base type for point extraction operations.</summary>
    public abstract record PointOperation;

    /// <summary>Centroids and vertices via mass properties.</summary>
    public sealed record Analytical : PointOperation;

    /// <summary>Geometric extrema including endpoints, corners, and bounding box vertices.</summary>
    public sealed record Extremal : PointOperation;

    /// <summary>NURBS Greville points computed from knot vectors.</summary>
    public sealed record Greville : PointOperation;

    /// <summary>Curve inflection points where curvature changes sign.</summary>
    public sealed record Inflection : PointOperation;

    /// <summary>Circle/ellipse quadrant points at cardinal angles.</summary>
    public sealed record Quadrant : PointOperation;

    /// <summary>Topology edge midpoints for Brep, Mesh, and polycurve structures.</summary>
    public sealed record EdgeMidpoints : PointOperation;

    /// <summary>Topology face centroids computed via area properties.</summary>
    public sealed record FaceCentroids : PointOperation;

    /// <summary>Extract discontinuity points.</summary>
    public sealed record Discontinuity(Continuity Type) : PointOperation;

    /// <summary>Extract extreme points along direction.</summary>
    public sealed record ByDirection(Vector3d Direction) : PointOperation;

    /// <summary>Osculating frames sampled along curve via perpendicular frame computation.</summary>
    public sealed record OsculatingFrames(int Count = 10) : PointOperation;

    /// <summary>Divide curve by count.</summary>
    public sealed record ByCount(int Count, bool IncludeEnds = true) : PointOperation;

    /// <summary>Divide curve by length.</summary>
    public sealed record ByLength(double Length, bool IncludeEnds = true) : PointOperation;

    /// <summary>Base type for curve extraction operations.</summary>
    public abstract record CurveOperation;

    /// <summary>Boundary curves including outer loop and inner holes.</summary>
    public sealed record Boundary : CurveOperation;

    /// <summary>Sharp feature curves detected via edge angle threshold.</summary>
    public sealed record FeatureEdges(double AngleThreshold) : CurveOperation;

    /// <summary>Isocurves extracted at specified direction.</summary>
    public sealed record Isocurves(IsocurveDirection Direction, int Count) : CurveOperation;

    /// <summary>Isocurves at explicit parameters.</summary>
    public sealed record IsocurvesAt(IsocurveDirection Direction, double[] Parameters) : CurveOperation;

    /// <summary>Isocurve direction specification.</summary>
    public abstract record IsocurveDirection;

    /// <summary>U-direction isocurves.</summary>
    public sealed record UDirection : IsocurveDirection;

    /// <summary>V-direction isocurves.</summary>
    public sealed record VDirection : IsocurveDirection;

    /// <summary>Combined U and V isocurves.</summary>
    public sealed record BothDirections : IsocurveDirection;

    /// <summary>Base type for detected feature classifications.</summary>
    public abstract record Feature;

    /// <summary>Fillet feature with estimated radius.</summary>
    public sealed record Fillet(double Radius) : Feature;

    /// <summary>Chamfer feature with dihedral angle.</summary>
    public sealed record Chamfer(double Angle) : Feature;

    /// <summary>Hole feature with area measurement.</summary>
    public sealed record Hole(double Area) : Feature;

    /// <summary>Generic edge with length.</summary>
    public sealed record GenericEdge(double Length) : Feature;

    /// <summary>Variable radius fillet.</summary>
    public sealed record VariableRadiusFillet(double AverageRadius) : Feature;

    /// <summary>Result of feature extraction.</summary>
    [DebuggerDisplay("Features={Features.Length}, Confidence={Confidence:F3}")]
    public sealed record FeatureExtractionResult(
        Feature[] Features,
        double Confidence);

    /// <summary>Base type for primitive geometry classifications.</summary>
    public abstract record Primitive(Plane Frame);

    /// <summary>Unknown primitive type.</summary>
    public sealed record UnknownPrimitive(Plane Frame) : Primitive(Frame);

    /// <summary>Planar surface primitive.</summary>
    public sealed record PlanarPrimitive(Plane Frame, Point3d Origin) : Primitive(Frame);

    /// <summary>Spherical surface primitive.</summary>
    public sealed record SphericalPrimitive(Plane Frame, double Radius) : Primitive(Frame);

    /// <summary>Extrusion primitive.</summary>
    public sealed record ExtrusionPrimitive(Plane Frame, double Length) : Primitive(Frame);

    /// <summary>Cylindrical surface primitive.</summary>
    public sealed record CylindricalPrimitive(Plane Frame, double Radius, double Height) : Primitive(Frame);

    /// <summary>Toroidal surface primitive.</summary>
    public sealed record ToroidalPrimitive(Plane Frame, double MajorRadius, double MinorRadius) : Primitive(Frame);

    /// <summary>Conical surface primitive.</summary>
    public sealed record ConicalPrimitive(Plane Frame, double BaseRadius, double Height, double Angle) : Primitive(Frame);

    /// <summary>Result of primitive decomposition.</summary>
    [DebuggerDisplay("Primitives={Primitives.Length}")]
    public sealed record PrimitiveDecompositionResult(
        Primitive[] Primitives,
        double[] Residuals);

    /// <summary>Base type for detected geometric patterns.</summary>
    public abstract record Pattern(Transform SymmetryTransform);

    /// <summary>Linear pattern with translation symmetry.</summary>
    public sealed record LinearPattern(Transform SymmetryTransform) : Pattern(SymmetryTransform);

    /// <summary>Radial pattern with rotational symmetry.</summary>
    public sealed record RadialPattern(Transform SymmetryTransform) : Pattern(SymmetryTransform);

    /// <summary>Grid pattern with two-dimensional repetition.</summary>
    public sealed record GridPattern(Transform SymmetryTransform) : Pattern(SymmetryTransform);

    /// <summary>Scaling pattern with radial growth.</summary>
    public sealed record ScalingPattern(Transform SymmetryTransform) : Pattern(SymmetryTransform);

    /// <summary>Result of pattern detection.</summary>
    [DebuggerDisplay("Pattern={Pattern.GetType().Name}, Confidence={Confidence:F3}")]
    public sealed record PatternDetectionResult(
        Pattern Pattern,
        double Confidence);

    /// <summary>Extract points with specified operation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, PointOperation operation, IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.ExecutePoints(geometry: input, operation: operation, context: context);

    /// <summary>Batch point extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, PointOperation operation, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Points(item, operation, context)),]
            : [.. geometries.Select(item => Points(item, operation, context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
        };
    }

    /// <summary>Extract curves with specified operation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Curve>> Curves<T>(T input, CurveOperation operation, IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.ExecuteCurves(geometry: input, operation: operation, context: context);

    /// <summary>Batch curve extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(IReadOnlyList<T> geometries, CurveOperation operation, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Curve>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Curves(item, operation, context)),]
            : [.. geometries.Select(item => Curves(item, operation, context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
        };
    }

    /// <summary>Extracts design features (fillets, chamfers, holes) with confidence scoring.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FeatureExtractionResult> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        ExtractionCompute.ExtractFeatures(brep: brep, context: context);

    /// <summary>Decomposes geometry into best-fit primitives with residual measurements.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ExtractionCompute.DecomposeToPrimitives(geometry: geometry, context: context);

    /// <summary>Extracts geometric patterns (linear, radial, grid, scaling) with symmetry transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternDetectionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCompute.ExtractPatterns(geometries: geometries, context: context);
}
