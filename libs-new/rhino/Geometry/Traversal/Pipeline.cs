using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry.Traversal;

/// <summary>Geometry collection traversal utilities.</summary>
public sealed class Pipeline : IPipeline
{
    /// <summary>Traverses geometry collections with extraction and optional processing.</summary>
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
