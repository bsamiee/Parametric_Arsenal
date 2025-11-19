using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing via RTree and polymorphic dispatch with algebraic domain types.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    /// <summary>Base type for all spatial analysis requests.</summary>
    public abstract record AnalysisRequest;

    /// <summary>Range query request: finds indices of items within a geometric range (sphere or bounding box).</summary>
    public sealed record RangeAnalysis<TInput>(TInput Input, RangeShape Shape, int? BufferSize = null) : AnalysisRequest where TInput : notnull;

    /// <summary>Proximity query request: finds k-nearest or distance-limited neighbors.</summary>
    public sealed record ProximityAnalysis<TInput>(TInput Input, ProximityQuery Query) : AnalysisRequest where TInput : notnull;

    /// <summary>Mesh face overlap query request: finds overlapping faces between two meshes.</summary>
    public sealed record MeshOverlapAnalysis(Mesh First, Mesh Second, double AdditionalTolerance = 0.0, int? BufferSize = null) : AnalysisRequest;

    /// <summary>Base type for range query shapes.</summary>
    public abstract record RangeShape;

    /// <summary>Spherical range query shape.</summary>
    public sealed record SphereRange(Sphere Sphere) : RangeShape;

    /// <summary>Bounding box range query shape.</summary>
    public sealed record BoundingBoxRange(BoundingBox Box) : RangeShape;

    /// <summary>Base type for proximity queries with needle points.</summary>
    public abstract record ProximityQuery(Point3d[] Needles);

    /// <summary>K-nearest neighbors proximity query.</summary>
    public sealed record KNearestProximity(Point3d[] Needles, int Count) : ProximityQuery(Needles);

    /// <summary>Distance-limited proximity query.</summary>
    public sealed record DistanceLimitedProximity(Point3d[] Needles, double Distance) : ProximityQuery(Needles);

    /// <summary>Base type for all clustering requests.</summary>
    public abstract record ClusterRequest;

    /// <summary>K-means clustering request.</summary>
    public sealed record KMeansRequest(int K) : ClusterRequest;

    /// <summary>DBSCAN density-based clustering request.</summary>
    public sealed record DBSCANRequest(double Epsilon, int MinPoints = 4) : ClusterRequest;

    /// <summary>Hierarchical agglomerative clustering request.</summary>
    public sealed record HierarchicalRequest(int K) : ClusterRequest;

    /// <summary>Clustering result containing cluster centroid and member distances.</summary>
    public sealed record ClusteringResult(Point3d Centroid, double[] Radii);

    /// <summary>Directional proximity field request for anisotropic proximity computation.</summary>
    public sealed record DirectionalProximityRequest(Vector3d Direction, double MaxDistance, double AngleWeight);

    /// <summary>Proximity field result entry.</summary>
    public sealed record ProximityFieldResult(int Index, double Distance, double Angle);

    /// <summary>Cluster geometry by proximity using algebraic clustering request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClusteringResult[]> Cluster<T>(
        T[] geometry,
        ClusterRequest request,
        IGeometryContext context) where T : GeometryBase =>
        SpatialCore.Cluster(geometry: geometry, request: request, context: context);

    /// <summary>Compute medial axis skeleton for planar Breps → (skeleton curves[], stability[]).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(
        Brep brep,
        double tolerance,
        IGeometryContext context) =>
        SpatialCompute.MedialAxis(brep: brep, tolerance: tolerance, context: context);

    /// <summary>Compute directional proximity field using algebraic request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ProximityFieldResult[]> ProximityField(
        GeometryBase[] geometry,
        DirectionalProximityRequest request,
        IGeometryContext context) =>
        SpatialCore.ProximityField(geometry: geometry, request: request, context: context);

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

    /// <summary>Spatial query via type-based dispatch and RTree.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        int? bufferSize = null) where TInput : notnull where TQuery : notnull =>
        SpatialCore.Analyze(input: input, query: query, context: context, bufferSize: bufferSize);
}
