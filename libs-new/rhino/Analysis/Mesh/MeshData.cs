namespace Arsenal.Rhino.Analysis.Mesh;

/// <summary>Mesh planarity analysis report.</summary>
public readonly record struct PlanarityReport(
    double MaxDeviation,
    double AverageDeviation,
    double PlanarityRatio,
    int[] NonPlanarFaces,
    int TotalFaces);

/// <summary>Mesh quality metrics.</summary>
public readonly record struct MeshMetrics(
    double MinEdgeLength,
    double MaxEdgeLength,
    double AverageEdgeLength,
    double MinFaceArea,
    double MaxFaceArea,
    double AverageFaceArea,
    double MinFaceAngle,
    double MaxFaceAngle,
    double AspectRatio);

/// <summary>Mesh validation result.</summary>
public readonly record struct MeshValidation(
    bool IsValid,
    string[] Issues);
