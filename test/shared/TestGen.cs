using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic test generation and execution using polymorphic dispatch.</summary>
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
        switch (typeof(T), assertion) {
            case (Type, Func<T, bool> prop):
                gen.Sample(prop, iter: iter);
                break;
            case (Type, Action<T> act):
                gen.Sample(v => { act(v); return true; }, iter: iter);
                break;
            case (Type { IsGenericType: true } t, Delegate d) when t.GetGenericTypeDefinition() == typeof(ValueTuple<,>):
                gen.Sample(v => {
                    object[] args = [v!.GetType().GetField("Item1")!.GetValue(v)!, v!.GetType().GetField("Item2")!.GetValue(v)!,];
                    return d.DynamicInvoke(args) switch { bool b => b, _ => true, };
                }, iter: iter);
                break;
            case (Type { IsGenericType: true } t, Delegate d) when t.GetGenericTypeDefinition() == typeof(ValueTuple<,,>):
                gen.Sample(v => {
                    object[] args = [v!.GetType().GetField("Item1")!.GetValue(v)!, v!.GetType().GetField("Item2")!.GetValue(v)!, v!.GetType().GetField("Item3")!.GetValue(v)!,];
                    return d.DynamicInvoke(args) switch { bool b => b, _ => true, };
                }, iter: iter);
                break;
            default:
                throw new ArgumentException($"Unsupported assertion: {typeof(T)}, {assertion.GetType()}", nameof(assertion));
        }
    }

    /// <summary>Executes multiple assertions in parallel using Array.ForEach.</summary>
    public static void RunAll(params Action[] assertions) => Array.ForEach(assertions, static action => action());
}
