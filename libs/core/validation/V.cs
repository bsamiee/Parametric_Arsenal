using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Validation;

/// <summary>
/// Validation mode configuration using bitwise flag operations for combinable validation rules.
///
/// <para><b>Usage:</b></para>
/// <code>
/// V mode = V.Standard | V.Topology;
/// bool hasStandard = mode.Has(V.Standard);  // true
/// bool hasArea = mode.Has(V.AreaCentroid);  // false
/// </code>
///
/// <para><b>Extensibility:</b></para>
/// <para>1. Add new flag constant (next power of 2)</para>
/// <para>2. Update All computation to include new flag</para>
/// <para>3. Add validation rule to ValidationRules.cs</para>
/// </summary>
public readonly struct V(ushort flags) : IEquatable<V> {
    private readonly ushort _flags = flags;

    public static readonly V None = new(flags: 0);
    public static readonly V Standard = new(flags: 1);
    public static readonly V AreaCentroid = new(flags: 2);
    public static readonly V BoundingBox = new(flags: 4);
    public static readonly V MassProperties = new(flags: 8);
    public static readonly V Topology = new(flags: 16);
    public static readonly V Degeneracy = new(flags: 32);
    public static readonly V Tolerance = new(flags: 64);
    public static readonly V SelfIntersection = new(flags: 128);
    public static readonly V MeshSpecific = new(flags: 256);
    public static readonly V SurfaceContinuity = new(flags: 512);

    public static readonly V All = new((ushort)(
        Standard._flags | AreaCentroid._flags |
        BoundingBox._flags | MassProperties._flags |
        Topology._flags | Degeneracy._flags |
        Tolerance._flags | SelfIntersection._flags |
        MeshSpecific._flags | SurfaceContinuity._flags
    ));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static V operator |(V left, V right) => new((ushort)(left._flags | right._flags));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static V operator &(V left, V right) => new((ushort)(left._flags & right._flags));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(V left, V right) => left._flags == right._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(V left, V right) => left._flags != right._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(V other) =>
        other._flags == 0
            ? this._flags == 0
            : (this._flags & other._flags) == other._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ushort(V v) => v._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator V(ushort flags) => new(flags: flags);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is V other && this._flags == other._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(V other) => this._flags == other._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _flags;
}
