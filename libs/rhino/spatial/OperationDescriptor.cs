using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Operation descriptor encapsulating factory, validation mode, buffer size, and execution strategy for spatial operations.</summary>
internal readonly record struct OperationDescriptor(
    Func<object, RTree>? Factory,
    V Mode,
    int BufferSize,
    Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> Execute);
