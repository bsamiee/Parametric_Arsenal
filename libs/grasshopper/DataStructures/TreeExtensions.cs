using System.Collections.Generic;
using System.Linq;
using Arsenal.Core;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Arsenal.Grasshopper.DataStructures;

/// <summary>Extension methods for GH_Structure providing common tree manipulation operations.</summary>
public static class TreeExtensions
{
    /// <summary>Grafts tree by adding one level to all paths, creating a new branch for each item.</summary>
    public static Result<GH_Structure<T>> Graft<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<GH_Structure<T>>.Fail(validation.Error!);
        }

        GH_Structure<T> grafted = new GH_Structure<T>();
        int itemIndex = 0;

        // Cache paths for better performance
        GH_Path[] paths = [.. tree!.Paths];

        foreach (GH_Path path in paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);
            foreach (T item in branch.Cast<T>())
            {
                // Use optimized path construction
                GH_Path newPath = new GH_Path(path);
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
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<GH_Structure<T>>.Fail(validation.Error!);
        }

        // Create a copy to avoid modifying the original
        GH_Structure<T> simplified = new GH_Structure<T>();
        simplified.MergeStructure(tree!);

        // Use modern SDK simplification method
        simplified.Simplify(GH_SimplificationMode.CollapseAllOverlaps);

        return Result<GH_Structure<T>>.Success(simplified);
    }

    /// <summary>Flattens tree to a single branch.</summary>
    public static Result<GH_Structure<T>> Flatten<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<GH_Structure<T>>.Fail(validation.Error!);
        }

        GH_Structure<T> flattened = new GH_Structure<T>();
        GH_Path flatPath = new GH_Path(0);

        // Iterate through all branches to flatten
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

    /// <summary>Reverses items in each branch of the tree.</summary>
    public static Result<GH_Structure<T>> ReverseBranches<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<GH_Structure<T>>.Fail(validation.Error!);
        }

        GH_Structure<T> reversed = new GH_Structure<T>();

        // Cache paths for better performance
        GH_Path[] paths = [.. tree!.Paths];

        foreach (GH_Path path in paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);

            // Use modern collection expressions and optimized reversal
            List<T> reversedBranch = [.. branch.Cast<T>()];
            reversedBranch.Reverse();
            reversed.AppendRange(reversedBranch, path);
        }

        return Result<GH_Structure<T>>.Success(reversed);
    }

    /// <summary>Gets branch at specified path from the tree.</summary>
    public static Result<List<T>> GetBranch<T>(this GH_Structure<T>? tree, GH_Path path) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<List<T>>.Fail(validation.Error!);
        }

        if (!tree!.PathExists(path))
        {
            return Result<List<T>>.Fail($"Path {path} does not exist in tree with {tree.PathCount} paths");
        }

        System.Collections.IList branchList = tree.get_Branch(path);
        List<T> branch = [.. branchList.Cast<T>()];
        return Result<List<T>>.Success(branch);
    }

    /// <summary>Gets all items from the tree as a flat list.</summary>
    public static Result<List<T>> GetAllItems<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<List<T>>.Fail(validation.Error!);
        }

        // Collect all items from all branches
        List<T> allItems = [];
        foreach (GH_Path path in tree!.Paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);
            allItems.AddRange(branch.Cast<T>());
        }

        return Result<List<T>>.Success(allItems);
    }

    /// <summary>Filters tree to include only branches matching path pattern.</summary>
    public static Result<GH_Structure<T>> FilterByPath<T>(this GH_Structure<T>? tree, GH_Path pattern) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<GH_Structure<T>>.Fail(validation.Error!);
        }

        GH_Structure<T> filtered = new GH_Structure<T>();

        // Cache paths for better performance
        GH_Path[] paths = [.. tree!.Paths];

        foreach (GH_Path path in paths)
        {
            if (PathMatchesPattern(path, pattern))
            {
                System.Collections.IList branch = tree.get_Branch(path);
                filtered.AppendRange(branch.Cast<T>(), path);
            }
        }

        return Result<GH_Structure<T>>.Success(filtered);
    }

    /// <summary>Transforms all items in the tree using the provided transformation function.</summary>
    public static Result<GH_Structure<TOutput>> Transform<TInput, TOutput>(
        this GH_Structure<TInput>? tree,
        System.Func<TInput, Result<TOutput>> transformer)
        where TInput : IGH_Goo
        where TOutput : IGH_Goo
    {
        Result<GH_Structure<TInput>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<GH_Structure<TOutput>>.Fail(validation.Error!);
        }

        Result<System.Func<TInput, Result<TOutput>>> transformerValidation =
            Guard.RequireNonNull(transformer, nameof(transformer));
        if (!transformerValidation.Ok)
        {
            return Result<GH_Structure<TOutput>>.Fail(transformerValidation.Error!);
        }

        GH_Structure<TOutput> transformed = new GH_Structure<TOutput>();
        List<string> errors = [];

        // Cache paths for better performance
        GH_Path[] paths = [.. tree!.Paths];

        foreach (GH_Path path in paths)
        {
            System.Collections.IList branch = tree.get_Branch(path);
            foreach (TInput item in branch.Cast<TInput>())
            {
                Result<TOutput> transformResult = transformer(item);
                if (transformResult.Ok)
                {
                    transformed.Append(transformResult.Value!, path);
                }
                else
                {
                    errors.Add(transformResult.Error ?? "Unknown transformation error");
                }
            }
        }

        if (errors.Count > 0)
        {
            string errorMessage = $"Failed to transform {errors.Count} items: {string.Join(", ", errors)}";
            return Result<GH_Structure<TOutput>>.Fail(errorMessage);
        }

        return Result<GH_Structure<TOutput>>.Success(transformed);
    }

    /// <summary>Merges multiple trees into a single tree with path offset.</summary>
    public static Result<GH_Structure<T>> MergeTrees<T>(
        IEnumerable<GH_Structure<T>?>? trees,
        int pathOffset = 0) where T : IGH_Goo
    {
        if (trees is null)
        {
            return Result<GH_Structure<T>>.Fail("Trees collection cannot be null");
        }

        GH_Structure<T> merged = new GH_Structure<T>();
        int currentOffset = pathOffset;

        foreach (GH_Structure<T>? tree in trees)
        {
            // Skip null trees in the collection
            if (tree is null)
            {
                continue;
            }

            // Use SDK's MergeStructure with path offset
            GH_Structure<T> offsetTree = new GH_Structure<T>();
            foreach (GH_Path path in tree.Paths)
            {
                GH_Path newPath = new GH_Path(currentOffset);
                newPath = newPath.AppendElement(path[0]);
                for (int i = 1; i < path.Length; i++)
                {
                    newPath = newPath.AppendElement(path[i]);
                }

                System.Collections.IList branch = tree.get_Branch(path);
                offsetTree.AppendRange(branch.Cast<T>(), newPath);
            }

            merged.MergeStructure(offsetTree);
            currentOffset++;
        }

        return Result<GH_Structure<T>>.Success(merged);
    }

    /// <summary>Splits tree into multiple trees based on path depth.</summary>
    public static Result<List<GH_Structure<T>>> SplitByDepth<T>(
        this GH_Structure<T>? tree,
        int splitDepth) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<List<GH_Structure<T>>>.Fail(validation.Error!);
        }

        if (splitDepth < 0)
        {
            return Result<List<GH_Structure<T>>>.Fail("Split depth must be non-negative");
        }

        Dictionary<int, GH_Structure<T>> splitTrees = new Dictionary<int, GH_Structure<T>>();

        foreach (GH_Path path in tree!.Paths)
        {
            int key = splitDepth < path.Length ? path[splitDepth] : 0;

            if (!splitTrees.TryGetValue(key, out GH_Structure<T>? existingTree))
            {
                existingTree = new GH_Structure<T>();
                splitTrees[key] = existingTree;
            }

            System.Collections.IList branch = tree.get_Branch(path);
            existingTree.AppendRange(branch.Cast<T>(), path);
        }

        List<GH_Structure<T>> result = [.. splitTrees.Values];
        return Result<List<GH_Structure<T>>>.Success(result);
    }

    /// <summary>Validates tree structure and provides detailed diagnostics.</summary>
    public static Result<TreeDiagnostics> DiagnoseTree<T>(this GH_Structure<T>? tree) where T : IGH_Goo
    {
        Result<GH_Structure<T>> validation = Guard.RequireNonNull(tree, nameof(tree));
        if (!validation.Ok)
        {
            return Result<TreeDiagnostics>.Fail(validation.Error!);
        }

        TreeDiagnostics diagnostics = new TreeDiagnostics
        {
            PathCount = tree!.PathCount,
            DataCount = tree.DataCount,
            MaxDepth = tree.Paths.Any() ? tree.Paths.Max(p => p.Length) : 0,
            MinDepth = tree.Paths.Any() ? tree.Paths.Min(p => p.Length) : 0,
            EmptyBranches = tree.Paths.Count(p => tree.get_Branch(p).Count == 0),
            MaxBranchSize = tree.Paths.Any() ? tree.Paths.Max(p => tree.get_Branch(p).Count) : 0,
            MinBranchSize = tree.Paths.Any() ? tree.Paths.Min(p => tree.get_Branch(p).Count) : 0
        };

        return Result<TreeDiagnostics>.Success(diagnostics);
    }

    /// <summary>Checks if a path matches a pattern by comparing indices at each level.</summary>
    private static bool PathMatchesPattern(GH_Path path, GH_Path pattern)
    {
        if (pattern.Length > path.Length)
        {
            return false;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            if (path[i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Diagnostic information about a tree structure.</summary>
public record TreeDiagnostics
{
    /// <summary>Total number of paths in the tree.</summary>
    public int PathCount { get; init; }
    /// <summary>Total number of data items in the tree.</summary>
    public int DataCount { get; init; }
    /// <summary>Maximum depth of any path in the tree.</summary>
    public int MaxDepth { get; init; }
    /// <summary>Minimum depth of any path in the tree.</summary>
    public int MinDepth { get; init; }
    /// <summary>Number of empty branches in the tree.</summary>
    public int EmptyBranches { get; init; }
    /// <summary>Maximum number of items in any branch.</summary>
    public int MaxBranchSize { get; init; }
    /// <summary>Minimum number of items in any branch.</summary>
    public int MinBranchSize { get; init; }
}
