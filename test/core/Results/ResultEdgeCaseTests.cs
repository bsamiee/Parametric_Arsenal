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
    public void ValueTypeDefaultHandlingCreatesSuccessfully() => TestGen.RunAll(
        () => Assert.Equal((true, 0), (ResultFactory.Create(input: 0).IsSuccess, ResultFactory.Create(input: 0).Value)),
        () => Assert.Equal((true, 0.0), (ResultFactory.Create(input: 0.0).IsSuccess, ResultFactory.Create(input: 0.0).Value)),
        () => Assert.Equal((true, false), (ResultFactory.Create(input: false).IsSuccess, ResultFactory.Create(input: false).Value)),
        () => Assert.True(ResultFactory.Create(input: default(int)).IsSuccess),
        () => Assert.True(ResultFactory.Create(input: default(double)).IsSuccess),
        () => Assert.True(ResultFactory.Create(input: default(bool)).IsSuccess));

    /// <summary>Verifies Validate with empty validation array returns identity.</summary>
    [Fact]
    public void ValidateEmptyValidationArrayReturnsIdentity() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> original = ResultFactory.Create(input: v);
            Result<int> validated = original.Validate();
            Assert.Equal((original.IsSuccess, original.Value), (validated.IsSuccess, validated.Value));
        })),
        () => Assert.True(ResultFactory.Create(input: 42).Validate().IsSuccess),
        () => Assert.Equal(42, ResultFactory.Create(input: 42).Validate().Value));

    /// <summary>Verifies Recover parameter precedence using mutually exclusive handlers.</summary>
    [Fact]
    public void RecoverParameterPrecedenceAppliesCorrectly() => TestGen.RunAll(
        () => {
            Result<int> mapped = ResultFactory.Create<int>(input: Errors.E1).Recover((Func<SystemError[], SystemError[]>)(_ => [Errors.E2]));
            Assert.Equal(Errors.E2, mapped.Error);
        },
        () => {
            Result<int> recovered = ResultFactory.Create<int>(input: Errors.E1).Recover((Func<SystemError[], int>)(_ => 99));
            Assert.Equal((true, 99), (recovered.IsSuccess, recovered.Value));
        },
        () => {
            Result<int> recoveredWith = ResultFactory.Create<int>(input: Errors.E1).Recover((Func<SystemError[], Result<int>>)(_ => ResultFactory.Create(input: 77)));
            Assert.Equal((true, 77), (recoveredWith.IsSuccess, recoveredWith.Value));
        });

    /// <summary>Verifies Traverse with single values versus collections using type-based dispatch.</summary>
    [Fact]
    public void TraverseSingleValueVersusCollectionBehavesCorrectly() => TestGen.RunAll(
        () => {
            Result<IReadOnlyList<string>> single = ResultFactory.Create(input: 42).Traverse(x => ResultFactory.Create(input: x.ToString(CultureInfo.InvariantCulture)));
            Assert.Equal((true, 1, "42"), (single.IsSuccess, single.Value.Count, single.Value[0]));
        },
        () => Gen.Int.List[1, 5].Run((Action<List<int>>)(items => {
            Result<IReadOnlyList<string>> traversed = ResultFactory.Create<IEnumerable<int>>(input: items)
                .TraverseElements(x => ResultFactory.Create(input: x.ToString(CultureInfo.InvariantCulture)));
            Assert.Equal((true, items.Count), (traversed.IsSuccess, traversed.Value.Count));
        }), 20),
        () => {
            Result<IReadOnlyList<int>> failed = ResultFactory.Create<IEnumerable<int>>(input: TestArray)
                .TraverseElements(x => x == 2 ? ResultFactory.Create<int>(input: Errors.E1) : ResultFactory.Create(input: x * 10));
            Assert.False(failed.IsSuccess);
        });

    /// <summary>Verifies TraverseElements with empty collections and error propagation.</summary>
    [Fact]
    public void TraverseElementsEmptyAndErrorPropagationHandlesCorrectly() => TestGen.RunAll(
        () => {
            Result<IReadOnlyList<int>> empty = ResultFactory.Create<IEnumerable<int>>(input: []).TraverseElements(x => ResultFactory.Create(input: x * 2));
            Assert.Equal((true, 0), (empty.IsSuccess, empty.Value.Count));
        },
        () => {
            Result<IReadOnlyList<int>> errorSource = ResultFactory.Create<IEnumerable<int>>(input: Errors.E1).TraverseElements(x => ResultFactory.Create(input: x * 2));
            Assert.False(errorSource.IsSuccess);
        },
        () => Gen.Int.List[1, 10].Run((Action<List<int>>)(items => {
            int count = 0;
            int threshold = items.Count / 2;
            Result<IReadOnlyList<int>> partial = ResultFactory.Create<IEnumerable<int>>(input: items)
                .TraverseElements(x => count++ == threshold ? ResultFactory.Create<int>(input: Errors.E1) : ResultFactory.Create(input: x));
            Assert.False(partial.IsSuccess);
        }), 20));

    /// <summary>Verifies Lift partial application actually executes returned function.</summary>
    [Fact]
    public void LiftPartialApplicationExecutesCorrectly() => TestGen.RunAll(
        () => {
            Result<Func<object[], int>> partial = (Result<Func<object[], int>>)ResultFactory.Lift<int>(
                (Func<int, int, int, int>)((x, y, z) => x + y + z),
                [ResultFactory.Create(input: 10)]);
            Assert.True(partial.IsSuccess);
            int result = partial.Value([20, 30]);
            Assert.Equal(60, result);
        },
        () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((a, b) => {
            Result<Func<object[], int>> partial = (Result<Func<object[], int>>)ResultFactory.Lift<int>(
                (Func<int, int, int>)((x, y) => x * y),
                [ResultFactory.Create(input: a)]);
            Assert.Equal(a * b, partial.Value([b]));
        }), 20));

    /// <summary>Verifies Filter with deferred results evaluates lazily then filters.</summary>
    [Fact]
    public void FilterDeferredEvaluationThenFiltersCorrectly() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int count = 0;
            Result<int> deferred = ResultFactory.Create(input: () => { count++; return ResultFactory.Create(input: v); });
            Result<int> filtered = deferred.Filter(x => x > 0, Errors.E1);
            Assert.Equal(0, count);
            bool success = filtered.IsSuccess;
            Assert.Equal(1, count);
            Assert.Equal(v > 0, success);
        }), 20));

    /// <summary>Verifies Reduce with only success handler defaults to seed on failure.</summary>
    [Fact]
    public void ReduceWithoutFailureHandlerDefaultsToSeed() => TestGen.RunAll(
        () => Gen.Int.Select(Gen.Int).Run((Action<int, int>)((seed, val) =>
            Assert.Equal(seed + val, ResultFactory.Create(input: val).Reduce(seed, (s, v) => s + v)))),
        () => Gen.Int.Run((Action<int>)(seed =>
            Assert.Equal(seed, ResultFactory.Create<int>(input: Errors.E1).Reduce(seed, (s, v) => s + v)))),
        () => Assert.Equal(100, ResultFactory.Create<int>(input: Errors.E1).Reduce(100, (s, v) => s + v)));

    /// <summary>Verifies Match executes correct branch with exhaustive pattern coverage.</summary>
    [Fact]
    public void MatchExecutesCorrectBranchExhaustively() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            bool successCalled = false, failureCalled = false;
            int result = ResultFactory.Create(input: v).Match(
                onSuccess: x => { successCalled = true; return x * 2; },
                onFailure: _ => { failureCalled = true; return -1; });
            Assert.Equal((v * 2, true, false), (result, successCalled, failureCalled));
        })),
        () => ResultGenerators.SystemErrorArrayGen.Run((Action<SystemError[]>)(errs => {
            bool successCalled = false, failureCalled = false;
            int result = ResultFactory.Create<int>(input: errs).Match(
                onSuccess: x => { successCalled = true; return x * 2; },
                onFailure: e => { failureCalled = true; return e.Length; });
            Assert.Equal((errs.Length, false, true), (result, successCalled, failureCalled));
        }), 20));

    /// <summary>Verifies Tap side-effect method preserves Result identity.</summary>
    [Fact]
    public void TapMethodPreservesResultIdentity() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> original = ResultFactory.Create(input: v);
            Result<int> tapped = original.Tap(onSuccess: _ => { });
            Assert.True(original.Equals(tapped));
        })),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err => {
            Result<int> original = ResultFactory.Create<int>(input: err);
            Result<int> tapped = original.Tap(onFailure: _ => { });
            Assert.Equal(original.IsSuccess, tapped.IsSuccess);
        }), 20));

    /// <summary>Verifies Validate with premise and conclusion implements logical implication.</summary>
    [Fact]
    public void ValidatePremiseConclusionImplementsImplication() => TestGen.RunAll(
        () => Assert.True(ResultFactory.Create(input: 5).Validate(input: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess),
        () => Assert.True(ResultFactory.Create(input: 50).Validate(input: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess),
        () => Assert.False(ResultFactory.Create(input: 150).Validate(input: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess),
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v is <= 10 or < 100,
                ResultFactory.Create(input: v).Validate(input: Errors.E1, premise: x => x > 10, conclusion: x => x < 100).IsSuccess))));

    /// <summary>Verifies Validate unless parameter inverts predicate logic.</summary>
    [Fact]
    public void ValidateUnlessParameterInvertsPredicateLogic() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v >= 0, ResultFactory.Create(input: v).Validate(predicate: x => x < 0, input: Errors.E1, unless: true).IsSuccess))),
        () => Assert.True(ResultFactory.Create(input: 5).Validate(predicate: x => x < 0, input: Errors.E1, unless: true).IsSuccess),
        () => Assert.False(ResultFactory.Create(input: -5).Validate(predicate: x => x < 0, input: Errors.E1, unless: true).IsSuccess));

    /// <summary>Verifies Validate with monadic validation executes conditional bind.</summary>
    [Fact]
    public void ValidateMonadicValidationExecutesConditionalBind() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> validated = ResultFactory.Create(input: v).Validate(
                predicate: x => x > 10,
                validation: x => ResultFactory.Create(input: x * 2));
            Assert.Equal(v > 10 ? v * 2 : v, validated.Value);
        })),
        () => Assert.Equal(5, ResultFactory.Create(input: 5).Validate(predicate: x => x > 10, validation: x => ResultFactory.Create(input: x * 2)).Value),
        () => Assert.Equal(30, ResultFactory.Create(input: 15).Validate(predicate: x => x > 10, validation: x => ResultFactory.Create(input: x * 2)).Value));

    /// <summary>Verifies Create with conditionals executes inline validation.</summary>
    [Fact]
    public void CreateWithConditionalsExecutesInlineValidation() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v =>
            Assert.Equal(v is > 0 and < 100, ResultFactory.Create(
                input: v,
                conditionals: [(x => x > 0, Errors.E1), (x => x < 100, Errors.E2)]).IsSuccess))),
        () => Assert.True(ResultFactory.Create(input: 50, conditionals: [(x => x > 0, Errors.E1)]).IsSuccess),
        () => Assert.False(ResultFactory.Create(input: -5, conditionals: [(x => x > 0, Errors.E1)]).IsSuccess));

    /// <summary>Verifies Create with nested Result flattens correctly.</summary>
    [Fact]
    public void CreateWithNestedResultFlattensCorrectly() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<Result<int>> nested = ResultFactory.Create(input: ResultFactory.Create(input: v));
            Result<int> flattened = ResultFactory.Create<int>(nested: nested);
            Assert.Equal((true, v), (flattened.IsSuccess, flattened.Value));
        })),
        () => {
            Result<Result<int>> nestedError = ResultFactory.Create<Result<int>>(input: Errors.E1);
            Result<int> flattened = ResultFactory.Create<int>(nested: nestedError);
            Assert.False(flattened.IsSuccess);
        },
        () => {
            Result<Result<int>> innerError = ResultFactory.Create(input: ResultFactory.Create<int>(input: Errors.E1));
            Result<int> flattened = ResultFactory.Create<int>(nested: innerError);
            Assert.False(flattened.IsSuccess);
        });

    /// <summary>Verifies TryGet extracts value correctly with out parameter pattern.</summary>
    [Fact]
    public void TryGetExtractsValueWithOutParameterPattern() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> result = ResultFactory.Create(input: v);
            bool success = result.TryGet(out int extracted);
            Assert.Equal((true, v), (success, extracted));
        })),
        () => ResultGenerators.SystemErrorGen.Run((Action<SystemError>)(err => {
            Result<int> result = ResultFactory.Create<int>(input: err);
            bool success = result.TryGet(out int extracted);
            Assert.Equal((false, default), (success, extracted));
        }), 20));

    /// <summary>Verifies deferred Result with Map/Bind chains evaluates lazily then correctly.</summary>
    [Fact]
    public void DeferredResultWithChainsEvaluatesLazilyThenCorrectly() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            int evalCount = 0, mapCount = 0, bindCount = 0;
            Result<int> deferred = ResultFactory.Create(input: () => { evalCount++; return ResultFactory.Create(input: v); });
            Result<int> chained = deferred.Map(x => { mapCount++; return x * 2; }).Bind(x => { bindCount++; return ResultFactory.Create(input: x + 10); });
            Assert.Equal((0, 0, 0), (evalCount, mapCount, bindCount));
            int final = chained.Value;
            Assert.Equal(((v * 2) + 10, 1, 1, 1), (final, evalCount, mapCount, bindCount));
        }), 20));

    /// <summary>Verifies Recover does not execute handlers on success.</summary>
    [Fact]
    public void RecoverDoesNotExecuteHandlersOnSuccess() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> resultMap = ResultFactory.Create(input: v).Recover((Func<SystemError[], SystemError[]>)(_ => [Errors.E1]));
            Result<int> resultRecover = ResultFactory.Create(input: v).Recover((Func<SystemError[], int>)(_ => 0));
            Result<int> resultRecoverWith = ResultFactory.Create(input: v).Recover((Func<SystemError[], Result<int>>)(_ => ResultFactory.Create(input: 0)));
            Assert.Equal((v, v, v), (resultMap.Value, resultRecover.Value, resultRecoverWith.Value));
        })));

    /// <summary>Verifies Validate with mixed validation array formats handles correctly.</summary>
    [Fact]
    public void ValidateMixedValidationArrayFormatsHandlesCorrectly() => TestGen.RunAll(
        () => Gen.Int.Run((Action<int>)(v => {
            Result<int> result = ResultFactory.Create(input: v).Validate(
                ((Func<int, bool>)(x => x > 0), Errors.E1),
                ((Func<int, bool>)(x => x < 100), Errors.E2));
            Assert.Equal(v is > 0 and < 100, result.IsSuccess);
        })),
        () => {
            (Func<int, bool>, SystemError)[] validations = [
                (x => x > 0, Errors.E1),
                (x => x < 100, Errors.E2),
            ];
            Result<int> result = ResultFactory.Create(input: 50).Validate(validations);
            Assert.True(result.IsSuccess);
        });
}
