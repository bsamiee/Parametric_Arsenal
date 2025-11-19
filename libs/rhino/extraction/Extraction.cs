using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Discriminated union API for polymorphic extraction operations.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Extraction is the primary API entry point")]
public static class Extraction {
    /// <summary>Abstract base type for all point extraction requests.</summary>
    public abstract record PointRequest;

    /// <summary>Abstract base type for all curve extraction requests.</summary>
    public abstract record CurveRequest;

    /// <summary>Semantic point extraction with optional sampling configuration.</summary>
    public sealed record SemanticPoint(PointSemantic Semantic, int? SampleCount = null) : PointRequest;

    /// <summary>Divide curve/surface by count with optional endpoint inclusion.</summary>
    public sealed record DivideByCount(int Count, bool IncludeEndpoints = true) : PointRequest;

    /// <summary>Divide curve by arc length with optional endpoint inclusion.</summary>
    public sealed record DivideByLength(double Length, bool IncludeEndpoints = true) : PointRequest;

    /// <summary>Extract extremal points in specified direction.</summary>
    public sealed record DirectionalExtrema(Vector3d Direction) : PointRequest;

    /// <summary>Extract discontinuity points at specified continuity level.</summary>
    public sealed record Discontinuities(Continuity Continuity) : PointRequest;

    /// <summary>Semantic curve extraction with optional configuration.</summary>
    public sealed record SemanticCurve(CurveSemantic Semantic, int? Count = null) : CurveRequest;

    /// <summary>Extract isocurves by count with optional direction specification.</summary>
    public sealed record IsocurveByCount(int Count, IsocurveDirection Direction = IsocurveDirection.Both) : CurveRequest;

    /// <summary>Extract isocurves at specified parameters with optional direction.</summary>
    public sealed record IsocurveByParameters(double[] Parameters, IsocurveDirection Direction = IsocurveDirection.Both) : CurveRequest;

    /// <summary>Extract feature edges with specified angle threshold.</summary>
    public sealed record FeatureEdges(double AngleThreshold) : CurveRequest;

    /// <summary>Point semantic discriminants for geometry analysis.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum matches internal operation kind")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "Non-zero values map to internal operation kinds")]
    public enum PointSemantic : byte {
        /// <summary>Centroids and vertices via mass properties.</summary>
        Analytical = 1,
        /// <summary>Geometric extrema including endpoints, corners, and bounding box vertices.</summary>
        Extremal = 2,
        /// <summary>NURBS Greville points computed from knot vectors.</summary>
        Greville = 3,
        /// <summary>Curve inflection points where curvature changes sign.</summary>
        Inflection = 4,
        /// <summary>Circle/ellipse quadrant points at cardinal angles.</summary>
        Quadrant = 5,
        /// <summary>Topology edge midpoints for Brep, Mesh, and polycurve structures.</summary>
        EdgeMidpoints = 6,
        /// <summary>Topology face centroids computed via area properties.</summary>
        FaceCentroids = 7,
        /// <summary>Osculating frames sampled along curve via perpendicular frame computation.</summary>
        OsculatingFrames = 8,
    }

    /// <summary>Curve semantic discriminants for surface/brep analysis.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum matches internal operation kind")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "Non-zero values map to internal operation kinds")]
    public enum CurveSemantic : byte {
        /// <summary>Boundary curves including outer loop and inner holes.</summary>
        Boundary = 20,
        /// <summary>U-direction isocurves extracted at default parameters.</summary>
        IsocurveU = 21,
        /// <summary>V-direction isocurves extracted at default parameters.</summary>
        IsocurveV = 22,
        /// <summary>Combined U and V isocurves extracted at default parameters.</summary>
        IsocurveUV = 23,
        /// <summary>Sharp feature curves detected via edge angle threshold.</summary>
        FeatureEdges = 24,
    }

    /// <summary>Isocurve extraction direction specification.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum for compact parameter passing")]
    public enum IsocurveDirection : byte {
        /// <summary>U-direction isocurves only.</summary>
        U = 0,
        /// <summary>V-direction isocurves only.</summary>
        V = 1,
        /// <summary>Both U and V direction isocurves.</summary>
        Both = 2,
    }

    /// <summary>Design feature kind discriminant.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum matches internal feature type")]
    public enum FeatureKind : byte {
        /// <summary>Constant radius fillet edge.</summary>
        Fillet = 0,
        /// <summary>Chamfered edge with linear transition.</summary>
        Chamfer = 1,
        /// <summary>Circular or elliptical hole feature.</summary>
        Hole = 2,
        /// <summary>Generic edge without specific feature classification.</summary>
        GenericEdge = 3,
        /// <summary>Variable radius fillet with curvature variation.</summary>
        VariableRadiusFillet = 4,
    }

    /// <summary>Primitive geometry kind discriminant.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum matches internal primitive type")]
    public enum PrimitiveKind : byte {
        /// <summary>Planar surface primitive.</summary>
        Plane = 0,
        /// <summary>Cylindrical surface primitive.</summary>
        Cylinder = 1,
        /// <summary>Spherical surface primitive.</summary>
        Sphere = 2,
        /// <summary>Unknown or unclassified primitive.</summary>
        Unknown = 3,
        /// <summary>Conical surface primitive.</summary>
        Cone = 4,
        /// <summary>Toroidal surface primitive.</summary>
        Torus = 5,
        /// <summary>Extruded surface primitive.</summary>
        Extrusion = 6,
    }

    /// <summary>Pattern kind discriminant for geometric arrays.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte enum matches internal pattern type")]
    public enum PatternKind : byte {
        /// <summary>Linear pattern with constant translation.</summary>
        Linear = 0,
        /// <summary>Radial pattern with rotation about center.</summary>
        Radial = 1,
        /// <summary>Grid pattern with orthogonal basis vectors.</summary>
        Grid = 2,
        /// <summary>Scaling pattern with uniform or non-uniform scale factors.</summary>
        Scaling = 3,
    }

    /// <summary>Design feature with kind discriminant and parameter.</summary>
    public sealed record Feature(FeatureKind Kind, double Parameter);

    /// <summary>Feature extraction result with confidence scoring.</summary>
    public sealed record FeatureExtractionResult(IReadOnlyList<Feature> Features, double Confidence);

    /// <summary>Primitive surface decomposition element.</summary>
    public sealed record Primitive(PrimitiveKind Kind, Plane Frame, IReadOnlyList<double> Parameters);

    /// <summary>Primitive decomposition result with residual measurements.</summary>
    public sealed record PrimitiveDecompositionResult(IReadOnlyList<Primitive> Primitives, IReadOnlyList<double> Residuals);

    /// <summary>Pattern detection result with symmetry transform.</summary>
    public sealed record PatternDetectionResult(PatternKind Kind, Transform SymmetryTransform, double Confidence);

    /// <summary>Extract points from geometry using discriminated union request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, PointRequest request, IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.NormalizePointRequest(request: request, geometryType: input.GetType(), context: context)
            .Bind(normalized => UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(geometry: item, request: normalized, context: context)),
                config: new OperationConfig<T, Point3d> {
                    Context = context,
                    ValidationMode = normalized.ValidationMode,
                    EnableDiagnostics = false,
                }));

    /// <summary>Batch point extraction with error accumulation and optional parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(
        IReadOnlyList<T> geometries,
        PointRequest request,
        IGeometryContext context,
        bool accumulateErrors = true,
        bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Points(input: item, request: request, context: context)),]
            : [.. geometries.Select(item => Points(input: item, request: request, context: context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
        };
    }

    /// <summary>Extract curves from geometry using discriminated union request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Curve>> Curves<T>(T input, CurveRequest request, IGeometryContext context) where T : GeometryBase =>
        ExtractionCore.NormalizeCurveRequest(request: request, geometryType: input.GetType())
            .Bind(normalized => UnifiedOperation.Apply(
                input: input,
                operation: (Func<T, Result<IReadOnlyList<Curve>>>)(item => ExtractionCore.ExecuteCurves(geometry: item, request: normalized, context: context)),
                config: new OperationConfig<T, Curve> {
                    Context = context,
                    ValidationMode = normalized.ValidationMode,
                    EnableDiagnostics = false,
                }));

    /// <summary>Batch curve extraction with error accumulation and optional parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(
        IReadOnlyList<T> geometries,
        CurveRequest request,
        IGeometryContext context,
        bool accumulateErrors = true,
        bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Curve>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Curves(input: item, request: request, context: context)),]
            : [.. geometries.Select(item => Curves(input: item, request: request, context: context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
        };
    }

    /// <summary>Extract design features with confidence scoring.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FeatureExtractionResult> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        ExtractionCompute.ExtractFeatures(brep: brep, context: context)
            .Map(result => new FeatureExtractionResult(
                Features: [.. result.Features.Select(f => new Feature(Kind: (FeatureKind)f.Type, Parameter: f.Param)),],
                Confidence: result.Confidence));

    /// <summary>Decompose geometry into best-fit primitives with residual measurements.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ExtractionCompute.DecomposeToPrimitives(geometry: geometry, context: context)
            .Map(result => new PrimitiveDecompositionResult(
                Primitives: [.. result.Primitives.Select(p => new Primitive(Kind: (PrimitiveKind)p.Type, Frame: p.Frame, Parameters: [.. p.Params,])),],
                Residuals: [.. result.Residuals,]));

    /// <summary>Extract geometric patterns with symmetry transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternDetectionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCompute.ExtractPatterns(geometries: geometries, context: context)
            .Map(result => new PatternDetectionResult(
                Kind: (PatternKind)result.Type,
                SymmetryTransform: result.SymmetryTransform,
                Confidence: result.Confidence));
}
