using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing, clustering, and tessellation APIs with algebraic configuration.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    /// <summary>Base request type for spatial operations.</summary>
    public abstract record Request;

    /// <summary>Base request for clustering operations.</summary>
    public abstract record ClusteringRequest(GeometryBase[] Geometry) : Request;

    /// <summary>RTree spatial query request.</summary>
    public sealed record RTreeQueryRequest(QuerySource Source, Query Query, int? BufferSize = null) : Request;

    /// <summary>K-means clustering configuration.</summary>
    public sealed record KMeansClusteringRequest(GeometryBase[] Geometry, int ClusterCount) : ClusteringRequest(Geometry);

    /// <summary>DBSCAN clustering configuration.</summary>
    public sealed record DBSCANClusteringRequest(GeometryBase[] Geometry, double Epsilon, int MinPoints) : ClusteringRequest(Geometry);

    /// <summary>Hierarchical clustering configuration.</summary>
    public sealed record HierarchicalClusteringRequest(GeometryBase[] Geometry, int ClusterCount) : ClusteringRequest(Geometry);

    /// <summary>Medial axis computation request.</summary>
    public sealed record MedialAxisRequest(Brep Brep, double Tolerance) : Request;

    /// <summary>Directional proximity field request.</summary>
    public sealed record ProximityFieldRequest(GeometryBase[] Geometry, Vector3d Direction, double MaxDistance, double AngleWeight) : Request;

    /// <summary>Convex hull computation request.</summary>
    public sealed record ConvexHull3DRequest(Point3d[] Points) : Request;

    /// <summary>Delaunay triangulation request.</summary>
    public sealed record DelaunayTriangulationRequest(Point3d[] Points) : Request;

    /// <summary>Voronoi diagram request.</summary>
    public sealed record VoronoiDiagramRequest(Point3d[] Points) : Request;

    /// <summary>Result record for clustering operations.</summary>
    public sealed record ClusterResult(Point3d Centroid, double[] Radii);

    /// <summary>Result record for directional proximity queries.</summary>
    public sealed record ProximitySample(int Index, double Distance, double Angle);

    /// <summary>Base type for spatial query sources.</summary>
    public abstract record QuerySource;

    /// <summary>Point array RTree source.</summary>
    public sealed record PointArraySource(Point3d[] Points) : QuerySource;

    /// <summary>Point cloud RTree source.</summary>
    public sealed record PointCloudSource(PointCloud PointCloud) : QuerySource;

    /// <summary>Mesh RTree source.</summary>
    public sealed record MeshSource(Mesh Mesh) : QuerySource;

    /// <summary>Mesh pair source for overlap detection.</summary>
    public sealed record MeshPairSource(Mesh First, Mesh Second) : QuerySource;

    /// <summary>Curve array RTree source.</summary>
    public sealed record CurveArraySource(Curve[] Curves) : QuerySource;

    /// <summary>Surface array RTree source.</summary>
    public sealed record SurfaceArraySource(Surface[] Surfaces) : QuerySource;

    /// <summary>Brep array RTree source.</summary>
    public sealed record BrepArraySource(Brep[] Breps) : QuerySource;

    /// <summary>Base type for spatial queries.</summary>
    public abstract record Query;

    /// <summary>Sphere range query.</summary>
    public sealed record RTreeSphereQuery(Sphere Sphere) : Query;

    /// <summary>Bounding box range query.</summary>
    public sealed record RTreeBoundingBoxQuery(BoundingBox BoundingBox) : Query;

    /// <summary>K-nearest neighborhood query.</summary>
    public sealed record KNearestQuery(Point3d[] Needles, int Count) : Query;

    /// <summary>Distance threshold proximity query.</summary>
    public sealed record DistanceThresholdQuery(Point3d[] Needles, double Distance) : Query;

    /// <summary>Mesh overlap query.</summary>
    public sealed record MeshOverlapQuery(double ExtraTolerance) : Query;

    /// <summary>Spatial query via algebraic source/query configuration.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Query(RTreeQueryRequest request, IGeometryContext context) =>
        SpatialCore.Query(request: request, context: context);

    /// <summary>Cluster geometry using the specified algebraic request.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ClusterResult[]> Cluster(ClusteringRequest request, IGeometryContext context) =>
        SpatialCore.Cluster(request: request, context: context);

    /// <summary>Compute medial axis skeleton for planar Breps.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(MedialAxisRequest request, IGeometryContext context) =>
        SpatialCore.MedialAxis(request: request, context: context);

    /// <summary>Compute directional proximity field.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ProximitySample[]> ProximityField(ProximityFieldRequest request, IGeometryContext context) =>
        SpatialCore.ProximityField(request: request, context: context);

    /// <summary>Compute 3D convex hull.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<int[][]> ConvexHull3D(ConvexHull3DRequest request, IGeometryContext context) =>
        SpatialCore.ConvexHull3D(request: request, context: context);

    /// <summary>Compute 2D Delaunay triangulation.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<int[][]> DelaunayTriangulation2D(DelaunayTriangulationRequest request, IGeometryContext context) =>
        SpatialCore.DelaunayTriangulation2D(request: request, context: context);

    /// <summary>Compute 2D Voronoi diagram.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Point3d[][]> VoronoiDiagram2D(VoronoiDiagramRequest request, IGeometryContext context) =>
        SpatialCore.VoronoiDiagram2D(request: request, context: context);
}
