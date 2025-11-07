using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Configuration constants and validation dispatch for topology operations.</summary>
internal static class TopologyConfig {
    /// <summary>Validation mode mapping for topology geometry types.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Extrusion)] = V.Standard | V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Default angle threshold ratio for curvature detection in edge classification.</summary>
    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Default BFS traversal queue capacity for connectivity analysis.</summary>
    internal const int ConnectivityQueueCapacity = 256;
}
