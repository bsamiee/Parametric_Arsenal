using System.Collections.Frozen;

namespace Arsenal.Rhino.Spatial;

/// <summary>Configuration constants and parameters for spatial algorithms.</summary>
internal static class SpatialConfig {
    internal const int DefaultBufferSize = 2048;
    internal const int LargeBufferSize = 4096;

    /// <summary>K-means convergence: max 100 iterations, tolerance from context.</summary>
    internal const int KMeansMaxIterations = 100;

    /// <summary>K-means++ uses seed 42 for deterministic initialization.</summary>
    internal const int KMeansSeed = 42;

    /// <summary>DBSCAN minimum 4 points to form core cluster.</summary>
    internal const int DBSCANMinPoints = 4;

    /// <summary>Medial axis offset distance multiplier for skeleton computation.</summary>
    internal const double MedialAxisOffsetMultiplier = 10.0;

    /// <summary>DBSCAN uses RTree spatial indexing when point count exceeds this threshold.</summary>
    internal const int DBSCANRTreeThreshold = 100;

    /// <summary>Clustering algorithm identifiers: 0=KMeans++, 1=DBSCAN, 2=Hierarchical.</summary>
    internal static readonly FrozenDictionary<byte, (int MaxIter, int MinPts)> ClusterParams =
        new Dictionary<byte, (int, int)> {
            [0] = (KMeansMaxIterations, 0),
            [1] = (0, DBSCANMinPoints),
            [2] = (0, 0),
        }.ToFrozenDictionary();
}
