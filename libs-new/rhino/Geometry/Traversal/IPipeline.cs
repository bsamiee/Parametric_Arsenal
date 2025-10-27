using System;
using System.Collections.Generic;
using Arsenal.Core.Result;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Traversal;

/// <summary>Geometry collection traversal operations.</summary>
public interface IPipeline
{
    /// <summary>Traverses geometry collections with extraction and optional processing.</summary>
    Result<IReadOnlyList<TOutput>> Traverse<TIntermediate, TOutput>(
        IEnumerable<GeometryBase> geometries,
        Func<GeometryBase, IEnumerable<TIntermediate>> extractor,
        Func<IEnumerable<TIntermediate>, IEnumerable<TOutput>>? pipeline = null);
}
