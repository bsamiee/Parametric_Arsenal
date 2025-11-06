using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Error domain discriminator using value-based dispatch instead of enum for extensibility.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Domain(byte value) : IEquatable<Domain> {
    public byte Value { get; } = value;

    public static readonly Domain None = new(0);
    public static readonly Domain Results = new(10);
    public static readonly Domain Geometry = new(20);
    public static readonly Domain Validation = new(30);
    public static readonly Domain Diagnostics = new(50);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Domain left, Domain right) => left.Value == right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Domain left, Domain right) => left.Value != right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Domain other && this.Value == other.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => this.Value.GetHashCode();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Domain other) => this.Value == other.Value;
}
