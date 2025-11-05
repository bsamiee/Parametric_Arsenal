using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Backward-compatible test utilities delegating to TestGen infrastructure.</summary>
public static class TestUtilities {
    /// <summary>Unified assertion dispatcher delegating to TestGen.Run.</summary>
    public static Action Assert<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) =>
        new(() => gen.Run(assertion, iterations));

    /// <summary>Algebraic generator composition using Cartesian product and select.</summary>
    public static Gen<(T1, T2)> Tuple<T1, T2>(this Gen<T1> gen1, Gen<T2> gen2) => gen1.Select(gen2);

    /// <summary>Algebraic generator composition with arity-3 Cartesian product.</summary>
    public static Gen<(T1, T2, T3)> Tuple<T1, T2, T3>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3) => gen1.Select(gen2, gen3);

    /// <summary>Monadic filter using predicate pattern matching.</summary>
    public static Gen<T> Matching<T>(this Gen<T> gen, Func<T, bool> pattern) => gen.Where(pattern);

    /// <summary>Parallel assertion execution delegating to TestGen.RunAll.</summary>
    public static void AssertAll(params Action[] assertions) => TestGen.RunAll(assertions);

    /// <summary>Assertion composition builder using closure over gen and assertion.</summary>
    public static Action ToAssertion<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) => () => gen.Run(assertion, iterations);
}
