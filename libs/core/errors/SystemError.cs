using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Zero-allocation error structure with domain classification and contextual information.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct SystemError(ErrorDomain domain, int code, string message) : IEquatable<SystemError> {
    public ErrorDomain Domain { get; } = domain;
    public int Code { get; } = code;
    public string Message { get; } = message;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(SystemError left, SystemError right) => left.Equals(right);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(SystemError left, SystemError right) => !left.Equals(right);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is SystemError other && this.Equals(other);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(this.Domain, this.Code, this.Message);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(SystemError other) =>
        this.Domain == other.Domain && this.Code == other.Code && string.Equals(this.Message, other.Message, StringComparison.Ordinal);

    /// <summary>Creates new error with additional context information.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemError WithContext(string context) =>
        new(this.Domain, this.Code, $"{this.Message} (Context: {context})");

    /// <summary>Creates error from registry using code-based lookup with automatic domain inference.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError From(int code, string? context = null) => ErrorRegistry.Get(code, context);

    /// <summary>Creates error array from codes for batch error creation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] From(params int[] codes) => ErrorRegistry.Get(codes);
}
