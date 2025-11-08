using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;

namespace Arsenal.Core.Operations;

/// <summary>Immutable transformation pipeline with composable monadic chaining and polymorphic execution strategies.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct TransformPipeline<T>(IReadOnlyList<Func<T, Result<T>>> transforms, bool shortCircuit = true) where T : notnull {
    private readonly IReadOnlyList<Func<T, Result<T>>> _transforms = transforms;
    private readonly bool _shortCircuit = shortCircuit;

    /// <summary>Appends transform using fluent composition (monadic bind equivalent).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TransformPipeline<T> Then(Func<T, Result<T>> transform) {
        ArgumentNullException.ThrowIfNull(transform);
        return new([.. this._transforms, transform,], this._shortCircuit);
    }

    /// <summary>Executes transformation pipeline with configured error handling strategy.</summary>
    [Pure]
    public Result<T> Execute(T input) =>
        (this._transforms?.Count ?? 0, this._shortCircuit) switch {
            (0, _) => ResultFactory.Create(value: input),
            (1, _) => this._transforms![0](input),
            (_, true) => this._transforms!.Aggregate(ResultFactory.Create(value: input), static (acc, f) => acc.IsSuccess ? acc.Bind(f) : acc),
            (_, false) => this._transforms!.Aggregate(
                (Value: input, Errors: (List<SystemError>)[]),
                static (state, f) => f(state.Value) switch {
                    { IsSuccess: true } r => (r.Value, state.Errors),
                    { IsSuccess: false } r => (state.Value, [.. state.Errors, .. r.Errors,]),
                }) switch {
                    (var v, []) => ResultFactory.Create(value: v),
                    (_, var e) => ResultFactory.Create<T>(errors: [.. e]),
                },
        };

    /// <summary>Count of transforms in pipeline.</summary>
    [Pure] public int Count => this._transforms?.Count ?? 0;
}
