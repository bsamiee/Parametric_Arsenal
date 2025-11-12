using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Immutable error with algorithmically-derived domain classification.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public readonly record struct SystemError(int Code, string Message) {
    [Pure] public string Domain => this.Code switch {
        >= 1000 and < 2000 => "Results",
        >= 2000 and < 3000 => "Geometry",
        >= 3000 and < 4000 => "Validation",
        >= 4000 and < 5000 => "Spatial",
        >= 5000 and < 6000 => "Topology",
        _ => "Unknown",
    };

    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"[{this.Domain}:{this.Code.ToString(CultureInfo.InvariantCulture)}] {this.Message}");

    [Pure]
    public override string ToString() => $"[{this.Domain}:{this.Code}] {this.Message}";

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Code, $"{this.Message} (Context: {context})");
}
