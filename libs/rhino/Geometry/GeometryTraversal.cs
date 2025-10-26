using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core;
using Rhino.Geometry;

namespace Arsenal.Rhino.Geometry;

/// <summary>Core functional patterns for traversing and extracting elements from geometry.</summary>
public static class GeometryTraversal
{
    /// <summary>Extracts elements from geometry using a provided extractor function.</summary>
    public static Result<T[]> Extract<T>(
        GeometryBase? geometry,
        Func<GeometryBase, T[]> extractor)
    {
        Result<GeometryBase> geometryValidation = Guard.RequireNonNull(geometry, nameof(geometry));
        if (!geometryValidation.Ok)
        {
            return Result<T[]>.Fail(geometryValidation.Error!);
        }

        if (geometryValidation.Value is not { IsValid: true })
        {
            return Result<T[]>.Fail($"Geometry is invalid: {geometry!.ObjectType} failed validation");
        }

        try
        {
            T[] elements = extractor(geometryValidation.Value);
            return Result<T[]>.Success(elements);
        }
        catch (Exception ex)
        {
            return Result<T[]>.Fail($"Extraction failed: {ex.Message}");
        }
    }

    /// <summary>Extracts and transforms elements from geometry in a single operation.</summary>
    public static Result<TOut[]> ExtractAndTransform<TIn, TOut>(
        GeometryBase? geometry,
        Func<GeometryBase, TIn[]> extractor,
        Func<TIn, TOut> transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);

        Result<TIn[]> extractionResult = Extract(geometry, extractor);
        if (!extractionResult.Ok)
        {
            return Result<TOut[]>.Fail(extractionResult.Error!);
        }

        try
        {
            TOut[] transformed = extractionResult.Value!.Select(transformer).ToArray();
            return Result<TOut[]>.Success(transformed);
        }
        catch (Exception ex)
        {
            return Result<TOut[]>.Fail($"Transformation failed: {ex.Message}");
        }
    }

    /// <summary>Extracts unique elements from geometry, removing duplicates based on equality comparison.</summary>
    public static Result<T[]> ExtractUnique<T>(
        GeometryBase? geometry,
        Func<GeometryBase, T[]> extractor,
        IEqualityComparer<T>? comparer = null)
    {
        Result<T[]> extractionResult = Extract(geometry, extractor);
        if (!extractionResult.Ok)
        {
            return extractionResult;
        }

        try
        {
            T[] unique = extractionResult.Value!.Distinct(comparer).ToArray();
            return Result<T[]>.Success(unique);
        }
        catch (Exception ex)
        {
            return Result<T[]>.Fail($"Deduplication failed: {ex.Message}");
        }
    }

    /// <summary>Extracts elements from multiple geometries and combines the results.</summary>
    public static Result<T[]> ExtractFromMany<T>(
        IEnumerable<GeometryBase>? geometries,
        Func<GeometryBase, T[]> extractor)
    {
        Result<IEnumerable<GeometryBase>> geometriesValidation = Guard.RequireNonNull(geometries, nameof(geometries));
        if (!geometriesValidation.Ok)
        {
            return Result<T[]>.Fail(geometriesValidation.Error!);
        }

        List<T> allElements = [];
        List<string> errors = [];

        foreach (GeometryBase geom in geometriesValidation.Value!)
        {
            Result<T[]> result = Extract(geom, extractor);
            if (result is { Ok: true, Value: not null })
            {
                allElements.AddRange(result.Value);
            }
            else if (result.Error is not null)
            {
                errors.Add(result.Error);
            }
        }

        if (errors.Count > 0 && allElements.Count == 0)
        {
            return Result<T[]>.Fail($"All extractions failed: {string.Join("; ", errors)}");
        }

        return Result<T[]>.Success([.. allElements]);
    }

    /// <summary>Filters extracted elements based on a predicate.</summary>
    public static Result<T[]> ExtractWhere<T>(
        GeometryBase? geometry,
        Func<GeometryBase, T[]> extractor,
        Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        Result<T[]> extractionResult = Extract(geometry, extractor);
        if (!extractionResult.Ok)
        {
            return extractionResult;
        }

        try
        {
            T[] filtered = extractionResult.Value!.Where(predicate).ToArray();
            return Result<T[]>.Success(filtered);
        }
        catch (Exception ex)
        {
            return Result<T[]>.Fail($"Filtering failed: {ex.Message}");
        }
    }
}
