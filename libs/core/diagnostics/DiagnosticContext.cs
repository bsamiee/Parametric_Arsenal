using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Diagnostics;

/// <summary>Zero-allocation observability infrastructure with compile-time toggleable tracing and DebuggerDisplay integration.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public readonly struct DiagnosticContext(
    string operation,
    TimeSpan elapsed,
    long allocations,
    ValidationMode? validationApplied = null,
    bool? cacheHit = null,
    int? errorCount = null) : IEquatable<DiagnosticContext> {
    public string Operation { get; } = operation;
    public TimeSpan Elapsed { get; } = elapsed;
    public long Allocations { get; } = allocations;
    public ValidationMode? ValidationApplied { get; } = validationApplied;
    public bool? CacheHit { get; } = cacheHit;
    public int? ErrorCount { get; } = errorCount;

    [Pure]
    private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
        $"{this.Operation} | {this.Elapsed.TotalMilliseconds:F3}ms | {this.Allocations}b{(this.CacheHit.HasValue ? this.CacheHit.Value ? " [cached]" : " [computed]" : string.Empty)}{(this.ValidationApplied.HasValue ? $" | Val:{this.ValidationApplied.Value}" : string.Empty)}{(this.ErrorCount.HasValue ? $" | Err:{this.ErrorCount.Value}" : string.Empty)}");

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(DiagnosticContext left, DiagnosticContext right) => left.Equals(right);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(DiagnosticContext left, DiagnosticContext right) => !left.Equals(right);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is DiagnosticContext other && this.Equals(other);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(this.Operation, this.Elapsed, this.Allocations, this.ValidationApplied, this.CacheHit, this.ErrorCount);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(DiagnosticContext other) =>
        string.Equals(this.Operation, other.Operation, StringComparison.Ordinal) &&
        this.Elapsed == other.Elapsed &&
        this.Allocations == other.Allocations &&
        this.ValidationApplied == other.ValidationApplied &&
        this.CacheHit == other.CacheHit &&
        this.ErrorCount == other.ErrorCount;
}

/// <summary>Polymorphic diagnostic capture engine with ConditionalWeakTable storage and compile-time tracing control.</summary>
public static class Diagnostics {
    private static readonly ConditionalWeakTable<object, DiagnosticContext> _metadata = [];
    private static readonly ActivitySource _activitySource = new("Arsenal.Core", "1.0.0");

    /// <summary>Captures operation diagnostics with allocation tracking and optional Activity tracing when enabled at compile-time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Capture<T>(
        this Result<T> result,
        string operation,
        ValidationMode? validationApplied = null,
        bool? cacheHit = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) {
#if DEBUG
        (Activity? activity, long startBytes, Stopwatch stopwatch) = (_activitySource.StartActivity(operation), GC.GetAllocatedBytesForCurrentThread(), Stopwatch.StartNew());
        _ = result.IsSuccess;
        stopwatch.Stop();
        (long allocated, DiagnosticContext ctx) = (GC.GetAllocatedBytesForCurrentThread() - startBytes,
            new(operation, stopwatch.Elapsed, allocated, validationApplied, cacheHit, result.IsSuccess ? null : result.Errors.Count));
        activity?.SetTag("operation", operation)
            .SetTag("elapsed_ms", stopwatch.Elapsed.TotalMilliseconds)
            .SetTag("allocations", allocated)
            .SetTag("caller", $"{callerFile}:{callerLine.ToString(CultureInfo.InvariantCulture)} ({callerMember})")
            .SetTag("success", result.IsSuccess)
            .SetTag("validation", validationApplied?.ToString() ?? "None")
            .SetTag("cache_hit", cacheHit?.ToString() ?? "N/A")
            .Dispose();
        _ = _metadata.AddOrUpdate(result, ctx);
        return result;
#else
        return result;
#endif
    }

    /// <summary>Retrieves diagnostic metadata for Result instance using ConditionalWeakTable lookup with safe null handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetDiagnostics<T>(this Result<T> result, [MaybeNullWhen(false)] out DiagnosticContext context) =>
#if DEBUG
        _metadata.TryGetValue(result, out context);
#else
        (context = default, false).Item2;
#endif

    /// <summary>Clears all diagnostic metadata enabling memory reclamation for long-running processes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clear() {
#if DEBUG
        _ = _metadata.Clear();
#endif
    }

    /// <summary>Compile-time feature detection for diagnostic capability presence in binary.</summary>
    [Pure]
    public static bool IsEnabled =>
#if DEBUG
        true;
#else
        false;
#endif
}
