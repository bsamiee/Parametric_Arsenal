using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Validation;

/// <summary>Combinable validation flags for geometric validation operations using bitwise composition.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct V(ushort flags) : IEquatable<V> {
    private readonly ushort _flags = flags;

    public static readonly V None = new(0);
    public static readonly V Standard = new(1);
    public static readonly V AreaCentroid = new(2);
    public static readonly V BoundingBox = new(4);
    public static readonly V MassProperties = new(8);
    public static readonly V Topology = new(16);
    public static readonly V Degeneracy = new(32);
    public static readonly V Tolerance = new(64);
    public static readonly V SelfIntersection = new(128);
    public static readonly V MeshSpecific = new(256);
    public static readonly V SurfaceContinuity = new(512);
    public static readonly V All = new(1023);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static V operator |(V left, V right) => new((ushort)(left._flags | right._flags));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static V operator &(V left, V right) => new((ushort)(left._flags & right._flags));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(V left, V right) => left._flags == right._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(V left, V right) => left._flags != right._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ushort(V v) => v._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator V(ushort flags) => new(flags);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(V other) => (this._flags & other._flags) == other._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is V other && this._flags == other._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => this._flags.GetHashCode();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(V other) => this._flags == other._flags;
}
