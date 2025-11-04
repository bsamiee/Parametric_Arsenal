using System.Diagnostics.CodeAnalysis;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;
using Xunit;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic test utilities using dispatch-based assertion with zero duplication.</summary>
public static class TestUtilities {
    /// <summary>Unified assertion dispatcher using algebraic delegate pattern matching.</summary>
    public static void Assert<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) =>
        assertion switch {
            Func<T, bool> property => gen.Sample(property, iter: iterations),
            Action<T> sample => gen.Sample(v => { sample(v); return true; }, iter: iterations),
            _ => throw new ArgumentException($"Unsupported assertion type: {assertion.GetType()}", nameof(assertion))
        };

    /// <summary>Unified tuple assertion dispatcher with arity-2 algebraic decomposition.</summary>
    public static void Assert<T1, T2>(this Gen<(T1, T2)> gen, Delegate assertion, int iterations = 100) =>
        _ = assertion switch {
            Func<T1, T2, bool> property => gen.Sample(t => property(t.Item1, t.Item2), iter: iterations),
            Action<T1, T2> sample => gen.Sample(t => { sample(t.Item1, t.Item2); return true; }, iter: iterations),
            _ => throw new ArgumentException($"Unsupported assertion type: {assertion.GetType()}", nameof(assertion))
        };

    /// <summary>Algebraic generator composition using Cartesian product.</summary>
    public static Gen<(T1, T2)> Tuple<T1, T2>(this Gen<T1> gen1, Gen<T2> gen2) => Gen.Select(gen1, gen2);

    /// <summary>Algebraic generator composition with arity-3.</summary>
    public static Gen<(T1, T2, T3)> Tuple<T1, T2, T3>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3) => Gen.Select(gen1, gen2, gen3);

    /// <summary>Monadic filter using pattern matching predicate.</summary>
    public static Gen<T> Matching<T>(this Gen<T> gen, Func<T, bool> pattern) => gen.Where(pattern);

    /// <summary>Parallel assertion execution using algebraic composition.</summary>
    public static void AssertAll(params Action[] assertions) => Array.ForEach(assertions, static action => action());

    /// <summary>Assertion composition builder using delegate type dispatch.</summary>
    public static Action ToAssertion<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) =>
        () => gen.Assert(assertion, iterations);

    // Backward compatibility extensions (to be removed once all tests migrated to unified Assert)
    public static void AssertProperty<T>(this Gen<T> gen, Func<T, bool> property, int iter = 100) => gen.Assert(property, iter);
    public static void AssertSample<T>(this Gen<T> gen, Action<T> assertion, int iter = 100) => gen.Assert(assertion, iter);
    public static void AssertProperty<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, bool> property, int iter = 100) => gen.Assert(property, iter);
    public static void AssertSample<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> assertion, int iter = 100) => gen.Assert(assertion, iter);
    public static Action ToAssertion<T>(this Gen<T> gen, Func<T, bool> property, int iter = 100) => gen.ToAssertion((Delegate)property, iter);
    public static Action ToAssertion<T>(this Gen<T> gen, Action<T> assertion, int iter = 100) => gen.ToAssertion((Delegate)assertion, iter);
}

/// <summary>Parameterized law verification using algebraic dispatch and zero duplication.</summary>
public static class LawVerification {
    /// <summary>Verifies functor identity law: fmap id ≡ id.</summary>
    public static void FunctorIdentity<T>(Gen<Result<T>> gen, int iter = 100) =>
        gen.Assert(static (Result<T> r) => r.Map(static x => x).Equals(r), iter);

    /// <summary>Verifies functor composition law: fmap (f ∘ g) ≡ fmap f ∘ fmap g.</summary>
    public static void FunctorComposition<T, T2, T3>(Gen<Result<T>> gen, Func<T, T2> f, Func<T2, T3> g, int iter = 100) =>
        gen.Assert((Result<T> r) => r.Map(x => g(f(x))).Equals(r.Map(f).Map(g)), iter);

    /// <summary>Verifies monad left identity: return a >>= f ≡ f a.</summary>
    public static void MonadLeftIdentity<T, T2>(Gen<T> valueGen, Gen<Func<T, Result<T2>>> funcGen, int iter = 100) =>
        valueGen.Tuple(funcGen).Assert((T v, Func<T, Result<T2>> f) => ResultFactory.Create(value: v).Bind(f).Equals(f(v)), iter);

    /// <summary>Verifies monad right identity: m >>= return ≡ m.</summary>
    public static void MonadRightIdentity<T>(Gen<Result<T>> gen, int iter = 100) =>
        gen.Assert((Result<T> r) => r.Bind(static x => ResultFactory.Create(value: x)).Equals(r), iter);

    /// <summary>Verifies monad associativity: (m >>= f) >>= g ≡ m >>= (λx → f x >>= g).</summary>
    public static void MonadAssociativity<T, T2, T3>(Gen<Result<T>> rGen, Gen<Func<T, Result<T2>>> fGen, Gen<Func<T2, Result<T3>>> gGen, int iter = 50) =>
        rGen.Tuple(fGen, gGen).Assert((Result<T> r, Func<T, Result<T2>> f, Func<T2, Result<T3>> g) =>
            r.Bind(f).Bind(g).Equals(r.Bind(x => f(x).Bind(g))), iter);

    /// <summary>Verifies applicative identity: pure id <*> v ≡ v.</summary>
    public static void ApplicativeIdentity<T>(Gen<Result<T>> gen, int iter = 100) =>
        gen.Assert(static (Result<T> r) => r.Apply(ResultFactory.Create<Func<T, T>>(value: static x => x)).Equals(r), iter);

    /// <summary>Verifies equality reflexivity: ∀x, x ≡ x.</summary>
    public static void EqualityReflexive<T>(Gen<Result<T>> gen, int iter = 100) =>
        gen.Assert(static (Result<T> r) => r.Equals(r), iter);

    /// <summary>Verifies equality symmetry: x ≡ y ⇒ y ≡ x.</summary>
    public static void EqualitySymmetric<T>(Gen<Result<T>> gen1, Gen<Result<T>> gen2, int iter = 100) =>
        gen1.Tuple(gen2).Assert(static (Result<T> r1, Result<T> r2) => r1.Equals(r2) == r2.Equals(r1), iter);

    /// <summary>Verifies hash code consistency: x ≡ y ⇒ hash(x) ≡ hash(y).</summary>
    public static void HashCodeConsistent<T>(Gen<T> gen, Func<T, Result<T>> toResult, int iter = 100) =>
        gen.Assert(v => { var (r1, r2) = (toResult(v), toResult(v)); return r1.Equals(r2) && r1.GetHashCode() == r2.GetHashCode(); }, iter);
}

/// <summary>Algebraic test data builders using dispatch and zero duplication.</summary>
public static class TestData {
    /// <summary>Creates test case from arguments using collection expression.</summary>
    public static object[] Case(params object?[] args) => args;

    /// <summary>Unified FromGen using delegate dispatch for arity polymorphism.</summary>
    public static IEnumerable<object[]> FromGen<T>(Gen<T> gen, Delegate mapper, int count = 10) =>
        mapper switch {
            Func<T, object[]> map => gen.Array[count].Single.Select(map),
            _ => throw new ArgumentException($"Unsupported mapper type: {mapper.GetType()}", nameof(mapper))
        };

    /// <summary>Boolean partition using collection expression.</summary>
    public static IEnumerable<object[]> BooleanPartition => [Case(true), Case(false)];

    /// <summary>Result state partition using collection expression and pattern.</summary>
    public static IEnumerable<object[]> ResultStatePartition<T>(T successValue, SystemError failureError) =>
        [Case(ResultFactory.Create(value: successValue), true), Case(ResultFactory.Create<T>(error: failureError), false)];
}

/// <summary>Algebraic generator combinators using zero-allocation composition.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public static class GenEx {
    /// <summary>Weighted sum type generator using algebraic frequency distribution.</summary>
    public static Gen<T> OneOfWeighted<T>(params (int Weight, Gen<T> Gen)[] weightedGens) => Gen.Frequency(weightedGens);

    /// <summary>Result generator using weighted success/failure distribution.</summary>
    public static Gen<Result<T>> ToResultGen<T>(this Gen<T> valueGen, Gen<SystemError> errorGen, int successWeight = 1, int failureWeight = 1) =>
        OneOfWeighted(
            (successWeight, valueGen.Select(static v => ResultFactory.Create(value: v))),
            (failureWeight, errorGen.Select(static e => ResultFactory.Create<T>(error: e))));

    /// <summary>Deferred Result generator using immediate/deferred distribution.</summary>
    public static Gen<Result<T>> ToResultGenDeferred<T>(this Gen<T> valueGen, Gen<SystemError> errorGen, int deferredWeight = 1, int immediateWeight = 1) =>
        OneOfWeighted(
            (immediateWeight, valueGen.ToResultGen(errorGen)),
            (deferredWeight, valueGen.ToResultGen(errorGen).Select(static r => ResultFactory.Create(deferred: () => r))));
}
