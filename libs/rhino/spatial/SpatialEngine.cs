using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial indexing using unified operation dispatch.</summary>
public static class SpatialEngine {

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Index<T>(
        T input,
        SpatialMethod method,
        IGeometryContext context,
        object? queryShape = null,
        IEnumerable<Point3d>? needles = null,
        int? k = null,
        double? limitDistance = null,
        double? toleranceBuffer = null) where T : notnull =>
        input switch {
            Point3d[] points => SpatialStrategies.Index(points, method, context, queryShape, needles, k, limitDistance, toleranceBuffer),
            GeometryBase geometry => SpatialStrategies.Index(geometry, method, context, queryShape, needles, k, limitDistance, toleranceBuffer),
            ValueTuple<Mesh, Mesh> meshPair => SpatialStrategies.Index(meshPair, method, context, queryShape, needles, k, limitDistance, toleranceBuffer),
            System.Collections.IEnumerable collection when collection is not string and not GeometryBase =>
                UnifiedOperation.Apply(
                    collection.Cast<object>(),
                    item => Index(item, method, context, queryShape, needles, k, limitDistance, toleranceBuffer),
                    new OperationConfig<object, int> {
                        Context = context,
                        ValidationMode = ValidationMode.None, // SpatialStrategies validates internally
                        ErrorStrategy = ErrorStrategy.AccumulateAll,
                    }),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: ValidationErrors.Geometry.Invalid)
        };
}
