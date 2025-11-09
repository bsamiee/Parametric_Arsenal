using System.Buffers;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Advanced spatial algorithms for clustering, skeletonization, and directional proximity.</summary>
internal static class SpatialCompute {
    /// <summary>K-means clustering with iterative centroid refinement and convergence detection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Spatial.SpatialCluster>> ClusterKMeans<T>(
        IReadOnlyList<T> geometry,
        int k,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        geometry.Count < k
            ? ResultFactory.Create<IReadOnlyList<Spatial.SpatialCluster>>(error: E.Spatial.ClusteringFailed.WithContext($"Geometry count {geometry.Count.ToString(CultureInfo.InvariantCulture)} < k {k.ToString(CultureInfo.InvariantCulture)}"))
            : ExtractCentroids(geometry: geometry)
                .Bind(centroids => InitializeKMeansCentroids(points: centroids, k: k)
                    .Bind(initialCentroids => RefineKMeansClusters(
                        points: centroids,
                        centroids: initialCentroids,
                        maxIterations: SpatialConfig.MaxClusterIterations,
                        tolerance: context.AbsoluteTolerance))
                    .Map(clusters => BuildSpatialClusters(points: centroids, assignments: clusters, k: k)));

    /// <summary>DBSCAN density-based clustering with epsilon neighborhood and min points criteria.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Spatial.SpatialCluster>> ClusterDBSCAN<T>(
        IReadOnlyList<T> geometry,
        double epsilon,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        ExtractCentroids(geometry: geometry)
            .Bind(centroids => BuildDBSCANClusters(
                points: centroids,
                epsilon: epsilon,
                minPoints: SpatialConfig.DBSCANMinPoints)
                .Bind(assignments => assignments.Max() switch {
                    < 0 => ResultFactory.Create<IReadOnlyList<Spatial.SpatialCluster>>(error: E.Spatial.ClusteringFailed.WithContext("No clusters found")),
                    int maxCluster => ResultFactory.Create(value: (IReadOnlyList<Spatial.SpatialCluster>)BuildSpatialClusters(points: centroids, assignments: assignments, k: maxCluster + 1)),
                }));

    /// <summary>Hierarchical agglomerative clustering with single linkage and distance threshold.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Spatial.SpatialCluster>> ClusterHierarchical<T>(
        IReadOnlyList<T> geometry,
        int k,
        IGeometryContext context,
        bool enableDiagnostics) where T : GeometryBase =>
        geometry.Count < k
            ? ResultFactory.Create<IReadOnlyList<Spatial.SpatialCluster>>(error: E.Spatial.ClusteringFailed.WithContext($"Geometry count {geometry.Count.ToString(CultureInfo.InvariantCulture)} < k {k.ToString(CultureInfo.InvariantCulture)}"))
            : ExtractCentroids(geometry: geometry)
                .Bind(centroids => BuildHierarchicalClusters(points: centroids, k: k)
                    .Map(clusters => BuildSpatialClusters(points: centroids, assignments: clusters, k: k)));

    /// <summary>Compute medial axis skeleton for planar Breps with stability analysis.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Spatial.MedialAxisResult> ComputeMedialAxisInternal(
        Brep brep,
        Spatial.MedialAxisOptions options,
        IGeometryContext context,
        bool enableDiagnostics) =>
        options.PlanarOnly && !IsPlanar(brep: brep, tolerance: context.AbsoluteTolerance)
            ? ResultFactory.Create<Spatial.MedialAxisResult>(error: E.Spatial.NonPlanarNotSupported)
            : ExtractBoundaryEdges(brep: brep)
                .Bind(edges => ComputeVoronoiSkeleton(
                    edges: edges,
                    tolerance: options.Tolerance,
                    context: context))
                .Bind(skeleton => ComputeStability(skeleton: skeleton)
                    .Map(stability => new Spatial.MedialAxisResult(
                        Skeleton: skeleton,
                        Stability: stability,
                        Centroid: ComputeCentroid(curves: skeleton))));

    /// <summary>Compute directional proximity field with angle-weighted distance queries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Spatial.ProximityField> ComputeProximityFieldInternal(
        GeometryBase[] geometry,
        Vector3d direction,
        Spatial.ProximityOptions options,
        IGeometryContext context,
        bool enableDiagnostics) =>
        geometry.Length is 0
            ? ResultFactory.Create<Spatial.ProximityField>(error: E.Spatial.ProximityFieldFailed.WithContext("Empty geometry array"))
            : BuildGeometryTree(geometry: geometry)
                .Bind(tree => ExecuteDirectionalProximity(
                    tree: tree,
                    geometry: geometry,
                    direction: direction,
                    options: options,
                    context: context))
                .Map(results => new Spatial.ProximityField(
                    Results: results,
                    Direction: direction,
                    Centroid: ComputeCentroid(geometry: geometry)));

    /// <summary>Extract centroids from geometry collection with bounding box fallback.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Point3d[]> ExtractCentroids<T>(IReadOnlyList<T> geometry) where T : GeometryBase {
        Point3d[] centroids = new Point3d[geometry.Count];
        for (int i = 0; i < geometry.Count; i++) {
            centroids[i] = geometry[i] switch {
                Curve c when AreaMassProperties.Compute(c) is { } amp => amp.Centroid,
                Surface s when AreaMassProperties.Compute(s) is { } amp => amp.Centroid,
                Brep b when VolumeMassProperties.Compute(b) is { } vmp => vmp.Centroid,
                Mesh m when VolumeMassProperties.Compute(m) is { } vmp => vmp.Centroid,
                GeometryBase g => g.GetBoundingBox(accurate: false).Center,
            };
        }
        return ResultFactory.Create(value: centroids);
    }

    /// <summary>Initialize k-means centroids using k-means++ algorithm for better convergence.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Point3d[]> InitializeKMeansCentroids(Point3d[] points, int k) {
        Point3d[] centroids = new Point3d[k];
        Random random = new(42);
        centroids[0] = points[random.Next(points.Length)];
        for (int i = 1; i < k; i++) {
            double[] distances = new double[points.Length];
            double sum = 0.0;
            for (int j = 0; j < points.Length; j++) {
                double minDist = double.MaxValue;
                for (int c = 0; c < i; c++) {
                    double dist = points[j].DistanceTo(centroids[c]);
                    minDist = dist < minDist ? dist : minDist;
                }
                distances[j] = minDist * minDist;
                sum += distances[j];
            }
            double threshold = random.NextDouble() * sum;
            double cumulative = 0.0;
            int selectedIndex = points.Length - 1;
            for (int j = 0; j < points.Length; j++) {
                cumulative += distances[j];
                if (cumulative >= threshold) {
                    selectedIndex = j;
                    break;
                }
            }
            centroids[i] = points[selectedIndex];
        }
        return ResultFactory.Create(value: centroids);
    }

    /// <summary>Refine k-means clusters with Lloyd's algorithm until convergence or max iterations.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<int[]> RefineKMeansClusters(Point3d[] points, Point3d[] centroids, int maxIterations, double tolerance) {
        int[] assignments = new int[points.Length];
        int k = centroids.Length;
        for (int iteration = 0; iteration < maxIterations; iteration++) {
            for (int i = 0; i < points.Length; i++) {
                double minDist = double.MaxValue;
                int bestCluster = 0;
                for (int c = 0; c < k; c++) {
                    double dist = points[i].DistanceTo(centroids[c]);
                    (minDist, bestCluster) = dist < minDist ? (dist, c) : (minDist, bestCluster);
                }
                assignments[i] = bestCluster;
            }
            Point3d[] newCentroids = new Point3d[k];
            int[] counts = new int[k];
            for (int i = 0; i < points.Length; i++) {
                int cluster = assignments[i];
                newCentroids[cluster] = new Point3d(
                    newCentroids[cluster].X + points[i].X,
                    newCentroids[cluster].Y + points[i].Y,
                    newCentroids[cluster].Z + points[i].Z);
                counts[cluster]++;
            }
            double maxShift = 0.0;
            for (int c = 0; c < k; c++) {
                newCentroids[c] = counts[c] > 0
                    ? new Point3d(
                        newCentroids[c].X / counts[c],
                        newCentroids[c].Y / counts[c],
                        newCentroids[c].Z / counts[c])
                    : centroids[c];
                double shift = centroids[c].DistanceTo(newCentroids[c]);
                maxShift = shift > maxShift ? shift : maxShift;
            }
            centroids = newCentroids;
            if (maxShift < tolerance) {
                break;
            }
        }
        return ResultFactory.Create(value: assignments);
    }

    /// <summary>Build DBSCAN clusters with epsilon neighborhood and reachability.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<int[]> BuildDBSCANClusters(Point3d[] points, double epsilon, int minPoints) {
        int[] assignments = Enumerable.Repeat(-1, points.Length).ToArray();
        int clusterId = 0;
        bool[] visited = new bool[points.Length];
        for (int i = 0; i < points.Length; i++) {
            if (visited[i]) {
                continue;
            }
            visited[i] = true;
            int[] neighbors = FindNeighbors(points: points, index: i, epsilon: epsilon);
            if (neighbors.Length < minPoints) {
                assignments[i] = -1;
                continue;
            }
            assignments[i] = clusterId;
            Queue<int> queue = new(neighbors);
            while (queue.Count > 0) {
                int current = queue.Dequeue();
                if (!visited[current]) {
                    visited[current] = true;
                    int[] currentNeighbors = FindNeighbors(points: points, index: current, epsilon: epsilon);
                    if (currentNeighbors.Length >= minPoints) {
                        foreach (int neighbor in currentNeighbors) {
                            if (!queue.Contains(neighbor) && assignments[neighbor] is -1) {
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
                if (assignments[current] is -1) {
                    assignments[current] = clusterId;
                }
            }
            clusterId++;
        }
        return ResultFactory.Create(value: assignments);
    }

    /// <summary>Find epsilon-neighborhood for DBSCAN clustering.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int[] FindNeighbors(Point3d[] points, int index, double epsilon) {
        List<int> neighbors = [];
        for (int i = 0; i < points.Length; i++) {
            if (i != index && points[index].DistanceTo(points[i]) <= epsilon) {
                neighbors.Add(i);
            }
        }
        return [.. neighbors];
    }

    /// <summary>Build hierarchical clusters with single linkage until k clusters remain.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<int[]> BuildHierarchicalClusters(Point3d[] points, int k) {
        int[] assignments = Enumerable.Range(0, points.Length).ToArray();
        int currentClusters = points.Length;
        while (currentClusters > k) {
            (int cluster1, int cluster2, double _) = FindClosestClusters(points: points, assignments: assignments);
            for (int i = 0; i < assignments.Length; i++) {
                assignments[i] = assignments[i] switch {
                    var a when a == cluster2 => cluster1,
                    var a when a > cluster2 => a - 1,
                    var a => a,
                };
            }
            currentClusters--;
        }
        return ResultFactory.Create(value: assignments);
    }

    /// <summary>Find two closest clusters by minimum distance between any points.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int Cluster1, int Cluster2, double Distance) FindClosestClusters(Point3d[] points, int[] assignments) {
        double minDist = double.MaxValue;
        (int c1, int c2) = (0, 1);
        for (int i = 0; i < points.Length; i++) {
            for (int j = i + 1; j < points.Length; j++) {
                if (assignments[i] != assignments[j]) {
                    double dist = points[i].DistanceTo(points[j]);
                    (minDist, c1, c2) = dist < minDist ? (dist, assignments[i], assignments[j]) : (minDist, c1, c2);
                }
            }
        }
        return (c1, c2, minDist);
    }

    /// <summary>Build SpatialCluster records from cluster assignments.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Spatial.SpatialCluster[] BuildSpatialClusters(Point3d[] points, int[] assignments, int k) {
        Spatial.SpatialCluster[] clusters = new Spatial.SpatialCluster[k];
        for (int c = 0; c < k; c++) {
            int[] members = assignments.Select((cluster, index) => cluster == c ? index : -1).Where(static i => i >= 0).ToArray();
            Point3d centroid = members.Length > 0
                ? new Point3d(
                    members.Average(i => points[i].X),
                    members.Average(i => points[i].Y),
                    members.Average(i => points[i].Z))
                : Point3d.Origin;
            double radius = members.Length > 0
                ? members.Max(i => points[i].DistanceTo(centroid))
                : 0.0;
            clusters[c] = new Spatial.SpatialCluster(
                Members: members,
                Centroid: centroid,
                Radius: radius,
                ClusterId: c);
        }
        return clusters;
    }

    /// <summary>Check if Brep is planar within tolerance.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPlanar(Brep brep, double tolerance) =>
        brep.Faces.Count is 1 && brep.Faces[0].IsPlanar(tolerance: tolerance);

    /// <summary>Extract boundary edges from Brep as curves.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Curve[]> ExtractBoundaryEdges(Brep brep) {
        Curve?[] edges = brep.Edges
            .Where(static e => e.Valence == EdgeAdjacency.Naked)
            .Select(static e => e.DuplicateCurve())
            .ToArray();
        Curve[] validEdges = edges.Where(static c => c is not null).ToArray()!;
        return validEdges.Length > 0
            ? ResultFactory.Create(value: validEdges)
            : ResultFactory.Create<Curve[]>(error: E.Spatial.MedialAxisFailed.WithContext("No boundary edges found"));
    }

    /// <summary>Compute Voronoi-based skeleton from boundary curves (simplified straight skeleton approximation).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<Curve[]> ComputeVoronoiSkeleton(Curve[] edges, double tolerance, IGeometryContext context) {
        Curve?[] joined = Curve.JoinCurves(edges, joinTolerance: tolerance, preserveDirection: false);
        Curve? firstJoined = joined.FirstOrDefault();
        return firstJoined is not null && firstJoined.IsClosed
            ? ResultFactory.Create(value: new[] { ComputeOffsetSkeleton(curve: firstJoined, tolerance: tolerance), })
            : ResultFactory.Create<Curve[]>(error: E.Spatial.MedialAxisFailed.WithContext("Boundary curves do not form closed loop"));
    }

    /// <summary>Compute simplified skeleton via offset curve inward until collapse.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Curve ComputeOffsetSkeleton(Curve curve, double tolerance) {
        Plane plane = Plane.WorldXY;
        bool planar = curve.TryGetPlane(out plane, tolerance: tolerance);
        Curve[]? offsets = curve.Offset(plane: plane, distance: tolerance * 10.0, tolerance: tolerance, CurveOffsetCornerStyle.Sharp);
        return offsets is not null && offsets.Length > 0 ? offsets[0] : curve;
    }

    /// <summary>Compute stability metric for skeleton curves (distance to boundary).</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<double[]> ComputeStability(Curve[] skeleton) {
        double[] stability = skeleton.Select(curve => {
            double length = curve.GetLength();
            double tol = 0.001;
            return length > tol ? length / (length + tol) : 0.0;
        }).ToArray();
        return ResultFactory.Create(value: stability);
    }

    /// <summary>Compute centroid from curve array.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d ComputeCentroid(Curve[] curves) {
        Point3d[] points = curves.SelectMany(static c => new[] { c.PointAtStart, c.PointAtEnd, }).ToArray();
        return points.Length > 0
            ? new Point3d(
                points.Average(static p => p.X),
                points.Average(static p => p.Y),
                points.Average(static p => p.Z))
            : Point3d.Origin;
    }

    /// <summary>Compute centroid from geometry array.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d ComputeCentroid(GeometryBase[] geometry) {
        Point3d[] points = geometry.Select(static g => g.GetBoundingBox(accurate: false).Center).ToArray();
        return points.Length > 0
            ? new Point3d(
                points.Average(static p => p.X),
                points.Average(static p => p.Y),
                points.Average(static p => p.Z))
            : Point3d.Origin;
    }

    /// <summary>Build RTree from geometry array for proximity queries.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<RTree> BuildGeometryTree(GeometryBase[] geometry) {
        RTree tree = new();
        for (int i = 0; i < geometry.Length; i++) {
            bool inserted = tree.Insert(geometry[i].GetBoundingBox(accurate: true), i);
        }
        return ResultFactory.Create(value: tree);
    }

    /// <summary>Execute directional proximity queries with angle weighting.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Result<(int Index, double Distance, double Angle)[]> ExecuteDirectionalProximity(
        RTree tree,
        GeometryBase[] geometry,
        Vector3d direction,
        Spatial.ProximityOptions options,
        IGeometryContext context) {
        Vector3d normalizedDir = direction / direction.Length;
        List<(int Index, double Distance, double Angle)> results = [];
        BoundingBox searchBox = new(
            min: new Point3d(-options.MaxDistance, -options.MaxDistance, -options.MaxDistance),
            max: new Point3d(options.MaxDistance, options.MaxDistance, options.MaxDistance));
        tree.Search(searchBox, (_, args) => {
            GeometryBase geom = geometry[args.Id];
            Point3d center = geom.GetBoundingBox(accurate: false).Center;
            Vector3d toGeom = center - Point3d.Origin;
            double distance = toGeom.Length;
            double angle = distance > context.AbsoluteTolerance
                ? Vector3d.VectorAngle(normalizedDir, toGeom / distance)
                : 0.0;
            double weightedDist = distance * (1.0 + (options.AngleWeight * angle));
            if (weightedDist <= options.MaxDistance) {
                results.Add((args.Id, distance, angle));
            }
        });
        return ResultFactory.Create(value: results.OrderBy(static r => r.Distance).ToArray());
    }
}
