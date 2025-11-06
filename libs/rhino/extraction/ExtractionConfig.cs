using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Extraction operation configurations using discriminated unions via readonly record structs.</summary>
public abstract record ExtractionConfig {
    private ExtractionConfig() { }

    /// <summary>Uniform point distribution with count-based subdivision.</summary>
    public sealed record UniformByCount(int Count, bool IncludeEnds = true) : ExtractionConfig;

    /// <summary>Uniform point distribution with length-based subdivision.</summary>
    public sealed record UniformByLength(double Length, bool IncludeEnds = true) : ExtractionConfig;

    /// <summary>Analytical feature extraction (centroids, control points, vertices).</summary>
    public sealed record Analytical : ExtractionConfig;

    /// <summary>Extremal point extraction (endpoints, corners, bounding box).</summary>
    public sealed record Extremal : ExtractionConfig;

    /// <summary>Quadrant point extraction for circular/elliptical curves.</summary>
    public sealed record Quadrant : ExtractionConfig;

    /// <summary>Edge midpoint extraction from topological elements.</summary>
    public sealed record EdgeMidpoints : ExtractionConfig;

    /// <summary>Greville point extraction from NURBS geometry.</summary>
    public sealed record Greville : ExtractionConfig;

    /// <summary>Inflection point extraction from curves.</summary>
    public sealed record Inflection : ExtractionConfig;

    /// <summary>Discontinuity point extraction with continuity specification.</summary>
    public sealed record Discontinuities(Continuity Continuity = Continuity.C1_continuous) : ExtractionConfig;

    /// <summary>Face centroid extraction from Brep faces.</summary>
    public sealed record FaceCentroids : ExtractionConfig;

    /// <summary>Positional extrema extraction along specified direction.</summary>
    public sealed record PositionalExtrema(Vector3d Direction) : ExtractionConfig;
}
