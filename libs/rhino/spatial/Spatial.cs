using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing with RTree algorithms and polymorphic dispatch.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "MA0049:Type name should not match containing namespace", Justification = "Spatial is the primary API entry point for the Spatial namespace")]
public static class Spatial {
    /// <summary>Result marker interface for polymorphic spatial result discrimination.</summary>
    public interface IResult {
        /// <summary>Centroid location in world coordinates.</summary>
        public Point3d Location { get; }
    }

    /// <summary>Clustering strategy for proximity-based grouping algorithms.</summary>
    public enum ClusteringStrategy : byte {
        /// <summary>K-means clustering with iterative centroid refinement.</summary>
        KMeans = 0,
        /// <summary>DBSCAN density-based clustering with epsilon parameter.</summary>
        DBSCAN = 1,
        /// <summary>Hierarchical agglomerative clustering with linkage criterion.</summary>
        Hierarchical = 2,
    }

    /// <summary>Spatial cluster with member indices, centroid, and bounding radius.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record SpatialCluster(
        int[] Members,
        Point3d Centroid,
        double Radius,
        int ClusterId) : IResult {
        /// <summary>Centroid location in world coordinates.</summary>
        public Point3d Location => this.Centroid;
        [Pure] private string DebuggerDisplay => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Cluster {this.ClusterId} | Members={this.Members.Length} | R={this.Radius:F3}");
    }

    /// <summary>Medial axis computation options with tolerance and planarity constraints.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct MedialAxisOptions(
        double Tolerance = 0.001,
        bool PlanarOnly = true);

    /// <summary>Medial axis result with skeleton curves and stability measures.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record MedialAxisResult(
        Curve[] Skeleton,
        double[] Stability,
        Point3d Centroid) : IResult {
        /// <summary>Centroid location in world coordinates.</summary>
        public Point3d Location => this.Centroid;
        [Pure] private string DebuggerDisplay => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MedialAxis | Curves={this.Skeleton.Length} | Stability={this.Stability.Average():F3}");
    }

    /// <summary>Proximity field options with max distance and angular weighting.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct ProximityOptions(
        double MaxDistance = 10.0,
        double AngleWeight = 0.5);

    /// <summary>Proximity field result with directional query data.</summary>
    [System.Diagnostics.DebuggerDisplay("{DebuggerDisplay}")]
    public sealed record ProximityField(
        (int Index, double Distance, double Angle)[] Results,
        Vector3d Direction,
        Point3d Centroid) : IResult {
        /// <summary>Centroid location in world coordinates.</summary>
        public Point3d Location => this.Centroid;
        [Pure] private string DebuggerDisplay => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"ProximityField | Results={this.Results.Length} | Dir={this.Direction}");
    }

    /// <summary>RTree cache with automatic GC-aware cleanup.</summary>
    internal static readonly ConditionalWeakTable<object, RTree> TreeCache = [];

    /// <summary>Spatial query with type-based dispatch and RTree algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<int>> Analyze<TInput, TQuery>(
        TInput input,
        TQuery query,
        IGeometryContext context,
        int? bufferSize = null,
        bool enableDiagnostics = false) where TInput : notnull where TQuery : notnull =>
        SpatialCore.OperationRegistry.TryGetValue((typeof(TInput), typeof(TQuery)), out (Func<object, RTree>? _, V mode, int bufferSize, Func<object, object, IGeometryContext, int, Result<IReadOnlyList<int>>> execute) config) switch {
            true => UnifiedOperation.Apply(
                input: input,
                operation: (Func<TInput, Result<IReadOnlyList<int>>>)(item => config.execute(item, query, context, bufferSize ?? config.bufferSize)),
                config: new OperationConfig<TInput, int> {
                    Context = context,
                    ValidationMode = config.mode,
                    OperationName = $"Spatial.{typeof(TInput).Name}.{typeof(TQuery).Name}",
                    EnableDiagnostics = enableDiagnostics,
                }),
            false => ResultFactory.Create<IReadOnlyList<int>>(
                error: E.Spatial.UnsupportedTypeCombo.WithContext(
                    $"Input: {typeof(TInput).Name}, Query: {typeof(TQuery).Name}")),
        };

    /// <summary>Proximity-based clustering with k-means, DBSCAN, or hierarchical algorithms.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<SpatialCluster>> ClusterByProximity<T>(
        IReadOnlyList<T> geometry,
        ClusteringStrategy strategy,
        IGeometryContext context,
        int k = 3,
        double epsilon = 1.0,
        bool enableDiagnostics = false) where T : GeometryBase =>
        strategy switch {
            ClusteringStrategy.KMeans when k > 0 => SpatialCore.ClusterKMeans(geometry: geometry, k: k, context: context, enableDiagnostics: enableDiagnostics),
            ClusteringStrategy.DBSCAN when epsilon > 0 => SpatialCore.ClusterDBSCAN(geometry: geometry, epsilon: epsilon, context: context, enableDiagnostics: enableDiagnostics),
            ClusteringStrategy.Hierarchical when k > 0 => SpatialCore.ClusterHierarchical(geometry: geometry, k: k, context: context, enableDiagnostics: enableDiagnostics),
            ClusteringStrategy.KMeans or ClusteringStrategy.Hierarchical => ResultFactory.Create<IReadOnlyList<SpatialCluster>>(error: E.Spatial.InvalidClusterK),
            ClusteringStrategy.DBSCAN => ResultFactory.Create<IReadOnlyList<SpatialCluster>>(error: E.Spatial.InvalidEpsilon),
            _ => ResultFactory.Create<IReadOnlyList<SpatialCluster>>(error: E.Spatial.ClusteringFailed),
        };

    /// <summary>Compute medial axis skeleton for planar shapes with stability analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<MedialAxisResult> ComputeMedialAxis(
        Brep brep,
        MedialAxisOptions options,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        SpatialCore.ComputeMedialAxisInternal(brep: brep, options: options, context: context, enableDiagnostics: enableDiagnostics);

    /// <summary>Compute directional proximity field with angle-weighted distance queries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<ProximityField> ComputeProximityField(
        GeometryBase[] geometry,
        Vector3d direction,
        ProximityOptions options,
        IGeometryContext context,
        bool enableDiagnostics = false) =>
        direction.Length > context.AbsoluteTolerance
            ? SpatialCore.ComputeProximityFieldInternal(geometry: geometry, direction: direction, options: options, context: context, enableDiagnostics: enableDiagnostics)
            : ResultFactory.Create<ProximityField>(error: E.Spatial.InvalidDirection);
}
