using System;
using Arsenal.Core.Result;

namespace Arsenal.Grasshopper.Runtime;

/// <summary>Executes component logic with consistent exception handling.</summary>
public interface ISolvePipeline
{
    /// <summary>Executes a Result-returning operation.</summary>
    Result Execute(Func<Result> solver);

    /// <summary>Executes a Result&lt;T&gt;-returning operation.</summary>
    Result<T> Execute<T>(Func<Result<T>> solver);
}

/// <summary>Default implementation wrapping operations in try/catch blocks.</summary>
public sealed class SolvePipeline : ISolvePipeline
{
    /// <summary>Gets the singleton default pipeline.</summary>
    public static SolvePipeline Default { get; } = new();

    private SolvePipeline()
    {
    }

    /// <inheritdoc/>
    public Result Execute(Func<Result> solver)
    {
        ArgumentNullException.ThrowIfNull(solver);

        try
        {
            return solver();
        }
        catch (Exception ex)
        {
            return Result.Fail(Failure.From(ex, "grasshopper.solve.exception"));
        }
    }

    /// <inheritdoc/>
    public Result<T> Execute<T>(Func<Result<T>> solver)
    {
        ArgumentNullException.ThrowIfNull(solver);

        try
        {
            return solver();
        }
        catch (Exception ex)
        {
            return Result<T>.Fail(Failure.From(ex, "grasshopper.solve.exception"));
        }
    }
}
