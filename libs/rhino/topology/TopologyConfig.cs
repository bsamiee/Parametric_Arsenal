using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Topology;

/// <summary>Configuration constants and validation dispatch for topology operations.</summary>
internal static class TopologyConfig {
    /// <summary>Gets validation mode for geometry type in topology operations.</summary>
    internal static V GetValidationMode<T>(T geometry) where T : notnull =>
        geometry switch {
            Brep => V.Standard | V.Topology,
            Mesh => V.Standard | V.MeshSpecific,
            _ => V.None,
        };
}
