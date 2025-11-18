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
    /// <typeparam name="TInput">Type of input geometry (Point3d[], PointCloud, Mesh, Curve[], Surface[], Brep[]).</typeparam>
    /// <param name="Input">Input geometry to analyze.</param>
    /// <param name="Shape">Query shape (sphere or bounding box).</param>
    /// <param name="BufferSize">Optional buffer size for query results (default: 2048).</param>
    public sealed record RangeAnalysis<TInput>(TInput Input, RangeShape Shape, int? BufferSize = null) : AnalysisRequest where TInput : notnull;

    /// <summary>Proximity query request: finds k-nearest or distance-limited neighbors.</summary>
    /// <typeparam name="TInput">Type of input geometry (Point3d[] or PointCloud).</typeparam>
    /// <param name="Input">Input geometry to analyze.</param>
    /// <param name="Query">Proximity query (k-nearest or distance-limited).</param>
    public sealed record ProximityAnalysis<TInput>(TInput Input, ProximityQuery Query) : AnalysisRequest where TInput : notnull;

    /// <summary>Mesh face overlap query request: finds overlapping faces between two meshes.</summary>
    /// <param name="First">First mesh.</param>
    /// <param name="Second">Second mesh.</param>
    /// <param name="AdditionalTolerance">Additional tolerance beyond context tolerance (default: 0.0).</param>
    /// <param name="BufferSize">Optional buffer size for overlap results (default: 4096).</param>
    public sealed record MeshOverlapAnalysis(Mesh First, Mesh Second, double AdditionalTolerance = 0.0, int? BufferSize = null) : AnalysisRequest;

    /// <summary>Base type for range query shapes.</summary>
    public abstract record RangeShape;

    /// <summary>Spherical range query shape.</summary>
    /// <param name="Sphere">Sphere defining the query range.</param>
    public sealed record SphereRange(Sphere Sphere) : RangeShape;

    /// <summary>Bounding box range query shape.</summary>
    /// <param name="Box">Bounding box defining the query range.</param>
    public sealed record BoundingBoxRange(BoundingBox Box) : RangeShape;

    /// <summary>Base type for proximity queries with needle points.</summary>
    /// <param name="Needles">Query points to find neighbors for.</param>
    public abstract record ProximityQuery(Point3d[] Needles);

    /// <summary>K-nearest neighbors proximity query.</summary>
    /// <param name="Needles">Query points.</param>
    /// <param name="Count">Number of nearest neighbors to find per query point (k).</param>
    public sealed record KNearestProximity(Point3d[] Needles, int Count) : ProximityQuery(Needles);

    /// <summary>Distance-limited proximity query.</summary>
    /// <param name="Needles">Query points.</param>
    /// <param name="Distance">Maximum search distance from each query point.</param>
    public sealed record DistanceLimitedProximity(Point3d[] Needles, double Distance) : ProximityQuery(Needles);

    /// <summary>Base type for all clustering requests.</summary>
    public abstract record ClusterRequest;

    /// <summary>K-means clustering request.</summary>
    /// <param name="K">Number of clusters to create (must be positive and ≤ point count).</param>
    public sealed record KMeansRequest(int K) : ClusterRequest;

    /// <summary>DBSCAN density-based clustering request.</summary>
    /// <param name="Epsilon">Maximum distance between points in same cluster (must be positive).</param>
    /// <param name="MinPoints">Minimum points to form dense region (default: 4).</param>
    public sealed record DBSCANRequest(double Epsilon, int MinPoints = 4) : ClusterRequest;

    /// <summary>Hierarchical agglomerative clustering request.</summary>
    /// <param name="K">Number of clusters to create (must be positive and ≤ point count).</param>
    public sealed record HierarchicalRequest(int K) : ClusterRequest;

    /// <summary>Clustering result containing cluster centroid and member distances.</summary>
    /// <param name="Centroid">Cluster center point.</param>
    /// <param name="Radii">Distances from centroid to each cluster member.</param>
    public sealed record ClusteringResult(Point3d Centroid, double[] Radii);

    /// <summary>Directional proximity field request for anisotropic proximity computation.</summary>
    /// <param name="Direction">Preferred direction vector (must be non-zero).</param>
    /// <param name="MaxDistance">Maximum search distance.</param>
    /// <param name="AngleWeight">Weight for angular deviation penalty (0 = isotropic, >0 = anisotropic).</param>
    public sealed record DirectionalProximityRequest(Vector3d Direction, double MaxDistance, double AngleWeight);

    /// <summary>Proximity field result entry.</summary>
    /// <param name="Index">Geometry index in original array.</param>
    /// <param name="Distance">Euclidean distance.</param>
    /// <param name="Angle">Angular deviation from preferred direction (radians).</param>
    public sealed record ProximityFieldResult(int Index, double Distance, double Angle);

    /// <summary>Spatial query via type-based dispatch and RTree.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        int? bufferSize = null) where TInput : notnull where TQuery : notnull =>
        SpatialCore.Analyze(input: input, query: query, context: context, bufferSize: bufferSize);

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
}
