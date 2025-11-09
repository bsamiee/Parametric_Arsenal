namespace Arsenal.Rhino.Spatial;

/// <summary>Buffer size constants for RTree spatial queries.</summary>
internal static class SpatialConfig {
    /// <summary>2048-element buffer for sphere/box queries.</summary>
    internal const int DefaultBufferSize = 2048;

    /// <summary>4096-element buffer for mesh overlap/proximity queries.</summary>
    internal const int LargeBufferSize = 4096;

    /// <summary>Maximum iterations for k-means convergence (100 iterations).</summary>
    internal const int MaxClusterIterations = 100;

    /// <summary>Minimum points for DBSCAN core point classification (4 points).</summary>
    internal const int DBSCANMinPoints = 4;

    /// <summary>Convergence threshold for k-means centroid movement (0.001 units).</summary>
    internal const double ClusterConvergenceThreshold = 0.001;
}
