namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration constants for extraction operations.</summary>
internal static class ExtractionConfig {
    /// <summary>Default buffer size for point array allocations during extraction.</summary>
    internal const int DefaultBufferSize = 256;

    /// <summary>Large buffer size for operations with potentially many output points.</summary>
    internal const int LargeBufferSize = 1024;
}
