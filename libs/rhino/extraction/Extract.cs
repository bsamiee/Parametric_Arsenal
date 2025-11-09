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

        /// <summary>Centroids and characteristic vertices via mass properties.</summary>
        public static readonly Semantic Analytical = new(1);

        /// <summary>Geometric extrema: endpoints, corners, bounding box vertices.</summary>
        public static readonly Semantic Extremal = new(2);

        /// <summary>NURBS Greville points from knot vectors.</summary>
        public static readonly Semantic Greville = new(3);

        /// <summary>Curve inflection points where curvature sign changes.</summary>
        public static readonly Semantic Inflection = new(4);

        /// <summary>Circle/ellipse quadrant points (0°, 90°, 180°, 270°).</summary>
        public static readonly Semantic Quadrant = new(5);

        /// <summary>Topology edge midpoints for Brep/Mesh/polycurve.</summary>
        public static readonly Semantic EdgeMidpoints = new(6);

        /// <summary>Topology face centroids via area mass properties.</summary>
        public static readonly Semantic FaceCentroids = new(7);
    }
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase =>
        spec switch {
            int c when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            double l when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            (int c, bool) when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            (double l, bool) when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            Vector3d dir when dir.Length <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidDirection),
            Semantic sem => UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = ExtractionConfig.GetValidationMode(sem.Kind, typeof(T)), }),
            int or double or (int, bool) or (double, bool) or Vector3d or Continuity => UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = V.Standard, }),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction),
        };

    /// <summary>Batch point extraction with error accumulation and parallel support.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, object spec, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = [.. (enableParallel ? geometries.AsParallel() : geometries.AsEnumerable()).Select(item => Points(item, spec, context)),];
        return (accumulateErrors, results.All(r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(r => !r.IsSuccess).SelectMany(r => r.Errors),]),
            (false, _) => results.FirstOrDefault(r => !r.IsSuccess) is { IsSuccess: false } failure ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,]) : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(r => r.Value),]),
        };
    }
}
