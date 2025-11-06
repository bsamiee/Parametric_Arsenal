using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Validation;

/// <summary>Combinable validation modes using bitflag pattern with operator overloading for extensibility.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ValidationMode(int value) : IEquatable<ValidationMode> {
    public int Value { get; } = value;

    public static readonly ValidationMode None = new(0);
    public static readonly ValidationMode Standard = new(1);
    public static readonly ValidationMode AreaCentroid = new(2);
    public static readonly ValidationMode BoundingBox = new(4);
    public static readonly ValidationMode MassProperties = new(8);
    public static readonly ValidationMode Topology = new(16);
    public static readonly ValidationMode Degeneracy = new(32);
    public static readonly ValidationMode Tolerance = new(64);
    public static readonly ValidationMode SelfIntersection = new(128);
    public static readonly ValidationMode MeshSpecific = new(256);
    public static readonly ValidationMode SurfaceContinuity = new(512);
    public static readonly ValidationMode All = new(1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 256 | 512);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationMode operator |(ValidationMode left, ValidationMode right) => new(left.Value | right.Value);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationMode operator &(ValidationMode left, ValidationMode right) => new(left.Value & right.Value);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationMode operator ^(ValidationMode left, ValidationMode right) => new(left.Value ^ right.Value);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationMode operator ~(ValidationMode mode) => new(~mode.Value);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ValidationMode left, ValidationMode right) => left.Value == right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ValidationMode left, ValidationMode right) => left.Value != right.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(ValidationMode mode) => mode.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ValidationMode(int value) => new(value);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasFlag(ValidationMode flag) => (this.Value & flag.Value) == flag.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is ValidationMode other && this.Equals(other);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => this.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ValidationMode other) => this.Value == other.Value;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => this.Value switch {
        0 => nameof(None),
        1 => nameof(Standard),
        2 => nameof(AreaCentroid),
        4 => nameof(BoundingBox),
        8 => nameof(MassProperties),
        16 => nameof(Topology),
        32 => nameof(Degeneracy),
        64 => nameof(Tolerance),
        128 => nameof(SelfIntersection),
        256 => nameof(MeshSpecific),
        512 => nameof(SurfaceContinuity),
        1023 => nameof(All),
        _ => $"Combined({this.Value})",
    };
}
