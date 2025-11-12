using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Immutable error with domain classification and context.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public readonly record struct SystemError(byte Domain, int Code, string Message) {
    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"[{Domain switch { 1 => "Results", 2 => "Geometry", 3 => "Validation", 4 => "Spatial", 5 => "Topology", _ => "Unknown" }}:{Code.ToString(CultureInfo.InvariantCulture)}] {Message}");

    [Pure]
    public override string ToString() => $"[{Domain switch { 1 => "Results", 2 => "Geometry", 3 => "Validation", 4 => "Spatial", 5 => "Topology", _ => "Unknown" }}:{Code}] {Message}";

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Domain, this.Code, $"{this.Message} (Context: {context})");
}
