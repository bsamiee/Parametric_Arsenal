using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arsenal.Core.Errors;

namespace Arsenal.Core.Validation;

/// <summary>Validation mode flags with bitwise operations.</summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{DebuggerDisplay}")]
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
    public static readonly V MeshSpecific = new(128);
    public static readonly V SurfaceContinuity = new(256);
    public static readonly V PolycurveStructure = new(512);
    public static readonly V NurbsGeometry = new(1024);
    public static readonly V ExtrusionGeometry = new(2048);
    public static readonly V UVDomain = new(4096);
    public static readonly V SelfIntersection = new(8192);
    public static readonly V BrepGranular = new(16384);
    public static readonly V All = new((ushort)(
        Standard._flags | AreaCentroid._flags | BoundingBox._flags | MassProperties._flags |
        Topology._flags | Degeneracy._flags | Tolerance._flags |
        MeshSpecific._flags | SurfaceContinuity._flags | PolycurveStructure._flags |
        NurbsGeometry._flags | ExtrusionGeometry._flags | UVDomain._flags |
        SelfIntersection._flags | BrepGranular._flags
    ));

    public static readonly FrozenSet<V> AllFlags = ((V[])[Standard, AreaCentroid, BoundingBox, MassProperties, Topology, Degeneracy, Tolerance, MeshSpecific, SurfaceContinuity, PolycurveStructure, NurbsGeometry, ExtrusionGeometry, UVDomain, SelfIntersection, BrepGranular,]).ToFrozenSet();

    /// <summary>Validation rules: V flag to (properties, methods) mapping for expression tree compilation.</summary>
    internal static readonly FrozenDictionary<V, ((string Member, SystemError Error)[] Properties, (string Member, SystemError Error)[] Methods)> ValidationRules =
        new Dictionary<V, ((string Member, SystemError Error)[], (string Member, SystemError Error)[])> {
            [Standard] = ([(Member: "IsValid", Error: E.Validation.GeometryInvalid),], []),
            [AreaCentroid] = ([(Member: "IsClosed", Error: E.Validation.CurveNotClosedOrPlanar),], [(Member: "IsPlanar", Error: E.Validation.CurveNotClosedOrPlanar),]),
            [BoundingBox] = ([], [(Member: "GetBoundingBox", Error: E.Validation.BoundingBoxInvalid),]),
            [MassProperties] = ([(Member: "IsSolid", Error: E.Validation.MassPropertiesComputationFailed), (Member: "IsClosed", Error: E.Validation.MassPropertiesComputationFailed),], []),
            [Topology] = ([(Member: "IsManifold", Error: E.Validation.InvalidTopology), (Member: "IsClosed", Error: E.Validation.InvalidTopology), (Member: "IsSolid", Error: E.Validation.InvalidTopology), (Member: "IsSurface", Error: E.Validation.InvalidTopology), (Member: "HasNakedEdges", Error: E.Validation.InvalidTopology),], [(Member: "IsManifold", Error: E.Validation.InvalidTopology), (Member: "IsPointInside", Error: E.Validation.InvalidTopology),]),
            [Degeneracy] = ([(Member: "IsPeriodic", Error: E.Validation.DegenerateGeometry), (Member: "IsPolyline", Error: E.Validation.DegenerateGeometry),], [(Member: "IsShort", Error: E.Validation.DegenerateGeometry), (Member: "IsSingular", Error: E.Validation.DegenerateGeometry), (Member: "IsDegenerate", Error: E.Validation.DegenerateGeometry), (Member: "IsRectangular", Error: E.Validation.DegenerateGeometry), (Member: "GetLength", Error: E.Validation.DegenerateGeometry),]),
            [Tolerance] = ([], [(Member: "IsPlanar", Error: E.Validation.ToleranceExceeded), (Member: "IsLinear", Error: E.Validation.ToleranceExceeded), (Member: "IsArc", Error: E.Validation.ToleranceExceeded), (Member: "IsCircle", Error: E.Validation.ToleranceExceeded), (Member: "IsEllipse", Error: E.Validation.ToleranceExceeded),]),
            [MeshSpecific] = ([(Member: "IsManifold", Error: E.Validation.MeshInvalid), (Member: "IsClosed", Error: E.Validation.MeshInvalid), (Member: "HasNgons", Error: E.Validation.MeshInvalid), (Member: "HasVertexColors", Error: E.Validation.MeshInvalid), (Member: "HasVertexNormals", Error: E.Validation.MeshInvalid), (Member: "IsTriangleMesh", Error: E.Validation.MeshInvalid), (Member: "IsQuadMesh", Error: E.Validation.MeshInvalid), (Member: "FacesCount", Error: E.Validation.MeshInvalid),], []),
            [SurfaceContinuity] = ([(Member: "IsPeriodic", Error: E.Validation.PositionalDiscontinuity),], [(Member: "IsContinuous", Error: E.Validation.PositionalDiscontinuity),]),
            [PolycurveStructure] = ([(Member: "IsValid", Error: E.Validation.PolycurveGaps), (Member: "HasGap", Error: E.Validation.PolycurveGaps), (Member: "IsNested", Error: E.Validation.PolycurveGaps),], []),
            [NurbsGeometry] = ([(Member: "IsValid", Error: E.Validation.NurbsControlPointCount), (Member: "IsPeriodic", Error: E.Validation.NurbsControlPointCount), (Member: "IsRational", Error: E.Validation.NurbsControlPointCount), (Member: "Degree", Error: E.Validation.NurbsControlPointCount),], []),
            [ExtrusionGeometry] = ([(Member: "IsValid", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsSolid", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsClosed", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsCappedAtTop", Error: E.Validation.ExtrusionProfileInvalid), (Member: "IsCappedAtBottom", Error: E.Validation.ExtrusionProfileInvalid), (Member: "CapCount", Error: E.Validation.ExtrusionProfileInvalid),], []),
            [UVDomain] = ([(Member: "IsValid", Error: E.Validation.UVDomainSingularity), (Member: "HasNurbsForm", Error: E.Validation.UVDomainSingularity), (Member: "DomainLength", Error: E.Validation.UVDomainSingularity),], []),
            [SelfIntersection] = ([], [(Member: "CurveSelf", Error: E.Validation.SelfIntersecting),]),
            [BrepGranular] = ([], [(Member: "IsValidTopology", Error: E.Validation.BrepTopologyInvalid), (Member: "IsValidGeometry", Error: E.Validation.BrepGeometryInvalid), (Member: "IsValidTolerancesAndFlags", Error: E.Validation.BrepTolerancesAndFlagsInvalid),]),
        }.ToFrozenDictionary();

    [Pure] private string DebuggerDisplay => this.ToString();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(V left, V right) => left._flags == right._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(V left, V right) => left._flags != right._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA2225:Operator overloads have named alternates", Justification = "Bitwise operations are idiomatic for flag types")]
    public static V operator |(V left, V right) => new((ushort)(left._flags | right._flags));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA2225:Operator overloads have named alternates", Justification = "Bitwise operations are idiomatic for flag types")]
    public static V operator &(V left, V right) => new((ushort)(left._flags & right._flags));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA2225:Operator overloads have named alternates", Justification = "Implicit conversions for internal use")]
    public static implicit operator ushort(V v) => v._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA2225:Operator overloads have named alternates", Justification = "Implicit conversions for internal use")]
    public static implicit operator V(ushort flags) => new(flags);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => this._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is V other && this._flags == other._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(V other) => this._flags == other._flags;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(V other) =>
        other._flags == 0
            ? this._flags == 0
            : (this._flags & other._flags) == other._flags;

    [Pure]
    public override string ToString() => this._flags == All._flags
        ? nameof(All)
        : this._flags switch {
            0 => nameof(None),
            1 => nameof(Standard),
            2 => nameof(AreaCentroid),
            4 => nameof(BoundingBox),
            8 => nameof(MassProperties),
            16 => nameof(Topology),
            32 => nameof(Degeneracy),
            64 => nameof(Tolerance),
            128 => nameof(MeshSpecific),
            256 => nameof(SurfaceContinuity),
            512 => nameof(PolycurveStructure),
            1024 => nameof(NurbsGeometry),
            2048 => nameof(ExtrusionGeometry),
            4096 => nameof(UVDomain),
            8192 => nameof(SelfIntersection),
            16384 => nameof(BrepGranular),
            _ => $"Combined({this._flags})",
        };
}
