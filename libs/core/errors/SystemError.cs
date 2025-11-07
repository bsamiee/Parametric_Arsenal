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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte storage for memory efficiency")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0048:File name must match type name", Justification = "Domain enum is part of SystemError")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0104:Type exists in another namespace", Justification = "Arsenal.Core.Errors.Domain is intentional")]
public enum Domain : byte {
    Unknown = 0,
    Results = 10,
    Geometry = 20,
    Validation = 30,
    Spatial = 40,
}
