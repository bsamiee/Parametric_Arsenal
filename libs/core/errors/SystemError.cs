using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Zero-allocation error structure with computed domain classification and contextual information.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct SystemError(int code, string message) : IEquatable<SystemError> {
    public int Code { get; } = code;
    public string Message { get; } = message;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(SystemError left, SystemError right) => left.Equals(right);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(SystemError left, SystemError right) => !left.Equals(right);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is SystemError other && this.Equals(other);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(this.Code, this.Message);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(SystemError other) =>
        this.Code == other.Code && string.Equals(this.Message, other.Message, StringComparison.Ordinal);

    /// <summary>Creates new error with additional context information.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Code, $"{this.Message} (Context: {context})");
}
