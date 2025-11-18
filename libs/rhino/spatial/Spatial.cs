using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing via RTree and polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    /// <summary>Base type for all spatial requests.</summary>
    public abstract record SpatialRequest;

    /// <summary>Aggregate request for clustering geometry collections.</summary>
    public sealed record ClusteringRequest(GeometryBase[] Geometry, ClusteringStrategy Strategy) : SpatialRequest;

    /// <summary>Request for planar Brep medial axis extraction.</summary>
    public sealed record MedialAxisRequest(Brep Brep, double Tolerance) : SpatialRequest;

    /// <summary>Request for weighted proximity field sampling.</summary>
    public sealed record ProximityFieldRequest(GeometryBase[] Geometry, Vector3d Direction, double MaxDistance, double AngleWeight) : SpatialRequest;

    /// <summary>Base type for clustering strategies.</summary>
    public abstract record ClusteringStrategy;

    /// <summary>K-means clustering with explicit cluster count.</summary>
    public sealed record KMeansClusteringStrategy(int ClusterCount) : ClusteringStrategy;

    /// <summary>Density-based spatial clustering with epsilon neighborhood.</summary>
    public sealed record DBSCANClusteringStrategy(double Epsilon, int MinimumPoints) : ClusteringStrategy;

    /// <summary>Hierarchical agglomerative clustering down to target clusters.</summary>
    public sealed record HierarchicalClusteringStrategy(int ClusterCount) : ClusteringStrategy;

    /// <summary>Cluster centroid + member radii summary.</summary>
    public sealed record ClusterProfile(Point3d Centroid, double[] Radii);

    /// <summary>Medial axis skeleton curves with per-edge stability.</summary>
    public sealed record MedialAxisSkeleton(Curve[] Skeleton, double[] Stability);

    /// <summary>Directional proximity field sample.</summary>
    public sealed record ProximitySample(int Index, double Distance, double Angle);

    /// <summary>Spatial query via type-based dispatch and RTree.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        int? bufferSize = null) where TInput : notnull where TQuery : notnull =>
        SpatialCore.OperationRegistry.TryGetValue((typeof(TInput), typeof(TQuery)), out (Func<object, RTree>? _, V mode, int bufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> execute) config) switch {
            true => UnifiedOperation.Apply(
                input: input,
                operation: (Func<TInput, Result<IReadOnlyList<int>>>)(item => config.execute(item, query, context, bufferSize ?? config.bufferSize)),
                config: new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.mode,
                    OperationName = $"Spatial.{typeof(TInput).Name}.{typeof(TQuery).Name}",
                    EnableDiagnostics = false,
                }),
            false => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };

    /// <summary>Cluster geometry by proximity using explicit strategy variants.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClusterProfile[]> Cluster<T>(
        T[] geometry,
        ClusteringStrategy strategy,
        IGeometryContext context) where T : GeometryBase =>
        SpatialCore.Cluster(
            request: new ClusteringRequest(Geometry: geometry, Strategy: strategy),
            context: context);

    /// <summary>Compute medial axis skeleton for planar Breps.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MedialAxisSkeleton> MedialAxis(
        MedialAxisRequest request,
        IGeometryContext context) =>
        SpatialCore.MedialAxis(request: request, context: context);

    /// <summary>Compute directional proximity field using strongly-typed configuration.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ProximitySample[]> ProximityField(
        ProximityFieldRequest request,
        IGeometryContext context) =>
        SpatialCore.ProximityField(request: request, context: context);

    /// <summary>Compute 3D convex hull → mesh face vertex indices as int[][].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<int[][]> ConvexHull3D(
        Point3d[] points,
        IGeometryContext context) =>
        SpatialCore.ConvexHull3D(points: points, context: context);

    /// <summary>Compute 2D Delaunay triangulation → triangle vertex indices as int[][].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<int[][]> DelaunayTriangulation2D(
        Point3d[] points,
        IGeometryContext context) =>
        SpatialCore.DelaunayTriangulation2D(points: points, context: context);

    /// <summary>Compute 2D Voronoi diagram → cell vertices Point3d[][] for each input point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Point3d[][]> VoronoiDiagram2D(
        Point3d[] points,
        IGeometryContext context) =>
        SpatialCore.VoronoiDiagram2D(points: points, context: context);
}
