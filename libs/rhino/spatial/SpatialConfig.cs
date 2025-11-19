using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial configuration: algorithmic constants and unified polymorphic dispatch table.</summary>
[Pure]
internal static class SpatialConfig {
    /// <summary>Unified spatial operation metadata with discriminators for tree building and operation type.</summary>
    internal sealed record SpatialOperationMetadata(
        V ValidationMode,
        string OperationName,
        int BufferSize,
        Func<GeometryBase, Point3d>? CentroidExtractor = null);

    /// <summary>Singular unified operation dispatch table: (InputType, OperationType) → metadata.</summary>
    internal static readonly FrozenDictionary<(Type InputType, string OperationType), SpatialOperationMetadata> Operations =
        new Dictionary<(Type, string), SpatialOperationMetadata> {
            [(typeof(Point3d[]), "Range")] = new(V.None, "Spatial.PointArray.Range", 2048),
            [(typeof(PointCloud), "Range")] = new(V.Standard, "Spatial.PointCloud.Range", 2048),
            [(typeof(Mesh), "Range")] = new(V.MeshSpecific, "Spatial.Mesh.Range", 2048),
            [(typeof(Curve[]), "Range")] = new(V.Degeneracy, "Spatial.CurveArray.Range", 2048),
            [(typeof(Surface[]), "Range")] = new(V.BoundingBox, "Spatial.SurfaceArray.Range", 2048),
            [(typeof(Brep[]), "Range")] = new(V.Topology, "Spatial.BrepArray.Range", 2048),
            [(typeof(Point3d[]), "Proximity")] = new(V.None, "Spatial.PointArray.Proximity", 2048),
            [(typeof(PointCloud), "Proximity")] = new(V.Standard, "Spatial.PointCloud.Proximity", 2048),
            [(typeof(Mesh), "Overlap")] = new(V.MeshSpecific, "Spatial.Mesh.Overlap", 4096),
            [(typeof(GeometryBase[]), "Clustering")] = new(V.Standard, "Spatial.Clustering", 2048),
            [(typeof(GeometryBase[]), "ProximityField")] = new(V.Standard, "Spatial.ProximityField", 2048),
            [(typeof(Curve), "Centroid")] = new(V.None, "Spatial.CentroidExtraction", 0, static g => g is Curve c ? (AreaMassProperties.Compute(c) is { Centroid: { IsValid: true } ct } ? ct : c.GetBoundingBox(accurate: false).Center) : Point3d.Origin),
            [(typeof(Surface), "Centroid")] = new(V.None, "Spatial.CentroidExtraction", 0, static g => g is Surface s ? (AreaMassProperties.Compute(s) is { Centroid: { IsValid: true } ct } ? ct : s.GetBoundingBox(accurate: false).Center) : Point3d.Origin),
            [(typeof(Brep), "Centroid")] = new(V.None, "Spatial.CentroidExtraction", 0, static g => g is Brep b ? (VolumeMassProperties.Compute(b) is { Centroid: { IsValid: true } ct } ? ct : b.GetBoundingBox(accurate: false).Center) : Point3d.Origin),
            [(typeof(Mesh), "Centroid")] = new(V.None, "Spatial.CentroidExtraction", 0, static g => g is Mesh m ? (VolumeMassProperties.Compute(m) is { Centroid: { IsValid: true } ct } ? ct : m.GetBoundingBox(accurate: false).Center) : Point3d.Origin),
            [(typeof(GeometryBase), "Centroid")] = new(V.None, "Spatial.CentroidExtraction", 0, static g => g.GetBoundingBox(accurate: false).Center),
        }.ToFrozenDictionary();

    /// <summary>K-means clustering algorithm parameters.</summary>
    internal const int KMeansMaxIterations = 100;
    internal const int KMeansSeed = 42;

    /// <summary>DBSCAN clustering algorithm parameters.</summary>
    internal const int DBSCANMinPoints = 4;
    internal const int DBSCANRTreeThreshold = 100;

    /// <summary>Medial axis sampling bounds for planar boundary analysis.</summary>
    internal const int MedialAxisMinSampleCount = 50;
    internal const int MedialAxisMaxSampleCount = 500;

    /// <summary>Delaunay triangulation super-triangle construction parameters.</summary>
    internal const double DelaunaySuperTriangleScale = 2.0;
    internal const double DelaunaySuperTriangleCenterWeight = 0.5;
}
