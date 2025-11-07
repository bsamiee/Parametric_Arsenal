using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Immutable error record with domain classification and contextual information.</summary>
public readonly record struct SystemError(ErrorDomain Domain, int Code, string Message) {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Domain, this.Code, $"{this.Message} (Context: {context})");

    [Pure]
    public override string ToString() => $"[{this.Domain}:{this.Code}] {this.Message}";
}
