using System.Globalization;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Comprehensive edge case tests for Result operations covering untested branches and boundary conditions.</summary>
public sealed class ResultEdgeCaseTests {
    private static readonly (SystemError E1, SystemError E2, SystemError E3) Errors = (
        new(ErrorDomain.Results, 7001, "Edge1"),
        new(ErrorDomain.Results, 7002, "Edge2"),
        new(ErrorDomain.Results, 7003, "Edge3"));

    private static readonly int[] TestArray = [1, 2, 3];

    /// <summary>Verifies value type default handling using zero-value semantics.</summary>
    [Fact]
    public void ValueTypeDefaultHandlingCreatesSuccessfully() => Test.RunAll(
        () => Assert.Equal((true, 0), (ResultFactory.Create(value: 0).IsSuccess, ResultFactory.Create(value: 0).Value)),
        () => Assert.Equal((true, 0.0), (ResultFactory.Create(value: 0.0).IsSuccess, ResultFactory.Create(value: 0.0).Value)),
        () => Assert.Equal((true, false), (ResultFactory.Create(value: false).IsSuccess, ResultFactory.Create(value: false).Value)),
        () => Assert.True(ResultFactory.Create(value: default(int)).IsSuccess),
        () => Assert.True(ResultFactory.Create(value: default(double)).IsSuccess),
        () => Assert.True(ResultFactory.Create(value: default(bool)).IsSuccess));

    /// <summary>Verifies Ensure with empty validation array returns identity.</summary>
    [Fact]
    public void EnsureEmptyValidationArrayReturnsIdentity() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> original = ResultFactory.Create(value: v);
            Result<int> ensured = original.Ensure([]);
            Assert.Equal((original.IsSuccess, original.Value), (ensured.IsSuccess, ensured.Value));
        })),
        () => Assert.True(ResultFactory.Create(value: 42).Ensure([]).IsSuccess),
        () => Assert.Equal(42, ResultFactory.Create(value: 42).Ensure([]).Value));

    /// <summary>Verifies OnError overloads apply correct transformations with explicit type dispatch.</summary>
    [Fact]
    public void OnErrorOverloadsApplyCorrectTransformations() => Test.RunAll(
        () => {
            Result<int> mapped = ResultFactory.Create<int>(error: Errors.E1).OnError(_ => [Errors.E2]);
            Assert.Equal(Errors.E2, mapped.Error);
        },
        () => {
            Result<int> recovered = ResultFactory.Create<int>(error: Errors.E1).OnError(_ => 99);
            Assert.Equal((true, 99), (recovered.IsSuccess, recovered.Value));
        },
        () => {
            Result<int> recoveredWith = ResultFactory.Create<int>(error: Errors.E1).OnError(_ => ResultFactory.Create(value: 77));
            Assert.Equal((true, 77), (recoveredWith.IsSuccess, recoveredWith.Value));
        },
        () => {
            Result<int> mapThenRecover = ResultFactory.Create<int>(error: Errors.E1).OnError(_ => [Errors.E2]).OnError(_ => 50);
            Assert.Equal((true, 50), (mapThenRecover.IsSuccess, mapThenRecover.Value));
        });

    /// <summary>Verifies Traverse with single values versus collections using type-based dispatch.</summary>
    [Fact]
    public void TraverseSingleValueVersusCollectionBehavesCorrectly() => Test.RunAll(
        () => {
            Result<IReadOnlyList<string>> single = ResultFactory.Create(value: 42).Traverse(x => ResultFactory.Create(value: x.ToString(CultureInfo.InvariantCulture)));
            Assert.Equal((true, 1, "42"), (single.IsSuccess, single.Value.Count, single.Value[0]));
        },
        () => Gen.Int.List[1, 5].Run((Action<List<int>>)(items => {
            Result<IReadOnlyList<string>> traversed = ResultFactory.Create<IEnumerable<int>>(value: items)
                .TraverseElements(x => ResultFactory.Create(value: x.ToString(CultureInfo.InvariantCulture)));
            Assert.Equal((true, items.Count), (traversed.IsSuccess, traversed.Value.Count));
        }), 20),
        () => {
            Result<IReadOnlyList<int>> failed = ResultFactory.Create<IEnumerable<int>>(value: TestArray)
                .TraverseElements(x => x == 2 ? ResultFactory.Create<int>(error: Errors.E1) : ResultFactory.Create(value: x * 10));
            Assert.False(failed.IsSuccess);
        });

    /// <summary>Verifies TraverseElements with empty collections and error propagation.</summary>
    [Fact]
    public void TraverseElementsEmptyAndErrorPropagationHandlesCorrectly() => Test.RunAll(
        () => {
            Result<IReadOnlyList<int>> empty = ResultFactory.Create<IEnumerable<int>>(value: []).TraverseElements(x => ResultFactory.Create(value: x * 2));
            Assert.Equal((true, 0), (empty.IsSuccess, empty.Value.Count));
        },
        () => {
            Result<IReadOnlyList<int>> errorSource = ResultFactory.Create<IEnumerable<int>>(error: Errors.E1).TraverseElements(x => ResultFactory.Create(value: x * 2));
            Assert.False(errorSource.IsSuccess);
        },
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items => {
            int count = 0;
            int threshold = items.Count / 2;
            Result<IReadOnlyList<int>> partial = ResultFactory.Create<IEnumerable<int>>(value: items)
                .TraverseElements(x => count++ == threshold ? ResultFactory.Create<int>(error: Errors.E1) : ResultFactory.Create(value: x));
            Assert.False(partial.IsSuccess);
        }), 20));

    /// <summary>Verifies Lift partial application actually executes returned function.</summary>
    [Fact]
    public void LiftPartialApplicationExecutesCorrectly() => Test.RunAll(
        () => {
            Result<Func<object[], int>> partial = (Result<Func<object[], int>>)ResultFactory.Lift<int>(
                (Func<int, int, int, int>)((x, y, z) => unchecked(x + y + z)),
                [ResultFactory.Create(value: 10)]);
            Assert.True(partial.IsSuccess);
            int result = partial.Value([20, 30]);
            Assert.Equal(60, result);
        },
        () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((a, b) => {
            Result<Func<object[], int>> partial = (Result<Func<object[], int>>)ResultFactory.Lift<int>(
                (Func<int, int, int, int>)((x, y, z) => unchecked(x * y * z)),
                [ResultFactory.Create(value: a), b]);
            Assert.Equal(unchecked(a * b * 5), partial.Value([5]));
        }), 20));

    /// <summary>Verifies Ensure with deferred results evaluates lazily then filters.</summary>
    [Fact]
    public void EnsureDeferredEvaluationThenFiltersCorrectly() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int count = 0;
            Result<int> deferred = ResultFactory.Create(deferred: () => { count++; return ResultFactory.Create(value: v); });
            Result<int> filtered = deferred.Ensure(x => x > 0, Errors.E1);
            Assert.Equal(0, count);
            bool success = filtered.IsSuccess;
            Assert.Equal(1, count);
            Assert.Equal(v > 0, success);
        }), 20));

    /// <summary>Verifies Match for reduction semantics with success handler defaults to seed on failure.</summary>
    [Fact]
    public void MatchReductionWithoutFailureHandlerDefaultsToSeed() => Test.RunAll(
        () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((seed, val) =>
            Assert.Equal(unchecked(seed + val), ResultFactory.Create(value: val).Match(v => unchecked(seed + v), _ => seed)))),
        () => Gen.Int.Run((Action<int>)(seed =>
            Assert.Equal(seed, ResultFactory.Create<int>(error: Errors.E1).Match(v => unchecked(seed + v), _ => seed)))),
        () => Assert.Equal(100, ResultFactory.Create<int>(error: Errors.E1).Match(v => 100 + v, _ => 100)));

    /// <summary>Verifies Match executes correct branch with exhaustive pattern coverage.</summary>
    [Fact]
    public void MatchExecutesCorrectBranchExhaustively() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            bool successCalled = false, failureCalled = false;
            int result = ResultFactory.Create(value: v).Match(
                onSuccess: x => { successCalled = true; return unchecked(x * 2); },
                onFailure: _ => { failureCalled = true; return -1; });
            Assert.Equal((unchecked(v * 2), true, false), (result, successCalled, failureCalled));
        })),
        () => ResultGenerators.SystemErrorArrayGen.Run((Action<SystemError[]>)(errs => {
            bool successCalled = false, failureCalled = false;
            int result = ResultFactory.Create<int>(errors: errs).Match(
                onSuccess: x => { successCalled = true; return unchecked(x * 2); },
                onFailure: e => { failureCalled = true; return e.Length; });
            Assert.Equal((errs.Length, false, true), (result, successCalled, failureCalled));
        }), 20));

    /// <summary>Verifies Tap side-effect method preserves Result identity.</summary>
    [Fact]
    public void TapMethodPreservesResultIdentity() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> original = ResultFactory.Create(value: v);
            Result<int> tapped = original.Tap(onSuccess: _ => { });
            Assert.True(original.Equals(tapped));
        })),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err => {
            Result<int> original = ResultFactory.Create<int>(error: err);
            Result<int> tapped = original.Tap(onFailure: _ => { });
            Assert.Equal(original.IsSuccess, tapped.IsSuccess);
        }), 20));

    /// <summary>Verifies Validate with premise and conclusion implements logical implication.</summary>
    [Fact]
    public void ValidatePremiseConclusionImplementsImplication() => Test.RunAll(
        () => Assert.True(ResultFactory.Create(value: 5).Validate(error: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess),
        () => Assert.True(ResultFactory.Create(value: 50).Validate(error: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess),
        () => Assert.False(ResultFactory.Create(value: 150).Validate(error: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v is <= 10 or < 100,
                ResultFactory.Create(value: v).Validate(error: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess))));

    /// <summary>Verifies Validate unless parameter inverts predicate logic.</summary>
    [Fact]
    public void ValidateUnlessParameterInvertsPredicateLogic() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v >= 0, ResultFactory.Create(value: v).Validate(predicate: x => x < 0, error: Errors.E1, unless: true).IsSuccess))),
        () => Assert.True(ResultFactory.Create(value: 5).Validate(predicate: x => x < 0, error: Errors.E1, unless: true).IsSuccess),
        () => Assert.False(ResultFactory.Create(value: -5).Validate(predicate: x => x < 0, error: Errors.E1, unless: true).IsSuccess));

    /// <summary>Verifies Validate with monadic validation executes conditional bind.</summary>
    [Fact]
    public void ValidateMonadicValidationExecutesConditionalBind() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> validated = ResultFactory.Create(value: v).Validate(
                predicate: x => x > 10,
                validation: x => ResultFactory.Create(value: unchecked(x * 2)));
            Assert.Equal(v > 10 ? unchecked(v * 2) : v, validated.Value);
        })),
        () => Assert.Equal(5, ResultFactory.Create(value: 5).Validate(predicate: x => x > 10, validation: x => ResultFactory.Create(value: x * 2)).Value),
        () => Assert.Equal(30, ResultFactory.Create(value: 15).Validate(predicate: x => x > 10, validation: x => ResultFactory.Create(value: x * 2)).Value));

    /// <summary>Verifies Create with conditionals executes inline validation.</summary>
    [Fact]
    public void CreateWithConditionalsExecutesInlineValidation() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v is > 0 and < 100, ResultFactory.Create(
                value: v,
                conditionals: [(x => x > 0, Errors.E1), (x => x < 100, Errors.E2)]).IsSuccess))),
        () => Assert.True(ResultFactory.Create(value: 50, conditionals: [(x => x > 0, Errors.E1)]).IsSuccess),
        () => Assert.False(ResultFactory.Create(value: -5, conditionals: [(x => x > 0, Errors.E1)]).IsSuccess));

    /// <summary>Verifies Create with nested Result flattens correctly.</summary>
    [Fact]
    public void CreateWithNestedResultFlattensCorrectly() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<Result<int>> nested = ResultFactory.Create(value: ResultFactory.Create(value: v));
            Result<int> flattened = ResultFactory.Create<int>(nested: nested);
            Assert.Equal((true, v), (flattened.IsSuccess, flattened.Value));
        })),
        () => {
            Result<Result<int>> nestedError = ResultFactory.Create<Result<int>>(error: Errors.E1);
            Result<int> flattened = ResultFactory.Create<int>(nested: nestedError);
            Assert.False(flattened.IsSuccess);
        },
        () => {
            Result<Result<int>> innerError = ResultFactory.Create(value: ResultFactory.Create<int>(error: Errors.E1));
            Result<int> flattened = ResultFactory.Create<int>(nested: innerError);
            Assert.False(flattened.IsSuccess);
        });

    /// <summary>Verifies Match extracts value correctly with pattern matching semantics.</summary>
    [Fact]
    public void MatchExtractsValueWithPatternMatchingSemantics() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> result = ResultFactory.Create(value: v);
            (bool success, int extracted) = result.Match(val => (true, val), _ => (false, default));
            Assert.Equal((true, v), (success, extracted));
        })),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err => {
            Result<int> result = ResultFactory.Create<int>(error: err);
            (bool success, int extracted) = result.Match(val => (true, val), _ => (false, default(int)));
            Assert.Equal((false, default), (success, extracted));
        }), 20));

    /// <summary>Verifies deferred Result with Map/Bind chains evaluates lazily then correctly.</summary>
    [Fact]
    public void DeferredResultWithChainsEvaluatesLazilyThenCorrectly() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int evalCount = 0, mapCount = 0, bindCount = 0;
            Result<int> deferred = ResultFactory.Create(deferred: () => { evalCount++; return ResultFactory.Create(value: v); });
            Result<int> chained = deferred.Map(x => { mapCount++; return unchecked(x * 2); }).Bind(x => { bindCount++; return ResultFactory.Create(value: unchecked(x + 10)); });
            Assert.Equal((0, 0, 0), (evalCount, mapCount, bindCount));
            int final = chained.Value;
            Assert.Equal(unchecked((v * 2) + 10), final);
            Assert.True(evalCount >= 1 && mapCount >= 1 && bindCount >= 1);
        }), 20));

    /// <summary>Verifies OnError does not execute handlers on success with all overloads.</summary>
    [Fact]
    public void OnErrorDoesNotExecuteHandlersOnSuccess() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            bool mapCalled = false;
            Result<int> result = ResultFactory.Create(value: v).OnError(_ => { mapCalled = true; return [Errors.E1]; });
            Assert.Equal((v, false), (result.Value, mapCalled));
        })),
        () => Gen.Int.Run((Action<int>)(v => {
            bool recoverCalled = false;
            Result<int> result = ResultFactory.Create(value: v).OnError(_ => { recoverCalled = true; return 0; });
            Assert.Equal((v, false), (result.Value, recoverCalled));
        })),
        () => Gen.Int.Run((Action<int>)(v => {
            bool recoverWithCalled = false;
            Result<int> result = ResultFactory.Create(value: v).OnError(_ => { recoverWithCalled = true; return ResultFactory.Create(value: 0); });
            Assert.Equal((v, false), (result.Value, recoverWithCalled));
        })));

    /// <summary>Verifies Ensure with mixed validation array formats handles correctly.</summary>
    [Fact]
    public void EnsureMixedValidationArrayFormatsHandlesCorrectly() => Test.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> result = ResultFactory.Create(value: v).Ensure(
                (x => x > 0, Errors.E1),
                (x => x < 100, Errors.E2));
            Assert.Equal(v is > 0 and < 100, result.IsSuccess);
        })),
        () => {
            (Func<int, bool>, SystemError)[] validations = [
                (x => x > 0, Errors.E1),
                (x => x < 100, Errors.E2),
            ];
            Result<int> result = ResultFactory.Create(value: 50).Ensure(validations);
            Assert.True(result.IsSuccess);
        });
}
