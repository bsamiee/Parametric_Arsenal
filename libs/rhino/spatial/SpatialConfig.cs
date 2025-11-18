using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial configuration: algorithmic constants and polymorphic dispatch tables.</summary>
[Pure]
internal static class SpatialConfig {
    /// <summary>Centroid extractors keyed by geometry type for clustering operations.</summary>
    internal static readonly FrozenDictionary<Type, Func<GeometryBase, Point3d>> CentroidExtractors =
        new Dictionary<Type, Func<GeometryBase, Point3d>> {
            [typeof(Curve)] = static geometry => geometry is Curve curve
                ? AreaMassProperties.Compute(curve) is { Centroid: { IsValid: true } centroid }
                    ? centroid
                    : curve.GetBoundingBox(accurate: false).Center
                : Point3d.Origin,
            [typeof(Surface)] = static geometry => geometry is Surface surface
                ? AreaMassProperties.Compute(surface) is { Centroid: { IsValid: true } centroid }
                    ? centroid
                    : surface.GetBoundingBox(accurate: false).Center
                : Point3d.Origin,
            [typeof(Brep)] = static geometry => geometry is Brep brep
                ? VolumeMassProperties.Compute(brep) is { Centroid: { IsValid: true } centroid }
                    ? centroid
                    : brep.GetBoundingBox(accurate: false).Center
                : Point3d.Origin,
            [typeof(Mesh)] = static geometry => geometry is Mesh mesh
                ? VolumeMassProperties.Compute(mesh) is { Centroid: { IsValid: true } centroid }
                    ? centroid
                    : mesh.GetBoundingBox(accurate: false).Center
                : Point3d.Origin,
            [typeof(GeometryBase)] = static geometry => geometry.GetBoundingBox(accurate: false).Center,
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
}
