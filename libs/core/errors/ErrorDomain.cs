using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Errors;

/// <summary>Hierarchical error domain with compile-time constants and operator overloading for extensibility.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ErrorDomain(int value) : IEquatable<ErrorDomain>, IComparable<ErrorDomain> {
    public int Value { get; } = value;

    public static readonly ErrorDomain None = new(0);
    public static readonly ErrorDomain Results = new(1000);
    public static readonly ErrorDomain Geometry = new(2000);
    public static readonly ErrorDomain Validation = new(3000);
    public static readonly ErrorDomain Operations = new(4000);
    public static readonly ErrorDomain Diagnostics = new(5000);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ErrorDomain left, ErrorDomain right) => left.Value == right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ErrorDomain left, ErrorDomain right) => left.Value != right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(ErrorDomain left, ErrorDomain right) => left.Value < right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(ErrorDomain left, ErrorDomain right) => left.Value > right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(ErrorDomain left, ErrorDomain right) => left.Value <= right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(ErrorDomain left, ErrorDomain right) => left.Value >= right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(ErrorDomain domain) => domain.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ErrorDomain(int value) => new(value);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is ErrorDomain other && this.Equals(other);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => this.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ErrorDomain other) => this.Value == other.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(ErrorDomain other) => this.Value.CompareTo(other.Value);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => this.Value switch {
        0 => nameof(None),
        1000 => nameof(Results),
        2000 => nameof(Geometry),
        3000 => nameof(Validation),
        4000 => nameof(Operations),
        5000 => nameof(Diagnostics),
        int v when v >= 1000 && v < 2000 => nameof(Results),
        int v when v >= 2000 && v < 3000 => nameof(Geometry),
        int v when v >= 3000 && v < 4000 => nameof(Validation),
        int v when v >= 4000 && v < 5000 => nameof(Operations),
        int v when v >= 5000 && v < 6000 => nameof(Diagnostics),
        _ => $"Custom({this.Value})",
    };
}
