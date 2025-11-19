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
    /// <summary>Semantic extraction mode discriminating point/curve operation types.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct Semantic(byte operation) {
        internal readonly byte _operation = operation;
        internal ExtractionConfig.OpType Operation => (ExtractionConfig.OpType)this._operation;

        /// <summary>Centroids and vertices via mass properties.</summary>
        public static readonly Semantic Analytical = new((byte)ExtractionConfig.OpType.Analytical);
        /// <summary>Geometric extrema including endpoints, corners, and bounding box vertices.</summary>
        public static readonly Semantic Extremal = new((byte)ExtractionConfig.OpType.Extremal);
        /// <summary>NURBS Greville points computed from knot vectors.</summary>
        public static readonly Semantic Greville = new((byte)ExtractionConfig.OpType.Greville);
        /// <summary>Curve inflection points where curvature changes sign.</summary>
        public static readonly Semantic Inflection = new((byte)ExtractionConfig.OpType.Inflection);
        /// <summary>Circle/ellipse quadrant points at cardinal angles.</summary>
        public static readonly Semantic Quadrant = new((byte)ExtractionConfig.OpType.Quadrant);
        /// <summary>Topology edge midpoints for Brep, Mesh, and polycurve structures.</summary>
        public static readonly Semantic EdgeMidpoints = new((byte)ExtractionConfig.OpType.EdgeMidpoints);
        /// <summary>Topology face centroids computed via area properties.</summary>
        public static readonly Semantic FaceCentroids = new((byte)ExtractionConfig.OpType.FaceCentroids);
        /// <summary>Osculating frames sampled along curve via perpendicular frame computation.</summary>
        public static readonly Semantic OsculatingFrames = new((byte)ExtractionConfig.OpType.OsculatingFrames);
        /// <summary>Boundary curves including outer loop and inner holes.</summary>
        public static readonly Semantic Boundary = new((byte)ExtractionConfig.OpType.Boundary);
        /// <summary>U-direction isocurves extracted at specified parameters.</summary>
        public static readonly Semantic IsocurveU = new((byte)ExtractionConfig.OpType.IsocurveU);
        /// <summary>V-direction isocurves extracted at specified parameters.</summary>
        public static readonly Semantic IsocurveV = new((byte)ExtractionConfig.OpType.IsocurveV);
        /// <summary>Combined U and V isocurves extracted at specified parameters.</summary>
        public static readonly Semantic IsocurveUV = new((byte)ExtractionConfig.OpType.IsocurveUV);
        /// <summary>Sharp feature curves detected via edge angle threshold.</summary>
        public static readonly Semantic FeatureEdges = new((byte)ExtractionConfig.OpType.FeatureEdges);
    }

    /// <summary>Normalized extraction request computed from heterogeneous specifications.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    internal readonly struct Request {
        internal readonly ExtractionConfig.OpType Operation;
        internal readonly object? Parameter;
        internal readonly bool IncludeEnds;
        internal readonly V ValidationMode;
        internal readonly string OperationName;

        internal Request(ExtractionConfig.OpType operation, object? parameter, bool includeEnds, V validationMode, string operationName) {
            this.Operation = operation;
            this.Parameter = parameter;
            this.IncludeEnds = includeEnds;
            this.ValidationMode = validationMode;
            this.OperationName = operationName;
        }
    }
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase {
        Type geometryType = input.GetType();

        Result<Request> requestResult = spec switch {
            int count when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            int count => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.DivideByCount, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.DivideByCount, parameter: count, includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            (int count, bool include) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            (int count, bool include) => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.DivideByCount, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.DivideByCount, parameter: count, includeEnds: include, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            double length when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            double length => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.DivideByLength, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.DivideByLength, parameter: length, includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            (double length, bool include) when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            (double length, bool include) => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.DivideByLength, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.DivideByLength, parameter: length, includeEnds: include, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            Vector3d direction when direction.Length <= context.AbsoluteTolerance => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection),
            Vector3d direction => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.DirectionalExtrema, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.DirectionalExtrema, parameter: direction, includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            Continuity continuity => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.Discontinuities, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.Discontinuities, parameter: continuity, includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            Semantic semantic => ExtractionConfig.GetOperationMeta(semantic.Operation, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: semantic.Operation, parameter: null, includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            _ => ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
        };

        return requestResult.Bind(request =>
            UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, request, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = request.ValidationMode, OperationName = request.OperationName, EnableDiagnostics = false, }));
    }

    /// <summary>Batch point extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, object spec, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Points(item, spec, context)),]
            : [.. geometries.Select(item => Points(item, spec, context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
        };
    }

    /// <summary>Extract curves with semantic/parameterized modes.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Curve>> Curves<T>(T input, object spec, IGeometryContext context) where T : GeometryBase {
        Type geometryType = input.GetType();

        Result<Request> requestResult = spec switch {
            int count when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            int count => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.UniformIsocurves, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.UniformIsocurves, parameter: count, includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            (int count, byte direction) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            (int count, byte direction) when direction > 2 => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection.WithContext("Direction must be 0(U), 1(V), or 2(Both)")),
            (int count, byte direction) => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.DirectionalIsocurves, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.DirectionalIsocurves, parameter: (count, direction), includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            double[] parameters when parameters.Length == 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty")),
            double[] parameters => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.ParameterIsocurves, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.ParameterIsocurves, parameter: parameters, includeEnds: false, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            (double[] parameters, byte direction) when parameters.Length == 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty")),
            (double[] parameters, byte direction) when direction > 2 => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection.WithContext("Direction must be 0(U), 1(V), or 2(Both)")),
            (double[] parameters, byte direction) => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.DirectionalParameterIsocurves, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.DirectionalParameterIsocurves, parameter: (parameters, direction), includeEnds: false, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            double angleThreshold when angleThreshold <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidAngle.WithContext("Angle threshold must be positive")),
            double angleThreshold => ExtractionConfig.GetOperationMeta(ExtractionConfig.OpType.CustomFeatureEdges, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: ExtractionConfig.OpType.CustomFeatureEdges, parameter: angleThreshold, includeEnds: false, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            Semantic semantic => ExtractionConfig.GetOperationMeta(semantic.Operation, geometryType) is (V mode, string name)
                ? ResultFactory.Create(value: new Request(operation: semantic.Operation, parameter: null, includeEnds: true, validationMode: mode, operationName: name))
                : ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
            _ => ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction.WithContext("Unsupported curve extraction specification")),
        };

        return requestResult.Bind(request =>
            UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Curve>>>)(item => ExtractionCore.ExecuteCurves(item, request, context)), new OperationConfig<T, Curve> { Context = context, ValidationMode = request.ValidationMode, OperationName = request.OperationName, EnableDiagnostics = false, }));
    }

    /// <summary>Batch curve extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Curve>>> CurvesMultiple<T>(IReadOnlyList<T> geometries, object spec, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Curve>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => Curves(item, spec, context)),]
            : [.. geometries.Select(item => Curves(item, spec, context)),];
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
    public static Result<((byte Type, double Parameter)[] Features, double Confidence)> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        ExtractionCompute.ExtractFeatures(brep, context);

    /// <summary>Decomposes geometry into best-fit primitives with residual measurements.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<((byte Type, Plane Frame, double[] Parameters)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ExtractionCompute.DecomposeToPrimitives(geometry, context);

    /// <summary>Extracts geometric patterns (linear, radial, grid, scaling) with symmetry transforms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCompute.ExtractPatterns(geometries, context: context);
}
