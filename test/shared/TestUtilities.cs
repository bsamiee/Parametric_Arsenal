using System.Diagnostics.CodeAnalysis;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;
using Xunit;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic test utilities using polymorphic dispatch with zero duplication.</summary>
public static class TestUtilities {
    /// <summary>Unified assertion dispatcher using pattern matching on delegate type and generic arity.</summary>
    public static void Assert<T>(this Gen<T> gen, Delegate assertion, int iterations = 100) =>
        (typeof(T), assertion) switch {
            (Type t, Func<T, bool> prop) when !t.IsGenericType => new Action(() => gen.Sample(prop, iter: iterations)),
            (Type t, Action<T> act) when !t.IsGenericType => new Action(() => gen.Sample(v => (act(v), true).Item2, iter: iterations)),
            (Type { IsGenericType: true } t, Action<object, object> act) when t.GetGenericTypeDefinition() == typeof(ValueTuple<,>) =>
                new Action(() => gen.Sample(v => ((dynamic d) => (act(d.Item1, d.Item2), true).Item2)(v), iter: iterations)),
            (Type { IsGenericType: true } t, Func<T1, T2, bool>) when t.GetGenericTypeDefinition() == typeof(ValueTuple<,>) =>
                new Action(() => gen.Sample(v => (bool)assertion.DynamicInvoke(((dynamic)v).Item1, ((dynamic)v).Item2)!, iter: iterations)),
            (Type { IsGenericType: true } t, Action<T1, T2> act) when t.GetGenericTypeDefinition() == typeof(ValueTuple<,>) =>
                new Action(() => gen.Sample(v => ((dynamic d) => (act(d.Item1, d.Item2), true).Item2)(v), iter: iterations)),
            _ => throw new ArgumentException($"Unsupported assertion pattern: {typeof(T)}, {assertion.GetType()}", nameof(assertion)),
        }();

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
