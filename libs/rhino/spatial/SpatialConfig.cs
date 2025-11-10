using System.Collections.Frozen;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Configuration constants and FrozenDictionary dispatch for spatial algorithms.</summary>
internal static class SpatialConfig {
    internal const int DefaultBufferSize = 2048;
    internal const int LargeBufferSize = 4096;
    internal const int KMeansMaxIterations = 100;
    internal const int KMeansSeed = 42;
    internal const int DBSCANMinPoints = 4;
    internal const double MedialAxisOffsetMultiplier = 10.0;
    internal const int DBSCANRTreeThreshold = 100;

    /// <summary>Clustering algorithm metadata: byte id → (name, requires k, requires epsilon, maxIter, minPts, assign function).</summary>
    internal static readonly FrozenDictionary<byte, (string Name, bool RequiresK, bool RequiresEpsilon, int MaxIter, int MinPts, Func<Point3d[], int, double, (int MaxIter, int MinPts), IGeometryContext, int[]> Assign)> ClusterAlgorithms =
        new Dictionary<byte, (string, bool, bool, int, int, Func<Point3d[], int, double, (int MaxIter, int MinPts), IGeometryContext, int[]>)> {
            [0] = ("KMeans++", true, false, KMeansMaxIterations, 0, static (pts, k, _, config, ctx) => SpatialCompute.KMeansAssign(pts, k, ctx.AbsoluteTolerance, config.MaxIter)),
            [1] = ("DBSCAN", false, true, 0, DBSCANMinPoints, static (pts, _, eps, config, _) => SpatialCompute.DBSCANAssign(pts, eps, config.MinPts)),
            [2] = ("Hierarchical", true, false, 0, 0, static (pts, k, _, _, _) => SpatialCompute.HierarchicalAssign(pts, k)),
        }.ToFrozenDictionary();

    /// <summary>Geometry type → centroid extractor with mass properties fallback to bounding box.</summary>
    internal static readonly FrozenDictionary<Type, Func<GeometryBase, Point3d>> CentroidExtractors =
        new Dictionary<Type, Func<GeometryBase, Point3d>> {
            [typeof(Curve)] = static g => g is Curve c ? ((Func<Point3d>)(() => { using AreaMassProperties amp = AreaMassProperties.Compute(c); return amp?.Centroid ?? c.GetBoundingBox(accurate: false).Center; }))() : Point3d.Origin,
            [typeof(Surface)] = static g => g is Surface s ? ((Func<Point3d>)(() => { using AreaMassProperties amp = AreaMassProperties.Compute(s); return amp?.Centroid ?? s.GetBoundingBox(accurate: false).Center; }))() : Point3d.Origin,
            [typeof(Brep)] = static g => g is Brep b ? ((Func<Point3d>)(() => { using VolumeMassProperties vmp = VolumeMassProperties.Compute(b); return vmp?.Centroid ?? b.GetBoundingBox(accurate: false).Center; }))() : Point3d.Origin,
            [typeof(Mesh)] = static g => g is Mesh m ? ((Func<Point3d>)(() => { using VolumeMassProperties vmp = VolumeMassProperties.Compute(m); return vmp?.Centroid ?? m.GetBoundingBox(accurate: false).Center; }))() : Point3d.Origin,
            [typeof(GeometryBase)] = static g => g.GetBoundingBox(accurate: false).Center,
        }.ToFrozenDictionary();
}
