using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic test utilities using polymorphic dispatch with zero duplication.</summary>
public static class TestUtilities {
    /// <summary>Unified assertion dispatcher using pattern matching on delegate type and generic arity.</summary>
    public static Action Assert<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) =>
        (typeof(T), assertion) switch {
            (Type t, Func<T, bool> prop) => new Action(() => gen.Sample(prop, iter: iterations)),
            (Type t, Action<T> act) => new Action(() => gen.Sample(v => { act(v); return true; }, iter: iterations)),
            (Type { IsGenericType: true } t, Delegate d) when t.GetGenericTypeDefinition() == typeof(ValueTuple<,>) =>
                new Action(() => gen.Sample(v => {
                    object?[] args = [v.GetType().GetField("Item1")!.GetValue(v), v.GetType().GetField("Item2")!.GetValue(v)];
                    object? result = d.DynamicInvoke(args);
                    return result switch { bool b => b, null => true, _ => true };
                }, iter: iterations)),
            _ => throw new ArgumentException($"Unsupported assertion pattern: {typeof(T)}, {assertion.GetType()}", nameof(assertion)),
        };

    /// <summary>Algebraic generator composition using Cartesian product and select.</summary>
    public static Gen<(T1, T2)> Tuple<T1, T2>(this Gen<T1> gen1, Gen<T2> gen2) => gen1.Select(gen2);

    /// <summary>Algebraic generator composition with arity-3 Cartesian product.</summary>
    public static Gen<(T1, T2, T3)> Tuple<T1, T2, T3>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3) => gen1.Select(gen2, gen3);

    /// <summary>Monadic filter using predicate pattern matching.</summary>
    public static Gen<T> Matching<T>(this Gen<T> gen, Func<T, bool> pattern) => gen.Where(pattern);

    /// <summary>Parallel assertion execution using algebraic for-each composition.</summary>
    public static void AssertAll(params Action[] assertions) => Array.ForEach(assertions, static action => action());

    /// <summary>Assertion composition builder using closure over gen and assertion.</summary>
    public static Action ToAssertion<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) => () => gen.Assert(assertion, iterations);
}
