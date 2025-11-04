using System.Diagnostics.CodeAnalysis;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;
using Xunit;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic test utilities with zero-boilerplate CsCheck extensions and pattern-based assertions.</summary>
public static class TestUtilities {
    /// <summary>Verifies property holds for generated values using pattern matching semantics.</summary>
    public static void AssertProperty<T>(this Gen<T> gen, Func<T, bool> property, int iterations = 100) =>
        gen.Sample(property, iter: iterations);

    /// <summary>Verifies property with assertion-based validation instead of boolean return.</summary>
    public static void AssertSample<T>(this Gen<T> gen, Action<T> assertion, int iterations = 100) =>
        gen.Sample(value => { assertion(value); return true; }, iter: iterations);

    /// <summary>Verifies property for tuple generators with algebraic decomposition.</summary>
    public static void AssertProperty<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, bool> property, int iterations = 100) =>
        gen.Sample(t => property(t.Item1, t.Item2), iter: iterations);

    /// <summary>Verifies assertion for tuple generators.</summary>
    public static void AssertSample<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> assertion, int iterations = 100) =>
        gen.Sample(t => { assertion(t.Item1, t.Item2); return true; }, iter: iterations);

    /// <summary>Creates generator tuple from two generators for algebraic composition.</summary>
    public static Gen<(T1, T2)> Tuple<T1, T2>(this Gen<T1> gen1, Gen<T2> gen2) =>
        Gen.Select(gen1, gen2);

    /// <summary>Creates generator tuple from three generators.</summary>
    public static Gen<(T1, T2, T3)> Tuple<T1, T2, T3>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3) =>
        Gen.Select(gen1, gen2, gen3);

    /// <summary>Filters generator using pattern matching predicate.</summary>
    public static Gen<T> Matching<T>(this Gen<T> gen, Func<T, bool> pattern) =>
        gen.Where(pattern);

    /// <summary>Tests Result using pattern matching on success/failure state.</summary>
    public static TResult MatchTest<T, TResult>(
        this Result<T> result,
        Func<T, TResult> onSuccess,
        Func<SystemError[], TResult> onFailure) =>
        result.Match(onSuccess, onFailure);

    /// <summary>Asserts Result matches expected pattern using algebraic decomposition.</summary>
    public static void AssertMatch<T>(
        this Result<T> result,
        (bool IsSuccess, Func<T, bool>? ValuePredicate, Func<SystemError[], bool>? ErrorPredicate) expected) =>
        (result.IsSuccess, expected) switch {
            (true, (true, var valuePred, _)) when valuePred is not null =>
                Assert.True(valuePred(result.Value)),
            (true, (true, null, _)) =>
                Assert.True(result.IsSuccess),
            (false, (false, _, var errorPred)) when errorPred is not null =>
                Assert.True(errorPred([.. result.Errors])),
            (false, (false, _, null)) =>
                Assert.False(result.IsSuccess),
            _ => Assert.Fail($"Expected success={expected.IsSuccess}, got success={result.IsSuccess}")
        };

    /// <summary>Creates test data for Result state verification using pattern matching.</summary>
    public static (bool IsSuccess, Func<T, bool>? ValuePred, Func<SystemError[], bool>? ErrorPred) SuccessMatching<T>(
        Func<T, bool>? valuePredicate = null) =>
        (true, valuePredicate, null);

    /// <summary>Creates test data for failure verification.</summary>
    public static (bool IsSuccess, Func<T, bool>? ValuePred, Func<SystemError[], bool>? ErrorPred) FailureMatching<T>(
        Func<SystemError[], bool>? errorPredicate = null) =>
        (false, null, errorPredicate);

    /// <summary>Runs multiple property tests in parallel using algebraic composition.</summary>
    public static void AssertAll(params Action[] assertions) =>
        Array.ForEach(assertions, action => action());

    /// <summary>Creates assertion from property check for composability.</summary>
    public static Action ToAssertion<T>(this Gen<T> gen, Func<T, bool> property, int iterations = 100) =>
        () => gen.AssertProperty(property, iterations);

    /// <summary>Creates assertion from sample check.</summary>
    public static Action ToAssertion<T>(this Gen<T> gen, Action<T> assertion, int iterations = 100) =>
        () => gen.AssertSample(assertion, iterations);
}

/// <summary>Builder for law verification tests using algebraic composition.</summary>
public static class LawVerification {
    /// <summary>Verifies functor identity law: fmap id = id.</summary>
    public static void FunctorIdentity<T>(Gen<Result<T>> gen, int iterations = 100) =>
        gen.AssertProperty(result => result.Map(x => x).Equals(result), iterations);

    /// <summary>Verifies functor composition law: fmap (f . g) = fmap f . fmap g.</summary>
    public static void FunctorComposition<T, T2, T3>(
        Gen<Result<T>> gen,
        Func<T, T2> f,
        Func<T2, T3> g,
        int iterations = 100) =>
        gen.AssertProperty(result =>
            result.Map(x => g(f(x))).Equals(result.Map(f).Map(g)), iterations);

    /// <summary>Verifies monad left identity: return a >>= f ≡ f a.</summary>
    public static void MonadLeftIdentity<T, T2>(
        Gen<T> valueGen,
        Gen<Func<T, Result<T2>>> funcGen,
        int iterations = 100) =>
        valueGen.Tuple(funcGen).AssertProperty((value, f) =>
            ResultFactory.Create(value: value).Bind(f).Equals(f(value)), iterations);

    /// <summary>Verifies monad right identity: m >>= return ≡ m.</summary>
    public static void MonadRightIdentity<T>(Gen<Result<T>> gen, int iterations = 100) =>
        gen.AssertProperty(result =>
            result.Bind(x => ResultFactory.Create(value: x)).Equals(result), iterations);

    /// <summary>Verifies monad associativity: (m >>= f) >>= g ≡ m >>= (\x -> f x >>= g).</summary>
    public static void MonadAssociativity<T, T2, T3>(
        Gen<Result<T>> resultGen,
        Gen<Func<T, Result<T2>>> fGen,
        Gen<Func<T2, Result<T3>>> gGen,
        int iterations = 50) =>
        resultGen.Tuple(fGen, gGen).AssertProperty((result, f, g) =>
            result.Bind(f).Bind(g).Equals(result.Bind(x => f(x).Bind(g))), iterations);

    /// <summary>Verifies applicative identity: pure id <*> v ≡ v.</summary>
    public static void ApplicativeIdentity<T>(Gen<Result<T>> gen, int iterations = 100) =>
        gen.AssertProperty(result =>
            result.Apply(ResultFactory.Create<Func<T, T>>(value: x => x)).Equals(result), iterations);

    /// <summary>Verifies equality reflexivity: x = x.</summary>
    public static void EqualityReflexive<T>(Gen<Result<T>> gen, int iterations = 100) =>
        gen.AssertProperty(result => result.Equals(result), iterations);

    /// <summary>Verifies equality symmetry: x = y ⇒ y = x.</summary>
    public static void EqualitySymmetric<T>(Gen<Result<T>> gen1, Gen<Result<T>> gen2, int iterations = 100) =>
        gen1.Tuple(gen2).AssertProperty((r1, r2) => r1.Equals(r2) == r2.Equals(r1), iterations);

    /// <summary>Verifies hash code consistency: x = y ⇒ hash(x) = hash(y).</summary>
    public static void HashCodeConsistent<T>(Gen<T> gen, Func<T, Result<T>> toResult, int iterations = 100) =>
        gen.AssertProperty(value => {
            var r1 = toResult(value);
            var r2 = toResult(value);
            return r1.Equals(r2) && r1.GetHashCode() == r2.GetHashCode();
        }, iterations);
}

/// <summary>Parameterized test data builders using algebraic composition.</summary>
public static class TestData {
    /// <summary>Creates test case tuple for Theory tests.</summary>
    public static object[] Case(params object?[] args) => args;

    /// <summary>Creates multiple test cases using generator and mapper.</summary>
    public static IEnumerable<object[]> FromGen<T>(Gen<T> gen, Func<T, object[]> mapper, int count = 10) =>
        gen.Array[count].Single.Select(mapper);

    /// <summary>Creates test cases from tuple generator.</summary>
    public static IEnumerable<object[]> FromGen<T1, T2>(
        Gen<(T1, T2)> gen,
        Func<T1, T2, object[]> mapper,
        int count = 10) =>
        gen.Array[count].Single.Select(t => mapper(t.Item1, t.Item2));

    /// <summary>Creates boolean partition test cases: (true, false).</summary>
    public static IEnumerable<object[]> BooleanPartition =>
        [Case(true), Case(false)];

    /// <summary>Creates Result state partition: success/failure cases.</summary>
    public static IEnumerable<object[]> ResultStatePartition<T>(T successValue, SystemError failureError) =>
        [
            Case(ResultFactory.Create(value: successValue), true),
            Case(ResultFactory.Create<T>(error: failureError), false)
        ];
}

/// <summary>CsCheck generator combinators for algebraic composition.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public static class GenEx {
    /// <summary>Creates partition generator: true/false split.</summary>
    public static Gen<(T Success, T Failure)> Partition<T>(Gen<T> successGen, Gen<T> failureGen) =>
        Gen.Select(successGen, failureGen);

    /// <summary>Creates generator with algebraic sum type semantics.</summary>
    public static Gen<T> OneOfWeighted<T>(params (int Weight, Gen<T> Gen)[] weightedGens) =>
        Gen.Frequency(weightedGens);

    /// <summary>Creates Result generator from value generator with success/failure distribution.</summary>
    public static Gen<Result<T>> ToResultGen<T>(this Gen<T> valueGen, Gen<SystemError> errorGen, int successWeight = 1, int failureWeight = 1) =>
        OneOfWeighted(
            (successWeight, valueGen.Select(v => ResultFactory.Create(value: v))),
            (failureWeight, errorGen.Select(e => ResultFactory.Create<T>(error: e))));

    /// <summary>Creates deferred Result generator.</summary>
    public static Gen<Result<T>> ToResultGenDeferred<T>(this Gen<T> valueGen, Gen<SystemError> errorGen, int deferredWeight = 1, int immediateWeight = 1) =>
        OneOfWeighted(
            (immediateWeight, valueGen.ToResultGen(errorGen)),
            (deferredWeight, valueGen.ToResultGen(errorGen).Select(r =>
                ResultFactory.Create(deferred: () => r))));
}
