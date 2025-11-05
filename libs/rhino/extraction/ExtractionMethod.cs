namespace Arsenal.Rhino.Extraction;

/// <summary>Point extraction strategies with bitwise flags for geometry-specific algorithms.</summary>
[Flags]
public enum ExtractionMethod {
    None = 0,
    Uniform = 1,
    Analytical = 2,
    Extremal = 4,
    Quadrant = 8,
    EdgeMidpoints = 16,
    Greville = 32,
    Inflection = 64,
    Discontinuities = 128,
    FaceCentroids = 256,
    PositionalExtrema = 512,
    All = Uniform | Analytical | Extremal | Quadrant | EdgeMidpoints | Greville | Inflection | Discontinuities | FaceCentroids | PositionalExtrema,
}
