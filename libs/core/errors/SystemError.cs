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
    [Pure] private string DomainName => E.DomainNames.TryGetValue(this.Domain, out string? name) ? name : "Unknown";
    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"[{this.DomainName}:{this.Code.ToString(CultureInfo.InvariantCulture)}] {this.Message}");

    [Pure]
    public override string ToString() => $"[{this.DomainName}:{this.Code}] {this.Message}";

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Domain, this.Code, $"{this.Message} (Context: {context})");
}
