using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point and curve extraction from geometry with algebraic type-safe API.</summary>
public static class Extraction {
    /// <summary>Base type for surface direction specification.</summary>
    public abstract record SurfaceDirection;

    /// <summary>U-direction only.</summary>
    public sealed record UDirection : SurfaceDirection;

    /// <summary>V-direction only.</summary>
    public sealed record VDirection : SurfaceDirection;

    /// <summary>Both U and V directions.</summary>
    public sealed record BothDirections : SurfaceDirection;

    /// <summary>Base type for point extraction modes.</summary>
    public abstract record PointMode;

    /// <summary>Centroids and vertices via mass properties.</summary>
    public sealed record Analytical : PointMode;

    /// <summary>Geometric extrema including endpoints, corners, and bounding box vertices.</summary>
    public sealed record Extremal : PointMode;

    /// <summary>NURBS Greville points computed from knot vectors.</summary>
    public sealed record Greville : PointMode;

    /// <summary>Curve inflection points where curvature changes sign.</summary>
    public sealed record Inflection : PointMode;

    /// <summary>Circle/ellipse quadrant points at cardinal angles.</summary>
    public sealed record Quadrant : PointMode;

    /// <summary>Topology edge midpoints for Brep, Mesh, and polycurve structures.</summary>
    public sealed record EdgeMidpoints : PointMode;

    /// <summary>Topology face centroids computed via area properties.</summary>
    public sealed record FaceCentroids : PointMode;

    /// <summary>Osculating frames sampled along curve via perpendicular frame computation.</summary>
    public sealed record OsculatingFrames(int? Count = null) : PointMode;

    /// <summary>Divide geometry by integer count.</summary>
    public sealed record DivideByCount(int Count, bool IncludeEnds = true) : PointMode;

    /// <summary>Divide geometry by segment length.</summary>
    public sealed record DivideByLength(double Length, bool IncludeEnds = true) : PointMode;

    /// <summary>Extreme points in specified direction.</summary>
    public sealed record DirectionalExtreme(Vector3d Direction) : PointMode;

    /// <summary>Discontinuity points at specified continuity level.</summary>
    public sealed record DiscontinuityPoints(Continuity Continuity) : PointMode;

    /// <summary>Base type for curve extraction modes.</summary>
    public abstract record CurveMode;

    /// <summary>Boundary curves including outer loop and inner holes.</summary>
    public sealed record Boundary : CurveMode;

    /// <summary>Uniform isocurves in specified direction with default count.</summary>
    public sealed record IsocurveUniform(SurfaceDirection Direction) : CurveMode;

    /// <summary>Isocurves at uniform intervals with specified count and direction.</summary>
    public sealed record IsocurveCount(int Count, SurfaceDirection Direction) : CurveMode;

    /// <summary>Isocurves at specific parameter values with direction.</summary>
    public sealed record IsocurveParameters(double[] Parameters, SurfaceDirection Direction) : CurveMode;

    /// <summary>Sharp feature edges with optional angle threshold.</summary>
    public sealed record FeatureEdges(double? AngleThreshold = null) : CurveMode;

    /// <summary>Base type for detected design features.</summary>
    public abstract record FeatureType;

    /// <summary>Fillet feature with detected radius.</summary>
    public sealed record FilletFeature(double Radius) : FeatureType;

    /// <summary>Chamfer feature with detected angle.</summary>
    public sealed record ChamferFeature(double Angle) : FeatureType;

    /// <summary>Hole feature with computed area.</summary>
    public sealed record HoleFeature(double Area) : FeatureType;

    /// <summary>Generic edge feature with length.</summary>
    public sealed record GenericEdgeFeature(double Length) : FeatureType;

    /// <summary>Variable radius fillet with average radius.</summary>
    public sealed record VariableRadiusFilletFeature(double Radius) : FeatureType;

    /// <summary>Base type for primitive decomposition results.</summary>
    public abstract record PrimitiveType;

    /// <summary>Planar surface primitive.</summary>
    public sealed record PlanePrimitive(Plane Frame) : PrimitiveType;

    /// <summary>Cylindrical surface primitive.</summary>
    public sealed record CylinderPrimitive(Plane Frame, double Radius, double Height) : PrimitiveType;

    /// <summary>Spherical surface primitive.</summary>
    public sealed record SpherePrimitive(Plane Frame, double Radius) : PrimitiveType;

    /// <summary>Conical surface primitive.</summary>
    public sealed record ConePrimitive(Plane Frame, double Radius, double Height, double Angle) : PrimitiveType;

    /// <summary>Toroidal surface primitive.</summary>
    public sealed record TorusPrimitive(Plane Frame, double MajorRadius, double MinorRadius) : PrimitiveType;

    /// <summary>Extrusion surface primitive.</summary>
    public sealed record ExtrusionPrimitive(Plane Frame, double Length) : PrimitiveType;

    /// <summary>Unclassified primitive.</summary>
    public sealed record UnknownPrimitive : PrimitiveType;

    /// <summary>Base type for detected patterns.</summary>
    public abstract record PatternType;

    /// <summary>Linear arrangement with consistent spacing.</summary>
    public sealed record LinearPattern : PatternType;

    /// <summary>Radial arrangement around center.</summary>
    public sealed record RadialPattern : PatternType;

    /// <summary>Grid arrangement in two dimensions.</summary>
    public sealed record GridPattern : PatternType;

    /// <summary>Scaling arrangement from center.</summary>
    public sealed record ScalingPattern : PatternType;

    /// <summary>No recognizable pattern.</summary>
    public sealed record NoPattern : PatternType;

    /// <summary>Result of design feature extraction.</summary>
    public sealed record FeatureExtractionResult(
        FeatureType[] Features,
        double Confidence);

    /// <summary>Result of primitive decomposition.</summary>
    public sealed record PrimitiveDecompositionResult(
        (PrimitiveType Primitive, double Residual)[] Decomposition);

    /// <summary>Result of pattern extraction.</summary>
    public sealed record PatternExtractionResult(
        PatternType Pattern,
        Transform SymmetryTransform,
        double Confidence);

    /// <summary>Extract points from geometry using specified mode.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T geometry, PointMode mode, IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.ExecutePoints(geometry: geometry, mode: mode, context: context);

    /// <summary>Batch point extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(
        IReadOnlyList<T> geometries,
        PointMode mode,
        IGeometryContext context,
        bool accumulateErrors = true,
        bool enableParallel = false) where T : GeometryBase =>
        ExtractionCore.ExecutePointsMultiple(geometries: geometries, mode: mode, context: context, accumulateErrors: accumulateErrors, enableParallel: enableParallel);

    /// <summary>Extract curves from geometry using specified mode.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Curve>> Curves<T>(T geometry, CurveMode mode, IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.ExecuteCurves(geometry: geometry, mode: mode, context: context);

    /// <summary>Batch curve extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(
        IReadOnlyList<T> geometries,
        CurveMode mode,
        IGeometryContext context,
        bool accumulateErrors = true,
        bool enableParallel = false) where T : GeometryBase =>
        ExtractionCore.ExecuteCurvesMultiple(geometries: geometries, mode: mode, context: context, accumulateErrors: accumulateErrors, enableParallel: enableParallel);

    /// <summary>Extract design features (fillets, chamfers, holes) from Brep with confidence scoring.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FeatureExtractionResult> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        ExtractionCore.ExecuteFeatureExtraction(brep: brep, context: context);

    /// <summary>Decompose geometry into best-fit primitives with residual measurements.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ExtractionCore.ExecutePrimitiveDecomposition(geometry: geometry, context: context);

    /// <summary>Extract geometric patterns (linear, radial, grid, scaling) with symmetry transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternExtractionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCore.ExecutePatternExtraction(geometries: geometries, context: context);
}
