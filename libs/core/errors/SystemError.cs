using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Immutable error record with domain classification and contextual information.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
public readonly record struct SystemError(ErrorDomain Domain, int Code, string Message) {
    [Pure] private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"[{this.Domain}:{this.Code.ToString(CultureInfo.InvariantCulture)}] {this.Message}");

    [Pure]
    public override string ToString() => $"[{this.Domain}:{this.Code}] {this.Message}";

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Domain, this.Code, $"{this.Message} (Context: {context})");
}
