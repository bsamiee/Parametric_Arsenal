namespace Arsenal.Rhino.Spatial;

/// <summary>Buffer size constants for RTree spatial queries.</summary>
internal static class SpatialConfig {
    /// <summary>2048-element buffer for sphere/box queries.</summary>
    internal const int DefaultBufferSize = 2048;

    /// <summary>4096-element buffer for mesh overlap/proximity queries.</summary>
    internal const int LargeBufferSize = 4096;
}
