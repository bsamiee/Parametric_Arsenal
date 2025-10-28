using Arsenal.Core.Result;

namespace Arsenal.Grasshopper.Runtime;

/// <summary>Represents the outcome of an asynchronous solve operation.</summary>
/// <typeparam name="T">Type of the computed data.</typeparam>
public sealed class SolveWorkResult<T>
{
    /// <summary>Initializes a new instance of the <see cref="SolveWorkResult{T}"/> class.</summary>
    public SolveWorkResult(Result<T> outcome)
    {
        Outcome = outcome;
    }

    /// <summary>Gets the result of the operation.</summary>
    public Result<T> Outcome { get; }
}
