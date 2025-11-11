using System.Diagnostics.Contracts;
using System.Linq;
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

        internal Request(byte kind, object? parameter, bool includeEnds) {
            Kind = kind;
            Parameter = parameter;
            IncludeEnds = includeEnds;
        }
    }
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase =>
        (Result<Request>)((spec, context.AbsoluteTolerance) switch {
            (int count, _) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            (int count, _) => ResultFactory.Create(value: new Request(10, count, true)),
            ((int count, bool include), _) when count <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidCount),
            ((int count, bool include), _) => ResultFactory.Create(value: new Request(10, count, include)),
            (double length, _) when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            (double length, _) => ResultFactory.Create(value: new Request(11, length, true)),
            ((double length, bool include), _) when length <= 0 => ResultFactory.Create<Request>(error: E.Geometry.InvalidLength),
            ((double length, bool include), _) => ResultFactory.Create(value: new Request(11, length, include)),
            (Vector3d direction, double tolerance) when direction.Length <= tolerance => ResultFactory.Create<Request>(error: E.Geometry.InvalidDirection),
            (Vector3d direction, _) => ResultFactory.Create(value: new Request(12, direction, true)),
            (Continuity continuity, _) => ResultFactory.Create(value: new Request(13, continuity, true)),
            (Semantic semantic, _) => ResultFactory.Create(value: new Request(semantic.Kind, null, true)),
            _ => ResultFactory.Create<Request>(error: E.Geometry.InvalidExtraction),
        })).Bind(request =>
            UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, request, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = ExtractionConfig.GetValidationMode(request.Kind, input.GetType()), }));

    /// <summary>Batch point extraction with error accumulation and parallelism.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, object spec, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = [.. (enableParallel ? geometries.AsParallel() : geometries.AsEnumerable()).Select(item => Points(item, spec, context)),];
        Result<IReadOnlyList<Point3d>>[] failures = [.. results.Where(r => !r.IsSuccess),];
        return (accumulateErrors, failures.Length) switch {
            (true, 0) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),]),
            (true, > 0) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failures.SelectMany(f => f.Errors),]),
            (false, > 0) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failures[0].Errors,]),
            (false, 0) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),]),
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
