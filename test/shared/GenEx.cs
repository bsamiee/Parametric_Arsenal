using System.Diagnostics.CodeAnalysis;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic generator combinators using zero-allocation composition.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public static class GenEx {
    /// <summary>Weighted sum type generator using algebraic frequency distribution.</summary>
    public static Gen<T> OneOfWeighted<T>(params (int Weight, Gen<T> Gen)[] weightedGens) => Gen.Frequency<T>([.. weightedGens.Select(wg => (wg.Weight, (IGen<T>)wg.Gen))]);

    /// <summary>Result generator using weighted success/failure distribution.</summary>
    public static Gen<Result<T>> ToResultGen<T>(this Gen<T> valueGen, Gen<SystemError> errorGen, int successWeight = 1, int failureWeight = 1) =>
        OneOfWeighted(
            (successWeight, valueGen.Select(static v => ResultFactory.Create(value: v))),
            (failureWeight, errorGen.Select(static e => ResultFactory.Create<T>(error: e))));

    /// <summary>Deferred Result generator using immediate/deferred distribution.</summary>
    public static Gen<Result<T>> ToResultGenDeferred<T>(this Gen<T> valueGen, Gen<SystemError> errorGen, int deferredWeight = 1, int immediateWeight = 1) =>
        OneOfWeighted(
            (immediateWeight, valueGen.ToResultGen(errorGen)),
            (deferredWeight, valueGen.ToResultGen(errorGen).Select(static r => ResultFactory.Create(deferred: () => r))));
}
