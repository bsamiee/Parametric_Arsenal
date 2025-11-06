namespace Arsenal.Core.Validation;

/// <summary>Validation mode constants as ulong flags for bitwise combination without enum overhead.</summary>
public static class Modes {
    public const ulong None = 0UL;
    public const ulong Standard = 1UL;
    public const ulong AreaCentroid = 2UL;
    public const ulong BoundingBox = 4UL;
    public const ulong MassProperties = 8UL;
    public const ulong Topology = 16UL;
    public const ulong Degeneracy = 32UL;
    public const ulong Tolerance = 64UL;
    public const ulong SelfIntersection = 128UL;
    public const ulong MeshSpecific = 256UL;
    public const ulong SurfaceContinuity = 512UL;
    public const ulong All = Standard | AreaCentroid | BoundingBox | MassProperties | Topology | Degeneracy | Tolerance | SelfIntersection | MeshSpecific | SurfaceContinuity;
}
