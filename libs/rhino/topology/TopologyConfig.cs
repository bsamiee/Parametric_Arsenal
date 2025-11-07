using Arsenal.Core.Validation;

namespace Arsenal.Rhino.Topology;

/// <summary>Configuration constants for topology analysis operations.</summary>
internal static class TopologyConfig {
    /// <summary>Default angle threshold ratio for curvature detection in edge classification.</summary>
    internal const double CurvatureThresholdRatio = 0.1;

    /// <summary>Default BFS traversal queue capacity for connectivity analysis.</summary>
    internal const int ConnectivityQueueCapacity = 256;
}
