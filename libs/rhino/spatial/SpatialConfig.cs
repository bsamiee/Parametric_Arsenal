using System.Collections.Frozen;
using Arsenal.Core.Context;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Consolidated configuration: 2 comprehensive FrozenDicts + essential constants.</summary>
internal static class SpatialConfig {
    // Essential algorithmic constants
    internal const int DefaultBufferSize = 2048;
    internal const int LargeBufferSize = 4096;
    internal const int KMeansMaxIterations = 100;
    internal const int KMeansSeed = 42;
    internal const int DBSCANMinPoints = 4;
    internal const double MedialAxisOffsetMultiplier = 10.0;
    internal const int DBSCANRTreeThreshold = 100;

    /// <summary>Type extractors: polymorphic dispatch for centroid, RTree factory, etc.</summary>
    internal static readonly FrozenDictionary<(string Operation, Type GeometryType), Func<object, object>> TypeExtractors =
        new Dictionary<(string, Type), Func<object, object>> {
            // Centroid extraction via mass properties
            [("Centroid", typeof(Curve))] = static g => g is Curve c ? (AreaMassProperties.Compute(c) is { Centroid: { IsValid: true } ct } ? ct : c.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(Surface))] = static g => g is Surface s ? (AreaMassProperties.Compute(s) is { Centroid: { IsValid: true } ct } ? ct : s.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(Brep))] = static g => g is Brep b ? (VolumeMassProperties.Compute(b) is { Centroid: { IsValid: true } ct } ? ct : b.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(Mesh))] = static g => g is Mesh m ? (VolumeMassProperties.Compute(m) is { Centroid: { IsValid: true } ct } ? ct : m.GetBoundingBox(accurate: false).Center) : Point3d.Origin,
            [("Centroid", typeof(GeometryBase))] = static g => g is GeometryBase gb ? gb.GetBoundingBox(accurate: false).Center : Point3d.Origin,
            // RTree factory construction
            [("RTreeFactory", typeof(Point3d[]))] = static s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(),
            [("RTreeFactory", typeof(PointCloud))] = static s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(),
            [("RTreeFactory", typeof(Mesh))] = static s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(),
            // Clustering algorithm dispatch
            [("ClusterAssign", typeof(void))] = static input => input is (byte alg, Point3d[] pts, int k, double eps, IGeometryContext ctx)
                ? alg switch {
                    0 => SpatialCompute.KMeansAssign(pts, k, ctx.AbsoluteTolerance, KMeansMaxIterations),
                    1 => SpatialCompute.DBSCANAssign(pts, eps, DBSCANMinPoints),
                    2 => SpatialCompute.HierarchicalAssign(pts, k),
                    _ => [],
                }
                : [],
        }.ToFrozenDictionary();
}
