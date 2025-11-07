using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Configuration constants and validation mode dispatch for topology operations.</summary>
internal static class TopologyConfig {
    /// <summary>Validation modes for topology analysis by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
        }.ToFrozenDictionary();

    /// <summary>Curvature threshold factor relative to angle threshold for smooth/curvature classification.</summary>
    internal const double CurvatureThresholdFactor = 0.1;
}
