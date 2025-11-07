namespace Arsenal.Rhino.Topology;

/// <summary>Configuration constants for topology operations.</summary>
internal static class TopologyConfig {
    /// <summary>Empty indices list reused across operations to avoid allocations.</summary>
    internal static readonly IReadOnlyList<int> EmptyIndices = [];
}
