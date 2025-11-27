using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.InteropServices;
using Arsenal.Core.Validation;

namespace Arsenal.Core.Diagnostics;

/// <summary>Zero-allocation observability with compile-time toggleable tracing.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
internal readonly record struct DiagnosticContext(
    string Operation,
    TimeSpan Elapsed,
    long Allocations,
    V? ValidationApplied = null,
    bool? CacheHit = null,
    int? ErrorCount = null) {
    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture,
        $"{this.Operation} | {this.Elapsed.TotalMilliseconds:F3}ms | {this.Allocations.ToString(CultureInfo.InvariantCulture)}b{(this.CacheHit is true ? " [cached]" : this.CacheHit is false ? " [computed]" : string.Empty)}{(this.ValidationApplied.HasValue ? $" | Val:{this.ValidationApplied.Value}" : string.Empty)}{(this.ErrorCount.HasValue ? $" | Err:{this.ErrorCount.Value.ToString(CultureInfo.InvariantCulture)}" : string.Empty)}");
}
