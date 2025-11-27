using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial configuration: algorithmic constants and unified polymorphic dispatch table.</summary>
[Pure] internal static class SpatialConfig {
    /// <summary>Operation type discriminators for dispatch table keys.</summary>
    internal const string OperationTypeRange = "Range";
    internal const string OperationTypeProximity = "Proximity";
    internal const string OperationTypeOverlap = "Overlap";
    internal const string OperationTypeClustering = "Clustering";
    internal const string OperationTypeProximityField = "ProximityField";

    /// <summary>Singular unified operation dispatch table: (InputType, OperationType) → metadata.</summary>
    internal static readonly FrozenDictionary<(Type InputType, string OperationType), SpatialOperationMetadata> Operations =
        new Dictionary<(Type, string), SpatialOperationMetadata> {
            [(typeof(Point3d[]), OperationTypeRange)] = new(V.None, "Spatial.PointArray.Range", 2048),
            [(typeof(PointCloud), OperationTypeRange)] = new(V.Standard, "Spatial.PointCloud.Range", 2048),
            [(typeof(Mesh), OperationTypeRange)] = new(V.MeshSpecific, "Spatial.Mesh.Range", 2048),
            [(typeof(Curve[]), OperationTypeRange)] = new(V.Degeneracy, "Spatial.CurveArray.Range", 2048),
            [(typeof(Surface[]), OperationTypeRange)] = new(V.BoundingBox, "Spatial.SurfaceArray.Range", 2048),
            [(typeof(Brep[]), OperationTypeRange)] = new(V.Topology, "Spatial.BrepArray.Range", 2048),
            [(typeof(Point3d[]), OperationTypeProximity)] = new(V.None, "Spatial.PointArray.Proximity", 2048),
            [(typeof(PointCloud), OperationTypeProximity)] = new(V.Standard, "Spatial.PointCloud.Proximity", 2048),
            [(typeof(Mesh), OperationTypeOverlap)] = new(V.MeshSpecific, "Spatial.Mesh.Overlap", 4096),
            [(typeof(GeometryBase[]), OperationTypeClustering)] = new(V.Standard, "Spatial.Clustering", 2048),
            [(typeof(GeometryBase[]), OperationTypeProximityField)] = new(V.Standard, "Spatial.ProximityField", 2048),
        }.ToFrozenDictionary();

    /// <summary>Spatial operation metadata for dispatch table.</summary>
    internal sealed record SpatialOperationMetadata(
        V ValidationMode,
        string OperationName,
        int BufferSize);

    /// <summary>DBSCAN clustering algorithm parameters.</summary>
    internal const int DBSCANRTreeThreshold = 100;

    /// <summary>K-means clustering algorithm parameters.</summary>
    internal const int KMeansMaxIterations = 100;
    internal const int KMeansSeed = 42;

    /// <summary>Medial axis sampling bounds for planar boundary analysis.</summary>
    internal const int MedialAxisMinSampleCount = 50;
    internal const int MedialAxisMaxSampleCount = 500;

    /// <summary>Delaunay triangulation super-triangle construction parameters.</summary>
    internal const double DelaunaySuperTriangleScale = 2.0;
    internal const double DelaunaySuperTriangleCenterWeight = 0.5;
}
