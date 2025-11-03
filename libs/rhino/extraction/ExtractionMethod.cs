namespace Arsenal.Rhino.Extraction;

/// <summary>Point extraction strategies with bitwise combination for geometry-specific algorithm selection.</summary>
[Flags]
public enum ExtractionMethod {
    None = 0,
    Uniform = 1,
    Analytical = 2,
    Extremal = 4,
    Quadrant = 8,
    All = Uniform | Analytical | Extremal | Quadrant,
}
