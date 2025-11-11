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

        Result<Request> requestResult = (spec, context.AbsoluteTolerance) switch {
            (int count, _) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            (int count, double tolerance) => ResultFactory.Create(value: new Request(kind: 10, parameter: count, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(10, geometryType))),
            ((int count, bool include), _) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            ((int count, bool include), double tolerance) => ResultFactory.Create(value: new Request(kind: 10, parameter: count, includeEnds: include, validationMode: ExtractionConfig.GetValidationMode(10, geometryType))),
            (double length, _) when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            (double length, double tolerance) => ResultFactory.Create(value: new Request(kind: 11, parameter: length, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(11, geometryType))),
            ((double length, bool include), _) when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            ((double length, bool include), double tolerance) => ResultFactory.Create(value: new Request(kind: 11, parameter: length, includeEnds: include, validationMode: ExtractionConfig.GetValidationMode(11, geometryType))),
            (Vector3d direction, double tolerance) when direction.Length <= tolerance => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection),
            (Vector3d direction, double tolerance) => ResultFactory.Create(value: new Request(kind: 12, parameter: direction, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(12, geometryType))),
            (Continuity continuity, double tolerance) => ResultFactory.Create(value: new Request(kind: 13, parameter: continuity, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(13, geometryType))),
            (Semantic semantic, double tolerance) => ResultFactory.Create(value: new Request(kind: semantic.Kind, parameter: null, includeEnds: true, validationMode: ExtractionConfig.GetValidationMode(semantic.Kind, geometryType))),
            _ => ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
        };

        return requestResult.Bind(request =>
            UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, request, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = request.ValidationMode, }));
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
