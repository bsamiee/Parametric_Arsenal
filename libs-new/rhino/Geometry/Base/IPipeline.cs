using System.Collections.Generic;
using Arsenal.Core.Result;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Base;

/// <summary>Reusable traversal helpers for geometry collections.</summary>
public interface IPipeline
{
    /// <summary>Traverses geometry collections with extraction and optional pipeline processing.</summary>
    /// <typeparam name="TIntermediate">The intermediate type extracted from geometry.</typeparam>
    /// <typeparam name="TOutput">The final output type after pipeline processing.</typeparam>
    /// <param name="geometries">The geometry collection to traverse.</param>
    /// <param name="extractor">Function to extract intermediate values from each geometry.</param>
    /// <param name="pipeline">Optional pipeline function to transform intermediate values.</param>
    /// <returns>A result containing the processed output collection or a failure.</returns>
    Result<IReadOnlyList<TOutput>> Traverse<TIntermediate, TOutput>(
        IEnumerable<GeometryBase> geometries,
        System.Func<GeometryBase, IEnumerable<TIntermediate>> extractor,
        System.Func<IEnumerable<TIntermediate>, IEnumerable<TOutput>>? pipeline = null);
}
