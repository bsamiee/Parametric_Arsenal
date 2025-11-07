namespace Arsenal.Core.Validation;

/// <summary>Type alias for V struct - use V instead for new code during transition.</summary>
public readonly struct ValidationMode(ushort flags) : IEquatable<ValidationMode> {
    private readonly ushort _flags = flags;

    public static readonly ValidationMode None = new(flags: 0);
    public static readonly ValidationMode Standard = new(flags: 1);
    public static readonly ValidationMode AreaCentroid = new(flags: 2);
    public static readonly ValidationMode BoundingBox = new(flags: 4);
    public static readonly ValidationMode MassProperties = new(flags: 8);
    public static readonly ValidationMode Topology = new(flags: 16);
    public static readonly ValidationMode Degeneracy = new(flags: 32);
    public static readonly ValidationMode Tolerance = new(flags: 64);
    public static readonly ValidationMode SelfIntersection = new(flags: 128);
    public static readonly ValidationMode MeshSpecific = new(flags: 256);
    public static readonly ValidationMode SurfaceContinuity = new(flags: 512);
    public static readonly ValidationMode All = new(flags: 1023);

    public static ValidationMode operator |(ValidationMode left, ValidationMode right) => new(flags: (ushort)(left._flags | right._flags));
    public static ValidationMode operator &(ValidationMode left, ValidationMode right) => new(flags: (ushort)(left._flags & right._flags));
    public static bool operator ==(ValidationMode left, ValidationMode right) => left._flags == right._flags;
    public static bool operator !=(ValidationMode left, ValidationMode right) => left._flags != right._flags;

    public static ValidationMode BitwiseOr(ValidationMode left, ValidationMode right) => left | right;
    public static ValidationMode BitwiseAnd(ValidationMode left, ValidationMode right) => left & right;

    public bool HasFlag(ValidationMode flag) =>
        flag._flags == 0
            ? this._flags == 0
            : (this._flags & flag._flags) == flag._flags;

    public static implicit operator V(ValidationMode mode) => new(flags: mode._flags);
    public static implicit operator ValidationMode(V v) => new(flags: (ushort)v);

    public V ToV() => this;
    public static ValidationMode FromV(V v) => v;

    public override bool Equals(object? obj) => obj is ValidationMode other && this._flags == other._flags;
    public override int GetHashCode() => this._flags;
    public bool Equals(ValidationMode other) => this._flags == other._flags;
}
