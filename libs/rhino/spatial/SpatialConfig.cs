using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial configuration: algorithmic constants and polymorphic dispatch tables.</summary>
[Pure]
internal static class SpatialConfig {
    /// <summary>Range operation metadata: validation mode, operation name, buffer size.</summary>
    internal sealed record RangeOperationMetadata(
        V ValidationMode,
        string OperationName,
        int BufferSize);

    /// <summary>Proximity operation metadata: validation mode, operation name.</summary>
    internal sealed record ProximityOperationMetadata(
        V ValidationMode,
        string OperationName);

    /// <summary>Unified range operation configuration by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, RangeOperationMetadata> RangeOperations =
        new Dictionary<Type, RangeOperationMetadata> {
            [typeof(Point3d[])] = new(
                ValidationMode: V.None,
                OperationName: "Spatial.PointArray.Range",
                BufferSize: 2048),
            [typeof(PointCloud)] = new(
                ValidationMode: V.Standard,
                OperationName: "Spatial.PointCloud.Range",
                BufferSize: 2048),
            [typeof(Mesh)] = new(
                ValidationMode: V.MeshSpecific,
                OperationName: "Spatial.Mesh.Range",
                BufferSize: 2048),
            [typeof(Curve[])] = new(
                ValidationMode: V.Degeneracy,
                OperationName: "Spatial.CurveArray.Range",
                BufferSize: 2048),
            [typeof(Surface[])] = new(
                ValidationMode: V.BoundingBox,
                OperationName: "Spatial.SurfaceArray.Range",
                BufferSize: 2048),
            [typeof(Brep[])] = new(
                ValidationMode: V.Topology,
                OperationName: "Spatial.BrepArray.Range",
                BufferSize: 2048),
        }.ToFrozenDictionary();

    /// <summary>Unified proximity operation configuration by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, ProximityOperationMetadata> ProximityOperations =
        new Dictionary<Type, ProximityOperationMetadata> {
            [typeof(Point3d[])] = new(
                ValidationMode: V.None,
                OperationName: "Spatial.PointArray.Proximity"),
            [typeof(PointCloud)] = new(
                ValidationMode: V.Standard,
                OperationName: "Spatial.PointCloud.Proximity"),
        }.ToFrozenDictionary();

    /// <summary>Mesh overlap operation configuration.</summary>
    internal const string MeshOverlapOperationName = "Spatial.Mesh.Overlap";
    internal static readonly V MeshOverlapValidationMode = V.MeshSpecific;
    internal const int LargeBufferSize = 4096;

    /// <summary>Clustering operation configuration.</summary>
    internal const string ClusteringOperationName = "Spatial.Clustering";

    /// <summary>Proximity field operation configuration.</summary>
    internal const string ProximityFieldOperationName = "Spatial.ProximityField";

    /// <summary>Polymorphic centroid extractors for clustering operations.</summary>
    internal static readonly FrozenDictionary<Type, Func<GeometryBase, Point3d>> CentroidExtractors =
        new Dictionary<Type, Func<GeometryBase, Point3d>> {
            [typeof(Curve)] = static g => g is Curve c ? (AreaMassProperties.Compute(c) is { Centroid: { IsValid: true } ct } ? ct : c.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(Surface)] = static g => g is Surface s ? (AreaMassProperties.Compute(s) is { Centroid: { IsValid: true } ct } ? ct : s.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(Brep)] = static g => g is Brep b ? (VolumeMassProperties.Compute(b) is { Centroid: { IsValid: true } ct } ? ct : b.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(Mesh)] = static g => g is Mesh m ? (VolumeMassProperties.Compute(m) is { Centroid: { IsValid: true } ct } ? ct : m.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [typeof(GeometryBase)] = static g => g.GetBoundingBox(accurate: false).Center,
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
