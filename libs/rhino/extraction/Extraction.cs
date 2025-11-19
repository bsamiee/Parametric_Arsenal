using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic extraction operations for points, curves, features, primitives, and patterns.</summary>
public static class Extraction {
    // ═══════════════════════════════════════════════════════════════════════════════
    // Surface Direction
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Direction discriminator for surface isocurve extraction.</summary>
    public enum SurfaceDirection : byte {
        /// <summary>U-direction isocurves (constant V parameter).</summary>
        U = 0,
        /// <summary>V-direction isocurves (constant U parameter).</summary>
        V = 1,
        /// <summary>Both U and V direction isocurves.</summary>
        UV = 2,
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Feature, Primitive, and Pattern Kind Enums
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Classification of detected design features.</summary>
    public enum FeatureKind : byte {
        /// <summary>Constant-radius fillet.</summary>
        Fillet = 0,
        /// <summary>Chamfered edge.</summary>
        Chamfer = 1,
        /// <summary>Circular or elliptical hole.</summary>
        Hole = 2,
        /// <summary>Unclassified edge.</summary>
        GenericEdge = 3,
        /// <summary>Variable-radius fillet.</summary>
        VariableRadiusFillet = 4,
    }

    /// <summary>Classification of detected geometric primitives.</summary>
    public enum PrimitiveKind : byte {
        /// <summary>Planar surface.</summary>
        Plane = 0,
        /// <summary>Cylindrical surface.</summary>
        Cylinder = 1,
        /// <summary>Spherical surface.</summary>
        Sphere = 2,
        /// <summary>Unclassified surface.</summary>
        Unknown = 3,
        /// <summary>Conical surface.</summary>
        Cone = 4,
        /// <summary>Toroidal surface.</summary>
        Torus = 5,
        /// <summary>Extruded surface.</summary>
        Extrusion = 6,
    }

    /// <summary>Classification of detected spatial patterns.</summary>
    public enum PatternKind : byte {
        /// <summary>Linear array pattern.</summary>
        Linear = 0,
        /// <summary>Radial/polar array pattern.</summary>
        Radial = 1,
        /// <summary>Rectangular grid pattern.</summary>
        Grid = 2,
        /// <summary>Scaling pattern from centroid.</summary>
        Scaling = 3,
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Point Extraction Operations (Discriminated Union)
    // ═══════════════════════════════════════════════════════════════════════════════

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

    /// <summary>Osculating frames sampled along curve via perpendicular frame computation.</summary>
    public sealed record OsculatingFrames(int? FrameCount = null) : PointOperation;

    /// <summary>Divide geometry by point count.</summary>
    public sealed record ByCount(int Count, bool IncludeEnds = true) : PointOperation;

    /// <summary>Divide curve by segment length.</summary>
    public sealed record ByLength(double Length, bool IncludeEnds = true) : PointOperation;

    /// <summary>Extract extreme points along direction.</summary>
    public sealed record ByDirection(Vector3d Direction) : PointOperation;

    /// <summary>Extract discontinuity points at specified continuity level.</summary>
    public sealed record ByContinuity(Continuity Continuity) : PointOperation;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Curve Extraction Operations (Discriminated Union)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Base type for curve extraction operations.</summary>
    public abstract record CurveOperation;

    /// <summary>Boundary curves including outer loop and inner holes.</summary>
    public sealed record Boundary : CurveOperation;

    /// <summary>Feature edges detected via angle threshold.</summary>
    public sealed record FeatureEdges(double? AngleThreshold = null) : CurveOperation;

    /// <summary>Isocurves at evenly distributed parameters.</summary>
    public sealed record IsocurveCount(int Count, SurfaceDirection Direction = SurfaceDirection.UV) : CurveOperation;

    /// <summary>Isocurves at specified normalized parameters.</summary>
    public sealed record IsocurveParams(double[] Parameters, SurfaceDirection Direction = SurfaceDirection.UV) : CurveOperation;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Result Records
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Detected design feature with classification and parameter.</summary>
    [DebuggerDisplay("{Kind}: {Parameter:F3}")]
    public sealed record Feature(FeatureKind Kind, double Parameter);

    /// <summary>Result of design feature extraction.</summary>
    [DebuggerDisplay("Features={Features.Count}, Confidence={Confidence:F3}")]
    public sealed record FeatureResult(IReadOnlyList<Feature> Features, double Confidence);

    /// <summary>Detected geometric primitive with frame and parameters.</summary>
    [DebuggerDisplay("{Kind}: {Parameters.Length} params")]
    public sealed record Primitive(PrimitiveKind Kind, Plane Frame, double[] Parameters);

    /// <summary>Result of primitive decomposition.</summary>
    [DebuggerDisplay("Primitives={Primitives.Count}")]
    public sealed record PrimitiveResult(IReadOnlyList<Primitive> Primitives, double[] Residuals);

    /// <summary>Result of pattern detection.</summary>
    [DebuggerDisplay("{Kind}: Confidence={Confidence:F3}")]
    public sealed record PatternResult(PatternKind Kind, Transform SymmetryTransform, double Confidence);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Public API - Point Extraction
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Extracts points from geometry using the specified operation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(
        T geometry,
        PointOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.ExecutePoints(geometry: geometry, operation: operation, context: context);

    /// <summary>Batch point extraction with error accumulation and optional parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(
        IReadOnlyList<T> geometries,
        PointOperation operation,
        IGeometryContext context,
        bool accumulateErrors = true,
        bool enableParallel = false) where T : GeometryBase =>
        ExtractionCore.ExecutePointsMultiple(
            geometries: geometries,
            operation: operation,
            context: context,
            accumulateErrors: accumulateErrors,
            enableParallel: enableParallel);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Public API - Curve Extraction
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Extracts curves from geometry using the specified operation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Curve>> Curves<T>(
        T geometry,
        CurveOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.ExecuteCurves(geometry: geometry, operation: operation, context: context);

    /// <summary>Batch curve extraction with error accumulation and optional parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(
        IReadOnlyList<T> geometries,
        CurveOperation operation,
        IGeometryContext context,
        bool accumulateErrors = true,
        bool enableParallel = false) where T : GeometryBase =>
        ExtractionCore.ExecuteCurvesMultiple(
            geometries: geometries,
            operation: operation,
            context: context,
            accumulateErrors: accumulateErrors,
            enableParallel: enableParallel);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Public API - Analysis Operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Extracts design features (fillets, chamfers, holes) with confidence scoring.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FeatureResult> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        ExtractionCore.ExecuteFeatureExtraction(brep: brep, context: context);

    /// <summary>Decomposes geometry into best-fit primitives with residual measurements.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PrimitiveResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ExtractionCore.ExecutePrimitiveDecomposition(geometry: geometry, context: context);

    /// <summary>Extracts geometric patterns (linear, radial, grid, scaling) with symmetry transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCore.ExecutePatternExtraction(geometries: geometries, context: context);
}
