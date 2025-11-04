using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point extraction using unified operation dispatch.</summary>
public static class PointExtractionEngine {
    /// <summary>Extracts points from geometry with automatic validation and error accumulation.</summary>
    [Pure]
    public static Result<IReadOnlyList<Point3d>> Extract<T>(
        T input,
        ExtractionMethod method,
        IGeometryContext context,
        int? count = null,
        double? length = null,
        bool includeEnds = true) where T : notnull =>
        UnifiedOperation.Apply(
            (object)input!,
            (object item) => item switch {
                GeometryBase g => ExtractionStrategies.Extract(g, method, context, count, length, includeEnds),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ValidationErrors.Geometry.Invalid),
            },
            new OperationConfig<object, Point3d> {
                Context = context,
                ValidationMode = ValidationMode.None,  // ExtractionStrategies already validates
                ErrorStrategy = ErrorStrategy.FailFast,
            });
}
