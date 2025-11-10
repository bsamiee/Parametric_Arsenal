using System.Collections.Frozen;
using System.Globalization;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Unified property-based testing with generation, execution, assertions, and category theory law verification using FrozenDictionary dispatch.</summary>
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

    private static readonly FrozenDictionary<string, Func<object, object, bool>> _comparisons = new Dictionary<string, Func<object, object, bool>>(StringComparer.Ordinal) {
        ["Equal"] = static (a, b) => Equals(a, b),
        ["NotEqual"] = static (a, b) => !Equals(a, b),
        ["LessThan"] = static (a, b) => ((IComparable)a).CompareTo(b) < 0,
        ["LessThanOrEqual"] = static (a, b) => ((IComparable)a).CompareTo(b) <= 0,
        ["GreaterThan"] = static (a, b) => ((IComparable)a).CompareTo(b) > 0,
        ["GreaterThanOrEqual"] = static (a, b) => ((IComparable)a).CompareTo(b) >= 0,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, Func<IComparable, IComparable, bool>> _orderings = new Dictionary<string, Func<IComparable, IComparable, bool>>(StringComparer.Ordinal) {
        ["Increasing"] = static (a, b) => a.CompareTo(b) < 0,
        ["Decreasing"] = static (a, b) => a.CompareTo(b) > 0,
        ["NonDecreasing"] = static (a, b) => a.CompareTo(b) <= 0,
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

    /// <summary>Verifies universal quantification (∀x: P(x)) using property-based generation.</summary>
    public static void ForAll<T>(Gen<T> gen, Func<T, bool> predicate, int iter = 100, string? message = null) =>
        gen.Sample(value => {
            _ = predicate(value) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"ForAll failed: predicate false for {value}"));
            return true;
        }, iter: iter);

    /// <summary>Verifies existential quantification (∃x: P(x)) by finding witness within iteration budget.</summary>
    public static void Exists<T>(Gen<T> gen, Func<T, bool> predicate, int maxAttempts = 1000, string? message = null) {
        (int attempts, bool found) = (0, false);
        gen.Sample(value => {
            (attempts, found) = (attempts + 1, found || predicate(value));
            _ = !(attempts >= maxAttempts && !found) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Exists failed: no witness in {maxAttempts} attempts"));
            return true;
        }, iter: maxAttempts);
    }

    /// <summary>Verifies implication (P(x) ⇒ Q(x)) for all generated values satisfying premise.</summary>
    public static void Implies<T>(Gen<T> gen, Func<T, bool> premise, Func<T, bool> conclusion, int iter = 100, string? message = null) =>
        gen.Sample(value => {
            _ = !premise(value) || conclusion(value) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Implication failed: premise holds but conclusion false for {value}"));
            return true;
        }, iter: iter);

    /// <summary>Verifies equivalence (P(x) ⇔ Q(x)) for all generated values.</summary>
    public static void Equivalent<T>(Gen<T> gen, Func<T, bool> left, Func<T, bool> right, int iter = 100, string? message = null) =>
        gen.Sample(value => {
            _ = left(value) == right(value) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Equivalence failed: left={left(value)}, right={right(value)} for {value}"));
            return true;
        }, iter: iter);

    /// <summary>Verifies predicate eventually holds within iteration budget (temporal: ◇P).</summary>
    public static void Eventually<T>(Gen<T> gen, Func<T, bool> predicate, int maxAttempts = 100, string? message = null) {
        (bool satisfied, int attempt) = (false, 0);
        gen.Sample(value => {
            (satisfied, attempt) = (satisfied || predicate(value), attempt + 1);
            _ = attempt < maxAttempts || satisfied ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Eventually failed: predicate never satisfied in {maxAttempts} attempts"));
            return true;
        }, iter: maxAttempts);
    }

    /// <summary>Verifies predicate holds continuously for all values (temporal: □P).</summary>
    public static void Always<T>(Gen<T> gen, Func<T, bool> predicate, int iter = 100, string? message = null) =>
        ForAll(gen, predicate, iter, message ?? "Always failed: predicate violated");

    /// <summary>Verifies comparison using FrozenDictionary dispatch for O(1) lookup.</summary>
    public static void Compare<T>(T left, T right, string comparison, string? message = null) where T : IComparable =>
        _ = _comparisons.TryGetValue(comparison, out Func<object, object, bool>? op) && op(left!, right!)
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Comparison failed: {left} {comparison} {right}"));

    /// <summary>Verifies numeric comparison with tolerance for floating-point values.</summary>
    public static void EqualWithin(double left, double right, double tolerance, string? message = null) =>
        _ = Math.Abs(left - right) <= tolerance
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"EqualWithin failed: |{left} - {right}| = {Math.Abs(left - right)} > {tolerance}"));

    /// <summary>Verifies Result is success and value satisfies predicate.</summary>
    public static void Success<T>(Result<T> result, Func<T, bool>? predicate = null, string? message = null) =>
        _ = result.Match(
            onSuccess: value => {
                _ = predicate is null || predicate(value) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Success predicate failed for value {value}"));
                return true;
            },
            onFailure: errors => throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Expected success but got {errors.Length} error(s): {errors[0].Message}")));

    /// <summary>Verifies Result is failure with optional error predicate.</summary>
    public static void Failure<T>(Result<T> result, Func<SystemError[], bool>? predicate = null, string? message = null) =>
        _ = result.Match(
            onSuccess: value => throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Expected failure but got success with value {value}")),
            onFailure: errors => {
                _ = predicate is null || predicate(errors) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Failure predicate violated for errors: {string.Join(", ", errors.Select(e => e.Message))}"));
                return true;
            });

    /// <summary>Verifies collection contains exactly the expected count of elements satisfying predicate.</summary>
    public static void Count<T>(IEnumerable<T> collection, Func<T, bool> predicate, int expectedCount, string? message = null) {
        int actualCount = 0;
        foreach (T item in collection) {
            actualCount = predicate(item) ? actualCount + 1 : actualCount;
        }
        _ = actualCount == expectedCount ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Count failed: expected {expectedCount} but found {actualCount}"));
    }

    /// <summary>Verifies all elements satisfy predicate.</summary>
    public static void All<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null) {
        foreach (T item in collection) {
            _ = predicate(item) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"All failed: predicate violated for {item}"));
        }
    }

    /// <summary>Verifies at least one element satisfies predicate.</summary>
    public static void Any<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null) {
        bool found = false;
        foreach (T item in collection) {
            found = found || predicate(item);
        }
        _ = found ? true : throw new InvalidOperationException(message ?? "Any failed: no element satisfied predicate");
    }

    /// <summary>Verifies no element satisfies predicate.</summary>
    public static void None<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null) {
        foreach (T item in collection) {
            _ = !predicate(item) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"None failed: predicate satisfied for {item}"));
        }
    }

    /// <summary>Verifies ordering relation holds for consecutive elements.</summary>
    public static void Ordered<T>(IEnumerable<T> collection, Func<T, T, bool> relation, string? message = null) {
        T? previous = default;
        bool first = true;
        foreach (T current in collection) {
            _ = first || relation(previous!, current) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Ordered failed: relation violated between {previous} and {current}"));
            (previous, first) = (current, false);
        }
    }

    /// <summary>Verifies collection ordering using FrozenDictionary dispatch for O(1) lookup (Increasing, Decreasing, NonDecreasing).</summary>
    public static void Ordering<T>(IEnumerable<T> collection, string ordering, string? message = null) where T : IComparable {
        _ = _orderings.TryGetValue(ordering, out Func<IComparable, IComparable, bool>? relation) ? true : throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Unknown ordering: {ordering}"), nameof(ordering));
        T? previous = default;
        bool first = true;
        foreach (T current in collection) {
            _ = first || relation(previous!, current) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"{ordering} failed: relation violated between {previous} and {current}"));
            (previous, first) = (current, false);
        }
    }

    /// <summary>Verifies collection is strictly increasing using FrozenDictionary dispatch.</summary>
    public static void Increasing<T>(IEnumerable<T> collection, string? message = null) where T : IComparable =>
        Ordering(collection, "Increasing", message);

    /// <summary>Verifies collection is strictly decreasing using FrozenDictionary dispatch.</summary>
    public static void Decreasing<T>(IEnumerable<T> collection, string? message = null) where T : IComparable =>
        Ordering(collection, "Decreasing", message);

    /// <summary>Verifies collection is non-decreasing using FrozenDictionary dispatch.</summary>
    public static void NonDecreasing<T>(IEnumerable<T> collection, string? message = null) where T : IComparable =>
        Ordering(collection, "NonDecreasing", message);

    /// <summary>Verifies action throws exception of expected type with optional predicate.</summary>
    public static void Throws<TException>(Action action, Func<TException, bool>? predicate = null, string? message = null) where TException : Exception {
        try {
            action();
            throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Expected {typeof(TException).Name} but no exception was thrown"));
        } catch (TException ex) {
            _ = predicate is null || predicate(ex) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Exception predicate failed for {ex.GetType().Name}: {ex.Message}"));
        }
    }

    /// <summary>Combines multiple assertions with short-circuit evaluation on first failure.</summary>
    public static void Combine(params Func<bool>[] assertions) {
        for (int i = 0; i < assertions.Length; i++) {
            _ = assertions[i]() ? true : throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Combined assertion {i} failed"));
        }
    }

    /// <summary>Verifies exactly one assertion succeeds (exclusive OR).</summary>
    public static void ExactlyOne(params Func<bool>[] assertions) {
        int successCount = 0;
        for (int i = 0; i < assertions.Length; i++) {
            successCount = assertions[i]() ? successCount + 1 : successCount;
        }
        _ = successCount == 1 ? true : throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"ExactlyOne failed: {successCount} assertions succeeded"));
    }
}
