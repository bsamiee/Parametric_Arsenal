using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arsenal.Core.Validation;

/// <summary>Combinable validation configuration using bitwise flag operations instead of enum for extensibility.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ValidationConfig(int flags) : IEquatable<ValidationConfig> {
    public int Flags { get; } = flags;

    public static readonly ValidationConfig None = new(0);
    public static readonly ValidationConfig Standard = new(1);
    public static readonly ValidationConfig AreaCentroid = new(2);
    public static readonly ValidationConfig BoundingBox = new(4);
    public static readonly ValidationConfig MassProperties = new(8);
    public static readonly ValidationConfig Topology = new(16);
    public static readonly ValidationConfig Degeneracy = new(32);
    public static readonly ValidationConfig Tolerance = new(64);
    public static readonly ValidationConfig SelfIntersection = new(128);
    public static readonly ValidationConfig MeshSpecific = new(256);
    public static readonly ValidationConfig SurfaceContinuity = new(512);
    public static readonly ValidationConfig All = new(Standard.Flags | AreaCentroid.Flags | BoundingBox.Flags | MassProperties.Flags | Topology.Flags | Degeneracy.Flags | Tolerance.Flags | SelfIntersection.Flags | MeshSpecific.Flags | SurfaceContinuity.Flags);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationConfig operator |(ValidationConfig left, ValidationConfig right) =>
        new(left.Flags | right.Flags);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValidationConfig operator &(ValidationConfig left, ValidationConfig right) =>
        new(left.Flags & right.Flags);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ValidationConfig left, ValidationConfig right) => left.Flags == right.Flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ValidationConfig left, ValidationConfig right) => left.Flags != right.Flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasFlag(ValidationConfig flag) => (this.Flags & flag.Flags) == flag.Flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is ValidationConfig other && this.Flags == other.Flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => this.Flags.GetHashCode();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ValidationConfig other) => this.Flags == other.Flags;
}
