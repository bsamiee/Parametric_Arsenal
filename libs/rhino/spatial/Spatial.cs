using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing via RTree and polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    public const int DefaultDBSCANMinPoints = SpatialConfig.DBSCANMinPoints;

    public abstract record Query;

    public sealed record SphereQuery(Sphere Sphere) : Query;

    public sealed record BoundingBoxQuery(BoundingBox BoundingBox) : Query;

    public sealed record KNearestNeighborsQuery(Point3d[] Points, int Count) : Query;

    public sealed record DistanceLimitedQuery(Point3d[] Points, double Distance) : Query;

    public sealed record MeshOverlapQuery(double AdditionalTolerance) : Query;

    public abstract record ClusteringRequest;

    public sealed record KMeansClusteringRequest(int ClusterCount) : ClusteringRequest;

    public sealed record HierarchicalClusteringRequest(int ClusterCount) : ClusteringRequest;

    public sealed record DBSCANClusteringRequest(double Epsilon, int MinPoints = DefaultDBSCANMinPoints) : ClusteringRequest;

    public sealed record ProximityFieldRequest(Vector3d Direction, double MaxDistance, double AngleWeight);

    /// <summary>Spatial query via type-based dispatch and RTree.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput>(
        TInput input,
        Query query,
        IGeometryContext context,
        int? bufferSize = null) where TInput : notnull =>
        SpatialCore.Analyze(
            input: input,
            query: query,
            context: context,
            bufferSize: bufferSize);

    /// <summary>Cluster geometry by proximity via algebraic request types.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Centroid, double[] Radii)[]> Cluster<T>(
        T[] geometry,
        ClusteringRequest request,
        IGeometryContext context) where T : GeometryBase =>
        SpatialCompute.Cluster(geometry: geometry, request: request, context: context);

    /// <summary>Compute medial axis skeleton for planar Breps → (skeleton curves[], stability[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(
        Brep brep,
        double tolerance,
        IGeometryContext context) =>
        SpatialCompute.MedialAxis(brep: brep, tolerance: tolerance, context: context);

    /// <summary>Compute directional proximity field via algebraic request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(int Index, double Distance, double Angle)[]> ProximityField(
        GeometryBase[] geometry,
        ProximityFieldRequest request,
        IGeometryContext context) =>
        SpatialCompute.ProximityField(geometry: geometry, request: request, context: context);

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
