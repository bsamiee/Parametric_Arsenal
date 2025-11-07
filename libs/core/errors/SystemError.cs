using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Immutable error record with domain classification and contextual information.</summary>
public readonly record struct SystemError(Domain Domain, int Code, string Message) {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Domain, this.Code, $"{this.Message} (Context: {context})");

    [Pure]
    public override string ToString() => $"[{this.Domain}:{this.Code}] {this.Message}";
}

/// <summary>Error domain categorization for system-wide error classification.</summary>
public enum Domain : byte {
    Unknown = 0,
    Results = 10,
    Geometry = 20,
    Validation = 30,
    Spatial = 40,
}
