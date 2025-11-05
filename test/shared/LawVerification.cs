using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

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
        gen.Assert((T v) => { (Result<T> r1, Result<T> r2) = (toResult(v), toResult(v)); return r1.Equals(r2) && r1.GetHashCode() == r2.GetHashCode(); }, iter);
}
