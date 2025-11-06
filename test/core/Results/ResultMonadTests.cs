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
    public void FunctorLaws() => TestGen.RunAll(
        () => TestLaw.Verify<int>("FunctorIdentity", ResultGenerators.ResultGen<int>()),
        () => TestLaw.VerifyFunctor(
            ResultGenerators.ResultGen<int>(),
            f: x => x.ToString(CultureInfo.InvariantCulture),
            g: s => s.Length));

    /// <summary>Verifies Map actually transforms values with concrete examples.</summary>
    [Fact]
    public void MapValueTransformationAppliesCorrectly() => TestGen.RunAll(
        () => Assert.Equal(10, ResultFactory.Create(value: 5).Map(static x => x * 2).Value),
        () => Assert.Equal("42", ResultFactory.Create(value: 42).Map(static x => x.ToString(CultureInfo.InvariantCulture)).Value),
        () => Assert.Equal(3, ResultFactory.Create(value: "abc").Map(static s => s.Length).Value),
        () => Gen.Int.Run((Action<int>)(n => Assert.Equal(n + 100, ResultFactory.Create(value: n).Map(x => x + 100).Value))),
        () => Assert.False(ResultFactory.Create<int>(error: TestError).Map(static x => x * 2).IsSuccess));

    /// <summary>Verifies Bind actually chains transformations with concrete examples.</summary>
    [Fact]
    public void BindValueTransformationChainsCorrectly() => TestGen.RunAll(
        () => Assert.Equal(15, ResultFactory.Create(value: 5).Bind(static x => ResultFactory.Create(value: x * 3)).Value),
        () => Assert.Equal("10", ResultFactory.Create(value: 5).Bind(static x => ResultFactory.Create(value: (x * 2).ToString(CultureInfo.InvariantCulture))).Value),
        () => Gen.Int.Run((Action<int>)(n => Assert.Equal((n * 2) + 10,
            ResultFactory.Create(value: n).Bind(x => ResultFactory.Create(value: x * 2)).Bind(x => ResultFactory.Create(value: x + 10)).Value))),
        () => Assert.False(ResultFactory.Create<int>(error: TestError).Bind(static x => ResultFactory.Create(value: x * 2)).IsSuccess),
        () => Assert.False(ResultFactory.Create(value: 5).Bind(static _ => ResultFactory.Create<int>(error: TestError)).IsSuccess));

    /// <summary>Verifies monad category laws: left identity, right identity, and associativity.</summary>
    [Fact]
    public void MonadLaws() => TestGen.RunAll(
        () => Gen.Int.Select(ResultGenerators.MonadicFunctionGen<int, string>()).Run((int v, Func<int, Result<string>> f) =>
            ResultFactory.Create(value: v).Bind(f).Equals(f(v)), 100),
        () => TestLaw.Verify<int>("MonadRightIdentity", ResultGenerators.ResultGen<int>(), 100),
        () => ResultGenerators.ResultGen<int>().Select(ResultGenerators.MonadicFunctionGen<int, string>(), ResultGenerators.MonadicFunctionGen<string, double>()).Run(
            (Result<int> r, Func<int, Result<string>> f, Func<string, Result<double>> g) =>
                r.Bind(f).Bind(g).Equals(r.Bind(x => f(x).Bind(g))), 50));

    /// <summary>Verifies applicative functor and equality laws.</summary>
    [Fact]
    public void ApplicativeAndEqualityLaws() => TestGen.RunAll(
        () => TestLaw.Verify<int>("ApplicativeIdentity", ResultGenerators.ResultGen<int>()),
        () => TestLaw.Verify<int>("EqualityReflexive", ResultGenerators.ResultGen<int>(), 100),
        () => TestLaw.Verify<int>("EqualitySymmetric", ResultGenerators.ResultGen<int>(), ResultGenerators.ResultGen<int>(), 100),
        () => TestLaw.Verify<int>("HashConsistent", Gen.Int, (Func<int, Result<int>>)(v => ResultFactory.Create(value: v)), 100));

    /// <summary>Verifies TryGet extraction using algebraic success/failure partition.</summary>
    [Theory]
    [MemberData(nameof(TryGetTestCases))]
    public void TryGetExtractionBehaviorMatchesResultState(Result<int> result, bool expectedSuccess) =>
        Assert.Equal(expectedSuccess, result.TryGet(out int extracted));

    public static IEnumerable<object[]> TryGetTestCases {
        get {
            (int value, SystemError error) = Gen.Int.Select(ResultGenerators.SystemErrorGen).Single();
            return [
                [ResultFactory.Create(value: value), true,],
                [ResultFactory.Create<int>(error: error), false,],
                [ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)), true,],
                [ResultFactory.Create(deferred: () => ResultFactory.Create<int>(error: error)), false,],
            ];
        }
    }

    /// <summary>Verifies Reduce accumulation using algebraic handler selection.</summary>
    [Fact]
    public void ReduceAccumulationBehaviorAppliesCorrectHandler() => TestGen.RunAll(
        () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((value, seed) =>
            Assert.Equal(seed + value, ResultFactory.Create(value: value).Reduce(seed, (s, v) => s + v)))),
        () => ResultGenerators.SystemErrorArrayGen.Select(Gen.Int).Run((Action<SystemError[], int>)((errors, seed) =>
            Assert.Equal(seed + errors.Length,
                ResultFactory.Create<int>(errors: errors).Reduce(seed, (s, v) => s + v, (s, errs) => s + errs.Length)))),
        () => Gen.Int.Run((Action<int>)(seed =>
            Assert.Equal(seed, ResultFactory.Create<int>(error: TestError).Reduce(seed, (s, v) => s + v)))));

    /// <summary>Verifies Filter using predicate pattern matching and error propagation.</summary>
    [Fact]
    public void FilterPredicateValidationFiltersCorrectly() => TestGen.RunAll(
        () => Gen.Int[1, 100].Run((Action<int>)(value =>
            Assert.True(ResultFactory.Create(value: value).Filter(x => x > 0, TestError).IsSuccess))),
        () => Gen.Int[1, 100].Run((Action<int>)(value =>
            Assert.False(ResultFactory.Create(value: value).Filter(x => x < 0, TestError).IsSuccess))),
        () => ResultGenerators.FailureGen<int>().Run((Action<Result<int>>)(result =>
            Assert.False(result.Filter(x => x > 0, TestError).IsSuccess))),
        () => Gen.Int.Run((Action<int>)(value =>
            Assert.Equal(value > 0,
                ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)).Filter(x => x > 0, TestError).IsSuccess))));

    /// <summary>Verifies Ensure validation using algebraic error accumulation and partition logic.</summary>
    [Fact]
    public void EnsureMultipleValidationsAccumulatesErrors() {
        (SystemError e1, SystemError e2, SystemError e3) = (
            TestError,
            new(ErrorDomain.Results, 1002, "E2"),
            new(ErrorDomain.Results, 1003, "E3"));
        TestGen.RunAll(
            () => Gen.Int[1, 100].Run((Action<int>)(v => Assert.True(ResultFactory.Create(value: v).Ensure((Func<int, bool>)(x => x > 0), e1).IsSuccess)), 50),
            () => Gen.Int[1, 100].Run((Action<int>)(v => Assert.False(ResultFactory.Create(value: v).Ensure((Func<int, bool>)(x => x < 0), e1).IsSuccess)), 50),
            () => Gen.Int.Run((Action<int>)(v =>
                Assert.Equal((v is > 0 and < 100), ResultFactory.Create(value: v).Ensure(((Func<int, bool>)(x => x > 0), e1), ((Func<int, bool>)(x => x < 100), e2)).IsSuccess))),
            () => Gen.Int.Run((Action<int>)(v => Assert.Equal(v > 0,
                ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)).Ensure((Func<int, bool>)(x => x > 0), e1).IsSuccess)), 50));
    }

    /// <summary>Verifies Match using algebraic pattern exhaustion.</summary>
    [Fact]
    public void MatchPatternMatchingInvokesCorrectHandler() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => Assert.Equal(v * 2, ResultFactory.Create(value: v).Match(x => x * 2, _ => -1)))),
        () => ResultGenerators.SystemErrorArrayGen.Run((Action<SystemError[]>)(errs =>
            Assert.Equal(errs.Length, ResultFactory.Create<int>(errors: errs).Match(v => v * 2, e => e.Length)))),
        () => Gen.Int.Run((Action<int>)(v => Assert.Equal(v.ToString(CultureInfo.InvariantCulture),
            ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)).Match(x => x.ToString(CultureInfo.InvariantCulture), _ => "error"))), 50));

    /// <summary>Verifies Apply applicative functor using algebraic error accumulation matrix with ordering.</summary>
    [Fact]
    public void ApplyApplicativeFunctorAccumulatesErrorsInParallel() {
        (SystemError funcErr, SystemError valErr) = (
            new(ErrorDomain.Results, 5001, "Function error"),
            new(ErrorDomain.Results, 5002, "Value error"));
        TestGen.RunAll(
            () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((a, b) => {
                Result<int> applied = ResultFactory.Create(value: a).Apply(ResultFactory.Create<Func<int, int>>(value: x => x + b));
                Assert.Equal((true, a + b), (applied.IsSuccess, applied.Value));
            })),
            () => {
                Result<int> applied = ResultFactory.Create<int>(error: valErr).Apply(ResultFactory.Create<Func<int, int>>(value: static x => x + 10));
                Assert.Equal((false, 1, valErr), (applied.IsSuccess, applied.Errors.Count, applied.Error));
            },
            () => {
                Result<int> applied = ResultFactory.Create(value: 5).Apply(ResultFactory.Create<Func<int, int>>(error: funcErr));
                Assert.Equal((false, 1, funcErr), (applied.IsSuccess, applied.Errors.Count, applied.Error));
            },
            () => {
                Result<int> applied = ResultFactory.Create<int>(error: valErr).Apply(ResultFactory.Create<Func<int, int>>(error: funcErr));
                Assert.Equal((false, 2, funcErr, valErr), (applied.IsSuccess, applied.Errors.Count, applied.Errors[0], applied.Errors[1]));
            });
    }

    /// <summary>Verifies Traverse using monadic collection transformation algebra.</summary>
    [Fact]
    public void TraverseCollectionTransformationTransformsAndAccumulates() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => Assert.Single(ResultFactory.Create(value: v).Traverse(x => ResultFactory.Create(value: x.ToString(CultureInfo.InvariantCulture))).Value))),
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items => {
            Result<IReadOnlyList<int>> traversed = ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => ResultFactory.Create(value: x * 2));
            Assert.Equal(items.Count, traversed.Value.Count);
        }), 50),
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items =>
            Assert.Equal(!items.Exists(x => x % 2 != 0),
                ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => x % 2 == 0
                    ? ResultFactory.Create(value: x) : ResultFactory.Create<int>(error: TestError)).IsSuccess)), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.False(ResultFactory.Create<IEnumerable<int>>(error: err).TraverseElements(x => ResultFactory.Create(value: x * 2)).IsSuccess)), 50));

    /// <summary>Verifies deferred execution using lazy evaluation algebra and resource safety.</summary>
    [Fact]
    public void DeferredExecutionLazyEvaluationBehavesCorrectly() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int count = 0;
            Result<int> deferred = ResultFactory.Create(deferred: () => { count++; return ResultFactory.Create(value: v); });
            Assert.True(deferred.IsDeferred);
            Assert.Equal(0, count);
            _ = deferred.Value;
            _ = deferred.Value;
            Assert.Equal(1, count); // Only evaluates once
        }), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err => {
            bool executed = false;
            Result<int> deferred = ResultFactory.Create(deferred: () => { executed = true; return ResultFactory.Create<int>(error: err); });
            Assert.Equal(executed, !deferred.IsSuccess);
        }), 50),
        () => Gen.Int.Run((Action<int>)(v => {
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
    public void ApplyMethodSideEffectsPreservesState() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int captured = 0;
            Result<int> result = ResultFactory.Create(value: v).Apply(onSuccess: x => captured = x);
            Assert.Equal((v, v, true), (captured, result.Value, result.IsSuccess));
        })),
        () => ResultGenerators.SystemErrorArrayGen.Run((Action<SystemError[]>)(errs => {
            SystemError[]? captured = null;
            Result<int> result = ResultFactory.Create<int>(errors: errs).Apply(onFailure: e => captured = [.. e]);
            Assert.Equal((errs.Length, false), (captured!.Length, result.IsSuccess));
        }), 50),
        () => Gen.Bool.Run((Action<bool>)(isSuccess => {
            (bool successCalled, bool failureCalled) = (false, false);
            (isSuccess ? ResultFactory.Create(value: 42) : ResultFactory.Create<int>(error: TestError))
                .Apply(_ => successCalled = true, _ => failureCalled = true);
            Assert.Equal((isSuccess, !isSuccess), (successCalled, failureCalled));
        })));

    /// <summary>Verifies OnError transformation and recovery using algebraic error morphisms.</summary>
    [Fact]
    public void OnErrorErrorHandlingTransformsAndRecovers() {
        (SystemError e1, SystemError e2) = (TestError, new(ErrorDomain.Results, 1002, "E2"));
        TestGen.RunAll(
            () => {
                Result<int> mapped = ResultFactory.Create<int>(error: e1).OnError(mapError: _ => [e2]);
                Assert.True(!mapped.IsSuccess && mapped.Errors.Contains(e2) && !mapped.Errors.Contains(e1));
            },
            () => Assert.Equal(99, ResultFactory.Create<int>(error: e1).OnError(recover: _ => 99).Value),
            () => Assert.Equal(77, ResultFactory.Create<int>(error: e1).OnError(recoverWith: _ => ResultFactory.Create(value: 77)).Value),
            () => Gen.Int.Run((Action<int>)(v => Assert.Equal(v, ResultFactory.Create(value: v).OnError(mapError: _ => [e2]).Value)), 50));
    }
}
