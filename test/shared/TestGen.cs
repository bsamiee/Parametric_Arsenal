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
        _ = (typeof(T), typeof(T).IsGenericType ? typeof(T).GetGenericTypeDefinition() : null, assertion) switch {
            (_, _, Func<T, bool> prop) => RunAction(() => ExecuteFunc(gen, prop, iter)),
            (_, _, Action<T> act) => RunAction(() => ExecuteAction(gen, act, iter)),
            (Type gt, Type gtd, _) when gtd == typeof(ValueTuple<,>) => RunAction(() => ExecuteTuple2(gen, gt, assertion, iter)),
            (Type gt, Type gtd, _) when gtd == typeof(ValueTuple<,,>) => RunAction(() => ExecuteTuple3(gen, gt, assertion, iter)),
            (Type gt, _, _) => throw new ArgumentException($"Unsupported assertion: {gt}, {assertion.GetType()}", nameof(assertion)),
        };
    }

    private static int RunAction(Action action) { action(); return 0; }

    private static void ExecuteFunc<T>(Gen<T> gen, Func<T, bool> prop, int iter) => gen.Sample(prop, iter: iter);

    private static void ExecuteAction<T>(Gen<T> gen, Action<T> act, int iter) => gen.Sample(v => { act(v); return true; }, iter: iter);

    private static void ExecuteTuple2<T>(Gen<T> gen, Type genType, Delegate assertion, int iter) {
        bool isAction = assertion.GetType() == typeof(Action<,>).MakeGenericType(genType.GetGenericArguments());
        Func<T, bool> runner = isAction
            ? v => { SafeInvoke(assertion, [v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v),]); return true; }
        : v => (bool)SafeInvoke(assertion, [v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v),])!;
        gen.Sample(runner, iter: iter);
    }

    private static void ExecuteTuple3<T>(Gen<T> gen, Type genType, Delegate assertion, int iter) {
        bool isAction = assertion.GetType() == typeof(Action<,,>).MakeGenericType(genType.GetGenericArguments());
        Func<T, bool> runner = isAction
            ? v => { SafeInvoke(assertion, [v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v), v!.GetType().GetField("Item3")!.GetValue(v),]); return true; }
        : v => (bool)SafeInvoke(assertion, [v!.GetType().GetField("Item1")!.GetValue(v), v!.GetType().GetField("Item2")!.GetValue(v), v!.GetType().GetField("Item3")!.GetValue(v),])!;
        gen.Sample(runner, iter: iter);
    }

    private static object? SafeInvoke(Delegate del, object?[] args) {
        try {
            return del.DynamicInvoke(args);
        } catch (System.Reflection.TargetInvocationException tie) {
            throw tie.InnerException ?? tie;
        }
    }

    /// <summary>Executes multiple assertions in parallel using Array.ForEach.</summary>
    public static void RunAll(params Action[] assertions) => Array.ForEach(assertions, static action => action());
}
