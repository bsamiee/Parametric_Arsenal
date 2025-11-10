using System.Collections.Frozen;
using System.Globalization;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Property-based testing with generation, execution, and category theory law verification using FrozenDictionary dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Shared test utilities used across test projects")]
public static class Test {
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

    /// <summary>Generates Result with algebraic state distribution (success/failure × immediate/deferred).</summary>
    public static Gen<Result<T>> ToResult<T>(this Gen<T> valueGen, Gen<SystemError> errorGen, int successWeight = 1, int failureWeight = 1, int deferredWeight = 0) =>
        deferredWeight == 0
            ? Gen.Frequency([
                (successWeight, (IGen<Result<T>>)valueGen.Select(static v => ResultFactory.Create(value: v))),
                (failureWeight, (IGen<Result<T>>)errorGen.Select(static e => ResultFactory.Create<T>(error: e))),
            ])
            : Gen.Frequency([
                (successWeight, (IGen<Result<T>>)valueGen.Select(static v => ResultFactory.Create(value: v))),
                (failureWeight, (IGen<Result<T>>)errorGen.Select(static e => ResultFactory.Create<T>(error: e))),
                (deferredWeight, (IGen<Result<T>>)valueGen.ToResult(errorGen, successWeight, failureWeight).Select(static r => ResultFactory.Create(deferred: () => r))),
            ]);

    /// <summary>Executes property-based test with polymorphic delegate dispatch for Func, Action, and tuple patterns.</summary>
    public static void Run<T>(this Gen<T> gen, Delegate assertion, int iter = 100) {
        (Type type, Type? genericDef) = (typeof(T), typeof(T).IsGenericType ? typeof(T).GetGenericTypeDefinition() : null);
        _ = (type, genericDef, assertion) switch {
            (_, _, Func<T, bool> prop) => RunFunc(gen, prop, iter),
            (_, _, Action<T> act) => RunAction(gen, act, iter),
            (Type gt, Type gtd, _) when gtd == typeof(ValueTuple<,>) => RunTuple2(gen, gt, assertion, iter),
            (Type gt, Type gtd, _) when gtd == typeof(ValueTuple<,,>) => RunTuple3(gen, gt, assertion, iter),
            (Type gt, _, _) => throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Unsupported assertion: {gt}, {assertion.GetType()}"), nameof(assertion)),
        };
    }

    private static int RunFunc<T>(Gen<T> gen, Func<T, bool> prop, int iter) { gen.Sample(prop, iter: iter); return 0; }

    private static int RunAction<T>(Gen<T> gen, Action<T> act, int iter) { gen.Sample(v => { act(v); return true; }, iter: iter); return 0; }

    private static int RunTuple2<T>(Gen<T> gen, Type genType, Delegate assertion, int iter) {
        Type[] typeArgs = genType.GetGenericArguments();
        bool isAction = assertion.GetType() == typeof(Action<,>).MakeGenericType(typeArgs);
        Func<T, bool> runner = isAction
            ? v => { (object? item1, object? item2) = (v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v)); InvokeTuple(assertion, item1, item2); return true; }
            : v => { (object? item1, object? item2) = (v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v)); return (bool)InvokeTuple(assertion, item1, item2)!; };
        gen.Sample(runner, iter: iter);
        return 0;
    }

    private static int RunTuple3<T>(Gen<T> gen, Type genType, Delegate assertion, int iter) {
        Type[] typeArgs = genType.GetGenericArguments();
        bool isAction = assertion.GetType() == typeof(Action<,,>).MakeGenericType(typeArgs);
        Func<T, bool> runner = isAction
            ? v => { (object? item1, object? item2, object? item3) = (v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v), v!.GetType().GetField("Item3")!.GetValue(v)); InvokeTuple(assertion, item1, item2, item3); return true; }
            : v => { (object? item1, object? item2, object? item3) = (v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v), v!.GetType().GetField("Item3")!.GetValue(v)); return (bool)InvokeTuple(assertion, item1, item2, item3)!; };
        gen.Sample(runner, iter: iter);
        return 0;
    }

    private static object? InvokeTuple(Delegate del, params object?[] args) {
        try {
            return del.DynamicInvoke(args);
        } catch (System.Reflection.TargetInvocationException tie) {
            throw tie.InnerException ?? tie;
        }
    }

    /// <summary>Executes multiple assertions sequentially using for loop for optimal performance.</summary>
    public static void RunAll(params Action[] assertions) {
        for (int i = 0; i < assertions.Length; i++) {
            assertions[i]();
        }
    }

    /// <summary>Verifies category theory laws using FrozenDictionary O(1) dispatch with polymorphic arity.</summary>
    public static void Law<T>(string law, params object[] args) where T : notnull {
        int argCount = args.Length;
        _ = (law, argCount) switch {
            ("FunctorIdentity" or "MonadRightIdentity" or "ApplicativeIdentity" or "EqualityReflexive", 1) =>
                InvokeLawUnary(law, (Gen<Result<T>>)args[0], 100),
            ("FunctorIdentity" or "MonadRightIdentity" or "ApplicativeIdentity" or "EqualityReflexive", 2) =>
                InvokeLawUnary(law, (Gen<Result<T>>)args[0], (int)args[1]),
            ("EqualitySymmetric", 2) =>
                InvokeLawBinary(law, (Gen<Result<T>>)args[0], (Gen<Result<T>>)args[1], 100),
            ("EqualitySymmetric", 3) =>
                InvokeLawBinary(law, (Gen<Result<T>>)args[0], (Gen<Result<T>>)args[1], (int)args[2]),
            ("HashConsistent", 2) =>
                InvokeLawHash(law, (Gen<T>)args[0], (Func<T, Result<T>>)args[1], 100),
            ("HashConsistent", 3) =>
                InvokeLawHash(law, (Gen<T>)args[0], (Func<T, Result<T>>)args[1], (int)args[2]),
            _ => throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Unsupported law: {law} with {argCount} args"), nameof(law)),
        };
    }

    private static int InvokeLawUnary<T>(string law, Gen<Result<T>> gen, int iter) where T : notnull {
        ((Action<Gen<Result<object>>, int>)_laws[law])(gen.Select(static r => r.Map(static x => (object)x!)), iter);
        return 0;
    }

    private static int InvokeLawBinary<T>(string law, Gen<Result<T>> gen1, Gen<Result<T>> gen2, int iter) where T : notnull {
        ((Action<Gen<Result<object>>, Gen<Result<object>>, int>)_laws[law])(
            gen1.Select(static r => r.Map(static x => (object)x!)),
            gen2.Select(static r => r.Map(static x => (object)x!)),
            iter);
        return 0;
    }

    private static int InvokeLawHash<T>(string law, Gen<T> gen, Func<T, Result<T>> toResult, int iter) where T : notnull {
        ((Action<Gen<object>, Func<object, Result<object>>, int>)_laws[law])(
            gen.Select(static x => (object)x!),
            v => toResult((T)v).Map(static x => (object)x!),
            iter);
        return 0;
    }

    /// <summary>Verifies functor identity and composition laws.</summary>
    public static void Functor<T, T2, T3>(Gen<Result<T>> gen, Func<T, T2> f, Func<T2, T3> g, int iter = 100) where T : notnull where T2 : notnull where T3 : notnull {
        gen.Sample(static r => r.Map(static x => x).Equals(r), iter: iter);
        gen.Sample(r => r.Map(x => g(f(x))).Equals(r.Map(f).Map(g)), iter: iter);
    }

    /// <summary>Verifies monad left identity, right identity, and associativity laws.</summary>
    public static void Monad<T, T2, T3>(Gen<T> valueGen, Gen<Result<T>> rGen, Gen<Func<T, Result<T2>>> fGen, Gen<Func<T2, Result<T3>>> gGen, int iter = 100) where T : notnull where T2 : notnull where T3 : notnull {
        valueGen.Select(fGen).Sample((v, f) => ResultFactory.Create(value: v).Bind(f).Equals(f(v)), iter: iter);
        rGen.Sample(static r => r.Bind(static x => ResultFactory.Create(value: x)).Equals(r), iter: iter);
        rGen.Select(fGen, gGen).Sample((r, f, g) =>
            r.Bind(f).Bind(g).Equals(r.Bind(x => f(x).Bind(g))), iter: iter / 2);
    }
}
