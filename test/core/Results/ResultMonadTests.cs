using Arsenal.Core.Results;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Tests monadic laws and deferred execution behavior.</summary>
public sealed class ResultMonadTests {

    /// <summary>Tests functor identity law.</summary>
    [Fact]
    public void FunctorIdentityLaw() =>
        Check.Sample(ResultGenerators.ResultGen<int>(), result =>
            result.Map(x => x).Equals(result));

    /// <summary>Tests monad left identity law.</summary>
    [Fact]
    public void MonadLeftIdentityLaw() =>
        Check.Sample(Gen.Int, value =>
            Check.Sample(ResultGenerators.MonadicFunctionGen<int, string>(), f =>
                ResultFactory.Create(value: value).Bind(f).Equals(f(value))));

    /// <summary>Tests monad right identity law.</summary>
    [Fact]
    public void MonadRightIdentityLaw() =>
        Check.Sample(ResultGenerators.ResultGen<int>(), result =>
            result.Bind(x => ResultFactory.Create(value: x)).Equals(result));

    /// <summary>Tests monad associativity law.</summary>
    [Fact]
    public void MonadAssociativityLaw() =>
        Check.Sample(ResultGenerators.ResultGen<int>(), result =>
            Check.Sample(ResultGenerators.MonadicFunctionGen<int, string>(), f =>
                Check.Sample(ResultGenerators.MonadicFunctionGen<string, double>(), g =>
                    result.Bind(f).Bind(g).Equals(result.Bind(x => f(x).Bind(g))))));

    /// <summary>Tests deferred execution behavior and edge cases.</summary>
    [Fact]
    public void DeferredExecutionBehavesCorrectly() {
        bool executed = false;
        Result<int> deferred = ResultFactory.Create(deferred: () => { executed = true; return ResultFactory.Create(value: 42); });

        // Deferred execution validation
        Assert.False(executed);
        Assert.True(deferred.IsDeferred);
        Assert.Equal(42, deferred.Value);
        Assert.True(executed);

        // State access violation - accessing disposed resource
        using MemoryStream stream = new();
        Result<int> resourceResult = ResultFactory.Create(deferred: () => {
            stream.WriteByte(1); // Will throw after disposal
            return ResultFactory.Create(value: 1);
        });
        stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => resourceResult.Value);
    }
}
