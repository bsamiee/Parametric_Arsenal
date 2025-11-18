using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing via RTree and polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    /// <summary>Algebraic query request for Spatial.Analyze.</summary>
    public abstract record Query;

    /// <summary>Range query against a spherical region.</summary>
    public sealed record SphereQuery(Sphere Sphere) : Query;

    /// <summary>Range query against a bounding box.</summary>
    public sealed record BoundingBoxQuery(BoundingBox BoundingBox) : Query;

    /// <summary>Proximity search returning k nearest indices.</summary>
    public sealed record KNearestProximityQuery(Point3d[] Needles, int Count) : Query;

    /// <summary>Proximity search constrained by a maximum distance.</summary>
    public sealed record DistanceProximityQuery(Point3d[] Needles, double Distance) : Query;

    /// <summary>Mesh overlap search with adjustable tolerance.</summary>
    public sealed record MeshOverlapQuery(double AdditionalTolerance) : Query;

    /// <summary>Base clustering request.</summary>
    public abstract record ClusterRequest;

    /// <summary>K-means clustering request.</summary>
    public sealed record KMeansClusterRequest(int ClusterCount) : ClusterRequest;

    /// <summary>DBSCAN clustering request.</summary>
    public sealed record DbscanClusterRequest(double Epsilon) : ClusterRequest;

    /// <summary>Hierarchical clustering request.</summary>
    public sealed record HierarchicalClusterRequest(int ClusterCount) : ClusterRequest;

    /// <summary>Directional proximity field specification.</summary>
    public sealed record ProximityFieldRequest(Vector3d Direction, double MaxDistance, double AngleWeight);

    /// <summary>Spatial query via type-based dispatch and RTree.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput>(
        TInput input,
        Query query,
        IGeometryContext context,
        int? bufferSize = null) where TInput : notnull =>
        SpatialCore.Analyze(input: input, query: query, context: context, bufferSizeOverride: bufferSize);

    /// <summary>Cluster geometry by proximity: (algorithm: 0=KMeans|1=DBSCAN|2=Hierarchical, k, epsilon) → (centroid, radii[])[].</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Point3d Centroid, double[] Radii)[]> Cluster<T>(
        T[] geometry,
        ClusterRequest request,
        IGeometryContext context) where T : GeometryBase =>
        SpatialCompute.Cluster(geometry: geometry, request: request, context: context);

    /// <summary>Compute medial axis skeleton for planar Breps → (skeleton curves[], stability[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(
        Brep brep,
        double tolerance,
        IGeometryContext context) =>
        SpatialCompute.MedialAxis(brep: brep, tolerance: tolerance, context: context);

    /// <summary>Compute directional proximity field: (direction, maxDistance, angleWeight) → (index, distance, angle)[].</summary>
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
