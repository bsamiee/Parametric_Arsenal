using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Extraction operation configurations using discriminated unions via readonly record structs.</summary>
public abstract record ExtractionConfig {
    private ExtractionConfig() { }

    /// <summary>Uniform point distribution with count-based subdivision.</summary>
    public sealed record UniformByCount(int Count, bool IncludeEnds = true) : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Standard | ValidationMode.Degeneracy;
    }

    /// <summary>Uniform point distribution with length-based subdivision.</summary>
    public sealed record UniformByLength(double Length, bool IncludeEnds = true) : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Standard | ValidationMode.Degeneracy;
    }

    /// <summary>Analytical feature extraction (centroids, control points, vertices).</summary>
    public sealed record Analytical : ExtractionConfig {
        public static ValidationMode GetValidationMode(GeometryBase geometry) => geometry switch {
            Brep => ValidationMode.Standard | ValidationMode.MassProperties,
            Curve => ValidationMode.Standard | ValidationMode.AreaCentroid,
            Surface => ValidationMode.Standard | ValidationMode.AreaCentroid,
            _ => ValidationMode.Standard,
        };
    }

    /// <summary>Extremal point extraction (endpoints, corners, bounding box).</summary>
    public sealed record Extremal : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.BoundingBox;
    }

    /// <summary>Quadrant point extraction for circular/elliptical curves.</summary>
    public sealed record Quadrant : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Tolerance;
    }

    /// <summary>Edge midpoint extraction from topological elements.</summary>
    public sealed record EdgeMidpoints : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Standard | ValidationMode.Topology;
    }

    /// <summary>Greville point extraction from NURBS geometry.</summary>
    public sealed record Greville : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Standard;
    }

    /// <summary>Inflection point extraction from curves.</summary>
    public sealed record Inflection : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Standard | ValidationMode.Degeneracy;
    }

    /// <summary>Discontinuity point extraction with continuity specification.</summary>
    public sealed record Discontinuities(Continuity Continuity = Continuity.C1_continuous) : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Standard;
    }

    /// <summary>Face centroid extraction from Brep faces.</summary>
    public sealed record FaceCentroids : ExtractionConfig {
        public static ValidationMode ValidationMode => ValidationMode.Standard | ValidationMode.Topology;
    }

    /// <summary>Positional extrema extraction along specified direction.</summary>
    public sealed record PositionalExtrema(Vector3d Direction) : ExtractionConfig {
        public static ValidationMode GetValidationMode(GeometryBase geometry) => geometry switch {
            Surface or Brep => ValidationMode.Standard | ValidationMode.BoundingBox,
            Mesh => ValidationMode.Standard | ValidationMode.MeshSpecific,
            _ => ValidationMode.Standard,
        };
    }
}
