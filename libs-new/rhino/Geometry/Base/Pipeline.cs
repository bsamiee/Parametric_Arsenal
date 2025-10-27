using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Result;
using Arsenal.Core.Guard;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Base;

/// <summary>Reusable traversal helpers for geometry collections.</summary>
public sealed class Pipeline : IPipeline
{
    /// <summary>Traverses geometry collections with extraction and optional pipeline processing.</summary>
    /// <typeparam name="TIntermediate">The intermediate type extracted from geometry.</typeparam>
    /// <typeparam name="TOutput">The final output type after pipeline processing.</typeparam>
    /// <param name="geometries">The geometry collection to traverse.</param>
    /// <param name="extractor">Function to extract intermediate values from each geometry.</param>
    /// <param name="pipeline">Optional pipeline function to transform intermediate values.</param>
    /// <returns>A result containing the processed output collection or a failure.</returns>
    public Result<IReadOnlyList<TOutput>> Traverse<TIntermediate, TOutput>(
        IEnumerable<GeometryBase> geometries,
        Func<GeometryBase, IEnumerable<TIntermediate>> extractor,
        Func<IEnumerable<TIntermediate>, IEnumerable<TOutput>>? pipeline = null)
    {
        Result<IReadOnlyCollection<GeometryBase>> collectionResult = Guard.AgainstEmpty(geometries, nameof(geometries));
        if (!collectionResult.IsSuccess)
        {
            return Result<IReadOnlyList<TOutput>>.Fail(collectionResult.Failure!);
        }

        ArgumentNullException.ThrowIfNull(extractor);

        List<TIntermediate> intermediates = new();

        foreach (GeometryBase geometry in collectionResult.Value!)
        {
            if (!geometry.IsValid)
            {
                continue;
            }

            IEnumerable<TIntermediate> items = extractor(geometry);

            intermediates.AddRange(items);
        }

        IEnumerable<TOutput> output = pipeline is not null
            ? pipeline(intermediates)
            : intermediates.Cast<TOutput>();

        IReadOnlyList<TOutput> list = output.ToList();
        return Result<IReadOnlyList<TOutput>>.Success(list);
    }
}
