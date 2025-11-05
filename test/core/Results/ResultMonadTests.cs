using System.Globalization;
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
            f: x => x.ToString(CultureInfo.InvariantCulture),
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
    public void TryGetExtractionBehaviorMatchesResultState(Result<int> result, bool expectedSuccess) =>
        Assert.Equal(expectedSuccess, result.TryGet(out int extracted));

    public static IEnumerable<object[]> TryGetTestCases {
        get {
            (int value, SystemError error) = Gen.Int.Tuple(ResultGenerators.SystemErrorGen).Single();
            return new[] {
                TestData.Case(ResultFactory.Create(value: value), true),
                TestData.Case(ResultFactory.Create<int>(error: error), false),
                TestData.Case(ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)), true),
                TestData.Case(ResultFactory.Create(deferred: () => ResultFactory.Create<int>(error: error)), false),
            };
        }
    }

    /// <summary>Verifies Reduce accumulation using algebraic handler selection.</summary>
    [Fact]
    public void ReduceAccumulationBehaviorAppliesCorrectHandler() => TestUtilities.AssertAll(
        Gen.Int.Tuple(Gen.Int).ToAssertion((Action<int, int>)((value, seed) =>
            Assert.Equal(seed + value, ResultFactory.Create(value: value).Reduce(seed, (s, v) => s + v)))),
        ResultGenerators.SystemErrorArrayGen.Tuple(Gen.Int).ToAssertion((Action<SystemError[], int>)((errors, seed) =>
            Assert.Equal(seed + errors.Length,
                ResultFactory.Create<int>(errors: errors).Reduce(seed, (s, v) => s + v, (s, errs) => s + errs.Length)))),
        Gen.Int.ToAssertion((Action<int>)(seed =>
            Assert.Equal(seed, ResultFactory.Create<int>(error: TestError).Reduce(seed, (s, v) => s + v)))));

    /// <summary>Verifies Filter using predicate pattern matching and error propagation.</summary>
    [Fact]
    public void FilterPredicateValidationFiltersCorrectly() => TestUtilities.AssertAll(
        Gen.Int[1, 100].ToAssertion((Action<int>)(value =>
            Assert.True(ResultFactory.Create(value: value).Filter(x => x > 0, TestError).IsSuccess))),
        Gen.Int[1, 100].ToAssertion((Action<int>)(value =>
            Assert.False(ResultFactory.Create(value: value).Filter(x => x < 0, TestError).IsSuccess))),
        ResultGenerators.FailureGen<int>().ToAssertion((Action<Result<int>>)(result =>
            Assert.False(result.Filter(x => x > 0, TestError).IsSuccess))),
        Gen.Int.ToAssertion((Action<int>)(value =>
            Assert.Equal(value > 0,
                ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)).Filter(x => x > 0, TestError).IsSuccess))));

    /// <summary>Verifies Ensure validation using algebraic error accumulation and partition logic.</summary>
    [Fact]
    public void EnsureMultipleValidationsAccumulatesErrors() {
        (SystemError e1, SystemError e2, SystemError e3) = (
            TestError,
            new(ErrorDomain.Results, 1002, "E2"),
            new(ErrorDomain.Results, 1003, "E3"));
        TestUtilities.AssertAll(
            Gen.Int[1, 100].ToAssertion((Action<int>)(v => Assert.True(ResultFactory.Create(value: v).Ensure((Func<int, bool>)(x => x > 0), e1).IsSuccess)), 50),
            Gen.Int[1, 100].ToAssertion((Action<int>)(v => Assert.False(ResultFactory.Create(value: v).Ensure((Func<int, bool>)(x => x < 0), e1).IsSuccess)), 50),
            Gen.Int.ToAssertion((Action<int>)(v =>
                Assert.Equal((v is > 0 and < 100), ResultFactory.Create(value: v).Ensure(((Func<int, bool>)(x => x > 0), e1), ((Func<int, bool>)(x => x < 100), e2)).IsSuccess))),
            Gen.Int.ToAssertion((Action<int>)(v => Assert.Equal(v > 0,
                ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)).Ensure((Func<int, bool>)(x => x > 0), e1).IsSuccess)), 50));
    }

    /// <summary>Verifies Match using algebraic pattern exhaustion.</summary>
    [Fact]
    public void MatchPatternMatchingInvokesCorrectHandler() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion((Action<int>)(v => Assert.Equal(v * 2, ResultFactory.Create(value: v).Match(x => x * 2, _ => -1)))),
        ResultGenerators.SystemErrorArrayGen.ToAssertion((Action<SystemError[]>)(errs =>
            Assert.Equal(errs.Length, ResultFactory.Create<int>(errors: errs).Match(v => v * 2, e => e.Length)))),
        Gen.Int.ToAssertion((Action<int>)(v => Assert.Equal(v.ToString(CultureInfo.InvariantCulture),
            ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)).Match(x => x.ToString(CultureInfo.InvariantCulture), _ => "error"))), 50));

    /// <summary>Verifies Apply applicative functor using algebraic error accumulation matrix.</summary>
    [Fact]
    public void ApplyApplicativeFunctorAccumulatesErrorsInParallel() => TestUtilities.AssertAll(
        Gen.Int.Tuple(Gen.Int).ToAssertion((Action<int, int>)((a, b) => {
            Result<int> applied = ResultFactory.Create(value: a).Apply(ResultFactory.Create<Func<int, int>>(value: x => x + b));
            Assert.True(applied.IsSuccess);
            Assert.Equal(a + b, applied.Value);
        })),
        ResultGenerators.SystemErrorGen.Tuple(Gen.Int).ToAssertion((Action<SystemError, int>)((err, b) => {
            Result<int> applied = ResultFactory.Create<int>(error: err).Apply(ResultFactory.Create<Func<int, int>>(value: x => x + b));
            Assert.False(applied.IsSuccess);
            Assert.Contains(err, applied.Errors);
        }), 50),
        Gen.Int.Tuple(ResultGenerators.SystemErrorGen).ToAssertion((Action<int, SystemError>)((a, err) => {
            Result<int> applied = ResultFactory.Create(value: a).Apply(ResultFactory.Create<Func<int, int>>(error: err));
            Assert.False(applied.IsSuccess);
            Assert.Contains(err, applied.Errors);
        }), 50),
        ResultGenerators.SystemErrorGen.Tuple(ResultGenerators.SystemErrorGen).ToAssertion((Action<SystemError, SystemError>)((e1, e2) => {
            Result<int> applied = ResultFactory.Create<int>(error: e1).Apply(ResultFactory.Create<Func<int, int>>(error: e2));
            Assert.Equal(2, applied.Errors.Count);
        }), 50));

    /// <summary>Verifies Traverse using monadic collection transformation algebra.</summary>
    [Fact]
    public void TraverseCollectionTransformationTransformsAndAccumulates() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion((Action<int>)(v => Assert.Single(ResultFactory.Create(value: v).Traverse(x => ResultFactory.Create(value: x.ToString(CultureInfo.InvariantCulture))).Value))),
        Gen.Int.List[1, 10].ToAssertion((Action<List<int>>)(items => {
            Result<IReadOnlyList<int>> traversed = ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => ResultFactory.Create(value: x * 2));
            Assert.Equal(items.Count, traversed.Value.Count);
        }), 50),
        Gen.Int.List[1, 10].ToAssertion((Action<List<int>>)(items =>
            Assert.Equal(!items.Exists(x => x % 2 != 0),
                ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => x % 2 == 0
                    ? ResultFactory.Create(value: x) : ResultFactory.Create<int>(error: TestError)).IsSuccess)), 50),
        ResultGenerators.SystemErrorGen.ToAssertion((Action<SystemError>)(err =>
            Assert.False(ResultFactory.Create<IEnumerable<int>>(error: err).TraverseElements(x => ResultFactory.Create(value: x * 2)).IsSuccess)), 50));

    /// <summary>Verifies deferred execution using lazy evaluation algebra and resource safety.</summary>
    [Fact]
    public void DeferredExecutionLazyEvaluationBehavesCorrectly() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion((Action<int>)(v => {
            int count = 0;
            Result<int> deferred = ResultFactory.Create(deferred: () => { count++; return ResultFactory.Create(value: v); });
            Assert.True(deferred.IsDeferred);
            Assert.Equal(0, count);
            _ = deferred.Value;
            _ = deferred.Value;
            Assert.Equal(1, count); // Only evaluates once
        }), 50),
        ResultGenerators.SystemErrorGen.ToAssertion((Action<SystemError>)(err => {
            bool executed = false;
            Result<int> deferred = ResultFactory.Create(deferred: () => { executed = true; return ResultFactory.Create<int>(error: err); });
            Assert.Equal(executed, !deferred.IsSuccess);
        }), 50),
        Gen.Int.ToAssertion((Action<int>)(v => {
            int mapCount = 0, bindCount = 0;
            int final = ResultFactory.Create(deferred: () => ResultFactory.Create(value: v))
                .Map(x => { mapCount++; return x * 2; })
                .Bind(x => { bindCount++; return ResultFactory.Create(value: x + 10); })
                .Value;
            Assert.Equal(((v * 2) + 10, 1, 1), (final, mapCount, bindCount));
        }), 50),
        () => {
            using MemoryStream stream = new();
            Result<int> result = ResultFactory.Create(deferred: () => { stream.WriteByte(1); return ResultFactory.Create(value: 1); });
            stream.Dispose();
            Assert.Throws<ObjectDisposedException>(() => result.Value);
        });

    /// <summary>Verifies Apply side effects preserve Result state using algebraic identity preservation.</summary>
    [Fact]
    public void ApplyMethodSideEffectsPreservesState() => TestUtilities.AssertAll(
        Gen.Int.ToAssertion((Action<int>)(v => {
            int captured = 0;
            Result<int> result = ResultFactory.Create(value: v).Apply(onSuccess: x => captured = x);
            Assert.Equal((v, v, true), (captured, result.Value, result.IsSuccess));
        })),
        ResultGenerators.SystemErrorArrayGen.ToAssertion((Action<SystemError[]>)(errs => {
            SystemError[]? captured = null;
            Result<int> result = ResultFactory.Create<int>(errors: errs).Apply(onFailure: e => captured = [.. e]);
            Assert.Equal((errs.Length, false), (captured!.Length, result.IsSuccess));
        }), 50),
        Gen.Bool.ToAssertion((Action<bool>)(isSuccess => {
            (bool successCalled, bool failureCalled) = (false, false);
            (isSuccess ? ResultFactory.Create(value: 42) : ResultFactory.Create<int>(error: TestError))
                .Apply(_ => successCalled = true, _ => failureCalled = true);
            Assert.Equal((isSuccess, !isSuccess), (successCalled, failureCalled));
        })));

    /// <summary>Verifies OnError transformation and recovery using algebraic error morphisms.</summary>
    [Fact]
    public void OnErrorErrorHandlingTransformsAndRecovers() {
        (SystemError e1, SystemError e2) = (TestError, new(ErrorDomain.Results, 1002, "E2"));
        TestUtilities.AssertAll(
            () => {
                Result<int> mapped = ResultFactory.Create<int>(error: e1).OnError(mapError: _ => [e2]);
                Assert.True(!mapped.IsSuccess && mapped.Errors.Contains(e2) && !mapped.Errors.Contains(e1));
            },
            () => Assert.Equal(99, ResultFactory.Create<int>(error: e1).OnError(recover: _ => 99).Value),
            () => Assert.Equal(77, ResultFactory.Create<int>(error: e1).OnError(recoverWith: _ => ResultFactory.Create(value: 77)).Value),
            Gen.Int.ToAssertion((Action<int>)(v => Assert.Equal(v, ResultFactory.Create(value: v).OnError(mapError: _ => [e2]).Value)), 50));
    }
}
