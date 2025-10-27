using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>Functional pipeline for extracting and processing elements from geometry with composable operations.</summary>
public static class GeometryTraversal
{
    /// <summary>
    /// Extracts elements from geometry and applies a composable pipeline of operations.
    /// Supports transformation, filtering, and deduplication through function composition.
    /// </summary>
    public static Result<TResult[]> Extract<TExtracted, TResult>(
        GeometryBase? geometry,
        Func<GeometryBase, TExtracted[]> extractor,
        Func<IEnumerable<TExtracted>, IEnumerable<TResult>>? pipeline = null)
    {
        Result<GeometryBase> geometryValidation = Guard.RequireNonNull(geometry, nameof(geometry));
        if (!geometryValidation.Ok)
        {
            return Result<TResult[]>.Fail(geometryValidation.Error!);
        }

        if (geometryValidation.Value is not { IsValid: true })
        {
            return Result<TResult[]>.Fail($"Geometry is invalid: {geometry!.ObjectType} failed validation");
        }

        try
        {
            TExtracted[] extracted = extractor(geometryValidation.Value);

            // Apply pipeline if provided, otherwise assume TExtracted == TResult
            IEnumerable<TResult> processed = pipeline?.Invoke(extracted)
                                             ?? extracted.Cast<TResult>();

            return Result<TResult[]>.Success(processed.ToArray());
        }
        catch (Exception ex)
        {
            return Result<TResult[]>.Fail($"Pipeline execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts elements from multiple geometries and applies a composable pipeline.
    /// Aggregates results from all geometries, continuing on individual failures.
    /// </summary>
    public static Result<TResult[]> ExtractMany<TExtracted, TResult>(
        IEnumerable<GeometryBase>? geometries,
        Func<GeometryBase, TExtracted[]> extractor,
        Func<IEnumerable<TExtracted>, IEnumerable<TResult>>? pipeline = null)
    {
        Result<IEnumerable<GeometryBase>> geometriesValidation = Guard.RequireNonNull(geometries, nameof(geometries));
        if (!geometriesValidation.Ok)
        {
            return Result<TResult[]>.Fail(geometriesValidation.Error!);
        }

        List<TExtracted> allExtracted = [];
        List<string> errors = [];

        foreach (GeometryBase geom in geometriesValidation.Value!)
        {
            Result<TExtracted[]> result = Extract<TExtracted, TExtracted>(geom, extractor);
            if (result is { Ok: true, Value: not null })
            {
                allExtracted.AddRange(result.Value);
            }
            else if (result.Error is not null)
            {
                errors.Add(result.Error);
            }
        }

        if (errors.Count > 0 && allExtracted.Count == 0)
        {
            return Result<TResult[]>.Fail($"All extractions failed: {string.Join("; ", errors)}");
        }

        try
        {
            // Apply pipeline if provided, otherwise assume TExtracted == TResult
            IEnumerable<TResult> processed = pipeline?.Invoke(allExtracted)
                                             ?? allExtracted.Cast<TResult>();

            return Result<TResult[]>.Success(processed.ToArray());
        }
        catch (Exception ex)
        {
            return Result<TResult[]>.Fail($"Pipeline execution failed: {ex.Message}");
        }
    }
}

/// <summary>Extension methods for creating composable geometry processing pipelines.</summary>
public static class GeometryPipeline
{
    /// <summary>Transforms elements using the provided function.</summary>
    public static IEnumerable<TOut> Transform<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, TOut> transformer) => source.Select(transformer);

    /// <summary>Filters elements using the provided predicate.</summary>
    public static IEnumerable<T> Filter<T>(
        this IEnumerable<T> source,
        Func<T, bool> predicate) => source.Where(predicate);

    /// <summary>Removes duplicate elements using optional comparer.</summary>
    public static IEnumerable<T> Deduplicate<T>(
        this IEnumerable<T> source,
        IEqualityComparer<T>? comparer = null) => source.Distinct(comparer);

    /// <summary>Removes duplicate elements by key selector.</summary>
    public static IEnumerable<T> DeduplicateBy<T, TKey>(
        this IEnumerable<T> source,
        Func<T, TKey> keySelector) => source.GroupBy(keySelector).Select(g => g.First());
}
