using System.Collections.Frozen;
using System.Linq;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
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

    /// <summary>Mass-property centroid extractors keyed by geometry type hierarchy.</summary>
    internal static readonly FrozenDictionary<Type, Func<GeometryBase, Result<Point3d>>> CentroidExtractors =
        new Dictionary<Type, Func<GeometryBase, Result<Point3d>>> {
            [typeof(Curve)] = static geometry => geometry switch {
                Curve { IsValid: true } curve => AreaMassProperties.Compute(curve) switch {
                    { Centroid: { IsValid: true } centroid } => ResultFactory.Create(value: centroid),
                    _ => ResultFactory.Create<Point3d>(error: E.Spatial.ClusteringFailed.WithContext("Curve centroid invalid")),
                },
                _ => ResultFactory.Create<Point3d>(error: E.Validation.GeometryInvalid),
            },
            [typeof(Surface)] = static geometry => geometry switch {
                Surface { IsValid: true } surface => AreaMassProperties.Compute(surface) switch {
                    { Centroid: { IsValid: true } centroid } => ResultFactory.Create(value: centroid),
                    _ => ResultFactory.Create<Point3d>(error: E.Spatial.ClusteringFailed.WithContext("Surface centroid invalid")),
                },
                _ => ResultFactory.Create<Point3d>(error: E.Validation.GeometryInvalid),
            },
            [typeof(Brep)] = static geometry => geometry switch {
                Brep { IsValid: true } brep => VolumeMassProperties.Compute(brep) switch {
                    { Centroid: { IsValid: true } centroid } => ResultFactory.Create(value: centroid),
                    _ => ResultFactory.Create<Point3d>(error: E.Spatial.ClusteringFailed.WithContext("Brep centroid invalid")),
                },
                _ => ResultFactory.Create<Point3d>(error: E.Validation.GeometryInvalid),
            },
            [typeof(Mesh)] = static geometry => geometry switch {
                Mesh { IsValid: true } mesh => VolumeMassProperties.Compute(mesh) switch {
                    { Centroid: { IsValid: true } centroid } => ResultFactory.Create(value: centroid),
                    _ => ResultFactory.Create<Point3d>(error: E.Spatial.ClusteringFailed.WithContext("Mesh centroid invalid")),
                },
                _ => ResultFactory.Create<Point3d>(error: E.Validation.GeometryInvalid),
            },
            [typeof(GeometryBase)] = static geometry => geometry switch {
                GeometryBase { IsValid: true } baseGeometry => ResultFactory.Create(value: baseGeometry.GetBoundingBox(accurate: true).Center),
                _ => ResultFactory.Create<Point3d>(error: E.Validation.GeometryInvalid),
            },
        }.ToFrozenDictionary();

    /// <summary>RTree factories with validation-aware result creation.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, IGeometryContext, Result<RTree>>> RTreeFactories =
        new Dictionary<Type, Func<object, IGeometryContext, Result<RTree>>> {
            [typeof(Point3d[])] = static (source, _) => source switch {
                Point3d[] points when points.Length > 0 => ResultFactory.Create(value: RTree.CreateFromPointArray(points) ?? new RTree()),
                Point3d[] => ResultFactory.Create(value: new RTree()),
                _ => ResultFactory.Create<RTree>(error: E.Validation.GeometryInvalid),
            },
            [typeof(PointCloud)] = static (source, _) => source switch {
                PointCloud { Count: > 0 } cloud => ResultFactory.Create(value: RTree.CreatePointCloudTree(cloud) ?? new RTree()),
                PointCloud => ResultFactory.Create(value: new RTree()),
                _ => ResultFactory.Create<RTree>(error: E.Validation.GeometryInvalid),
            },
            [typeof(Mesh)] = static (source, _) => source switch {
                Mesh { IsValid: true } mesh => ResultFactory.Create(value: RTree.CreateMeshFaceTree(mesh) ?? new RTree()),
                _ => ResultFactory.Create<RTree>(error: E.Validation.GeometryInvalid),
            },
            [typeof(Curve[])] = static (source, _) => source switch {
                Curve[] curves => ResultFactory.Create(value: curves.Length == 0
                    ? new RTree()
                    : Enumerable.Range(0, curves.Length).Aggregate(new RTree(), (tree, index) => {
                        _ = tree.Insert(curves[index].GetBoundingBox(accurate: true), index);
                        return tree;
                    })),
                _ => ResultFactory.Create<RTree>(error: E.Validation.GeometryInvalid),
            },
            [typeof(Surface[])] = static (source, _) => source switch {
                Surface[] surfaces => ResultFactory.Create(value: surfaces.Length == 0
                    ? new RTree()
                    : Enumerable.Range(0, surfaces.Length).Aggregate(new RTree(), (tree, index) => {
                        _ = tree.Insert(surfaces[index].GetBoundingBox(accurate: true), index);
                        return tree;
                    })),
                _ => ResultFactory.Create<RTree>(error: E.Validation.GeometryInvalid),
            },
            [typeof(Brep[])] = static (source, _) => source switch {
                Brep[] breps => ResultFactory.Create(value: breps.Length == 0
                    ? new RTree()
                    : Enumerable.Range(0, breps.Length).Aggregate(new RTree(), (tree, index) => {
                        _ = tree.Insert(breps[index].GetBoundingBox(accurate: true), index);
                        return tree;
                    })),
                _ => ResultFactory.Create<RTree>(error: E.Validation.GeometryInvalid),
            },
        }.ToFrozenDictionary();

    /// <summary>Cluster assignment dispatch keyed by algorithm identifier.</summary>
    internal static readonly FrozenDictionary<byte, Func<(Point3d[] Points, int K, double Epsilon, IGeometryContext Context), Result<int[]>>> ClusterAssigners =
        new Dictionary<byte, Func<(Point3d[] Points, int K, double Epsilon, IGeometryContext Context), Result<int[]>>> {
            [0] = static parameters => ResultFactory.Create(value: SpatialCompute.KMeansAssign(parameters.Points, parameters.K, parameters.Context.AbsoluteTolerance, KMeansMaxIterations)),
            [1] = static parameters => ResultFactory.Create(value: SpatialCompute.DBSCANAssign(parameters.Points, parameters.Epsilon, DBSCANMinPoints)),
            [2] = static parameters => ResultFactory.Create(value: SpatialCompute.HierarchicalAssign(parameters.Points, parameters.K)),
        }.ToFrozenDictionary();
}
