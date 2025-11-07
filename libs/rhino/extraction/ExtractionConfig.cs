namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration constants for point extraction operations.</summary>
internal static class ExtractionConfig {
    /// <summary>Semantic extraction kind byte values mapped to validation requirements.</summary>
    internal const byte AnalyticalKind = 1;
    internal const byte ExtremalKind = 2;
    internal const byte GrevilleKind = 3;
    internal const byte InflectionKind = 4;
    internal const byte QuadrantKind = 5;
    internal const byte EdgeMidpointsKind = 6;
    internal const byte FaceCentroidsKind = 7;
}
