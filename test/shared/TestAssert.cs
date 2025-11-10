using System.Collections.Frozen;
using System.Globalization;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Property-based assertion combinators with quantifiers, temporal logic, and error context capture.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Shared test utilities used across test projects")]
public static class TestAssert {
    private static readonly FrozenDictionary<string, Func<object, object, bool>> _comparisons = new Dictionary<string, Func<object, object, bool>>(StringComparer.Ordinal) {
        ["Equal"] = static (a, b) => Equals(a, b),
        ["NotEqual"] = static (a, b) => !Equals(a, b),
        ["LessThan"] = static (a, b) => ((IComparable)a).CompareTo(b) < 0,
        ["LessThanOrEqual"] = static (a, b) => ((IComparable)a).CompareTo(b) <= 0,
        ["GreaterThan"] = static (a, b) => ((IComparable)a).CompareTo(b) > 0,
        ["GreaterThanOrEqual"] = static (a, b) => ((IComparable)a).CompareTo(b) >= 0,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Verifies universal quantification (∀x: P(x)) using property-based generation.</summary>
    public static void ForAll<T>(Gen<T> gen, Func<T, bool> predicate, int iter = 100, string? message = null) =>
        gen.Sample(value => predicate(value)
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"ForAll failed: predicate returned false for value {value}")), iter: iter);

    /// <summary>Verifies existential quantification (∃x: P(x)) by finding witness within iteration budget.</summary>
    public static void Exists<T>(Gen<T> gen, Func<T, bool> predicate, int maxAttempts = 1000, string? message = null) {
        int attempts = 0;
        bool found = false;
        gen.Sample(value => {
            attempts++;
            found = found || predicate(value);
            return attempts >= maxAttempts && !found
                ? throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Exists failed: no witness found in {maxAttempts} attempts"))
                : true;
        }, iter: maxAttempts);
    }

    /// <summary>Verifies implication (P(x) ⇒ Q(x)) for all generated values satisfying premise.</summary>
    public static void Implies<T>(Gen<T> gen, Func<T, bool> premise, Func<T, bool> conclusion, int iter = 100, string? message = null) =>
        gen.Sample(value => !premise(value) || conclusion(value)
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Implication failed: premise holds but conclusion false for {value}")), iter: iter);

    /// <summary>Verifies equivalence (P(x) ⇔ Q(x)) for all generated values.</summary>
    public static void Equivalent<T>(Gen<T> gen, Func<T, bool> left, Func<T, bool> right, int iter = 100, string? message = null) =>
        gen.Sample(value => left(value) == right(value)
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Equivalence failed: left={left(value)}, right={right(value)} for {value}")), iter: iter);

    /// <summary>Verifies predicate eventually holds within iteration budget (temporal: ◇P).</summary>
    public static void Eventually<T>(Gen<T> gen, Func<T, bool> predicate, int maxAttempts = 100, string? message = null) {
        bool satisfied = false;
        int attempt = 0;
        gen.Sample(value => {
            attempt++;
            satisfied = satisfied || predicate(value);
            return attempt >= maxAttempts && !satisfied
                ? throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Eventually failed: predicate never satisfied in {maxAttempts} attempts"))
                : true;
        }, iter: maxAttempts);
    }

    /// <summary>Verifies predicate holds continuously for all values (temporal: □P).</summary>
    public static void Always<T>(Gen<T> gen, Func<T, bool> predicate, int iter = 100, string? message = null) =>
        ForAll(gen, predicate, iter, message ?? "Always failed: predicate violated");

    /// <summary>Verifies comparison using FrozenDictionary dispatch for O(1) lookup.</summary>
    public static void Compare<T>(T left, T right, string comparison, string? message = null) where T : IComparable {
        _ = _comparisons.TryGetValue(comparison, out Func<object, object, bool>? op) && op(left!, right!)
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Comparison failed: {left} {comparison} {right}"));
    }

    /// <summary>Verifies numeric comparison with tolerance for floating-point values.</summary>
    public static void EqualWithin(double left, double right, double tolerance, string? message = null) {
        _ = Math.Abs(left - right) <= tolerance
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"EqualWithin failed: |{left} - {right}| = {Math.Abs(left - right)} > {tolerance}"));
    }

    /// <summary>Verifies Result is success and value satisfies predicate.</summary>
    public static void Success<T>(Result<T> result, Func<T, bool>? predicate = null, string? message = null) =>
        result.Match(
            onSuccess: value => predicate is null || predicate(value)
                ? true
                : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Success predicate failed for value {value}")),
            onFailure: errors => throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Expected success but got {errors.Length} error(s): {errors[0].Message}")));

    /// <summary>Verifies Result is failure with optional error predicate.</summary>
    public static void Failure<T>(Result<T> result, Func<SystemError[], bool>? predicate = null, string? message = null) =>
        result.Match(
            onSuccess: value => throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Expected failure but got success with value {value}")),
            onFailure: errors => predicate is null || predicate(errors)
                ? true
                : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Failure predicate violated for errors: {string.Join(", ", errors.Select(e => e.Message))}")));

    /// <summary>Verifies collection contains exactly the expected count of elements satisfying predicate.</summary>
    public static void Count<T>(IEnumerable<T> collection, Func<T, bool> predicate, int expectedCount, string? message = null) {
        int actualCount = 0;
        foreach (T item in collection) {
            actualCount = predicate(item) ? actualCount + 1 : actualCount;
        }
        _ = actualCount == expectedCount
            ? true
            : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Count failed: expected {expectedCount} but found {actualCount}"));
    }

    /// <summary>Verifies collection satisfies predicate for all, any, or none of its elements.</summary>
    public static void All<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null) {
        foreach (T item in collection) {
            _ = predicate(item) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"All failed: predicate violated for item {item}"));
        }
    }

    /// <summary>Verifies at least one element satisfies predicate.</summary>
    public static void Any<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null) {
        foreach (T item in collection) {
            if (predicate(item)) {
                return;
            }
        }
        throw new InvalidOperationException(message ?? "Any failed: no element satisfied predicate");
    }

    /// <summary>Verifies no element satisfies predicate.</summary>
    public static void None<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null) {
        foreach (T item in collection) {
            _ = !predicate(item) ? true : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"None failed: predicate satisfied for item {item}"));
        }
    }

    /// <summary>Verifies ordering relation holds for consecutive elements.</summary>
    public static void Ordered<T>(IEnumerable<T> collection, Func<T, T, bool> relation, string? message = null) {
        T? previous = default;
        bool first = true;
        foreach (T current in collection) {
            _ = first || relation(previous!, current)
                ? true
                : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Ordered failed: relation violated between {previous} and {current}"));
            (previous, first) = (current, false);
        }
    }

    /// <summary>Verifies collection is strictly increasing using numeric comparison.</summary>
    public static void Increasing<T>(IEnumerable<T> collection, string? message = null) where T : IComparable<T> =>
        Ordered(collection, static (a, b) => a.CompareTo(b) < 0, message ?? "Increasing failed");

    /// <summary>Verifies collection is strictly decreasing using numeric comparison.</summary>
    public static void Decreasing<T>(IEnumerable<T> collection, string? message = null) where T : IComparable<T> =>
        Ordered(collection, static (a, b) => a.CompareTo(b) > 0, message ?? "Decreasing failed");

    /// <summary>Verifies collection is non-decreasing using numeric comparison.</summary>
    public static void NonDecreasing<T>(IEnumerable<T> collection, string? message = null) where T : IComparable<T> =>
        Ordered(collection, static (a, b) => a.CompareTo(b) <= 0, message ?? "NonDecreasing failed");

    /// <summary>Verifies action throws exception of expected type with optional predicate.</summary>
    public static void Throws<TException>(Action action, Func<TException, bool>? predicate = null, string? message = null) where TException : Exception {
        try {
            action();
            throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Expected {typeof(TException).Name} but no exception was thrown"));
        } catch (TException ex) {
            _ = predicate is null || predicate(ex)
                ? true
                : throw new InvalidOperationException(message ?? string.Create(CultureInfo.InvariantCulture, $"Exception predicate failed for {ex.GetType().Name}: {ex.Message}"));
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
        _ = successCount == 1
            ? true
            : throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"ExactlyOne failed: {successCount} assertions succeeded"));
    }
}
