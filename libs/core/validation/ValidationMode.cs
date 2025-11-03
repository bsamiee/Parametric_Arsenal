namespace Arsenal.Core.Validation;

/// <summary>Bitwise combinable validation modes for comprehensive geometric validation operations.</summary>
[Flags]
public enum ValidationMode {
    None = 0,
    Standard = 1,
    AreaCentroid = 2,
    BoundingBox = 4,
    MassProperties = 8,
    Topology = 16,
    Degeneracy = 32,
    Tolerance = 64,
    SelfIntersection = 128,
    MeshSpecific = 256,
    SurfaceContinuity = 512,
    All = Standard | AreaCentroid | BoundingBox | MassProperties | Topology | Degeneracy | Tolerance | SelfIntersection | MeshSpecific | SurfaceContinuity,
}
