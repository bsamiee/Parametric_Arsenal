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
    /// <summary>Base type for clustering algorithm requests.</summary>
    public abstract record ClusteringRequest;

    /// <summary>K-means clustering with specified cluster count.</summary>
    public sealed record KMeansRequest(int K) : ClusteringRequest;

    /// <summary>DBSCAN density-based clustering with epsilon radius and minimum points.</summary>
    public sealed record DBSCANRequest(double Epsilon, int MinPoints) : ClusteringRequest;

    /// <summary>Hierarchical agglomerative clustering with target cluster count.</summary>
    public sealed record HierarchicalRequest(int K) : ClusteringRequest;

    /// <summary>Directional proximity field computation configuration.</summary>
    public sealed record ProximityFieldRequest(Vector3d Direction, double MaxDistance, double AngleWeight);
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

    /// <summary>Cluster geometry by proximity using specified clustering algorithm.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Centroid, double[] Radii)[]> Cluster<T>(
        T[] geometry,
        ClusteringRequest request,
        IGeometryContext context) where T : GeometryBase =>
        SpatialCore.ExecuteCluster(geometry: geometry, request: request, context: context);

    /// <summary>Compute medial axis skeleton for planar Breps → (skeleton curves[], stability[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(
        Brep brep,
        double tolerance,
        IGeometryContext context) =>
        SpatialCompute.MedialAxis(brep: brep, tolerance: tolerance, context: context);

    /// <summary>Compute directional proximity field with specified parameters.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(int Index, double Distance, double Angle)[]> ProximityField(
        GeometryBase[] geometry,
        ProximityFieldRequest request,
        IGeometryContext context) =>
        SpatialCompute.ProximityField(geometry: geometry, direction: request.Direction, maxDist: request.MaxDistance, angleWeight: request.AngleWeight, context: context);

    /// <summary>Compute 3D convex hull → mesh face vertex indices as int[][].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<int[][]> ConvexHull3D(
        Point3d[] points,
        IGeometryContext context) =>
        SpatialCompute.ConvexHull3D(points: points, context: context);

    /// <summary>Compute 2D Delaunay triangulation → triangle vertex indices as int[][].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<int[][]> DelaunayTriangulation2D(
        Point3d[] points,
        IGeometryContext context) =>
        SpatialCompute.DelaunayTriangulation2D(points: points, context: context);

    /// <summary>Compute 2D Voronoi diagram → cell vertices Point3d[][] for each input point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Point3d[][]> VoronoiDiagram2D(
        Point3d[] points,
        IGeometryContext context) =>
        SpatialCompute.VoronoiDiagram2D(points: points, context: context);
}
