using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Solid;

/// <summary>Validation modes and constants for solid boolean operations.</summary>
internal static class SolidConfig {
    /// <summary>Type-to-validation mode mapping for Brep boolean operations.</summary>
    internal static readonly FrozenDictionary<Type, V> BrepValidationModes =
        new Dictionary<Type, V> {
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Brep[])] = V.Standard | V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Type-to-validation mode mapping for Mesh boolean operations.</summary>
    internal static readonly FrozenDictionary<Type, V> MeshValidationModes =
        new Dictionary<Type, V> {
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Mesh[])] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Operation type identifiers for internal dispatch.</summary>
    internal const byte UnionOp = 0;
    internal const byte IntersectionOp = 1;
    internal const byte DifferenceOp = 2;
    internal const byte SplitOp = 3;
    internal const byte TrimOp = 4;
}
