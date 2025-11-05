using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial indexing engine with RhinoCommon RTree SDK integration and unified operation dispatch.</summary>
public static class SpatialEngine {
    /// <summary>Performs spatial indexing operations using RhinoCommon RTree algorithms with tolerance-aware geometry processing.</summary>
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
                    (Func<object, Result<IReadOnlyList<int>>>)(item => Index(item, method, context, queryShape, needles, k, limitDistance, toleranceBuffer)),
                    new OperationConfig<object, int> {
                        Context = context,
                        ValidationMode = ValidationMode.None, // SpatialStrategies validates internally
                        AccumulateErrors = true,
                    }),
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: ValidationErrors.Geometry.Invalid),
        };
}
