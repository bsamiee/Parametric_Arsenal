using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.Results;

/// <summary>Tests monadic laws, functor operations, applicative behavior, traversals, and comprehensive Result operations.</summary>
public sealed class ResultMonadTests {
    private static readonly SystemError TestError = new(ErrorDomain.Results, 1001, "Test error");

    /// <summary>Tests functor identity law: map(id) = id.</summary>
    [Fact]
    public void FunctorIdentityLaw() =>
        ResultGenerators.ResultGen<int>().Sample(result =>
            result.Map(x => x).Equals(result), iter: 100);

    /// <summary>Tests functor composition law: map(f ∘ g) = map(f) ∘ map(g).</summary>
    [Fact]
    public void FunctorCompositionLaw() =>
        ResultGenerators.ResultGen<int>().Sample(result => {
            Func<int, string> f = x => x.ToString();
            Func<string, int> g = s => s.Length;
            var left = result.Map(x => g(f(x)));
            var right = result.Map(f).Map(g);
            return left.Equals(right);
        }, iter: 100);

    /// <summary>Tests monad left identity law: return(a) >>= f = f(a).</summary>
    [Fact]
    public void MonadLeftIdentityLaw() =>
        Gen.Int.Sample(value => {
            ResultGenerators.MonadicFunctionGen<int, string>().Sample(f =>
                ResultFactory.Create(value: value).Bind(f).Equals(f(value)), iter: 50);
        }, iter: 100);

    /// <summary>Tests monad right identity law: m >>= return = m.</summary>
    [Fact]
    public void MonadRightIdentityLaw() =>
        ResultGenerators.ResultGen<int>().Sample(result =>
            result.Bind(x => ResultFactory.Create(value: x)).Equals(result), iter: 100);

    /// <summary>Tests monad associativity law: (m >>= f) >>= g = m >>= (x -> f(x) >>= g).</summary>
    [Fact]
    public void MonadAssociativityLaw() =>
        ResultGenerators.ResultGen<int>().Sample(result => {
            ResultGenerators.MonadicFunctionGen<int, string>().Sample(f => {
                ResultGenerators.MonadicFunctionGen<string, double>().Sample(g => {
                    var left = result.Bind(f).Bind(g);
                    var right = result.Bind(x => f(x).Bind(g));
                    return left.Equals(right);
                }, iter: 20);
            }, iter: 20);
        }, iter: 50);

    /// <summary>Tests TryGet with success/failure cases and null handling.</summary>
    [Fact]
    public void TryGet_ExtractionBehavior_HandlesSuccessAndFailure() {
        // Success case
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value);
            Assert.True(result.TryGet(out var extracted));
            Assert.Equal(value, extracted);
        }, iter: 100);

        // Failure case
        ResultGenerators.FailureGen<int>().Sample(result => {
            Assert.False(result.TryGet(out var extracted));
            Assert.Equal(default, extracted);
        }, iter: 100);

        // Deferred success
        Gen.Int.Sample(value => {
            var deferred = ResultFactory.Create(deferred: () => ResultFactory.Create(value: value));
            Assert.True(deferred.TryGet(out var extracted));
            Assert.Equal(value, extracted);
        }, iter: 50);

        // Deferred failure
        ResultGenerators.SystemErrorGen.Sample(error => {
            var deferred = ResultFactory.Create(deferred: () => ResultFactory.Create<int>(error: error));
            Assert.False(deferred.TryGet(out var extracted));
            Assert.Equal(default, extracted);
        }, iter: 50);
    }

    /// <summary>Tests Reduce for success/failure cases with seed accumulation.</summary>
    [Fact]
    public void Reduce_AccumulationBehavior_AppliesCorrectHandler() {
        // Success with onSuccess handler
        Gen.Select(Gen.Int, Gen.Int).Sample(t => {
            var (value, seed) = t;
            var result = ResultFactory.Create(value: value);
            var accumulated = result.Reduce(seed, (s, v) => s + v);
            Assert.Equal(seed + value, accumulated);
        }, iter: 100);

        // Failure with onFailure handler
        Gen.Select(ResultGenerators.SystemErrorArrayGen, Gen.Int).Sample(t => {
            var (errors, seed) = t;
            var result = ResultFactory.Create<int>(errors: errors);
            var accumulated = result.Reduce(seed,
                onSuccess: (s, v) => s + v,
                onFailure: (s, errs) => s + errs.Length);
            Assert.Equal(seed + errors.Length, accumulated);
        }, iter: 100);

        // Failure without onFailure handler returns seed
        Gen.Int.Sample(seed => {
            var result = ResultFactory.Create<int>(error: TestError);
            var accumulated = result.Reduce(seed, (s, v) => s + v);
            Assert.Equal(seed, accumulated);
        }, iter: 50);
    }

    /// <summary>Tests Filter with predicate satisfaction and error handling.</summary>
    [Fact]
    public void Filter_PredicateValidation_FiltersCorrectly() {
        // Predicate passes
        Gen.Int[1, 100].Sample(value => {
            var result = ResultFactory.Create(value: value).Filter(x => x > 0, TestError);
            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
        }, iter: 100);

        // Predicate fails
        Gen.Int[1, 100].Sample(value => {
            var result = ResultFactory.Create(value: value).Filter(x => x < 0, TestError);
            Assert.False(result.IsSuccess);
            Assert.Contains(TestError, result.Errors);
        }, iter: 100);

        // Failure propagates without filtering
        ResultGenerators.FailureGen<int>().Sample(result => {
            var filtered = result.Filter(x => x > 0, TestError);
            Assert.False(filtered.IsSuccess);
        }, iter: 50);

        // Deferred evaluation
        Gen.Int.Sample(value => {
            var deferred = ResultFactory.Create(deferred: () => ResultFactory.Create(value: value));
            var filtered = deferred.Filter(x => x > 0, TestError);
            Assert.Equal(value > 0, filtered.IsSuccess);
        }, iter: 50);
    }

    /// <summary>Tests Ensure with single and multiple validations, error accumulation.</summary>
    [Fact]
    public void Ensure_MultipleValidations_AccumulatesErrors() {
        var error1 = new SystemError(ErrorDomain.Results, 1001, "Error 1");
        var error2 = new SystemError(ErrorDomain.Results, 1002, "Error 2");
        var error3 = new SystemError(ErrorDomain.Results, 1003, "Error 3");

        // Single validation passes
        Gen.Int[1, 100].Sample(value => {
            var result = ResultFactory.Create(value: value).Ensure((Func<int, bool>)(x => x > 0), error1);
            Assert.True(result.IsSuccess);
        }, iter: 50);

        // Single validation fails
        Gen.Int[1, 100].Sample(value => {
            var result = ResultFactory.Create(value: value).Ensure((Func<int, bool>)(x => x < 0), error1);
            Assert.False(result.IsSuccess);
            Assert.Contains(error1, result.Errors);
        }, iter: 50);

        // Multiple validations - all pass
        Gen.Int[1, 50].Sample(value => {
            var result = ResultFactory.Create(value: value).Ensure(
                ((Func<int, bool>)(x => x > 0), error1),
                ((Func<int, bool>)(x => x < 100), error2),
                ((Func<int, bool>)(x => x % 2 == 0), error3));

            if (value is > 0 and < 100 && value % 2 == 0)
                Assert.True(result.IsSuccess);
        }, iter: 100);

        // Multiple validations - accumulates errors
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value).Ensure(
                ((Func<int, bool>)(x => x > 0), error1),
                ((Func<int, bool>)(x => x < 100), error2));

            var expectedErrorCount = 0;
            if (value <= 0) expectedErrorCount++;
            if (value >= 100) expectedErrorCount++;

            Assert.Equal(expectedErrorCount == 0, result.IsSuccess);
            if (!result.IsSuccess)
                Assert.Equal(expectedErrorCount, result.Errors.Count);
        }, iter: 100);

        // Deferred execution
        Gen.Int.Sample(value => {
            var deferred = ResultFactory.Create(deferred: () => ResultFactory.Create(value: value));
            var validated = deferred.Ensure((Func<int, bool>)(x => x > 0), error1);
            Assert.Equal(value > 0, validated.IsSuccess);
        }, iter: 50);
    }

    /// <summary>Tests Match for exhaustive pattern matching with success/failure handlers.</summary>
    [Fact]
    public void Match_PatternMatching_InvokesCorrectHandler() {
        // Success case
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value);
            var matched = result.Match(
                onSuccess: v => v * 2,
                onFailure: _ => -1);
            Assert.Equal(value * 2, matched);
        }, iter: 100);

        // Failure case
        ResultGenerators.SystemErrorArrayGen.Sample(errors => {
            var result = ResultFactory.Create<int>(errors: errors);
            var matched = result.Match(
                onSuccess: v => v * 2,
                onFailure: errs => errs.Length);
            Assert.Equal(errors.Length, matched);
        }, iter: 100);

        // Deferred evaluation
        Gen.Int.Sample(value => {
            var deferred = ResultFactory.Create(deferred: () => ResultFactory.Create(value: value));
            var matched = deferred.Match(
                onSuccess: v => v.ToString(),
                onFailure: _ => "error");
            Assert.Equal(value.ToString(), matched);
        }, iter: 50);
    }

    /// <summary>Tests Apply (applicative functor) for parallel validation and error accumulation.</summary>
    [Fact]
    public void Apply_ApplicativeFunctor_AccumulatesErrorsInParallel() {
        // Both successful
        Gen.Select(Gen.Int, Gen.Int).Sample(t => {
            var (a, b) = t;
            var resultValue = ResultFactory.Create(value: a);
            var resultFunc = ResultFactory.Create<Func<int, int>>(value: x => x + b);
            var applied = resultValue.Apply(resultFunc);

            Assert.True(applied.IsSuccess);
            Assert.Equal(a + b, applied.Value);
        }, iter: 100);

        // Value failed, function succeeded
        Gen.Select(ResultGenerators.SystemErrorGen, Gen.Int).Sample(t => {
            var (error, b) = t;
            var resultValue = ResultFactory.Create<int>(error: error);
            var resultFunc = ResultFactory.Create<Func<int, int>>(value: x => x + b);
            var applied = resultValue.Apply(resultFunc);

            Assert.False(applied.IsSuccess);
            Assert.Contains(error, applied.Errors);
        }, iter: 50);

        // Value succeeded, function failed
        Gen.Select(Gen.Int, ResultGenerators.SystemErrorGen).Sample(t => {
            var (a, error) = t;
            var resultValue = ResultFactory.Create(value: a);
            var resultFunc = ResultFactory.Create<Func<int, int>>(error: error);
            var applied = resultValue.Apply(resultFunc);

            Assert.False(applied.IsSuccess);
            Assert.Contains(error, applied.Errors);
        }, iter: 50);

        // Both failed - accumulates errors
        Gen.Select(ResultGenerators.SystemErrorGen, ResultGenerators.SystemErrorGen).Sample(t => {
            var (error1, error2) = t;
            var resultValue = ResultFactory.Create<int>(error: error1);
            var resultFunc = ResultFactory.Create<Func<int, int>>(error: error2);
            var applied = resultValue.Apply(resultFunc);

            Assert.False(applied.IsSuccess);
            Assert.Equal(2, applied.Errors.Count);
            Assert.Contains(error1, applied.Errors);
            Assert.Contains(error2, applied.Errors);
        }, iter: 50);
    }

    /// <summary>Tests Traverse for transforming collections with monadic operations and error accumulation.</summary>
    [Fact]
    public void Traverse_CollectionTransformation_TransformsAndAccumulates() {
        // Non-collection single value
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value);
            var traversed = result.Traverse(x => ResultFactory.Create(value: x.ToString()));

            Assert.True(traversed.IsSuccess);
            Assert.Single(traversed.Value);
            Assert.Equal(value.ToString(), traversed.Value[0]);
        }, iter: 100);

        // Collection - all successful
        Gen.Int.List[1, 10].Sample(items => {
            var result = ResultFactory.Create<IEnumerable<int>>(value: items);
            var traversed = result.Traverse(x => ResultFactory.Create(value: x * 2));

            Assert.True(traversed.IsSuccess);
            Assert.Equal(items.Count, traversed.Value.Count);
        }, iter: 50);

        // Collection - some failures
        Gen.Int.List[1, 10].Sample(items => {
            var result = ResultFactory.Create<IEnumerable<int>>(value: items);
            var traversed = result.Traverse(x => x % 2 == 0
                ? ResultFactory.Create(value: x)
                : ResultFactory.Create<int>(error: TestError));

            bool anyOdd = items.Any(x => x % 2 != 0);
            Assert.Equal(!anyOdd, traversed.IsSuccess);
        }, iter: 50);

        // Failure propagates
        ResultGenerators.SystemErrorGen.Sample(error => {
            var result = ResultFactory.Create<IEnumerable<int>>(error: error);
            var traversed = result.Traverse(x => ResultFactory.Create(value: x * 2));

            Assert.False(traversed.IsSuccess);
            Assert.Contains(error, traversed.Errors);
        }, iter: 50);
    }

    /// <summary>Tests deferred execution, lazy evaluation, side-effect timing, and resource handling.</summary>
    [Fact]
    public void DeferredExecution_LazyEvaluation_BehavesCorrectly() {
        // Basic deferred success
        Gen.Int.Sample(value => {
            int executionCount = 0;
            var deferred = ResultFactory.Create(deferred: () => {
                executionCount++;
                return ResultFactory.Create(value: value);
            });

            Assert.True(deferred.IsDeferred);
            Assert.Equal(0, executionCount);

            _ = deferred.Value;
            Assert.Equal(1, executionCount);

            _ = deferred.Value;
            _ = deferred.IsSuccess;
            Assert.Equal(1, executionCount); // Only evaluates once
        }, iter: 50);

        // Deferred failure
        ResultGenerators.SystemErrorGen.Sample(error => {
            bool executed = false;
            var deferred = ResultFactory.Create(deferred: () => {
                executed = true;
                return ResultFactory.Create<int>(error: error);
            });

            Assert.False(executed);
            Assert.False(deferred.IsSuccess);
            Assert.True(executed);
        }, iter: 50);

        // Chained deferred operations
        Gen.Int.Sample(value => {
            int mapCount = 0, bindCount = 0;
            var deferred = ResultFactory.Create(deferred: () => ResultFactory.Create(value: value))
                .Map(x => { mapCount++; return x * 2; })
                .Bind(x => { bindCount++; return ResultFactory.Create(value: x + 10); });

            Assert.Equal(0, mapCount);
            Assert.Equal(0, bindCount);

            var final = deferred.Value;
            Assert.Equal(value * 2 + 10, final);
            Assert.Equal(1, mapCount);
            Assert.Equal(1, bindCount);
        }, iter: 50);

        // Resource disposal error handling
        using (var stream = new MemoryStream()) {
            var resourceResult = ResultFactory.Create(deferred: () => {
                stream.WriteByte(1);
                return ResultFactory.Create(value: 1);
            });
            stream.Dispose();
            Assert.Throws<ObjectDisposedException>(() => resourceResult.Value);
        }
    }

    /// <summary>Tests Apply method for side effects without state modification.</summary>
    [Fact]
    public void ApplyMethod_SideEffects_PreservesState() {
        // Success side effects
        Gen.Int.Sample(value => {
            int captured = 0;
            var result = ResultFactory.Create(value: value)
                .Apply(onSuccess: v => captured = v);

            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
            Assert.Equal(value, captured);
        }, iter: 100);

        // Failure side effects
        ResultGenerators.SystemErrorArrayGen.Sample(errors => {
            SystemError[]? captured = null;
            var result = ResultFactory.Create<int>(errors: errors)
                .Apply(onFailure: errs => captured = [.. errs]);

            Assert.False(result.IsSuccess);
            Assert.NotNull(captured);
            Assert.Equal(errors.Length, captured.Length);
        }, iter: 50);

        // Both handlers
        Gen.Bool.Sample(isSuccess => {
            bool successCalled = false, failureCalled = false;
            var result = isSuccess
                ? ResultFactory.Create(value: 42)
                : ResultFactory.Create<int>(error: TestError);

            result.Apply(
                onSuccess: _ => successCalled = true,
                onFailure: _ => failureCalled = true);

            Assert.Equal(isSuccess, successCalled);
            Assert.Equal(!isSuccess, failureCalled);
        }, iter: 100);
    }

    /// <summary>Tests OnError for transformation, recovery, and monadic error handling.</summary>
    [Fact]
    public void OnError_ErrorHandling_TransformsAndRecovers() {
        var error1 = new SystemError(ErrorDomain.Results, 1001, "Error 1");
        var error2 = new SystemError(ErrorDomain.Results, 1002, "Error 2");

        // Error mapping
        var mapped = ResultFactory.Create<int>(error: error1)
            .OnError(mapError: _ => [error2]);
        Assert.False(mapped.IsSuccess);
        Assert.Contains(error2, mapped.Errors);
        Assert.DoesNotContain(error1, mapped.Errors);

        // Error recovery
        var recovered = ResultFactory.Create<int>(error: error1)
            .OnError(recover: _ => 99);
        Assert.True(recovered.IsSuccess);
        Assert.Equal(99, recovered.Value);

        // Monadic recovery
        var monadicRecovered = ResultFactory.Create<int>(error: error1)
            .OnError(recoverWith: _ => ResultFactory.Create(value: 77));
        Assert.True(monadicRecovered.IsSuccess);
        Assert.Equal(77, monadicRecovered.Value);

        // Success preserves through OnError
        Gen.Int.Sample(value => {
            var result = ResultFactory.Create(value: value)
                .OnError(mapError: _ => [error2]);
            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
        }, iter: 50);
    }

    /// <summary>Tests Result equality, hash code consistency, and reflexive/symmetric/transitive properties.</summary>
    [Fact]
    public void Equality_StructuralComparison_ObeysLaws() {
        // Reflexive
        ResultGenerators.ResultGen<int>().Sample(result =>
            result.Equals(result), iter: 100);

        // Symmetric
        Gen.Select(Gen.Int, Gen.Int).Sample(t => {
            var (a, b) = t;
            var r1 = ResultFactory.Create(value: a);
            var r2 = ResultFactory.Create(value: b);
            return r1.Equals(r2) == r2.Equals(r1);
        }, iter: 100);

        // Hash code consistency
        Gen.Int.Sample(value => {
            var r1 = ResultFactory.Create(value: value);
            var r2 = ResultFactory.Create(value: value);
            return r1.Equals(r2) && r1.GetHashCode() == r2.GetHashCode();
        }, iter: 100);

        // Operator overloads
        Gen.Int.Sample(value => {
            var r1 = ResultFactory.Create(value: value);
            var r2 = ResultFactory.Create(value: value);
            Assert.True(r1 == r2);
            Assert.False(r1 != r2);
        }, iter: 100);
    }
}
