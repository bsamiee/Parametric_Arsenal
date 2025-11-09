using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point extraction with singular API.</summary>
public static class Extract {
    /// <summary>Centroids and characteristic vertices (corners, midpoints) via mass properties.</summary>
    public const byte Analytical = 1;
    /// <summary>Geometric extrema: curve endpoints, surface domain corners, bounding box vertices.</summary>
    public const byte Extremal = 2;
    /// <summary>NURBS Greville points computed from knot vectors.</summary>
    public const byte Greville = 3;
    /// <summary>Curve inflection points where curvature sign changes.</summary>
    public const byte Inflection = 4;
    /// <summary>Circle/ellipse quadrant points at 0°, 90°, 180°, 270°.</summary>
    public const byte Quadrant = 5;
    /// <summary>Topology edge midpoints for Brep, Mesh, and polycurve structures.</summary>
    public const byte EdgeMidpoints = 6;
    /// <summary>Topology face centroids via area mass properties.</summary>
    public const byte FaceCentroids = 7;
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Points<T>(T input, object spec, IGeometryContext context) where T : GeometryBase =>
        spec switch {
            int c when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            (int c, bool) when c <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidCount),
            double l when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            (double l, bool) when l <= 0 => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidLength),
            Vector3d { Length: var len } when len <= context.AbsoluteTolerance => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidDirection),
            byte k when k is >= 1 and <= 7 => UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = ExtractionConfig.GetValidationMode(k, typeof(T)) }),
            int or double or (int, bool) or (double, bool) or Vector3d or Continuity => UnifiedOperation.Apply(input, (Func<T, Result<IReadOnlyList<Point3d>>>)(item => ExtractionCore.Execute(item, spec, context)), new OperationConfig<T, Point3d> { Context = context, ValidationMode = V.Standard }),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction),
        };

    /// <summary>Extracts points from heterogeneous geometry collections with unified error accumulation and parallel execution support.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<IReadOnlyList<Point3d>>> PointsMultiple<T>(IReadOnlyList<T> geometries, object spec, IGeometryContext context, bool accumulateErrors = true, bool enableParallel = false) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = [.. (enableParallel ? geometries.AsParallel() : geometries.AsEnumerable()).Select(item => Points(item, spec, context)),];
        IReadOnlyList<IReadOnlyList<Point3d>> successValues = [.. results.Select(r => r.Value),];
        return accumulateErrors
            ? results.All(r => r.IsSuccess) ? ResultFactory.Create(value: successValues) : ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(r => !r.IsSuccess).SelectMany(r => r.Errors),])
            : results.FirstOrDefault(r => !r.IsSuccess) is Result<IReadOnlyList<Point3d>> failure ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,]) : ResultFactory.Create(value: successValues);
    }
}
