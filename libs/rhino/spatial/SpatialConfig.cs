using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial configuration: algorithmic constants and polymorphic dispatch tables.</summary>
[Pure]
internal static class SpatialConfig {
    /// <summary>Polymorphic centroid extractors for clustering operations.</summary>
    internal static readonly FrozenDictionary<Type, Func<GeometryBase, Point3d>> CentroidExtractors =
        new Dictionary<Type, Func<GeometryBase, Point3d>> {
            [typeof(Curve)] = static g => g is Curve c ? (AreaMassProperties.Compute(c) is { Centroid: { IsValid: true } ct } ? ct : c.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(Surface)] = static g => g is Surface s ? (AreaMassProperties.Compute(s) is { Centroid: { IsValid: true } ct } ? ct : s.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(Brep)] = static g => g is Brep b ? (VolumeMassProperties.Compute(b) is { Centroid: { IsValid: true } ct } ? ct : b.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(Mesh)] = static g => g is Mesh m ? (VolumeMassProperties.Compute(m) is { Centroid: { IsValid: true } ct } ? ct : m.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(GeometryBase)] = static g => g.GetBoundingBox(accurate: false).Center,
        }.ToFrozenDictionary();

    /// <summary>Operation names for diagnostics and error reporting.</summary>
    internal static class OperationNames {
        internal const string PointArrayRange = "Spatial.PointArray.Range";
        internal const string PointArrayProximity = "Spatial.PointArray.Proximity";
        internal const string PointCloudRange = "Spatial.PointCloud.Range";
        internal const string PointCloudProximity = "Spatial.PointCloud.Proximity";
        internal const string MeshRange = "Spatial.Mesh.Range";
        internal const string MeshOverlap = "Spatial.Mesh.Overlap";
        internal const string CurveArrayRange = "Spatial.CurveArray.Range";
        internal const string SurfaceArrayRange = "Spatial.SurfaceArray.Range";
        internal const string BrepArrayRange = "Spatial.BrepArray.Range";
        internal const string Clustering = "Spatial.Clustering";
        internal const string ProximityField = "Spatial.ProximityField";
    }

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
}
