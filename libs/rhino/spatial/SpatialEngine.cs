using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Polymorphic spatial indexing with unified single/collection handling and monadic error composition.</summary>
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
        double? toleranceBuffer = null) where T : notnull {
        ArgumentNullException.ThrowIfNull(context);

        return (input, method, (queryShape, needles, k, limitDistance, toleranceBuffer)) switch {
            (Point3d[] points, var m, var p) => SpatialStrategies.Index(points, m, context, p.queryShape, p.needles, p.k, p.limitDistance, p.toleranceBuffer),
            (GeometryBase geometry, var m, var p) => SpatialStrategies.Index(geometry, m, context, p.queryShape, p.needles, p.k, p.limitDistance, p.toleranceBuffer),
            (ValueTuple<Mesh, Mesh> meshPair, var m, var p) => SpatialStrategies.Index(meshPair, m, context, p.queryShape, p.needles, p.k, p.limitDistance, p.toleranceBuffer),
            (System.Collections.IEnumerable collection, var m, var p) when collection is not string and not GeometryBase =>
                collection.Cast<object>().ToArray() switch {
                    [] => ResultFactory.Create(value: (IReadOnlyList<int>)[]),
                    [var single] => Index(single, m, context, p.queryShape, p.needles, p.k, p.limitDistance, p.toleranceBuffer),
                    var items => ResultFactory.Create(value: items.AsEnumerable())
                        .TraverseElements(item => Index(item, m, context, p.queryShape, p.needles, p.k, p.limitDistance, p.toleranceBuffer))
                        .Map(results => (IReadOnlyList<int>)results.SelectMany(r => r).ToArray())
                },
            _ => ResultFactory.Create<IReadOnlyList<int>>(error: ValidationErrors.Geometry.Invalid)
        };
    }
}
