namespace Arsenal.Rhino.Analysis;

/// <summary>Analysis computation modes with bitwise composition for polymorphic dispatch.</summary>
[Flags]
public enum AnalysisMethod {
    None = 0,
    Point = 1,
    Derivatives = 2,
    Curvature = 4,
    Frame = 8,
    Discontinuity = 16,
    Topology = 32,
    Proximity = 64,
    Singularity = 128,
    Metrics = 256,
    Domains = 512,
    Standard = Point | Derivatives | Curvature | Frame | Metrics | Domains,
    Comprehensive = Standard | Discontinuity | Topology | Proximity | Singularity,
}
