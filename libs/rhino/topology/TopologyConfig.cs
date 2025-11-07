using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Configuration constants and validation dispatch for topology operations.</summary>
internal static class TopologyConfig {
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(Mesh)] = V.Standard | V.MeshSpecific,
            [typeof(Extrusion)] = V.Standard | V.Topology,
        }.ToFrozenDictionary();

    internal const double CurvatureThresholdRatio = 0.1;
}
