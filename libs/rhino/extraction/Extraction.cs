using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic extraction utilities spanning points, curves, and higher-order analyses.</summary>
public static class Extraction {
    /// <summary>Request for point sampling or semantic extraction.</summary>
    public abstract record PointRequest;

    /// <summary>Semantic point extraction (analytical, extrema, greville, etc.).</summary>
    public sealed record SemanticPoint(PointSemantic Semantic, int? SampleCount = null) : PointRequest;

    /// <summary>Divide geometry by uniform count (curves or surfaces).</summary>
    public sealed record DivideByCount(int Count, bool IncludeEndpoints = true) : PointRequest;

    /// <summary>Divide geometry by uniform segment length.</summary>
    public sealed record DivideByLength(double Length, bool IncludeEndpoints = true) : PointRequest;

    /// <summary>Extract extremal points along a direction.</summary>
    public sealed record DirectionalExtrema(Vector3d Direction) : PointRequest;

    /// <summary>Extract discontinuities based on required continuity.</summary>
    public sealed record Discontinuities(Continuity Continuity) : PointRequest;

    /// <summary>Request for curve extraction (boundary, isocurves, feature edges).</summary>
    public abstract record CurveRequest;

    /// <summary>Semantic curve extraction (boundary loops, isocurves, default feature edges).</summary>
    public sealed record SemanticCurves(CurveSemantic Semantic) : CurveRequest;

    /// <summary>Uniform U/V isocurves across both directions.</summary>
    public sealed record UniformIsocurves(int Count) : CurveRequest;

    /// <summary>Directional uniform isocurves.</summary>
    public sealed record DirectionalIsocurves(int Count, IsocurveDirection Direction) : CurveRequest;

    /// <summary>Isocurves at explicit normalized parameters for both directions.</summary>
    public sealed record ParameterIsocurves(IReadOnlyList<double> Parameters) : CurveRequest;

    /// <summary>Directional parameter isocurves.</summary>
    public sealed record DirectionalParameterIsocurves(IReadOnlyList<double> Parameters, IsocurveDirection Direction) : CurveRequest;

    /// <summary>Feature edges with custom threshold.</summary>
    public sealed record FeatureEdgesByAngle(double AngleThreshold) : CurveRequest;

    /// <summary>Semantic discriminant for point extraction.</summary>
    public sealed record PointSemantic {
        private PointSemantic(PointOperationKind operation, string name) {
            this.Operation = operation;
            this.Name = name;
        }

        internal PointOperationKind Operation { get; }
        public string Name { get; }

        public static PointSemantic Analytical { get; } = new(PointOperationKind.Analytical, "Analytical");
        public static PointSemantic Extremal { get; } = new(PointOperationKind.Extremal, "Extremal");
        public static PointSemantic Greville { get; } = new(PointOperationKind.Greville, "Greville");
        public static PointSemantic Inflection { get; } = new(PointOperationKind.Inflection, "Inflection");
        public static PointSemantic Quadrant { get; } = new(PointOperationKind.Quadrant, "Quadrant");
        public static PointSemantic EdgeMidpoints { get; } = new(PointOperationKind.EdgeMidpoints, "EdgeMidpoints");
        public static PointSemantic FaceCentroids { get; } = new(PointOperationKind.FaceCentroids, "FaceCentroids");
        public static PointSemantic OsculatingFrames { get; } = new(PointOperationKind.OsculatingFrames, "OsculatingFrames");
    }

    /// <summary>Semantic discriminant for curve extraction.</summary>
    public sealed record CurveSemantic {
        private CurveSemantic(CurveOperationKind operation, string name) {
            this.Operation = operation;
            this.Name = name;
        }

        internal CurveOperationKind Operation { get; }
        public string Name { get; }

        public static CurveSemantic Boundary { get; } = new(CurveOperationKind.Boundary, "Boundary");
        public static CurveSemantic IsocurveU { get; } = new(CurveOperationKind.IsocurveU, "IsocurveU");
        public static CurveSemantic IsocurveV { get; } = new(CurveOperationKind.IsocurveV, "IsocurveV");
        public static CurveSemantic IsocurveUV { get; } = new(CurveOperationKind.IsocurveUV, "IsocurveUV");
        public static CurveSemantic FeatureEdges { get; } = new(CurveOperationKind.FeatureEdges, "FeatureEdges");
    }

    /// <summary>Isocurve direction selection.</summary>
    public enum IsocurveDirection : byte {
        U = 0,
        V = 1,
        Both = 2,
    }

    /// <summary>Feature extraction result.</summary>
    public sealed record FeatureExtractionResult(IReadOnlyList<Feature> Features, double Confidence);

    /// <summary>Feature descriptor.</summary>
    public sealed record Feature(FeatureKind Kind, double Parameter);

    /// <summary>Feature kind discriminant.</summary>
    public sealed record FeatureKind {
        private FeatureKind(byte code, string name) {
            this.Code = code;
            this.Name = name;
        }

        internal byte Code { get; }
        public string Name { get; }

        public static FeatureKind Fillet { get; } = new(0, "Fillet");
        public static FeatureKind Chamfer { get; } = new(1, "Chamfer");
        public static FeatureKind Hole { get; } = new(2, "Hole");
        public static FeatureKind GenericEdge { get; } = new(3, "GenericEdge");
        public static FeatureKind VariableRadiusFillet { get; } = new(4, "VariableRadiusFillet");
    }

    /// <summary>Primitive decomposition result.</summary>
    public sealed record PrimitiveDecompositionResult(IReadOnlyList<Primitive> Primitives, IReadOnlyList<double> Residuals);

    /// <summary>Primitive descriptor.</summary>
    public sealed record Primitive(PrimitiveKind Kind, Plane Frame, IReadOnlyList<double> Parameters);

    /// <summary>Primitive kind.</summary>
    public sealed record PrimitiveKind {
        private PrimitiveKind(byte code, string name) {
            this.Code = code;
            this.Name = name;
        }

        internal byte Code { get; }
        public string Name { get; }

        public static PrimitiveKind Plane { get; } = new(0, "Plane");
        public static PrimitiveKind Cylinder { get; } = new(1, "Cylinder");
        public static PrimitiveKind Sphere { get; } = new(2, "Sphere");
        public static PrimitiveKind Unknown { get; } = new(3, "Unknown");
        public static PrimitiveKind Cone { get; } = new(4, "Cone");
        public static PrimitiveKind Torus { get; } = new(5, "Torus");
        public static PrimitiveKind Extrusion { get; } = new(6, "Extrusion");
    }

    /// <summary>Pattern detection result.</summary>
    public sealed record PatternDetectionResult(PatternKind Kind, Transform SymmetryTransform, double Confidence);

    /// <summary>Pattern kind discriminant.</summary>
    public sealed record PatternKind {
        private PatternKind(byte code, string name) {
            this.Code = code;
            this.Name = name;
        }

        internal byte Code { get; }
        public string Name { get; }

        public static PatternKind Linear { get; } = new(0, "Linear");
        public static PatternKind Radial { get; } = new(1, "Radial");
        public static PatternKind Grid { get; } = new(2, "Grid");
        public static PatternKind Scaling { get; } = new(3, "Scaling");
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, PointRequest request, IGeometryContext context) where T : GeometryBase {
        if (request is null) {
            return ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Validation.GeometryInvalid.WithContext("Point request is null"));
        }

        return ExtractionCore.ExecutePoints(input, request, context);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, PointRequest request, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        if (request is null) {
            return ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(error: E.Validation.GeometryInvalid.WithContext("Point request is null"));
        }

        Result<IReadOnlyList<Point3d>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Points(item, request, context)),]
            : [.. geometries.Select(item => Points(item, request, context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Curve>> Curves<T>(T input, CurveRequest request, IGeometryContext context) where T : GeometryBase {
        if (request is null) {
            return ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Validation.GeometryInvalid.WithContext("Curve request is null"));
        }

        return ExtractionCore.ExecuteCurves(input, request, context);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(IReadOnlyList<T> geometries, CurveRequest request, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        if (request is null) {
            return ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(error: E.Validation.GeometryInvalid.WithContext("Curve request is null"));
        }

        Result<IReadOnlyList<Curve>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Curves(item, request, context)),]
            : [.. geometries.Select(item => Curves(item, request, context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
        };
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<FeatureExtractionResult> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        ExtractionCompute.ExtractFeatures(brep, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ExtractionCompute.DecomposeToPrimitives(geometry, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<PatternDetectionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCompute.ExtractPatterns(geometries, context);

    internal enum PointOperationKind : byte {
        Analytical = 1,
        Extremal = 2,
        Greville = 3,
        Inflection = 4,
        Quadrant = 5,
        EdgeMidpoints = 6,
        FaceCentroids = 7,
        OsculatingFrames = 8,
        DivideByCount = 10,
        DivideByLength = 11,
        DirectionalExtrema = 12,
        Discontinuities = 13,
    }

    internal enum CurveOperationKind : byte {
        Boundary = 20,
        IsocurveU = 21,
        IsocurveV = 22,
        IsocurveUV = 23,
        FeatureEdges = 24,
        UniformIsocurves = 30,
        DirectionalIsocurves = 31,
        ParameterIsocurves = 32,
        ParameterDirectionalIsocurves = 33,
        CustomFeatureEdges = 34,
    }
}
