using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Diagnostics;

/// <summary>Polymorphic diagnostic capture engine with ConditionalWeakTable storage and compile-time tracing control.</summary>
public static class DiagnosticCapture {
#if DEBUG
    private static readonly ConditionalWeakTable<object, StrongBox<DiagnosticContext>> _metadata = [];
    private static readonly ActivitySource _activitySource = new("Arsenal.Core", "1.0.0");
#endif

    /// <summary>Captures operation diagnostics with allocation tracking and optional Activity tracing when enabled at compile-time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Capture<T>(
        this Result<T> result,
        string operation,
        V? validationApplied = null,
        bool? cacheHit = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) {
#if DEBUG
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        Activity? activity = _activitySource.StartActivity(name: operation);
        _ = result.IsSuccess;
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        DiagnosticContext ctx = new(operation: operation, elapsed: stopwatch.Elapsed, allocations: allocated, validationApplied: validationApplied, cacheHit: cacheHit, errorCount: result.IsSuccess ? null : result.Errors.Count);
        activity?.SetTag(key: "operation", value: operation)
            .SetTag(key: "elapsed_ms", value: stopwatch.Elapsed.TotalMilliseconds)
            .SetTag(key: "allocations", value: allocated)
            .SetTag(key: "caller", value: $"{callerFile}:{callerLine.ToString(provider: CultureInfo.InvariantCulture)} ({callerMember})")
            .SetTag(key: "success", value: result.IsSuccess)
            .SetTag(key: "validation", value: validationApplied.HasValue ? validationApplied.Value.ToUInt16().ToString(provider: CultureInfo.InvariantCulture) : "None")
            .SetTag(key: "cache_hit", value: cacheHit?.ToString(provider: CultureInfo.InvariantCulture) ?? "N/A")
            .Dispose();
        _metadata.AddOrUpdate(key: result, value: new StrongBox<DiagnosticContext>(value: ctx));
        return result;
#else
        _ = operation; _ = validationApplied; _ = cacheHit; _ = callerMember; _ = callerFile; _ = callerLine;
        return result;
#endif
    }

    /// <summary>Retrieves diagnostic metadata for Result instance using ConditionalWeakTable lookup with safe null handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetDiagnostics<T>(this Result<T> result, [MaybeNullWhen(false)] out DiagnosticContext context) {
#if DEBUG
        bool found = _metadata.TryGetValue(result, out StrongBox<DiagnosticContext>? box);
        context = found && box is not null ? box.Value! : default;
        return found;
#else
        _ = result;
        context = default;
        return false;
#endif
    }

    /// <summary>Clears all diagnostic metadata enabling memory reclamation for long-running processes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable IDE0022 // Use expression body for method
    public static void Clear() {
#if DEBUG
        _metadata.Clear();
#endif
    }
#pragma warning restore IDE0022

    /// <summary>Compile-time feature detection for diagnostic capability presence in binary.</summary>
    [Pure]
    public static bool IsEnabled =>
#if DEBUG
        true;
#else
        false;
#endif
}
