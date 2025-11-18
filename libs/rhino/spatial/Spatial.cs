using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing via RTree and polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    public abstract record AnalysisRequest;

    public sealed record RangeAnalysis<TInput>(TInput Input, RangeShape Shape, int? BufferSize = null) : AnalysisRequest where TInput : notnull;

    public sealed record ProximityAnalysis<TInput>(TInput Input, ProximityQuery Query) : AnalysisRequest where TInput : notnull;

    public sealed record MeshOverlapAnalysis(Mesh First, Mesh Second, double AdditionalTolerance = 0.0, int? BufferSize = null) : AnalysisRequest;

    public abstract record RangeShape;

    public sealed record SphereRange(Sphere Sphere) : RangeShape;

    public sealed record BoundingBoxRange(BoundingBox Box) : RangeShape;

    public abstract record ProximityQuery(Point3d[] Needles);

    public sealed record KNearestProximity(Point3d[] Needles, int Count) : ProximityQuery(Needles);

    public sealed record DistanceLimitedProximity(Point3d[] Needles, double Distance) : ProximityQuery(Needles);

    public abstract record ClusterRequest;

    public sealed record KMeansCluster(int ClusterCount) : ClusterRequest;

    public sealed record DBSCANCluster(double Epsilon) : ClusterRequest;

    public sealed record HierarchicalCluster(int ClusterCount) : ClusterRequest;

    public sealed record ProximityFieldRequest(Vector3d Direction, double MaxDistance, double AngleWeight);

    /// <summary>Spatial query via type-based dispatch and RTree.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze(
        AnalysisRequest request,
        IGeometryContext context) =>
        SpatialCore.Analyze(request: request, context: context);

    /// <summary>Cluster geometry by proximity via algebraic requests → (centroid, radii[]).</summary>
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
