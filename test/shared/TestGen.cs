using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic test generation and execution using polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Shared test utilities used across test projects")]
public static class TestGen {
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
            (_, _, Func<T, bool> prop) => ExecuteFuncInline(gen, prop, iter),
            (_, _, Action<T> act) => ExecuteActionInline(gen, act, iter),
            (Type gt, Type gtd, _) when gtd == typeof(ValueTuple<,>) => ExecuteTuple2Inline(gen, gt, assertion, iter),
            (Type gt, Type gtd, _) when gtd == typeof(ValueTuple<,,>) => ExecuteTuple3Inline(gen, gt, assertion, iter),
            (Type gt, _, _) => throw new ArgumentException($"Unsupported assertion: {gt}, {assertion.GetType()}", nameof(assertion)),
        };
    }

    private static int ExecuteFuncInline<T>(Gen<T> gen, Func<T, bool> prop, int iter) { gen.Sample(prop, iter: iter); return 0; }

    private static int ExecuteActionInline<T>(Gen<T> gen, Action<T> act, int iter) { gen.Sample(v => { act(v); return true; }, iter: iter); return 0; }

    private static int ExecuteTuple2Inline<T>(Gen<T> gen, Type genType, Delegate assertion, int iter) {
        Type[] typeArgs = genType.GetGenericArguments();
        bool isAction = assertion.GetType() == typeof(Action<,>).MakeGenericType(typeArgs);
        Func<T, bool> runner = isAction
            ? v => { (object? item1, object? item2) = (v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v)); InvokeTuple(assertion, item1, item2); return true; }
            : v => { (object? item1, object? item2) = (v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v)); return (bool)InvokeTuple(assertion, item1, item2)!; };
        gen.Sample(runner, iter: iter);
        return 0;
    }

    private static int ExecuteTuple3Inline<T>(Gen<T> gen, Type genType, Delegate assertion, int iter) {
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
}
