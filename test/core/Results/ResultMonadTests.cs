using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Algebraic tests for Result monadic laws, operations, and invariants using zero-boilerplate composition.</summary>
public sealed class ResultMonadTests {
    private static readonly SystemError TestError = new(ErrorDomain.Results, 1001, "Test error");

    /// <summary>Verifies functor category laws: identity and composition.</summary>
    [Fact]
    public void FunctorLaws() => TestUtilities.AssertAll(
        () => LawVerification.FunctorIdentity(ResultGenerators.ResultGen<int>()),
        () => LawVerification.FunctorComposition(
            ResultGenerators.ResultGen<int>(),
            f: x => x.ToString(),
            g: s => s.Length));

    /// <summary>Verifies monad category laws: left identity, right identity, and associativity.</summary>
    [Fact]
    public void MonadLaws() => TestUtilities.AssertAll(
        () => LawVerification.MonadLeftIdentity(Gen.Int, ResultGenerators.MonadicFunctionGen<int, string>()),
        () => LawVerification.MonadRightIdentity(ResultGenerators.ResultGen<int>()),
        () => LawVerification.MonadAssociativity(
            ResultGenerators.ResultGen<int>(),
            ResultGenerators.MonadicFunctionGen<int, string>(),
            ResultGenerators.MonadicFunctionGen<string, double>()));

    /// <summary>Verifies applicative functor and equality laws.</summary>
    [Fact]
    public void ApplicativeAndEqualityLaws() => TestUtilities.AssertAll(
        () => LawVerification.ApplicativeIdentity(ResultGenerators.ResultGen<int>()),
        () => LawVerification.EqualityReflexive(ResultGenerators.ResultGen<int>()),
        () => LawVerification.EqualitySymmetric(ResultGenerators.ResultGen<int>(), ResultGenerators.ResultGen<int>()),
        () => LawVerification.HashCodeConsistent(Gen.Int, v => ResultFactory.Create(value: v)));

    /// <summary>Verifies TryGet extraction using algebraic success/failure partition.</summary>
    [Theory]
    [MemberData(nameof(TryGetTestCases))]
    public void TryGet_ExtractionBehavior_MatchesResultState(Result<int> result, bool expectedSuccess) =>
        Assert.Equal(expectedSuccess, result.TryGet(out var extracted) switch {
            true => extracted != default || !expectedSuccess,
            false => extracted == default && !expectedSuccess
        });

    public static IEnumerable<object[]> TryGetTestCases =>
        Gen.Int.Tuple(ResultGenerators.SystemErrorGen)
            .Select(t => t.Item1 switch {
                var value => new[] {
                    TestData.Case(ResultFactory.Create(value: value), true),
                    TestData.Case(ResultFactory.Create<int>(error: t.Item2), false),
                    TestData.Case(ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)), true),
                    TestData.Case(ResultFactory.Create(deferred: () => ResultFactory.Create<int>(error: t.Item2)), false)
                }
            })
            .Single
            .SelectMany(cases => cases);

    /// <summary>Verifies Reduce accumulation using algebraic handler selection.</summary>
    [Fact]
    public void Reduce_AccumulationBehavior_AppliesCorrectHandler() => TestUtilities.AssertAll(
        Gen.Int.Tuple(Gen.Int).ToAssertion((value, seed) =>
            Assert.Equal(seed + value, ResultFactory.Create(value: value).Reduce(seed, (s, v) => s + v))),
        ResultGenerators.SystemErrorArrayGen.Tuple(Gen.Int).ToAssertion((errors, seed) =>
            Assert.Equal(seed + errors.Length,
                ResultFactory.Create<int>(errors: errors).Reduce(seed, (s, v) => s + v, (s, errs) => s + errs.Length))),
        Gen.Int.ToAssertion(seed =>
            Assert.Equal(seed, ResultFactory.Create<int>(error: TestError).Reduce(seed, (s, v) => s + v))));

    /// <summary>Verifies Filter using predicate pattern matching and error propagation.</summary>
    [Fact]
    public void Filter_PredicateValidation_FiltersCorrectly() => TestUtilities.AssertAll(
        Gen.Int[1, 100].ToAssertion(value =>
            Assert.True(ResultFactory.Create(value: value).Filter(x => x > 0, TestError).IsSuccess)),
        Gen.Int[1, 100].ToAssertion(value =>
            Assert.False(ResultFactory.Create(value: value).Filter(x => x < 0, TestError).IsSuccess)),
        ResultGenerators.FailureGen<int>().ToAssertion(result =>
            Assert.False(result.Filter(x => x > 0, TestError).IsSuccess)),
        Gen.Int.ToAssertion(value =>
            Assert.Equal(value > 0,
                ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)).Filter(x => x > 0, TestError).IsSuccess)));

    /// <summary>Verifies Ensure validation using algebraic error accumulation and partition logic.</summary>
    [Fact]
    public void Ensure_MultipleValidations_AccumulatesErrors() {
        var (e1, e2, e3) = (TestError, new SystemError(ErrorDomain.Results, 1002, "E2"), new SystemError(ErrorDomain.Results, 1003, "E3"));
        TestUtilities.AssertAll(
            Gen.Int[1, 100].ToAssertion(v => Assert.True(ResultFactory.Create(value: v).Ensure((Func<int, bool>)(x => x > 0), e1).IsSuccess), 50),
            Gen.Int[1, 100].ToAssertion(v => Assert.False(ResultFactory.Create(value: v).Ensure((Func<int, bool>)(x => x < 0), e1).IsSuccess), 50),
            Gen.Int.ToAssertion(v => {
                var result = ResultFactory.Create(value: v).Ensure(((Func<int, bool>)(x => x > 0), e1), ((Func<int, bool>)(x => x < 100), e2));
                var expectedErrors = (v <= 0 ? 1 : 0) + (v >= 100 ? 1 : 0);
                Assert.Equal(expectedErrors == 0, result.IsSuccess);
            }),
            Gen.Int.ToAssertion(v => Assert.Equal(v > 0,
                ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)).Ensure((Func<int, bool>)(x => x > 0), e1).IsSuccess), 50));
    }

    /// <summary>Verifies Match using algebraic pattern exhaustion.</summary>
    [Fact]
    public void Match_PatternMatching_InvokesCorrectHandler() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion(v => Assert.Equal(v * 2, ResultFactory.Create(value: v).Match(v => v * 2, _ => -1))),
        ResultGenerators.SystemErrorArrayGen.ToAssertion(errs =>
            Assert.Equal(errs.Length, ResultFactory.Create<int>(errors: errs).Match(v => v * 2, e => e.Length))),
        Gen.Int.ToAssertion(v => Assert.Equal(v.ToString(),
            ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)).Match(x => x.ToString(), _ => "error")), 50));

    /// <summary>Verifies Apply applicative functor using algebraic error accumulation matrix.</summary>
    [Fact]
    public void Apply_ApplicativeFunctor_AccumulatesErrorsInParallel() => TestUtilities.AssertAll(
        Gen.Int.Tuple(Gen.Int).ToAssertion((a, b) => {
            var applied = ResultFactory.Create(value: a).Apply(ResultFactory.Create<Func<int, int>>(value: x => x + b));
            Assert.True(applied.IsSuccess);
            Assert.Equal(a + b, applied.Value);
        }),
        ResultGenerators.SystemErrorGen.Tuple(Gen.Int).ToAssertion((err, b) => {
            var applied = ResultFactory.Create<int>(error: err).Apply(ResultFactory.Create<Func<int, int>>(value: x => x + b));
            Assert.False(applied.IsSuccess);
            Assert.Contains(err, applied.Errors);
        }, 50),
        Gen.Int.Tuple(ResultGenerators.SystemErrorGen).ToAssertion((a, err) => {
            var applied = ResultFactory.Create(value: a).Apply(ResultFactory.Create<Func<int, int>>(error: err));
            Assert.False(applied.IsSuccess);
            Assert.Contains(err, applied.Errors);
        }, 50),
        ResultGenerators.SystemErrorGen.Tuple(ResultGenerators.SystemErrorGen).ToAssertion((e1, e2) => {
            var applied = ResultFactory.Create<int>(error: e1).Apply(ResultFactory.Create<Func<int, int>>(error: e2));
            Assert.Equal(2, applied.Errors.Count);
        }, 50));

    /// <summary>Verifies Traverse using monadic collection transformation algebra.</summary>
    [Fact]
    public void Traverse_CollectionTransformation_TransformsAndAccumulates() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion(v => Assert.Single(ResultFactory.Create(value: v).Traverse(x => ResultFactory.Create(value: x.ToString())).Value)),
        Gen.Int.List[1, 10].ToAssertion(items => {
            var traversed = ResultFactory.Create<IEnumerable<int>>(value: items).Traverse(x => ResultFactory.Create(value: x * 2));
            Assert.Equal(items.Count, traversed.Value.Count);
        }, 50),
        Gen.Int.List[1, 10].ToAssertion(items =>
            Assert.Equal(!items.Any(x => x % 2 != 0),
                ResultFactory.Create<IEnumerable<int>>(value: items).Traverse(x => x % 2 == 0
                    ? ResultFactory.Create(value: x) : ResultFactory.Create<int>(error: TestError)).IsSuccess), 50),
        ResultGenerators.SystemErrorGen.ToAssertion(err =>
            Assert.False(ResultFactory.Create<IEnumerable<int>>(error: err).Traverse(x => ResultFactory.Create(value: x * 2)).IsSuccess), 50));

    /// <summary>Verifies deferred execution using lazy evaluation algebra and resource safety.</summary>
    [Fact]
    public void DeferredExecution_LazyEvaluation_BehavesCorrectly() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion(v => {
            int count = 0;
            var deferred = ResultFactory.Create(deferred: () => { count++; return ResultFactory.Create(value: v); });
            Assert.True(deferred.IsDeferred);
            Assert.Equal(0, count);
            _ = deferred.Value;
            _ = deferred.Value;
            Assert.Equal(1, count); // Only evaluates once
        }, 50),
        ResultGenerators.SystemErrorGen.ToAssertion(err => {
            bool executed = false;
            var deferred = ResultFactory.Create(deferred: () => { executed = true; return ResultFactory.Create<int>(error: err); });
            Assert.False(executed && !deferred.IsSuccess || !executed && deferred.IsSuccess);
        }, 50),
        Gen.Int.ToAssertion(v => {
            int mapCount = 0, bindCount = 0;
            var final = ResultFactory.Create(deferred: () => ResultFactory.Create(value: v))
                .Map(x => { mapCount++; return x * 2; })
                .Bind(x => { bindCount++; return ResultFactory.Create(value: x + 10); })
                .Value;
            Assert.Equal((v * 2 + 10, 1, 1), (final, mapCount, bindCount));
        }, 50),
        () => {
            using var stream = new MemoryStream();
            var result = ResultFactory.Create(deferred: () => { stream.WriteByte(1); return ResultFactory.Create(value: 1); });
            stream.Dispose();
            Assert.Throws<ObjectDisposedException>(() => result.Value);
        });

    /// <summary>Verifies Apply side effects preserve Result state using algebraic identity preservation.</summary>
    [Fact]
    public void ApplyMethod_SideEffects_PreservesState() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion(v => {
            int captured = 0;
            var result = ResultFactory.Create(value: v).Apply(onSuccess: x => captured = x);
            Assert.Equal((v, v, true), (captured, result.Value, result.IsSuccess));
        }),
        ResultGenerators.SystemErrorArrayGen.ToAssertion(errs => {
            SystemError[]? captured = null;
            var result = ResultFactory.Create<int>(errors: errs).Apply(onFailure: e => captured = [.. e]);
            Assert.Equal((errs.Length, false), (captured!.Length, result.IsSuccess));
        }, 50),
        Gen.Bool.ToAssertion(isSuccess => {
            (bool successCalled, bool failureCalled) = (false, false);
            (isSuccess ? ResultFactory.Create(value: 42) : ResultFactory.Create<int>(error: TestError))
                .Apply(_ => successCalled = true, _ => failureCalled = true);
            Assert.Equal((isSuccess, !isSuccess), (successCalled, failureCalled));
        }));

    /// <summary>Verifies OnError transformation and recovery using algebraic error morphisms.</summary>
    [Fact]
    public void OnError_ErrorHandling_TransformsAndRecovers() {
        var (e1, e2) = (TestError, new SystemError(ErrorDomain.Results, 1002, "E2"));
        TestUtilities.AssertAll(
            () => {
                var mapped = ResultFactory.Create<int>(error: e1).OnError(mapError: _ => [e2]);
                Assert.True(!mapped.IsSuccess && mapped.Errors.Contains(e2) && !mapped.Errors.Contains(e1));
            },
            () => Assert.Equal(99, ResultFactory.Create<int>(error: e1).OnError(recover: _ => 99).Value),
            () => Assert.Equal(77, ResultFactory.Create<int>(error: e1).OnError(recoverWith: _ => ResultFactory.Create(value: 77)).Value),
            Gen.Int.ToAssertion(v => Assert.Equal(v, ResultFactory.Create(value: v).OnError(mapError: _ => [e2]).Value), 50));
    }
}
