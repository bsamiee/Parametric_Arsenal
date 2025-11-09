using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial operation configuration tuple for FrozenDictionary dispatch.</summary>
internal readonly record struct SpatialOperation(
    Func<object, RTree>? Factory,
    V Mode,
    int BufferSize,
    Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute);
