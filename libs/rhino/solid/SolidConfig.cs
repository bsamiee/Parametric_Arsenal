using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Solid;

/// <summary>Validation modes and constants for solid boolean operations.</summary>
internal static class SolidConfig {
    /// <summary>Operation type identifiers for internal dispatch.</summary>
    internal const byte UnionOp = 0;
    internal const byte IntersectionOp = 1;
    internal const byte DifferenceOp = 2;
    internal const byte SplitOp = 3;
    internal const byte TrimOp = 4;
}
