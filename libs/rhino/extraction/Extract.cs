using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Singular polymorphic extraction API with hidden configuration dispatch.</summary>
public static class Extract {
    /// <summary>Extracts analytical features (centroids, control points, vertices).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Analytical<T>(T input, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.Analytical(), context);

    /// <summary>Extracts extremal points (endpoints, corners, bounding box).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Extremal<T>(T input, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.Extremal(), context);

    /// <summary>Extracts quadrant points from circular or elliptical curves.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Quadrant<T>(T input, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.Quadrant(), context);

    /// <summary>Extracts edge midpoints from topological geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> EdgeMidpoints<T>(T input, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.EdgeMidpoints(), context);

    /// <summary>Extracts Greville points from NURBS geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Greville<T>(T input, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.Greville(), context);

    /// <summary>Extracts inflection points from curves.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Inflection<T>(T input, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.Inflection(), context);

    /// <summary>Extracts face centroids from Brep geometry.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> FaceCentroids<T>(T input, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.FaceCentroids(), context);

    /// <summary>Extracts uniformly distributed points with specified count.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Uniform<T>(T input, int count, IGeometryContext context, bool includeEnds = true) where T : notnull =>
        Apply(input, new ExtractionConfig.UniformByCount(count, includeEnds), context);

    /// <summary>Extracts uniformly distributed points with specified length interval.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Uniform<T>(T input, double length, IGeometryContext context, bool includeEnds = true) where T : notnull =>
        Apply(input, new ExtractionConfig.UniformByLength(length, includeEnds), context);

    /// <summary>Extracts discontinuity points with specified continuity.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Discontinuities<T>(T input, IGeometryContext context, Continuity continuity = Continuity.C1_continuous) where T : notnull =>
        Apply(input, new ExtractionConfig.Discontinuities(continuity), context);

    /// <summary>Extracts positional extrema along specified direction.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> PositionalExtrema<T>(T input, Vector3d direction, IGeometryContext context) where T : notnull =>
        Apply(input, new ExtractionConfig.PositionalExtrema(direction), context);

    /// <summary>Core polymorphic dispatch with UnifiedOperation integration.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<IReadOnlyList<Point3d>> Apply<T>(T input, ExtractionConfig config, IGeometryContext context) where T : notnull =>
        UnifiedOperation.Apply(
            input,
            (Func<object, Result<IReadOnlyList<Point3d>>>)(item => item switch {
                GeometryBase g => ExtractionOps.Execute(g, config, context),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ValidationErrors.Geometry.Invalid),
            }),
            new OperationConfig<object, Point3d> {
                Context = context,
                ValidationMode = ValidationMode.None,
                AccumulateErrors = false,
            });
}
