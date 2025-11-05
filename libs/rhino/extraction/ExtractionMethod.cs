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
    All = Uniform | Analytical | Extremal | Quadrant | EdgeMidpoints,
}
