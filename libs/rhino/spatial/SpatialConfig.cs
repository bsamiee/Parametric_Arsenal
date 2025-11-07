namespace Arsenal.Rhino.Spatial;

/// <summary>Configuration constants for spatial indexing operations.</summary>
internal static class SpatialConfig {
    /// <summary>Default buffer size for RTree search operations.</summary>
    internal const int DefaultBufferSize = 2048;

    /// <summary>Large buffer size for mesh overlap and complex proximity queries.</summary>
    internal const int LargeBufferSize = 4096;
}
