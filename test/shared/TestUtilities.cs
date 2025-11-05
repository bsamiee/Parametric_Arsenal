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
        (assertion switch {
            Func<T, bool> property => new Action(() => gen.Sample(property, iter: iterations)),
            Action<T> sample => new Action(() => gen.Sample(v => { sample(v); return true; }, iter: iterations)),
            _ => throw new ArgumentException($"Unsupported assertion type: {assertion.GetType()}", nameof(assertion)),
        })();

    /// <summary>Unified tuple assertion dispatcher with arity-2 algebraic decomposition.</summary>
    public static void Assert<T1, T2>(this Gen<(T1, T2)> gen, Delegate assertion, int iterations = 100) =>
        (assertion switch {
            Func<T1, T2, bool> property => new Action(() => gen.Sample(t => property(t.Item1, t.Item2), iter: iterations)),
            Action<T1, T2> sample => new Action(() => gen.Sample(t => { sample(t.Item1, t.Item2); return true; }, iter: iterations)),
            _ => throw new ArgumentException($"Unsupported assertion type: {assertion.GetType()}", nameof(assertion)),
        })();

    /// <summary>Algebraic generator composition using Cartesian product.</summary>
    public static Gen<(T1, T2)> Tuple<T1, T2>(this Gen<T1> gen1, Gen<T2> gen2) => gen1.Select(gen2);

    /// <summary>Algebraic generator composition with arity-3.</summary>
    public static Gen<(T1, T2, T3)> Tuple<T1, T2, T3>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3) => gen1.Select(gen2, gen3);

    /// <summary>Monadic filter using pattern matching predicate.</summary>
    public static Gen<T> Matching<T>(this Gen<T> gen, Func<T, bool> pattern) => gen.Where(pattern);

    /// <summary>Parallel assertion execution using algebraic composition.</summary>
    public static void AssertAll(params Action[] assertions) => Array.ForEach(assertions, static action => action());

    /// <summary>Assertion composition builder using delegate type dispatch.</summary>
    public static Action ToAssertion<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) =>
        () => gen.Assert(assertion, iterations);
}
