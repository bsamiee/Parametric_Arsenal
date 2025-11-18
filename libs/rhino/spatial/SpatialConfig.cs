using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial configuration: algorithmic constants and polymorphic dispatch tables.</summary>
[Pure]
internal static class SpatialConfig {
    /// <summary>Polymorphic type extractors for centroids, RTree factories, and clustering dispatch.</summary>
    internal static readonly FrozenDictionary<(string Operation, Type GeometryType), Func<object, object>> TypeExtractors =
        new Dictionary<(string, Type), Func<object, object>> {
            [("Centroid", typeof(Curve))] = static g => g is Curve c ? (AreaMassProperties.Compute(c) is { Centroid: { IsValid: true } ct } ? ct : c.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(Surface))] = static g => g is Surface s ? (AreaMassProperties.Compute(s) is { Centroid: { IsValid: true } ct } ? ct : s.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(Brep))] = static g => g is Brep b ? (VolumeMassProperties.Compute(b) is { Centroid: { IsValid: true } ct } ? ct : b.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(Mesh))] = static g => g is Mesh m ? (VolumeMassProperties.Compute(m) is { Centroid: { IsValid: true } ct } ? ct : m.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(GeometryBase))] = static g => g is GeometryBase gb ? gb.GetBoundingBox(accurate: false).Center : Point3d.Origin,
            [("RTreeFactory", typeof(Point3d[]))] = static s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(),
            [("RTreeFactory", typeof(PointCloud))] = static s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(),
            [("RTreeFactory", typeof(Mesh))] = static s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(),
        }.ToFrozenDictionary();

    /// <summary>Buffer sizes for RTree spatial query operations.</summary>
    internal const int DefaultBufferSize = 2048;
    internal const int LargeBufferSize = 4096;

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

    /// <summary>Operation metadata per clustering strategy type.</summary>
    internal static readonly FrozenDictionary<Type, (V Mode, string OperationName)> ClusterOperationMetadata =
        new Dictionary<Type, (V, string)> {
            [typeof(Spatial.KMeansClusteringStrategy)] = (V.Standard | V.MassProperties, "Spatial.Cluster.KMeans"),
            [typeof(Spatial.DBSCANClusteringStrategy)] = (V.None, "Spatial.Cluster.DBSCAN"),
            [typeof(Spatial.HierarchicalClusteringStrategy)] = (V.Standard, "Spatial.Cluster.Hierarchical"),
        }.ToFrozenDictionary();

    /// <summary>Medial axis orchestration metadata.</summary>
    internal static readonly (V Mode, string OperationName) MedialAxisOperation = (V.Topology | V.Degeneracy, "Spatial.MedialAxis");

    /// <summary>Proximity field orchestration metadata.</summary>
    internal static readonly (V Mode, string OperationName) ProximityFieldOperation = (V.BoundingBox, "Spatial.ProximityField");

    /// <summary>Convex hull orchestration metadata.</summary>
    internal static readonly (V Mode, string OperationName) ConvexHull3DOperation = (V.None, "Spatial.ConvexHull3D");

    /// <summary>Delaunay orchestration metadata.</summary>
    internal static readonly (V Mode, string OperationName) DelaunayOperation = (V.None, "Spatial.DelaunayTriangulation2D");

    /// <summary>Voronoi orchestration metadata.</summary>
    internal static readonly (V Mode, string OperationName) VoronoiOperation = (V.None, "Spatial.VoronoiDiagram2D");

    /// <summary>Default empty medial axis skeleton.</summary>
    internal static readonly Spatial.MedialAxisSkeleton EmptyMedialAxis = new([], []);
}
