using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Dense spatial algorithm implementations.</summary>
internal static class SpatialCompute {
    private static readonly IComparer<Type> _typeSpecificity = Comparer<Type>.Create(static (left, right) =>
        left == right ? 0 : left.IsAssignableFrom(right) ? 1 : right.IsAssignableFrom(left) ? -1 : 0);

    private static readonly (Type GeometryType, Func<object, object> Extractor)[] _centroidFallbacks =
        [.. SpatialConfig.TypeExtractors
            .Where(static kv => string.Equals(kv.Key.Operation, "Centroid", StringComparison.Ordinal))
            .OrderByDescending(static kv => kv.Key.GeometryType, _typeSpecificity)
            .Select(static kv => (kv.Key.GeometryType, kv.Value)),
        ];

    private static readonly ConcurrentDictionary<Type, Func<object, object>> _centroidExtractorCache = new();
    internal static Result<(Point3d, double[])[]> Cluster<T>(T[] geometry, byte algorithm, int k, double epsilon, IGeometryContext context) where T : GeometryBase =>
        (geometry.Length, algorithm, k, epsilon) switch {
            (0, _, _, _) => ResultFactory.Create<(Point3d, double[])[]>(error: E.Geometry.InvalidCount.WithContext("Cluster requires at least one geometry")),
            (_, > 2, _, _) => ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed.WithContext($"Unknown algorithm: {algorithm}")),
            (_, 0 or 2, <= 0, _) => ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.InvalidClusterK),
            (_, 1, _, <= 0) => ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.InvalidEpsilon),
            _ => ((Func<Result<(Point3d, double[])[]>>)(() => {
                Point3d[] pts = new Point3d[geometry.Length];
                for (int i = 0; i < geometry.Length; i++) {
                    GeometryBase current = geometry[i];
                    Type geometryType = current.GetType();
                    Func<object, object> extractor = _centroidExtractorCache.GetOrAdd(geometryType, ResolveCentroidExtractor);
                    pts[i] = (Point3d)extractor(current);
                }
                return (algorithm is 0 or 2) && k > pts.Length
                    ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.KExceedsPointCount)
                    : SpatialConfig.TypeExtractors.TryGetValue(("ClusterAssign", typeof(void)), out Func<object, object>? assignFunc) && assignFunc((algorithm, pts, k, epsilon, context)) is int[] assigns && assigns.Length > 0
                        ? (algorithm is 1 ? assigns.Where(a => a >= 0).DefaultIfEmpty(-1).Max() + 1 : k) is int clusterCount && clusterCount > 0
                            ? ResultFactory.Create<(Point3d, double[])[]>(value: [.. Enumerable.Range(0, clusterCount).Select(c => {
                                int[] members = [.. Enumerable.Range(0, pts.Length).Where(i => assigns[i] == c),];
                                return members.Length is 0
                                    ? (Point3d.Origin, Array.Empty<double>())
                                    : ((Func<(Point3d, double[])>)(() => {
                                        Vector3d sum = Vector3d.Zero;
                                        for (int memberIndex = 0; memberIndex < members.Length; memberIndex++) {
                                            Point3d point = pts[members[memberIndex]];
                                            sum += new Vector3d(point);
                                        }
                                        Point3d centroid = new(sum.X / members.Length, sum.Y / members.Length, sum.Z / members.Length);
                                        return (centroid, [.. members.Select(i => pts[i].DistanceTo(centroid)),]);
                                    }))();
                            }),
                            ])
                            : ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed)
                        : ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed);
            }))(),
        };

    internal static int[] KMeansAssign(Point3d[] pts, int k, double tol, int maxIter) {
        int[] assignments = new int[pts.Length];
        Point3d[] centroids = new Point3d[k];
        Random rng = new(SpatialConfig.KMeansSeed);
        double[] distSq = ArrayPool<double>.Shared.Rent(pts.Length);

        Point3d SelectWeighted(double sumWeights) {
            double target = rng.NextDouble() * sumWeights;
            double cumulative = 0.0;
            for (int j = 0; j < pts.Length; j++) {
                cumulative += distSq[j];
                if (cumulative >= target) {
                    return pts[j];
                }
            }
            return pts[^1];
        }

        try {
            // K-means++ initialization with squared distances
            centroids[0] = pts[rng.Next(pts.Length)];
            for (int i = 1; i < k; i++) {
                for (int j = 0; j < pts.Length; j++) {
                    double minDist = pts[j].DistanceTo(centroids[0]);
                    for (int c = 1; c < i; c++) {
                        double dist = pts[j].DistanceTo(centroids[c]);
                        minDist = dist < minDist ? dist : minDist;
                    }
                    distSq[j] = minDist * minDist;
                }

                double sum = 0.0;
                for (int j = 0; j < pts.Length; j++) {
                    sum += distSq[j];
                }

                centroids[i] = sum <= tol || sum <= 0.0
                    ? pts[rng.Next(pts.Length)]
                    : SelectWeighted(sum);
            }

            // Lloyd's algorithm with hot-path optimization
            (Vector3d Sum, int Count)[] clusters = new (Vector3d, int)[k];
            for (int iter = 0; iter < maxIter; iter++) {
                // Assign to nearest centroid (hot path - use for loops)
                for (int i = 0; i < pts.Length; i++) {
                    int nearest = 0;
                    double minDist = pts[i].DistanceTo(centroids[0]);
                    for (int j = 1; j < k; j++) {
                        double dist = pts[i].DistanceTo(centroids[j]);
                        (nearest, minDist) = dist < minDist ? (j, dist) : (nearest, minDist);
                    }
                    assignments[i] = nearest;
                }

                // Recompute centroids and check convergence
                for (int i = 0; i < k; i++) {
                    clusters[i] = (Vector3d.Zero, 0);
                }
                for (int i = 0; i < pts.Length; i++) {
                    int cluster = assignments[i];
                    clusters[cluster] = (clusters[cluster].Sum + new Vector3d(pts[i]), clusters[cluster].Count + 1);
                }

                double maxShift = 0.0;
                for (int i = 0; i < k; i++) {
                    Point3d newCentroid = clusters[i].Count > 0
                        ? new Point3d(
                            clusters[i].Sum.X / clusters[i].Count,
                            clusters[i].Sum.Y / clusters[i].Count,
                            clusters[i].Sum.Z / clusters[i].Count)
                        : centroids[i];
                    maxShift = Math.Max(maxShift, centroids[i].DistanceTo(newCentroid));
                    centroids[i] = newCentroid;
                }

                if (maxShift <= tol) {
                    break;
                }
            }

            return assignments;
        } finally {
            ArrayPool<double>.Shared.Return(distSq, clearArray: false);
        }
    }

    internal static int[] DBSCANAssign(Point3d[] pts, double eps, int minPts) {
        int[] assignments = [.. Enumerable.Repeat(-1, pts.Length),];
        bool[] visited = new bool[pts.Length];
        int clusterId = 0;

        // SDK RTree.CreateFromPointArray for O(log n) neighbor queries (vs O(n) linear scan) when pts.Length > 100
        using RTree? tree = pts.Length > SpatialConfig.DBSCANRTreeThreshold ? RTree.CreateFromPointArray(pts) : null;

        int[] GetNeighbors(int idx) => tree is not null
            ? ((Func<int[]>)(() => {
                List<int> buffer = [];
                _ = tree.Search(new Sphere(pts[idx], eps), (_, args) => {
                    if (args.Id != idx) {
                        buffer.Add(args.Id);
                    }
                });
                return [.. buffer,];
            }))()
            : [.. Enumerable.Range(0, pts.Length).Where(j => j != idx && pts[idx].DistanceTo(pts[j]) <= eps),];

        for (int i = 0; i < pts.Length; i++) {
            if (visited[i]) {
                continue;
            }

            visited[i] = true;
            int[] neighbors = GetNeighbors(i);

            if (neighbors.Length < minPts) {
                continue;
            }

            assignments[i] = clusterId;
            Queue<int> queue = new(neighbors);

            while (queue.Count > 0) {
                int cur = queue.Dequeue();
                if (visited[cur]) {
                    continue;
                }

                visited[cur] = true;
                int[] curNeighbors = GetNeighbors(cur);

                if (curNeighbors.Length >= minPts) {
                    for (int ni = 0; ni < curNeighbors.Length; ni++) {
                        int nb = curNeighbors[ni];
                        if (assignments[nb] == -1) {
                            queue.Enqueue(nb);
                        }
                    }
                }

                if (assignments[cur] == -1) {
                    assignments[cur] = clusterId;
                }
            }

            clusterId++;
        }

        return assignments;
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int[] HierarchicalAssign(Point3d[] pts, int k) =>
        ((Func<int[]>)(() => {
            int[] assignments = [.. Enumerable.Range(0, pts.Length),];
            int targetClusters = pts.Length - k;
            int[] clusterRepresentatives = [.. Enumerable.Range(0, pts.Length),];
            for (int iteration = 0; iteration < targetClusters; iteration++) {
                (int repr1, int repr2, double minDist) = (0, 1, double.MaxValue);
                for (int i = 0; i < pts.Length; i++) {
                    int cluster1 = assignments[i];
                    if (cluster1 != clusterRepresentatives[cluster1]) {
                        continue;
                    }
                    for (int j = i + 1; j < pts.Length; j++) {
                        int cluster2 = assignments[j];
                        if (cluster1 == cluster2 || cluster2 != clusterRepresentatives[cluster2]) {
                            continue;
                        }
                        double dist = pts[clusterRepresentatives[cluster1]].DistanceTo(pts[clusterRepresentatives[cluster2]]);
                        (repr1, repr2, minDist) = dist < minDist ? (cluster1, cluster2, dist) : (repr1, repr2, minDist);
                    }
                }
                for (int i = 0; i < assignments.Length; i++) {
                    assignments[i] = assignments[i] == repr2 ? repr1 : assignments[i] > repr2 ? assignments[i] - 1 : assignments[i];
                }
                if (repr2 < clusterRepresentatives.Length) {
                    clusterRepresentatives[repr2] = clusterRepresentatives[repr1];
                }
            }
            return assignments;
        }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Curve[], double[])> MedialAxis(Brep brep, double tolerance, IGeometryContext context) =>
        brep.Faces.Count is 0
            ? ResultFactory.Create<(Curve[], double[])>(error: E.Geometry.InvalidCount.WithContext("MedialAxis requires at least one face"))
            : (brep.Faces.Count is 1 && brep.Faces[0].IsPlanar(tolerance: context.AbsoluteTolerance), brep.Edges.Where(static e => e.Valence == EdgeAdjacency.Naked).Select(static e => e.DuplicateCurve()).Where(static c => c is not null).ToArray()) switch {
                (false, _) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.NonPlanarNotSupported),
                (true, { Length: 0 }) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("No boundary")),
                (true, Curve[] edges) when Curve.JoinCurves(edges, joinTolerance: Math.Max(tolerance, context.AbsoluteTolerance), preserveDirection: false).FirstOrDefault() is Curve boundary && boundary.IsClosed && boundary.TryGetPlane(out Plane plane, tolerance: Math.Max(tolerance, context.AbsoluteTolerance)) && boundary.GetLength() > context.AbsoluteTolerance => ((Func<Result<(Curve[], double[])>>)(() => {
                    double effectiveTolerance = Math.Max(tolerance, context.AbsoluteTolerance);
                    double length = boundary.GetLength();
                    int sampleCount = (int)RhinoMath.Clamp(length / effectiveTolerance, SpatialConfig.MedialAxisMinSampleCount, SpatialConfig.MedialAxisMaxSampleCount);
                    Transform toPlane = Transform.PlaneToPlane(plane, Plane.WorldXY);
                    Transform fromPlane = Transform.PlaneToPlane(Plane.WorldXY, plane);
                    Point3d To3D(Point3d p2d) { Point3d pt = p2d; pt.Transform(fromPlane); return pt; }
                    Point3d[] samples3D = [.. Enumerable.Range(0, sampleCount).Select(i => boundary.PointAtNormalizedLength((double)i / sampleCount)),];
                    Point3d[] samples2D = [.. samples3D.Select(p => { Point3d pt = p; pt.Transform(toPlane); return pt; }),];
                    return VoronoiDiagram2D(points: samples2D, context: context).Bind(cells => {
                        (Curve?, double)[] skeleton = [.. cells
                            .SelectMany((cell, _) => cell.Length > 0
                                ? Enumerable.Range(0, cell.Length).Select(j => (P1: cell[j], P2: cell[(j + 1) % cell.Length]))
                                : [])
                            .Select(edge => (edge.P1, edge.P2, Mid: Point3d.Interpolate(edge.P1, edge.P2, 0.5)))
                            .Select(edge => (edge.P1, edge.P2, edge.Mid, Mid3D: To3D(edge.Mid)))
                            .Where(edge => boundary.Contains(edge.Mid3D, plane, effectiveTolerance) is PointContainment.Inside)
                            .GroupBy(edge => (edge.P1, edge.P2) switch {
                                (Point3d p1, Point3d p2) when p1.X < p2.X - context.AbsoluteTolerance || (Math.Abs(p1.X - p2.X) <= context.AbsoluteTolerance && p1.Y < p2.Y - context.AbsoluteTolerance) => (Min: p1, Max: p2),
                                (Point3d p1, Point3d p2) => (Min: p2, Max: p1),
                            })
                            .Select(static g => g.First())
                            .Select(edge => (P1_3D: To3D(edge.P1), P2_3D: To3D(edge.P2), edge.Mid3D))
                            .Select(edge => boundary.ClosestPoint(edge.Mid3D, out double t)
                                ? (Curve: new LineCurve(edge.P1_3D, edge.P2_3D), Radius: edge.Mid3D.DistanceTo(boundary.PointAt(t)))
                                : (null, 0.0))
                            .Where(static pair => pair.Item1 is not null),
                        ];
                        return skeleton.Length > 0
                            ? ResultFactory.Create<(Curve[], double[])>(value: ([.. skeleton.Select(static s => s.Item1!),], [.. skeleton.Select(static s => s.Item2),]))
                            : ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("No skeleton edges inside boundary"));
                    });
                }))()
                ,
                _ => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("Boundary is not closed, planar, or degenerate")),
            };

    internal static Result<(int, double, double)[]> ProximityField(GeometryBase[] geometry, Vector3d direction, double maxDist, double angleWeight, IGeometryContext context) =>
        (geometry.Length, direction.Length <= context.AbsoluteTolerance, maxDist <= context.AbsoluteTolerance) switch {
            (0, _, _) => ResultFactory.Create<(int, double, double)[]>(error: E.Geometry.InvalidCount.WithContext("ProximityField requires at least one geometry")),
            (_, true, _) => ResultFactory.Create<(int, double, double)[]>(error: E.Spatial.ZeroLengthDirection),
            (_, _, true) => ResultFactory.Create<(int, double, double)[]>(error: E.Spatial.InvalidDistance.WithContext("MaxDistance must exceed tolerance")),
            _ => ((Func<Result<(int, double, double)[]>>)(() => {
                using RTree tree = new();
                BoundingBox bounds = BoundingBox.Empty;
                Point3d[] centers = new Point3d[geometry.Length];
                for (int i = 0; i < geometry.Length; i++) {
                    BoundingBox bbox = geometry[i].GetBoundingBox(accurate: true);
                    _ = tree.Insert(bbox, i);
                    bounds.Union(bbox);
                    centers[i] = bbox.Center;
                }
                Vector3d dir = direction / direction.Length;
                Point3d origin = bounds.Center;
                BoundingBox searchBox = new(origin - new Vector3d(maxDist, maxDist, maxDist), origin + new Vector3d(maxDist, maxDist, maxDist));
                List<(int, double, double)> results = [];
                _ = tree.Search(searchBox, (_, args) => {
                    Vector3d toGeom = centers[args.Id] - origin;
                    double dist = toGeom.Length;
                    double angle = dist > context.AbsoluteTolerance ? Vector3d.VectorAngle(dir, toGeom / dist) : 0.0;
                    double weightedDist = dist * (1.0 + (angleWeight * angle));
                    if (weightedDist <= maxDist) {
                        results.Add((args.Id, dist, angle));
                    }
                });
                return ResultFactory.Create(value: results.OrderBy(static r => r.Item2).ToArray());
            }))(),
        };

    /// <summary>Computes 2D convex hull of XY-coplanar points using Andrew's monotone chain algorithm.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d[]> ConvexHull2D(Point3d[] points, IGeometryContext context) =>
        points.Length < 3 ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidCount.WithContext("ConvexHull2D requires at least 3 points"))
            : points.Length > 1 && points[0].Z is double z0 && points.Skip(1).All(p => Math.Abs(p.Z - z0) < context.AbsoluteTolerance)
                ? ((Func<Result<Point3d[]>>)(() => {
                    Point3d[] pts = [.. points.OrderBy(static p => p.X).ThenBy(static p => p.Y),];
                    static double Cross2D(Point3d o, Point3d a, Point3d b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
                    List<Point3d> lower = [];
                    for (int i = 0; i < pts.Length; i++) {
                        while (lower.Count >= 2 && Cross2D(lower[^2], lower[^1], pts[i]) <= context.AbsoluteTolerance) {
                            lower.RemoveAt(lower.Count - 1);
                        }
                        lower.Add(pts[i]);
                    }
                    List<Point3d> upper = [];
                    for (int i = pts.Length - 1; i >= 0; i--) {
                        while (upper.Count >= 2 && Cross2D(upper[^2], upper[^1], pts[i]) <= context.AbsoluteTolerance) {
                            upper.RemoveAt(upper.Count - 1);
                        }
                        upper.Add(pts[i]);
                    }
                    Point3d[] result = [.. lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)),];
                    return result.Length >= 3
                        ? ResultFactory.Create(value: result)
                        : ResultFactory.Create<Point3d[]>(error: E.Validation.DegenerateGeometry.WithContext("ConvexHull2D failed: collinear or degenerate points"));
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidOrientationPlane.WithContext("ConvexHull2D requires all points to have the same Z coordinate (coplanar in XY plane)"));

    /// <summary>Computes 3D convex hull using incremental algorithm, returning mesh faces as vertex index triples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<int[][]> ConvexHull3D(Point3d[] points, IGeometryContext context) =>
        points.Length < 4
            ? ResultFactory.Create<int[][]>(error: E.Geometry.InvalidCount.WithContext("ConvexHull3D requires at least 4 points"))
            : points.Select((p, i) => (Point: p, Index: i)).ToArray() is (Point3d Point, int Index)[] indexed && ComputeInitialTetrahedron(indexed, context) is (bool Success, (int, int, int)[] Faces) initial && initial.Success
                ? BuildConvexHull3D(indexed: indexed, initialFaces: initial.Faces, context: context)
                : ResultFactory.Create<int[][]>(error: E.Validation.DegenerateGeometry.WithContext("ConvexHull3D failed: coplanar or degenerate points"));

    private static (bool Success, (int, int, int)[] Faces) ComputeInitialTetrahedron((Point3d Point, int Index)[] points, IGeometryContext context) =>
        (a: 0, b: points.Skip(1).Select((p, idx) => (Dist: points[0].Point.DistanceTo(p.Point), Idx: idx + 1)).MaxBy(x => x.Dist).Idx) is (int a, int b) &&
        points.Where((_, i) => i >= 2).Select((p, i) => (Area: Vector3d.CrossProduct(points[b].Point - points[a].Point, p.Point - points[a].Point).Length / 2.0, Idx: i + 2))
            .Where(x => x.Area > context.AbsoluteTolerance).OrderByDescending(x => x.Area).FirstOrDefault() is (double, int c) && c > 1 &&
        points.Where((_, i) => i >= 3).Select((p, i) => (Vol: Vector3d.Multiply(Vector3d.CrossProduct(points[b].Point - points[a].Point, points[c].Point - points[a].Point), p.Point - points[a].Point) / 6.0, Idx: i + 3))
            .Where(x => Math.Abs(x.Vol) > context.AbsoluteTolerance).OrderByDescending(x => Math.Abs(x.Vol)).FirstOrDefault() is (double v, int d) && d > 2
            ? (true, v > 0 ? [(a, b, c), (a, c, d), (a, d, b), (b, d, c),] : [(a, c, b), (a, d, c), (a, b, d), (b, c, d),])
            : (false, []);

    private static Result<int[][]> BuildConvexHull3D((Point3d Point, int Index)[] indexed, (int, int, int)[] initialFaces, IGeometryContext context) {
        HashSet<(int, int, int)> faces = [.. initialFaces,];
        HashSet<int> processed = [.. initialFaces.SelectMany(f => new[] { f.Item1, f.Item2, f.Item3 }),];

        for (int i = 0; i < indexed.Length; i++) {
            if (processed.Contains(i)) {
                continue;
            }

            (int, int, int)[] visibleFaces = [.. faces.Where(f => {
                Vector3d normal = Vector3d.CrossProduct(indexed[f.Item2].Point - indexed[f.Item1].Point, indexed[f.Item3].Point - indexed[f.Item1].Point);
                return Vector3d.Multiply(normal, indexed[i].Point - indexed[f.Item1].Point) > context.AbsoluteTolerance;
            }),
            ];

            if (visibleFaces.Length is 0) {
                continue;
            }

            HashSet<(int, int)> horizon = [];
            foreach ((int a, int b, int c) in visibleFaces) {
                _ = faces.Remove((a, b, c));
                void Toggle((int, int) edge, (int, int) reverse) => _ = horizon.Remove(reverse) || horizon.Add(edge);
                Toggle((a, b), (b, a));
                Toggle((b, c), (c, b));
                Toggle((c, a), (a, c));
            }

            foreach ((int a, int b) in horizon) {
                _ = faces.Add((a, b, i));
            }
            _ = processed.Add(i);
        }

        return ResultFactory.Create<int[][]>(value: [.. faces.Select(f => new int[] { indexed[f.Item1].Index, indexed[f.Item2].Index, indexed[f.Item3].Index }),]);
    }

    /// <summary>Computes 2D Delaunay triangulation using Bowyer-Watson algorithm, returning triangle vertex indices as triples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<int[][]> DelaunayTriangulation2D(Point3d[] points, IGeometryContext context) =>
        points.Length < 3
            ? ResultFactory.Create<int[][]>(error: E.Geometry.InvalidCount.WithContext("DelaunayTriangulation2D requires at least 3 points"))
            : ((Func<Result<int[][]>>)(() => {
                (double minX, double minY, double maxX, double maxY) = (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
                double dx = (maxX - minX) * SpatialConfig.DelaunaySuperTriangleScale;
                double dy = (maxY - minY) * SpatialConfig.DelaunaySuperTriangleScale;
                Point3d[] superTriangle = [new Point3d(minX - dx, minY - dy, 0), new Point3d(maxX + dx, minY - dy, 0), new Point3d(minX + ((maxX - minX) * SpatialConfig.DelaunaySuperTriangleCenterWeight), maxY + dy, 0),];
                HashSet<(int, int, int)> triangles = [(points.Length, points.Length + 1, points.Length + 2),];
                Point3d[] allPoints = [.. points, .. superTriangle,];
                for (int i = 0; i < points.Length; i++) {
                    (int, int, int)[] badTriangles = [.. triangles.Where(t => IsInCircumcircle(allPoints[t.Item1], allPoints[t.Item2], allPoints[t.Item3], points[i], context)),];
                    HashSet<(int, int)> polygon = [];
                    foreach ((int a, int b, int c) in badTriangles) {
                        void Toggle((int, int) edge, (int, int) reverse) => _ = polygon.Remove(reverse) || polygon.Add(edge);
                        Toggle((a, b), (b, a));
                        Toggle((b, c), (c, b));
                        Toggle((c, a), (a, c));
                        _ = triangles.Remove((a, b, c));
                    }
                    foreach ((int a, int b) in polygon) {
                        _ = triangles.Add((a, b, i));
                    }
                }
                return ResultFactory.Create<int[][]>(value: [.. triangles.Where(t => t.Item1 < points.Length && t.Item2 < points.Length && t.Item3 < points.Length).Select(t => new int[] { t.Item1, t.Item2, t.Item3 }),]);
            }))();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInCircumcircle(Point3d a, Point3d b, Point3d c, Point3d p, IGeometryContext context) {
        double orientation = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        double orientationTolerance = context.AbsoluteTolerance * context.AbsoluteTolerance;
        // Check for degenerate triangle: if orientation is near zero, the points are collinear and the incircle predicate is not meaningful.
        if (Math.Abs(orientation) <= orientationTolerance) {
            return false;
        }

        double ax = a.X - p.X;
        double ay = a.Y - p.Y;
        double bx = b.X - p.X;
        double by = b.Y - p.Y;
        double cx = c.X - p.X;
        double cy = c.Y - p.Y;
        double det = (((ax * ax) + (ay * ay)) * ((bx * cy) - (by * cx))) + (((bx * bx) + (by * by)) * ((cx * ay) - (cy * ax))) + (((cx * cx) + (cy * cy)) * ((ax * by) - (ay * bx)));
        // Adjust determinant sign for counter-clockwise orientation to maintain consistent incircle test semantics.
        double adjustedDet = orientation > 0.0 ? det : -det;
        return adjustedDet > context.AbsoluteTolerance;
    }

    /// <summary>Computes 2D Voronoi diagram from Delaunay triangulation, returning cell vertices for each input point.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d[][]> VoronoiDiagram2D(Point3d[] points, IGeometryContext context) =>
        points.Length < 3
            ? ResultFactory.Create<Point3d[][]>(error: E.Geometry.InvalidCount.WithContext("VoronoiDiagram2D requires at least 3 points"))
            : DelaunayTriangulation2D(points: points, context: context).Bind(triangles => ((Func<Result<Point3d[][]>>)(() => {
                Point3d[] circumcenters = new Point3d[triangles.Length];
                for (int ti = 0; ti < triangles.Length; ti++) {
                    (Point3d a, Point3d b, Point3d c) = (points[triangles[ti][0]], points[triangles[ti][1]], points[triangles[ti][2]]);
                    double orientation = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
                    double orientationTolerance = context.AbsoluteTolerance * context.AbsoluteTolerance;
                    if (Math.Abs(orientation) <= orientationTolerance) {
                        // Mark circumcenter as invalid for degenerate/collinear triangles.
                        // Point3d.Unset is used as a sentinel; see IsValid check at line 482.
                        circumcenters[ti] = Point3d.Unset;
                        continue;
                    }

                    double twoOrientation = 2.0 * orientation;
                    double aSq = (a.X * a.X) + (a.Y * a.Y);
                    double bSq = (b.X * b.X) + (b.Y * b.Y);
                    double cSq = (c.X * c.X) + (c.Y * c.Y);
                    circumcenters[ti] = new Point3d(((aSq * (b.Y - c.Y)) + (bSq * (c.Y - a.Y)) + (cSq * (a.Y - b.Y))) / twoOrientation, ((aSq * (c.X - b.X)) + (bSq * (a.X - c.X)) + (cSq * (b.X - a.X))) / twoOrientation, a.Z);
                }
                Point3d[][] cells = new Point3d[points.Length][];
                for (int i = 0; i < points.Length; i++) {
                    List<Point3d> cell = [];
                    for (int ti = 0; ti < triangles.Length; ti++) {
                        if (circumcenters[ti].IsValid && triangles[ti].Contains(i)) {
                            cell.Add(circumcenters[ti]);
                        }
                    }

                    cells[i] = cell.Count > 0
                        ? ((Func<Point3d[]>)(() => {
                            double centerX = cell.Average(c => c.X);
                            double centerY = cell.Average(c => c.Y);
                            return [.. cell.OrderBy(p => Math.Atan2(p.Y - centerY, p.X - centerX)),];
                        }))()
                        : [];
                }
                return ResultFactory.Create(value: cells);
            }))());

    private static Func<object, object> ResolveCentroidExtractor(Type geometryType) =>
        SpatialConfig.TypeExtractors.TryGetValue(("Centroid", geometryType), out Func<object, object>? exact)
            ? exact!
            : Array.FindIndex(_centroidFallbacks, entry => entry.GeometryType.IsAssignableFrom(geometryType)) is int match and >= 0
                ? _centroidFallbacks[match].Extractor
                : static geometry => geometry is GeometryBase baseGeometry ? baseGeometry.GetBoundingBox(accurate: false).Center : Point3d.Origin;
}
