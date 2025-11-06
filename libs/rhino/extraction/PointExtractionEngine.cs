using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Polymorphic point extraction engine with unified operation dispatch.</summary>
public static class PointExtractionEngine {
    /// <summary>Extracts points from geometry with validation and error handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<Point3d>> Extract<T>(
        T input,
        ExtractionMethod method,
        IGeometryContext context,
        int? count = null,
        double? length = null,
        bool includeEnds = true,
        Vector3d? direction = null,
        Continuity continuity = Continuity.C1_continuous) where T : notnull =>
        UnifiedOperation.Apply(
            input,
            (Func<object, Result<IReadOnlyList<Point3d>>>)(item => item switch {
                GeometryBase g => ExtractionStrategies.Extract(g, method, context, count, length, includeEnds, direction, continuity),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: ValidationErrors.Geometry.Invalid),
            }),
            new OperationConfig<object, Point3d> {
                Context = context,
                ValidationMode = ValidationMode.None,  // Validation handled by ExtractionStrategies
                AccumulateErrors = false,
            });
}
