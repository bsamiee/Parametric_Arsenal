using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point extraction from geometry.</summary>
public static class Extract {
    /// <summary>Semantic extraction mode for point operations.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Semantic(byte kind) {
        internal readonly byte Kind = kind;

        /// <summary>Centroids and vertices via mass properties.</summary>
        public static readonly Semantic Analytical = new(1);

        /// <summary>Geometric extrema: endpoints, corners, bbox vertices.</summary>
        public static readonly Semantic Extremal = new(2);

        /// <summary>NURBS Greville points from knot vectors.</summary>
        public static readonly Semantic Greville = new(3);

        /// <summary>Curve inflection where curvature sign changes.</summary>
        public static readonly Semantic Inflection = new(4);

        /// <summary>Circle/ellipse quadrant points (0°, 90°, 180°, 270°).</summary>
        public static readonly Semantic Quadrant = new(5);

        /// <summary>Topology edge midpoints for Brep/Mesh/polycurve.</summary>
        public static readonly Semantic EdgeMidpoints = new(6);

        /// <summary>Topology face centroids via area properties.</summary>
        public static readonly Semantic FaceCentroids = new(7);

        /// <summary>Boundary curves: outer loop and holes.</summary>
        public static readonly Semantic Boundary = new(20);

        /// <summary>U-direction isocurves at parameters.</summary>
        public static readonly Semantic IsocurveU = new(21);

        /// <summary>V-direction isocurves at parameters.</summary>
        public static readonly Semantic IsocurveV = new(22);

        /// <summary>Both U and V isocurves at parameters.</summary>
        public static readonly Semantic IsocurveUV = new(23);

        /// <summary>Sharp feature curves (edge angle threshold).</summary>
        public static readonly Semantic FeatureEdges = new(24);
    }

    /// <summary>Normalized extraction request computed from heterogeneous specifications.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Request {
        internal readonly byte Kind;
        internal readonly object? Parameter;
        internal readonly bool IncludeEnds;
        internal readonly V ValidationMode;

        internal Request(byte kind, object? parameter, bool includeEnds, V validationMode) {
            this.Kind = kind;
            this.Parameter = parameter;
            this.IncludeEnds = includeEnds;
            this.ValidationMode = validationMode;
        }
    }
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase {
        Type geometryType = input.GetType();

        Result<Request> requestResult = spec switch {
            int count when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            int count => ResultFactory.Create(value: new Request(kind: 10, parameter: count, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(10, geometryType))),
            (int count, bool include) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            (int count, bool include) => ResultFactory.Create(value: new Request(kind: 10, parameter: count, includeEnds: include, validationMode: ExtractionConfig.GetValidationMode(10, geometryType))),
            double length when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            double length => ResultFactory.Create(value: new Request(kind: 11, parameter: length, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(11, geometryType))),
            (double length, bool include) when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            (double length, bool include) => ResultFactory.Create(value: new Request(kind: 11, parameter: length, includeEnds: include, validationMode: ExtractionConfig.GetValidationMode(11, geometryType))),
            Vector3d direction when direction.Length <= context.AbsoluteTolerance => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection),
            Vector3d direction => ResultFactory.Create(value: new Request(kind: 12, parameter: direction, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(12, geometryType))),
            Continuity continuity => ResultFactory.Create(value: new Request(kind: 13, parameter: continuity, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(13, geometryType))),
            Semantic semantic => ResultFactory.Create(value: new Request(kind: semantic.Kind, parameter: null, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(semantic.Kind, geometryType))),
            _ => ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
        };

        return requestResult.Bind(request =>
            UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, request, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = request.ValidationMode, EnableDiagnostics = false, }));
    }

    /// <summary>Batch point extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, object spec, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = [.. (enableParallel ? geometries.AsParallel() : geometries.AsEnumerable()).Select(item => Points(item, spec, context)),];
        return (accumulateErrors, results.All(r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(r => !r.IsSuccess).SelectMany(r => r.Errors),]),
            (false, _) => results.FirstOrDefault(r => !r.IsSuccess) is { IsSuccess: false } failure ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,]) : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),]),
        };
    }

    /// <summary>Extract curves with semantic/parameterized modes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Curve>> Curves<T>(T input, object spec, IGeometryContext context) where T : GeometryBase {
        Type geometryType = input.GetType();

        Result<Request> requestResult = spec switch {
            int count when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            int count => ResultFactory.Create(value: new Request(kind: 30, parameter: count, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(kind: 30, geometryType))),
            (int count, byte direction) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            (int count, byte direction) when direction > 2 => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection.WithContext("Direction must be 0(U), 1(V), or 2(Both)")),
            (int count, byte direction) => ResultFactory.Create(value: new Request(kind: 31, parameter: (count, direction), includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(kind: 31, geometryType))),
            double[] parameters when parameters.Length == 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty")),
            double[] parameters => ResultFactory.Create(value: new Request(kind: 32, parameter: parameters, includeEnds: false, validationMode: ExtractionConfig.GetValidationMode(kind: 32, geometryType))),
            (double[] parameters, byte direction) when parameters.Length == 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty")),
            (double[] parameters, byte direction) when direction > 2 => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection.WithContext("Direction must be 0(U), 1(V), or 2(Both)")),
            (double[] parameters, byte direction) => ResultFactory.Create(value: new Request(kind: 33, parameter: (parameters, direction), includeEnds: false, validationMode: ExtractionConfig.GetValidationMode(kind: 33, geometryType))),
            double angleThreshold when angleThreshold <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidAngle.WithContext("Angle threshold must be positive")),
            double angleThreshold => ResultFactory.Create(value: new Request(kind: 34, parameter: angleThreshold, includeEnds: false, validationMode: ExtractionConfig.GetValidationMode(kind: 34, geometryType))),
            Semantic semantic => ResultFactory.Create(value: new Request(kind: semantic.Kind, parameter: null, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(kind: semantic.Kind, geometryType))),
            _ => ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction.WithContext("Unsupported curve extraction specification")),
        };

        return requestResult.Bind(request =>
            UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Curve>>>)(item => ExtractionCore.ExecuteCurves(item, request, context)), new OperationConfig<T, Curve> { Context = context, ValidationMode = request.ValidationMode, EnableDiagnostics = false, }));
    }

    /// <summary>Batch curve extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(IReadOnlyList<T> geometries, object spec, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Curve>>[] results = [.. (enableParallel ? geometries.AsParallel() : geometries.AsEnumerable()).Select(item => Curves(item, spec, context)),];
        return (accumulateErrors, results.All(r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. results.Where(r => !r.IsSuccess).SelectMany(r => r.Errors),]),
            (false, _) => results.FirstOrDefault(r => !r.IsSuccess) is { IsSuccess: false } failure ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. failure.Errors,]) : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(r => r.Value),]),
        };
    }

    /// <summary>Extract design features: fillets, chamfers, holes, bosses with confidence scores.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<((byte Type, double Parameter)[] Features, double Confidence)> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        ExtractionCompute.ExtractFeatures(brep, context);

    /// <summary>Decompose geometry to best-fit primitives: planes, cylinders, spheres with residuals.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<((byte Type, Plane Frame, double[] Parameters)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ExtractionCompute.DecomposeToPrimitives(geometry, context);

    /// <summary>Extract geometric patterns: symmetries, sequences, transformations with confidence.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCompute.ExtractPatterns(geometries, context: context);
}
