namespace Arsenal.Rhino.Spatial;

/// <summary>RTree buffer size constants for spatial query allocation strategies.</summary>
internal static class SpatialConfig {
    /// <summary>Standard buffer: 2048 elements for basic sphere/box queries.</summary>
    internal const int DefaultBufferSize = 2048;

    /// <summary>Extended buffer: 4096 elements for mesh overlap and proximity queries.</summary>
    internal const int LargeBufferSize = 4096;
}
