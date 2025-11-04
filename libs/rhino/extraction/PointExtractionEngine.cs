using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point extraction with unified single/collection handling and monadic error composition.</summary>
public static class PointExtractionEngine {
    /// <summary>Extracts points from geometry using strategy patterns with automatic validation and error accumulation.</summary>
    [Pure]
    public static Result<IReadOnlyList<Point3d>> Extract<T>(
        T input,
        ExtractionMethod method,
        IGeometryContext context,
        int? count = null,
        double? length = null,
        bool includeEnds = true) where T : notnull {
        ArgumentNullException.ThrowIfNull(context);

        return input switch {
            // Single geometry path with inline validation mode determination
            GeometryBase single => ResultFactory.Create(value: single)
                .Validate(args: [context, method switch {
                    ExtractionMethod.Analytical => ValidationMode.Standard | (single switch {
                        Brep => ValidationMode.MassProperties,
                        Curve or Surface => ValidationMode.AreaCentroid,
                        _ => ValidationMode.None,
                    }),
                    ExtractionMethod.Uniform => ValidationMode.Standard | ValidationMode.Degeneracy,
                    ExtractionMethod.Extremal => ValidationMode.BoundingBox,
                    ExtractionMethod.Quadrant => ValidationMode.Tolerance,
                    _ => ValidationMode.Standard,
                }])
                .Bind(g => ExtractionStrategies.Extract(g, method, context, count, length, includeEnds)),

            // Empty collection optimization
            IReadOnlyList<GeometryBase> list when list.Count == 0 =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[]),

            // Single item collection optimization
            IReadOnlyList<GeometryBase> list when list.Count == 1 =>
                Extract(list[0], method, context, count, length, includeEnds),

            // Multiple geometries with fail-fast accumulation
            IReadOnlyList<GeometryBase> list => list
                .Select(g => Extract(g, method, context, count, length, includeEnds))
                .Aggregate(
                    ResultFactory.Create(value: (IReadOnlyList<Point3d>)new List<Point3d>().AsReadOnly()),
                    (acc, curr) => acc.Bind(accPoints =>
                        curr.Map(newPoints => {
                            List<Point3d> combined = [.. accPoints, .. newPoints];
                            return (IReadOnlyList<Point3d>)combined.AsReadOnly();
                        }))),

            // IEnumerable conversion to array for processing
            IEnumerable<GeometryBase> enumerable =>
                Extract(enumerable.ToArray(), method, context, count, length, includeEnds),

            // Invalid input type
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(
                error: ValidationErrors.Geometry.Invalid),
        };
    }
}
