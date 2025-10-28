using System;
using System.Collections.Generic;
using System.Linq;
using Arsenal.Core.Guard;
using Arsenal.Core.Result;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Arsenal.Grasshopper.DataStructures;

/// <summary>Extension methods for GH_Structure providing common tree manipulation operations.</summary>
public static class TreeOperations
{
    /// <summary>Grafts tree by adding one level to all paths, creating a new branch for each item.</summary>
    public static Result<GH_Structure<T>> Graft<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.AgainstNull(tree, nameof(tree));
        if (!validation.IsSuccess)
        {
            return Result<GH_Structure<T>>.Fail(validation.Failure!);
        }

        GH_Structure<T> grafted = new();
        int itemIndex = 0;

        GH_Path[] paths = [.. tree!.Paths];

        foreach (GH_Path path in paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);
            foreach (T item in branch.Cast<T>())
            {
                GH_Path newPath = new(path);
                newPath = newPath.AppendElement(itemIndex);
                grafted.Append(item, newPath);
                itemIndex++;
            }
        }

        return Result<GH_Structure<T>>.Success(grafted);
    }

    /// <summary>Simplifies tree by removing unnecessary path levels and consolidating branches.</summary>
    public static Result<GH_Structure<T>> Simplify<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.AgainstNull(tree, nameof(tree));
        if (!validation.IsSuccess)
        {
            return Result<GH_Structure<T>>.Fail(validation.Failure!);
        }

        GH_Structure<T> simplified = new();
        simplified.MergeStructure(tree!);
        simplified.Simplify(GH_SimplificationMode.CollapseAllOverlaps);

        return Result<GH_Structure<T>>.Success(simplified);
    }

    /// <summary>Flattens tree to a single branch.</summary>
    public static Result<GH_Structure<T>> Flatten<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.AgainstNull(tree, nameof(tree));
        if (!validation.IsSuccess)
        {
            return Result<GH_Structure<T>>.Fail(validation.Failure!);
        }

        GH_Structure<T> flattened = new();
        GH_Path flatPath = new(0);

        foreach (GH_Path path in tree!.Paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);
            foreach (T item in branch.Cast<T>())
            {
                flattened.Append(item, flatPath);
            }
        }

        return Result<GH_Structure<T>>.Success(flattened);
    }

    /// <summary>Gets branch at specified path from the tree.</summary>
    public static Result<List<T>> GetBranch<T>(this GH_Structure<T>? tree, GH_Path path) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.AgainstNull(tree, nameof(tree));
        if (!validation.IsSuccess)
        {
            return Result<List<T>>.Fail(validation.Failure!);
        }

        if (!tree!.PathExists(path))
        {
            return Result<List<T>>.Fail(new Failure("tree.pathNotFound",
                $"Path {path} does not exist in tree with {tree.PathCount} paths"));
        }

        System.Collections.IList branchList = tree.get_Branch(path);
        List<T> branch = [.. branchList.Cast<T>()];
        return Result<List<T>>.Success(branch);
    }

    /// <summary>Gets all items from the tree as a flat list.</summary>
    public static Result<List<T>> GetAllItems<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.AgainstNull(tree, nameof(tree));
        if (!validation.IsSuccess)
        {
            return Result<List<T>>.Fail(validation.Failure!);
        }

        List<T> allItems = [];
        foreach (GH_Path path in tree!.Paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);
            allItems.AddRange(branch.Cast<T>());
        }

        return Result<List<T>>.Success(allItems);
    }

    /// <summary>Transforms all items in the tree using the provided transformation function.</summary>
    public static Result<GH_Structure<TOutput>> Transform<TInput, TOutput>(
        this GH_Structure<TInput>? tree,
        Func<TInput, Result<TOutput>> transformer)
        where TInput : IGH_Goo
        where TOutput : IGH_Goo
    {
        Result<GH_Structure<TInput>> validation = Guard.AgainstNull(tree, nameof(tree));
        if (!validation.IsSuccess)
        {
            return Result<GH_Structure<TOutput>>.Fail(validation.Failure!);
        }

        ArgumentNullException.ThrowIfNull(transformer);

        GH_Structure<TOutput> transformed = new();
        List<Failure> failures = [];

        GH_Path[] paths = [.. tree!.Paths];

        foreach (GH_Path path in paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);
            foreach (TInput item in branch.Cast<TInput>())
            {
                Result<TOutput> transformResult = transformer(item);
                if (transformResult.IsSuccess)
                {
                    transformed.Append(transformResult.Value!, path);
                }
                else if (transformResult.Failure is not null)
                {
                    failures.Add(transformResult.Failure);
                }
            }
        }

        if (failures.Count > 0)
        {
            Failure combinedFailure = failures.Count == 1
                ? failures[0]
                : new Failure("tree.transformFailed",
                    $"Failed to transform {failures.Count} items: {string.Join("; ", failures.Select(f => f.Message))}");
            return Result<GH_Structure<TOutput>>.Fail(combinedFailure);
        }

        return Result<GH_Structure<TOutput>>.Success(transformed);
    }
}
