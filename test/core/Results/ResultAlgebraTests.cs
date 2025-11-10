using System.Globalization;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Algebraic tests for Result monad with category theory laws, factory polymorphism, and operational semantics.</summary>
public sealed class ResultAlgebraTests {
    private static readonly (SystemError E1, SystemError E2, SystemError E3) Errors = (
        new(ErrorDomain.Results, 1001, "E1"),
        new(ErrorDomain.Results, 1002, "E2"),
        new(ErrorDomain.Results, 1003, "E3"));

    /// <summary>Verifies functor laws (identity and composition) via property-based testing.</summary>
    [Fact]
    public void FunctorLaws() => Test.RunAll(
        () => Test.Law<int>("FunctorIdentity", ResultGenerators.ResultGen<int>()),
        () => Test.Functor(
            ResultGenerators.ResultGen<int>(),
            f: x => x.ToString(CultureInfo.InvariantCulture),
            g: s => s.Length));

    /// <summary>Verifies monad laws (left identity, right identity, associativity) with algebraic composition.</summary>
    [Fact]
    public void MonadLaws() => Test.RunAll(
        () => Gen.Int.Select(ResultGenerators.MonadicFunctionGen<int, string>()).Run((int v, Func<int, Result<string>> f) =>
            ResultFactory.Create(value: v).Bind(f).Equals(f(v)), 100),
        () => Test.Law<int>("MonadRightIdentity", ResultGenerators.ResultGen<int>(), 100),
        () => ResultGenerators.ResultGen<int>().Select(ResultGenerators.MonadicFunctionGen<int, string>(), ResultGenerators.MonadicFunctionGen<string, double>()).Run(
            (Result<int> r, Func<int, Result<string>> f, Func<string, Result<double>> g) =>
                r.Bind(f).Bind(g).Equals(r.Bind(x => f(x).Bind(g))), 50));

    /// <summary>Verifies applicative functor and equality laws via reflexivity, symmetry, and hash consistency.</summary>
    [Fact]
    public void ApplicativeAndEqualityLaws() => Test.RunAll(
        () => Test.Law<int>("ApplicativeIdentity", ResultGenerators.ResultGen<int>()),
        () => Test.Law<int>("EqualityReflexive", ResultGenerators.ResultGen<int>(), 100),
        () => Test.Law<int>("EqualitySymmetric", ResultGenerators.ResultGen<int>(), ResultGenerators.ResultGen<int>(), 100),
        () => Test.Law<int>("HashConsistent", Gen.Int, (Func<int, Result<int>>)(v => ResultFactory.Create(value: v)), 100));

    /// <summary>Verifies Map functor transformation via property-based and concrete value testing.</summary>
    [Fact]
    public void MapFunctorTransformation() => Test.RunAll(
        () => Assert.Equal(10, ResultFactory.Create(value: 5).Map(static x => x * 2).Value),
        () => Assert.Equal("42", ResultFactory.Create(value: 42).Map(static x => x.ToString(CultureInfo.InvariantCulture)).Value),
        () => Assert.Equal(3, ResultFactory.Create(value: "abc").Map(static s => s.Length).Value),
        () => Gen.Int.Run((Action<int>)(n => Assert.Equal(unchecked(n + 100), ResultFactory.Create(value: n).Map(x => unchecked(x + 100)).Value))),
        () => Assert.False(ResultFactory.Create<int>(error: Errors.E1).Map(static x => x * 2).IsSuccess));

    /// <summary>Verifies Bind monadic chaining with error propagation and flatMap semantics.</summary>
    [Fact]
    public void BindMonadicChaining() => Test.RunAll(
        () => Assert.Equal(15, ResultFactory.Create(value: 5).Bind(static x => ResultFactory.Create(value: x * 3)).Value),
        () => Assert.Equal("10", ResultFactory.Create(value: 5).Bind(static x => ResultFactory.Create(value: (x * 2).ToString(CultureInfo.InvariantCulture))).Value),
        () => Gen.Int.Run((Action<int>)(n => Assert.Equal(unchecked((n * 2) + 10),
            ResultFactory.Create(value: n).Bind(x => ResultFactory.Create(value: unchecked(x * 2))).Bind(x => ResultFactory.Create(value: unchecked(x + 10))).Value))),
        () => Assert.False(ResultFactory.Create<int>(error: Errors.E1).Bind(static x => ResultFactory.Create(value: x * 2)).IsSuccess),
        () => Assert.False(ResultFactory.Create(value: 5).Bind(static _ => ResultFactory.Create<int>(error: Errors.E1)).IsSuccess));

    /// <summary>Verifies ResultFactory.Create polymorphic parameter detection via tuple pattern matching.</summary>
    [Fact]
    public void CreatePolymorphicParameterDetection() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> r = ResultFactory.Create(value: v);
            Assert.Equal((true, v), (r.IsSuccess, r.Value));
        }), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(e => {
            Result<int> r = ResultFactory.Create<int>(error: e);
            Assert.True(!r.IsSuccess && r.Errors.Contains(e));
        }), 50),
        () => ResultGenerators.SystemErrorArrayGen.Run((Action<SystemError[]>)(errs =>
            Assert.Equal(errs.Length, ResultFactory.Create<int>(errors: errs).Errors.Count)), 50),
        () => Gen.Int.Run((Action<int>)(v => {
            (bool executed, Result<int> result) = (false, ResultFactory.Create(deferred: () => { executed = true; return ResultFactory.Create(value: v); }));
            Assert.Equal((true, false, v, true), (result.IsDeferred, executed, result.Value, executed));
        }), 50),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v > 0, ResultFactory.Create(value: v, conditionals: [(x => x > 0, Errors.E1)]).IsSuccess)), 50),
        () => ResultGenerators.NestedResultGen<int>().Run((Action<Result<Result<int>>>)(nested =>
            Assert.Equal(nested.IsSuccess && nested.Value.IsSuccess, ResultFactory.Create<int>(nested: nested).IsSuccess)), 50));

    /// <summary>Verifies Validate polymorphic operations with predicate, unless, premise/conclusion, and batch validations.</summary>
    [Fact]
    public void ValidatePolymorphicOperations() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v > 0, ResultFactory.Create(value: v).Validate(predicate: x => x > 0, error: Errors.E1).IsSuccess)), 50),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v >= 0, ResultFactory.Create(value: v).Validate(predicate: x => x < 0, error: Errors.E1, unless: true).IsSuccess)), 50),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v is <= 10 or < 100,
                ResultFactory.Create(value: v).Validate(error: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess)), 50),
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> result = ResultFactory.Create(value: v).Validate(validations: [
                (x => x > 0, Errors.E1),
                (x => x < 100, Errors.E2),
                (x => x % 2 == 0, Errors.E3),
            ]);
            Assert.Equal((v is > 0 and < 100 && v % 2 == 0), result.IsSuccess);
        }), 50),
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> result = ResultFactory.Create(value: v).Validate(predicate: x => x > 10, validation: x => ResultFactory.Create(value: unchecked(x * 2)));
            Assert.Equal(v > 10 ? unchecked(v * 2) : v, result.Value);
        }), 50),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v > 0, ResultFactory.Create(value: v).Validate(args: [(Func<int, bool>)(x => x > 0), Errors.E1]).IsSuccess)), 50));

    /// <summary>Verifies Lift applicative functor with partial application and error accumulation.</summary>
    [Fact]
    public void LiftApplicativeFunctor() {
        Func<int, int, int> add = static (x, y) => unchecked(x + y);
        Test.RunAll(
            () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((a, b) => {
                Result<int> result = (Result<int>)ResultFactory.Lift<int>(add, ResultFactory.Create(value: a), ResultFactory.Create(value: b));
                Assert.Equal((true, unchecked(a + b)), (result.IsSuccess, result.Value));
            }), 50),
            () => Gen.Int.Select(ResultGenerators.SystemErrorGen).Run((Action<int, SystemError>)((v, err) => {
                Result<int> result = (Result<int>)ResultFactory.Lift<int>(add, ResultFactory.Create(value: v), ResultFactory.Create<int>(error: err));
                Assert.True(!result.IsSuccess && result.Errors.Contains(err));
            }), 50),
            () => ResultGenerators.SystemErrorGen.Select(ResultGenerators.SystemErrorGen).Run((Action<SystemError, SystemError>)((e1, e2) => {
                Result<int> result = (Result<int>)ResultFactory.Lift<int>(add, ResultFactory.Create<int>(error: e1), ResultFactory.Create<int>(error: e2));
                Assert.Equal((false, 2), (result.IsSuccess, result.Errors.Count));
            }), 50),
            () => Gen.Int.Run((Action<int>)(v => {
                Result<Func<object[], int>> partial = (Result<Func<object[], int>>)ResultFactory.Lift<int>((Func<int, int, int, int>)((x, y, z) => unchecked(x + y + z)), [ResultFactory.Create(value: v)]);
                Assert.True(partial.IsSuccess);
            }), 50));
    }

    /// <summary>Verifies TraverseElements monadic transformation with error accumulation and empty collections.</summary>
    [Fact]
    public void TraverseElementsMonadicTransformation() => Test.RunAll(
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items => {
            Result<IReadOnlyList<int>> result = ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => ResultFactory.Create(value: unchecked(x * 2)));
            Assert.True(result.IsSuccess);
            Assert.Equal(items.Count, result.Value.Count);
            Assert.Equal(items.Select(x => unchecked(x * 2)), result.Value);
        }), 50),
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items =>
            Assert.Equal(!items.Exists(x => x % 2 != 0),
                ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => x % 2 == 0
                    ? ResultFactory.Create(value: x) : ResultFactory.Create<int>(error: Errors.E1)).IsSuccess)), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.False(ResultFactory.Create<IEnumerable<int>>(error: err).TraverseElements(x => ResultFactory.Create(value: unchecked(x * 2))).IsSuccess)), 50),
        () => Assert.Empty(ResultFactory.Create<IEnumerable<int>>(value: []).TraverseElements(x => ResultFactory.Create(value: unchecked(x * 2))).Value));

    /// <summary>Verifies Match pattern exhaustion with algebraic extraction semantics.</summary>
    [Theory]
    [MemberData(nameof(MatchExtractionTestCases))]
    public void MatchExtractionExhaustivePatterns(Result<int> result, bool expectedSuccess) =>
        Assert.Equal(expectedSuccess, result.Match(_ => true, _ => false));

    public static IEnumerable<object[]> MatchExtractionTestCases {
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

    /// <summary>Verifies Match reduction with handler invocation and accumulation patterns.</summary>
    [Fact]
    public void MatchReductionSemantics() => Test.RunAll(
        () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((value, seed) =>
            Assert.Equal(unchecked(seed + value), ResultFactory.Create(value: value).Match(v => unchecked(seed + v), _ => seed)))),
        () => ResultGenerators.SystemErrorArrayGen.Select(Gen.Int).Run((Action<SystemError[], int>)((errors, seed) =>
            Assert.Equal(unchecked(seed + errors.Length),
                ResultFactory.Create<int>(errors: errors).Match(_ => seed, errs => unchecked(seed + errs.Length))))),
        () => Gen.Int.Run((Action<int>)(seed =>
            Assert.Equal(seed, ResultFactory.Create<int>(error: Errors.E1).Match(v => unchecked(seed + v), _ => seed)))));

    /// <summary>Verifies Ensure predicate validation with single and multiple patterns.</summary>
    [Fact]
    public void EnsurePredicateValidation() => Test.RunAll(
        () => Gen.Int[1, 100].Run((Action<int>)(value =>
            Assert.True(ResultFactory.Create(value: value).Ensure(x => x > 0, Errors.E1).IsSuccess))),
        () => Gen.Int[1, 100].Run((Action<int>)(value =>
            Assert.False(ResultFactory.Create(value: value).Ensure(x => x < 0, Errors.E1).IsSuccess))),
        () => ResultGenerators.FailureGen<int>().Run((Action<Result<int>>)(result =>
            Assert.False(result.Ensure(x => x > 0, Errors.E1).IsSuccess))),
        () => Gen.Int.Run((Action<int>)(value =>
            Assert.Equal(value > 0,
                ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)).Ensure(x => x > 0, Errors.E1).IsSuccess))));

    /// <summary>Verifies Ensure multiple validations with error accumulation.</summary>
    [Fact]
    public void EnsureMultipleValidationsErrorAccumulation() => Test.RunAll(
        () => Gen.Int[1, 100].Run((Action<int>)(v => Assert.True(ResultFactory.Create(value: v).Ensure(x => x > 0, Errors.E1).IsSuccess)), 50),
        () => Gen.Int[1, 100].Run((Action<int>)(v => Assert.False(ResultFactory.Create(value: v).Ensure(x => x < 0, Errors.E1).IsSuccess)), 50),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal((v is > 0 and < 100), ResultFactory.Create(value: v).Ensure((x => x > 0, Errors.E1), (x => x < 100, Errors.E2)).IsSuccess))),
        () => Gen.Int.Run((Action<int>)(v => Assert.Equal(v > 0,
            ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)).Ensure(x => x > 0, Errors.E1).IsSuccess)), 50));

    /// <summary>Verifies Apply applicative functor with parallel error accumulation.</summary>
    [Fact]
    public void ApplyApplicativeFunctorParallelErrors() {
        (SystemError funcErr, SystemError valErr) = (
            new(ErrorDomain.Results, 5001, "Function error"),
            new(ErrorDomain.Results, 5002, "Value error"));
        Test.RunAll(
            () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((a, b) => {
                Result<int> applied = ResultFactory.Create(value: a).Apply(ResultFactory.Create<Func<int, int>>(value: x => unchecked(x + b)));
                Assert.Equal((true, unchecked(a + b)), (applied.IsSuccess, applied.Value));
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

    /// <summary>Verifies Traverse collection transformation with single value and collection dispatch.</summary>
    [Fact]
    public void TraverseCollectionTransformation() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => Assert.Single(ResultFactory.Create(value: v).Traverse(x => ResultFactory.Create(value: x.ToString(CultureInfo.InvariantCulture))).Value))),
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items => {
            Result<IReadOnlyList<int>> traversed = ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => ResultFactory.Create(value: unchecked(x * 2)));
            Assert.Equal(items.Count, traversed.Value.Count);
        }), 50),
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items =>
            Assert.Equal(!items.Exists(x => x % 2 != 0),
                ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => x % 2 == 0
                    ? ResultFactory.Create(value: x) : ResultFactory.Create<int>(error: Errors.E1)).IsSuccess)), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.False(ResultFactory.Create<IEnumerable<int>>(error: err).TraverseElements(x => ResultFactory.Create(value: unchecked(x * 2))).IsSuccess)), 50));

    /// <summary>Verifies deferred execution with lazy evaluation semantics.</summary>
    [Fact]
    public void DeferredExecutionLazyEvaluation() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int count = 0;
            Result<int> deferred = ResultFactory.Create(deferred: () => { count++; return ResultFactory.Create(value: v); });
            Assert.True(deferred.IsDeferred);
            Assert.Equal(0, count);
            _ = deferred.Value;
            Assert.True(count >= 1);
            _ = deferred.Value;
            Assert.True(count >= 2);
        }), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err => {
            int execCount = 0;
            Result<int> deferred = ResultFactory.Create(deferred: () => { execCount++; return ResultFactory.Create<int>(error: err); });
            Assert.Equal(0, execCount);
            bool success = deferred.IsSuccess;
            Assert.False(success);
            Assert.True(execCount >= 1);
        }), 50),
        () => Gen.Int.Run((Action<int>)(v => {
            int mapCount = 0, bindCount = 0;
            int final = ResultFactory.Create(deferred: () => ResultFactory.Create(value: v))
                .Map(x => { mapCount++; return unchecked(x * 2); })
                .Bind(x => { bindCount++; return ResultFactory.Create(value: unchecked(x + 10)); })
                .Value;
            Assert.Equal(unchecked((v * 2) + 10), final);
            Assert.True(mapCount >= 1 && bindCount >= 1);
        }), 50),
        () => {
            using MemoryStream stream = new();
            Result<int> result = ResultFactory.Create(deferred: () => { stream.WriteByte(1); return ResultFactory.Create(value: 1); });
            stream.Dispose();
            Assert.Throws<ObjectDisposedException>(() => result.Value);
        });

    /// <summary>Verifies Tap side effects preserve Result state with algebraic identity.</summary>
    [Fact]
    public void TapSideEffectsPreserveState() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int captured = 0;
            Result<int> result = ResultFactory.Create(value: v).Tap(onSuccess: x => captured = x);
            Assert.Equal((v, v, true), (captured, result.Value, result.IsSuccess));
        })),
        () => ResultGenerators.SystemErrorArrayGen.Run((Action<SystemError[]>)(errs => {
            SystemError[]? captured = null;
            Result<int> result = ResultFactory.Create<int>(errors: errs).Tap(onFailure: e => captured = [.. e]);
            Assert.Equal((errs.Length, false), (captured!.Length, result.IsSuccess));
        }), 50),
        () => Gen.Bool.Run((Action<bool>)(isSuccess => {
            (bool successCalled, bool failureCalled) = (false, false);
            (isSuccess ? ResultFactory.Create(value: 42) : ResultFactory.Create<int>(error: Errors.E1))
                .Tap(_ => successCalled = true, _ => failureCalled = true);
            Assert.Equal((isSuccess, !isSuccess), (successCalled, failureCalled));
        })));

    /// <summary>Verifies OnError transformation and recovery with overload variants.</summary>
    [Fact]
    public void OnErrorTransformationAndRecovery() => Test.RunAll(
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(origErr => {
            Result<int> result = ResultFactory.Create<int>(error: origErr).OnError(_ => [Errors.E2]);
            Assert.True(!result.IsSuccess && result.Errors.Contains(Errors.E2) && !result.Errors.Contains(origErr));
        }), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.Equal(42, ResultFactory.Create<int>(error: err).OnError(_ => 42).Value)), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.Equal(99, ResultFactory.Create<int>(error: err).OnError(_ => ResultFactory.Create(value: 99)).Value)), 50),
        () => Assert.Contains(Errors.E1, ResultFactory.Create<int>(error: Errors.E1).Map(x => x * 2).Bind(x => ResultFactory.Create(value: x + 10)).Ensure(x => x > 0, Errors.E2).Errors));

    /// <summary>Verifies null argument handling via exception patterns.</summary>
    [Fact]
    public void NullArgumentExceptionPatterns() {
        (Result<int> success, Result<int> failure) = (ResultFactory.Create(value: 42), ResultFactory.Create<int>(error: Errors.E1));
        Test.RunAll(
            () => Assert.Throws<ArgumentNullException>(() => success.Map((Func<int, int>)null!)),
            () => Assert.Throws<ArgumentNullException>(() => success.Bind((Func<int, Result<int>>)null!)),
            () => Assert.Throws<ArgumentNullException>(() => success.Match(null!, _ => 0)),
            () => Assert.Throws<ArgumentNullException>(() => success.Ensure(null!, Errors.E1)),
            () => Assert.Throws<ArgumentNullException>(() => ResultFactory.Lift<int>(null!, 1, 2)),
            () => Assert.Throws<ArgumentNullException>(() => ResultFactory.Create<IEnumerable<int>>(value: [1, 2]).TraverseElements((Func<int, Result<int>>)null!)),
            () => Assert.Throws<InvalidOperationException>(() => failure.Value),
            () => Assert.NotEmpty(failure.Errors));
    }

    /// <summary>Verifies Lift arity mismatch detection with ArgumentException.</summary>
    [Fact]
    public void LiftArityMismatchExceptions() => Test.RunAll(
        () => Assert.Throws<ArgumentException>(() =>
            ResultFactory.Lift<int>((Func<int, int, int>)((x, y) => x + y), [ResultFactory.Create(value: 1)])),
        () => Assert.Throws<ArgumentException>(() =>
            ResultFactory.Lift<int>((Func<int, int, int>)((x, y) => x + y), [1, 2, 3])));
}
