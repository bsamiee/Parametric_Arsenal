using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Algebraic tests for ResultFactory operations using zero-boilerplate composition and pattern matching.</summary>
public sealed class ResultFactoryTests {
    private static readonly (SystemError E1, SystemError E2, SystemError E3) Errors = (
        new(ErrorDomain.Results, 1001, "E1"),
        new(ErrorDomain.Results, 1002, "E2"),
        new(ErrorDomain.Results, 1003, "E3"));

    /// <summary>Verifies Create parameter polymorphism using algebraic sum type semantics.</summary>
    [Fact]
    public void CreateAllParameterCombinationsBehavesCorrectly() => TestGen.RunAll(
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

    /// <summary>Verifies Validate polymorphism using algebraic validation pattern matching.</summary>
    [Fact]
    public void ValidateAllParameterCombinationsValidatesCorrectly() => TestGen.RunAll(
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
            Result<int> result = ResultFactory.Create(value: v).Validate(predicate: x => x > 10, validation: x => ResultFactory.Create(value: x * 2));
            Assert.Equal(v > 10 ? v * 2 : v, result.Value);
        }), 50),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v > 0, ResultFactory.Create(value: v).Validate(args: [(Func<int, bool>)(x => x > 0), Errors.E1]).IsSuccess)), 50));

    /// <summary>Verifies Lift using algebraic applicative composition and partial application.</summary>
    [Fact]
    public void LiftFunctionLiftingAccumulatesErrorsApplicatively() {
        Func<int, int, int> add = static (x, y) => x + y;
        TestGen.RunAll(
            () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((a, b) => {
                Result<int> result = (Result<int>)ResultFactory.Lift<int>(add, ResultFactory.Create(value: a), ResultFactory.Create(value: b));
                Assert.Equal((true, a + b), (result.IsSuccess, result.Value));
            }), 50),
            () => Gen.Int.Select(ResultGenerators.SystemErrorGen).Run((Action<int, SystemError>)((v, err) => {
                Result<int> result = (Result<int>)ResultFactory.Lift<int>(add, ResultFactory.Create(value: v), ResultFactory.Create<int>(error: err));
                Assert.True(!result.IsSuccess && result.Errors.Contains(err));
            }), 50),
            () => ResultGenerators.SystemErrorGen.Select(ResultGenerators.SystemErrorGen).Run((Action<SystemError, SystemError>)((e1, e2) => {
                Result<int> result = (Result<int>)ResultFactory.Lift<int>(add, ResultFactory.Create<int>(error: e1), ResultFactory.Create<int>(error: e2));
                Assert.Equal((false, 2), (result.IsSuccess, result.Errors.Count));
            }), 50),
            () => Gen.Int.Run((Action<int>)(v =>
                Assert.True(((Result<Func<object[], int>>)ResultFactory.Lift<int>((Func<int, int, int, int>)((x, y, z) => x + y + z), [ResultFactory.Create(value: v)])).IsSuccess)), 50));
    }

    /// <summary>Verifies TraverseElements using algebraic collection monadic composition.</summary>
    [Fact]
    public void TraverseElementsCollectionTransformationAccumulatesErrors() => TestGen.RunAll(
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items => {
            Result<IReadOnlyList<int>> result = ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => ResultFactory.Create(value: x * 2));
            Assert.Equal((true, items.Count, items.Select(x => x * 2)), (result.IsSuccess, result.Value.Count, result.Value));
        }), 50),
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items =>
            Assert.Equal(!items.Exists(x => x % 2 != 0),
                ResultFactory.Create<IEnumerable<int>>(value: items).TraverseElements(x => x % 2 == 0
                    ? ResultFactory.Create(value: x) : ResultFactory.Create<int>(error: Errors.E1)).IsSuccess)), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.False(ResultFactory.Create<IEnumerable<int>>(error: err).TraverseElements(x => ResultFactory.Create(value: x * 2)).IsSuccess)), 50),
        () => Assert.Empty(ResultFactory.Create<IEnumerable<int>>(value: []).TraverseElements(x => ResultFactory.Create(value: x * 2)).Value));

    /// <summary>Verifies null argument handling using algebraic exception pattern matching.</summary>
    [Fact]
    public void NullArgumentsThrowCorrectly() {
        (Result<int> success, Result<int> failure) = (ResultFactory.Create(value: 42), ResultFactory.Create<int>(error: Errors.E1));
        TestGen.RunAll(
            () => Assert.Throws<ArgumentNullException>(() => success.Map((Func<int, int>)null!)),
            () => Assert.Throws<ArgumentNullException>(() => success.Bind((Func<int, Result<int>>)null!)),
            () => Assert.Throws<ArgumentNullException>(() => success.Match(null!, _ => 0)),
            () => Assert.Throws<ArgumentNullException>(() => success.Ensure(null!, Errors.E1)),
            () => Assert.Throws<ArgumentNullException>(() => ResultFactory.Lift<int>(null!, 1, 2)),
            () => Assert.Throws<ArgumentNullException>(() => ResultFactory.Create<IEnumerable<int>>(value: [1, 2]).TraverseElements((Func<int, Result<int>>)null!)),
            () => Assert.Throws<InvalidOperationException>(() => failure.Value),
            () => Assert.NotEmpty(failure.Errors));
    }

    /// <summary>Verifies error handling using algebraic transformation and recovery morphisms with explicit overloads.</summary>
    [Fact]
    public void ErrorHandlingTransformationAndRecoveryBehavesCorrectly() => TestGen.RunAll(
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(origErr => {
            Result<int> result = ResultFactory.Create<int>(error: origErr).OnError(_ => [Errors.E2]);
            Assert.True(!result.IsSuccess && result.Errors.Contains(Errors.E2) && !result.Errors.Contains(origErr));
        }), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.Equal(42, ResultFactory.Create<int>(error: err).OnError(_ => 42).Value)), 50),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err =>
            Assert.Equal(99, ResultFactory.Create<int>(error: err).OnError(_ => ResultFactory.Create(value: 99)).Value)), 50),
        () => Assert.Contains(Errors.E1, ResultFactory.Create<int>(error: Errors.E1).Map(x => x * 2).Bind(x => ResultFactory.Create(value: x + 10)).Ensure(x => x > 0, Errors.E2).Errors));

    /// <summary>Verifies Validate batch validations accumulate all errors.</summary>
    [Fact]
    public void ValidateBatchValidationsAccumulatesAllErrors() => TestGen.RunAll(
        () => {
            Result<int> result = ResultFactory.Create(value: 151).Validate(validations: [
                (x => x > 0, Errors.E1),
                (x => x < 100, Errors.E2),
                (x => x % 2 == 0, Errors.E3),
            ]);
            Assert.Equal((false, 2), (result.IsSuccess, result.Errors.Count));
        },
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> result = ResultFactory.Create(value: v).Validate(validations: [
                (x => x > 0, Errors.E1),
                (x => x < 100, Errors.E2),
            ]);
            int expectedErrors = (v <= 0 ? 1 : 0) + (v >= 100 ? 1 : 0);
            Assert.Equal(expectedErrors == 0, result.IsSuccess);
        })));

    /// <summary>Verifies Lift with arity mismatch throws correctly.</summary>
    [Fact]
    public void LiftArityMismatchThrowsArgumentException() => TestGen.RunAll(
        () => Assert.Throws<ArgumentException>(() =>
            ResultFactory.Lift<int>((Func<int, int, int>)((x, y) => x + y), [ResultFactory.Create(value: 1)])),
        () => Assert.Throws<ArgumentException>(() =>
            ResultFactory.Lift<int>((Func<int, int, int>)((x, y) => x + y), [1, 2, 3])));
}
