using System.Collections.Frozen;
using System.Globalization;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Category theory law verification using FrozenDictionary dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Shared test utilities used across test projects")]
public static class TestLaw {
    private static readonly FrozenDictionary<string, Delegate> _laws = new Dictionary<string, Delegate>(StringComparer.Ordinal) {
        ["FunctorIdentity"] = new Action<Gen<Result<object>>, int>(static (gen, iter) => gen.Sample(static r => r.Map(static x => x).Equals(r), iter: iter)),
        ["MonadRightIdentity"] = new Action<Gen<Result<object>>, int>(static (gen, iter) => gen.Sample(static r => r.Bind(static x => ResultFactory.Create(value: x)).Equals(r), iter: iter)),
        ["ApplicativeIdentity"] = new Action<Gen<Result<object>>, int>(static (gen, iter) => gen.Sample(static r => r.Apply(ResultFactory.Create<Func<object, object>>(value: static x => x)).Equals(r), iter: iter)),
        ["EqualityReflexive"] = new Action<Gen<Result<object>>, int>(static (gen, iter) => gen.Sample(static r => r.Equals(r), iter: iter)),
        ["EqualitySymmetric"] = new Action<Gen<Result<object>>, Gen<Result<object>>, int>(static (gen1, gen2, iter) =>
            gen1.Select(gen2).Sample(static (r1, r2) => r1.Equals(r2) == r2.Equals(r1), iter: iter)),
        ["HashConsistent"] = new Action<Gen<object>, Func<object, Result<object>>, int>(static (gen, toResult, iter) =>
            gen.Sample(v => { (Result<object> r1, Result<object> r2) = (toResult(v), toResult(v)); return r1.Equals(r2) && r1.GetHashCode() == r2.GetHashCode(); }, iter: iter)),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Verifies category theory laws using FrozenDictionary O(1) dispatch with polymorphic arity.</summary>
    public static void Verify<T>(string law, params object[] args) where T : notnull {
        int argCount = args.Length;
        _ = (law, argCount) switch {
            ("FunctorIdentity" or "MonadRightIdentity" or "ApplicativeIdentity" or "EqualityReflexive", 1) =>
                InvokeUnaryLaw(law, (Gen<Result<T>>)args[0], 100),
            ("FunctorIdentity" or "MonadRightIdentity" or "ApplicativeIdentity" or "EqualityReflexive", 2) =>
                InvokeUnaryLaw(law, (Gen<Result<T>>)args[0], (int)args[1]),
            ("EqualitySymmetric", 2) =>
                InvokeBinaryLaw(law, (Gen<Result<T>>)args[0], (Gen<Result<T>>)args[1], 100),
            ("EqualitySymmetric", 3) =>
                InvokeBinaryLaw(law, (Gen<Result<T>>)args[0], (Gen<Result<T>>)args[1], (int)args[2]),
            ("HashConsistent", 2) =>
                InvokeHashLaw(law, (Gen<T>)args[0], (Func<T, Result<T>>)args[1], 100),
            ("HashConsistent", 3) =>
                InvokeHashLaw(law, (Gen<T>)args[0], (Func<T, Result<T>>)args[1], (int)args[2]),
            _ => throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Unsupported law: {law} with {argCount} args"), nameof(law)),
        };
    }

    private static int InvokeUnaryLaw<T>(string law, Gen<Result<T>> gen, int iter) where T : notnull {
        ((Action<Gen<Result<object>>, int>)_laws[law])(gen.Select(static r => r.Map(static x => (object)x!)), iter);
        return 0;
    }

    private static int InvokeBinaryLaw<T>(string law, Gen<Result<T>> gen1, Gen<Result<T>> gen2, int iter) where T : notnull {
        ((Action<Gen<Result<object>>, Gen<Result<object>>, int>)_laws[law])(
            gen1.Select(static r => r.Map(static x => (object)x!)),
            gen2.Select(static r => r.Map(static x => (object)x!)),
            iter);
        return 0;
    }

    private static int InvokeHashLaw<T>(string law, Gen<T> gen, Func<T, Result<T>> toResult, int iter) where T : notnull {
        ((Action<Gen<object>, Func<object, Result<object>>, int>)_laws[law])(
            gen.Select(static x => (object)x!),
            v => toResult((T)v).Map(static x => (object)x!),
            iter);
        return 0;
    }

    /// <summary>Verifies functor identity and composition laws.</summary>
    public static void VerifyFunctor<T, T2, T3>(Gen<Result<T>> gen, Func<T, T2> f, Func<T2, T3> g, int iter = 100) where T : notnull where T2 : notnull where T3 : notnull {
        gen.Sample(static r => r.Map(static x => x).Equals(r), iter: iter);
        gen.Sample(r => r.Map(x => g(f(x))).Equals(r.Map(f).Map(g)), iter: iter);
    }

    /// <summary>Verifies monad left identity, right identity, and associativity laws.</summary>
    public static void VerifyMonad<T, T2, T3>(Gen<T> valueGen, Gen<Result<T>> rGen, Gen<Func<T, Result<T2>>> fGen, Gen<Func<T2, Result<T3>>> gGen, int iter = 100) where T : notnull where T2 : notnull where T3 : notnull {
        valueGen.Select(fGen).Sample((v, f) => ResultFactory.Create(value: v).Bind(f).Equals(f(v)), iter: iter);
        rGen.Sample(static r => r.Bind(static x => ResultFactory.Create(value: x)).Equals(r), iter: iter);
        rGen.Select(fGen, gGen).Sample((r, f, g) =>
            r.Bind(f).Bind(g).Equals(r.Bind(x => f(x).Bind(g))), iter: iter / 2);
    }
}
