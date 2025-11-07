using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Diagnostics;

/// <summary>Zero-allocation observability infrastructure with compile-time toggleable tracing and DebuggerDisplay integration.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public readonly struct DiagnosticContext(
    string operation,
    TimeSpan elapsed,
    long allocations,
    V? validationApplied = null,
    bool? cacheHit = null,
    int? errorCount = null) : IEquatable<DiagnosticContext> {
    public string Operation { get; } = operation;
    public TimeSpan Elapsed { get; } = elapsed;
    public long Allocations { get; } = allocations;
    public V? ValidationApplied { get; } = validationApplied;
    public bool? CacheHit { get; } = cacheHit;
    public int? ErrorCount { get; } = errorCount;

    [Pure]
    private string DebuggerDisplay => string.Create(provider: CultureInfo.InvariantCulture,
        handler: $"{this.Operation} | {this.Elapsed.TotalMilliseconds:F3}ms | {this.Allocations.ToString(provider: CultureInfo.InvariantCulture)}b{(this.CacheHit.HasValue ? this.CacheHit.Value ? " [cached]" : " [computed]" : string.Empty)}{(this.ValidationApplied.HasValue ? $" | Val:{this.ValidationApplied.Value.ToUInt16().ToString(provider: CultureInfo.InvariantCulture)}" : string.Empty)}{(this.ErrorCount.HasValue ? $" | Err:{this.ErrorCount.Value.ToString(provider: CultureInfo.InvariantCulture)}" : string.Empty)}");

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
