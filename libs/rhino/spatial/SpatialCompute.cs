using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Dense spatial algorithm implementations.</summary>
internal static class SpatialCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d Centroid(IEnumerable<int> indices, Point3d[] pts) =>
        indices.ToArray() is { Length: > 0 } arr
            ? ((Func<Point3d>)(() => {
                double sumX = 0.0;
                double sumY = 0.0;
                double sumZ = 0.0;
                for (int i = 0; i < arr.Length; i++) {
                    sumX += pts[arr[i]].X;
                    sumY += pts[arr[i]].Y;
                    sumZ += pts[arr[i]].Z;
                }
                return new Point3d(sumX / arr.Length, sumY / arr.Length, sumZ / arr.Length);
            }))()
            : Point3d.Origin;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d ExtractCentroid(GeometryBase g) {
        Type gType = g.GetType();
        return SpatialConfig.TypeExtractors.TryGetValue(("Centroid", gType), out Func<object, object>? exactExtractor)
            ? (Point3d)exactExtractor(g)
            : SpatialConfig.TypeExtractors.FirstOrDefault(kv => string.Equals(kv.Key.Operation, "Centroid", StringComparison.Ordinal) && kv.Key.GeometryType.IsInstanceOfType(g)).Value is Func<object, object> fallbackExtractor
                ? (Point3d)fallbackExtractor(g)
                : g.GetBoundingBox(accurate: false).Center;
    }

    internal static Result<(Point3d, double[])[]> Cluster<T>(T[] geometry, byte algorithm, int k, double epsilon, IGeometryContext context) where T : GeometryBase =>
        geometry.Length is 0 ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Geometry.InvalidCount.WithContext("Cluster requires at least one geometry"))
            : algorithm is > 2 ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed.WithContext($"Unknown algorithm: {algorithm}"))
            : (algorithm is 0 or 2 && k <= 0) ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.InvalidClusterK)
            : (algorithm is 1 && epsilon <= 0) ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.InvalidEpsilon)
            : ClusterInternal(geometry: geometry, algorithm: algorithm, k: k, epsilon: epsilon, context: context);

    private static Result<(Point3d, double[])[]> ClusterInternal<T>(T[] geometry, byte algorithm, int k, double epsilon, IGeometryContext context) where T : GeometryBase {
        Point3d[] pts = [.. geometry.Select(ExtractCentroid),];
        return (algorithm is 0 or 2) && k > pts.Length
            ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.KExceedsPointCount)
            : SpatialConfig.TypeExtractors.TryGetValue(("ClusterAssign", typeof(void)), out Func<object, object>? assignFunc) && assignFunc((algorithm, pts, k, epsilon, context)) is int[] assigns && assigns.Length > 0
                ? (algorithm is 1 ? assigns.Where(a => a >= 0).DefaultIfEmpty(-1).Max() + 1 : k) is int clusterCount && clusterCount > 0
                    ? ResultFactory.Create<(Point3d, double[])[]>(value: [.. Enumerable.Range(0, clusterCount).Select(c => {
                        int[] members = [.. Enumerable.Range(0, pts.Length).Where(i => assigns[i] == c),];
                        Point3d centroid = members.Length > 0 ? Centroid(members, pts) : Point3d.Origin;
                        double[] distances = members.Length > 0 ? [.. members.Select(i => pts[i].DistanceTo(centroid)),] : [];
                        return (centroid, distances);
                    }),])
                    : ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed)
                : ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed);
    }

    internal static int[] KMeansAssign(Point3d[] pts, int k, double tol, int maxIter) {
        int[] assignments = new int[pts.Length];
        Point3d[] centroids = new Point3d[k];
        Random rng = new(SpatialConfig.KMeansSeed);

        // K-means++ initialization with squared distances
        centroids[0] = pts[rng.Next(pts.Length)];
        for (int i = 1; i < k; i++) {
            double[] distSq = new double[pts.Length];
            for (int j = 0; j < pts.Length; j++) {
                double minDist = pts[j].DistanceTo(centroids[0]);
                for (int c = 1; c < i; c++) {
                    double dist = pts[j].DistanceTo(centroids[c]);
                    minDist = dist < minDist ? dist : minDist;
                }
                distSq[j] = minDist * minDist;
            }

            double sum = 0.0;
            for (int j = 0; j < distSq.Length; j++) {
                sum += distSq[j];
            }

            if (sum <= tol || sum <= 0.0) {
                centroids[i] = pts[rng.Next(pts.Length)];
            } else {
                double target = rng.NextDouble() * sum;
                double cumulative = 0.0;
                int selectedIdx = pts.Length - 1;
                for (int j = 0; j < distSq.Length; j++) {
                    cumulative += distSq[j];
                    if (cumulative >= target) {
                        selectedIdx = j;
                        break;
                    }
                }
                centroids[i] = pts[selectedIdx];
            }
        }

        // Lloyd's algorithm with hot-path optimization
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
            (Point3d Sum, int Count)[] clusters = [.. Enumerable.Range(0, k).Select(static _ => (Point3d.Origin, 0)),];
            for (int i = 0; i < pts.Length; i++) {
                clusters[assignments[i]] = (clusters[assignments[i]].Sum + pts[i], clusters[assignments[i]].Count + 1);
            }

            double maxShift = 0.0;
            for (int i = 0; i < k; i++) {
                Point3d newCentroid = clusters[i].Count > 0 ? clusters[i].Sum / clusters[i].Count : centroids[i];
                double shift = centroids[i].DistanceTo(newCentroid);
                maxShift = shift > maxShift ? shift : maxShift;
                centroids[i] = newCentroid;
            }

            if (maxShift <= tol) {
                break;
            }
        }

        return assignments;
    }

    internal static int[] DBSCANAssign(Point3d[] pts, double eps, int minPts) {
        int[] assignments = [.. Enumerable.Repeat(-1, pts.Length),];
        bool[] visited = new bool[pts.Length];
        int clusterId = 0;

        // Use RTree for large point sets (O(log n) neighbor queries vs O(n) linear scan)
        using RTree? tree = pts.Length > SpatialConfig.DBSCANRTreeThreshold ? RTree.CreateFromPointArray(pts) : null;

        int[] GetNeighbors(int idx) {
            return tree is not null
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
        }

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
                    foreach (int nb in curNeighbors.Where(nb => assignments[nb] == -1)) {
                        queue.Enqueue(nb);
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
        Enumerable.Range(0, pts.Length - k).Aggregate(Enumerable.Range(0, pts.Length).ToArray(), (a, _) => Enumerable.Range(0, pts.Length).SelectMany(i => Enumerable.Range(i + 1, pts.Length - i - 1).Where(j => a[i] != a[j]).Select(j => (Cluster1: a[i], Cluster2: a[j], Distance: pts[i].DistanceTo(pts[j])))).OrderBy(t => t.Distance).First() is (int c1, int c2, double) ? [.. Enumerable.Range(0, a.Length).Select(i => a[i] == c2 ? c1 : a[i] > c2 ? a[i] - 1 : a[i])] : a);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Curve[], double[])> MedialAxis(Brep brep, double tolerance, IGeometryContext context) =>
        brep.Faces.Count is 0
            ? ResultFactory.Create<(Curve[], double[])>(error: E.Geometry.InvalidCount.WithContext("MedialAxis requires at least one face"))
            : (brep.Faces.Count is 1 && brep.Faces[0].IsPlanar(tolerance: context.AbsoluteTolerance), brep.Edges.Where(static e => e.Valence == EdgeAdjacency.Naked).Select(static e => e.DuplicateCurve()).Where(static c => c is not null).ToArray()) switch {
                (false, _) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.NonPlanarNotSupported),
                (true, { Length: 0 }) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("No boundary")),
                (true, Curve[] edges) when Curve.JoinCurves(edges, joinTolerance: tolerance, preserveDirection: false).FirstOrDefault() is Curve joined && joined.IsClosed && joined.TryGetPlane(out Plane plane, tolerance: tolerance) && joined.Offset(plane: plane, distance: tolerance * SpatialConfig.MedialAxisOffsetMultiplier, tolerance: tolerance, CurveOffsetCornerStyle.Sharp) is { Length: > 0 } offsets && offsets.All(o => o?.IsValid is true) => ResultFactory.Create(value: (offsets, offsets.Select(static c => c.GetLength()).ToArray())),
                _ => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("Not closed planar or offset failed")),
            };

    internal static Result<(int, double, double)[]> ProximityField(GeometryBase[] geometry, Vector3d direction, double maxDist, double angleWeight, IGeometryContext context) =>
        geometry.Length is 0
            ? ResultFactory.Create<(int, double, double)[]>(error: E.Geometry.InvalidCount.WithContext("ProximityField requires at least one geometry"))
            : direction.Length <= context.AbsoluteTolerance
                ? ResultFactory.Create<(int, double, double)[]>(error: E.Spatial.ZeroLengthDirection)
                : maxDist <= context.AbsoluteTolerance
                    ? ResultFactory.Create<(int, double, double)[]>(error: E.Spatial.InvalidDistance.WithContext("MaxDistance must exceed tolerance"))
                    : ProximityFieldCompute(geometry: geometry, direction: direction, maxDist: maxDist, angleWeight: angleWeight, context: context);

    private static Result<(int, double, double)[]> ProximityFieldCompute(GeometryBase[] geometry, Vector3d direction, double maxDist, double angleWeight, IGeometryContext context) {
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
    }

    /// <summary>
    /// Computes the 2D convex hull of a set of points projected onto the XY plane.
    /// <para>
    /// <b>All input points must have the same Z coordinate (i.e., be coplanar in the XY plane).</b>
    /// If this condition is not met, the result is an error.
    /// </para>
    /// </summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d[]> ConvexHull2D(Point3d[] points, IGeometryContext context) =>
        points.Length < 3 ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidCount.WithContext("ConvexHull2D requires at least 3 points"))
            : points.Length > 1 && points[0].Z is double z0 && points.Skip(1).All(p => Math.Abs(p.Z - z0) < context.AbsoluteTolerance) ? BuildConvexHull2D(points: points, context: context)
            : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidOrientationPlane.WithContext("ConvexHull2D requires all points to have the same Z coordinate (coplanar in XY plane)"));

    private static Result<Point3d[]> BuildConvexHull2D(Point3d[] points, IGeometryContext context) {
        Point3d[] pts = [.. points.OrderBy(static p => p.X).ThenBy(static p => p.Y),];

        // Build lower hull (Andrew's monotone chain)
        List<Point3d> lower = [];
        for (int i = 0; i < pts.Length; i++) {
            while (lower.Count >= 2 && CrossProduct2D(lower[^2], lower[^1], pts[i]) <= context.AbsoluteTolerance) {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(pts[i]);
        }

        // Build upper hull
        List<Point3d> upper = [];
        Point3d[] reversed = [.. pts.AsEnumerable().Reverse(),];
        for (int i = 0; i < reversed.Length; i++) {
            while (upper.Count >= 2 && CrossProduct2D(upper[^2], upper[^1], reversed[i]) <= context.AbsoluteTolerance) {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(reversed[i]);
        }

        Point3d[] result = [.. lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)),];
        return result.Length >= 3
            ? ResultFactory.Create(value: result)
            : ResultFactory.Create<Point3d[]>(error: E.Validation.DegenerateGeometry.WithContext("ConvexHull2D failed: collinear or degenerate points"));
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CrossProduct2D(Point3d o, Point3d a, Point3d b) =>
        ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

    /// <summary>
    /// Computes the 3D convex hull of a set of points using incremental algorithm.
    /// <para>Returns mesh faces as vertex index triples.</para>
    /// </summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<int[][]> ConvexHull3D(Point3d[] points, IGeometryContext context) =>
        points.Length < 4
            ? ResultFactory.Create<int[][]>(error: E.Geometry.InvalidCount.WithContext("ConvexHull3D requires at least 4 points"))
            : points.Select((p, i) => (Point: p, Index: i)).ToArray() is (Point3d Point, int Index)[] indexed && ComputeInitialTetrahedron(indexed, context) is (bool Success, (int, int, int)[] Faces) initial && initial.Success
                ? BuildConvexHull3D(indexed: indexed, initialFaces: initial.Faces, context: context)
                : ResultFactory.Create<int[][]>(error: E.Validation.DegenerateGeometry.WithContext("ConvexHull3D failed: coplanar or degenerate points"));

    private static (bool Success, (int, int, int)[] Faces) ComputeInitialTetrahedron((Point3d Point, int Index)[] points, IGeometryContext context) =>
        (a: 0, b: points.Skip(1).Select((p, idx) => (Dist: points[0].Point.DistanceTo(p.Point), Idx: idx + 1)).MaxBy(x => x.Dist).Idx) is (int a, int b) &&
        points.Where((_, i) => i >= 2).Select((p, i) => (Area: TriangleArea(points[a].Point, points[b].Point, p.Point), Idx: i + 2))
            .Where(x => x.Area > context.AbsoluteTolerance).OrderByDescending(x => x.Area).FirstOrDefault() is (double, int c) && c > 1 &&
        points.Where((_, i) => i >= 3).Select((p, i) => (Vol: TetrahedronVolume(points[a].Point, points[b].Point, points[c].Point, p.Point), Idx: i + 3))
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
                _ = horizon.Contains((b, a)) ? horizon.Remove((b, a)) : horizon.Add((a, b));
                _ = horizon.Contains((c, b)) ? horizon.Remove((c, b)) : horizon.Add((b, c));
                _ = horizon.Contains((a, c)) ? horizon.Remove((a, c)) : horizon.Add((c, a));
            }

            foreach ((int a, int b) in horizon) {
                _ = faces.Add((a, b, i));
            }
            _ = processed.Add(i);
        }

        return ResultFactory.Create<int[][]>(value: [.. faces.Select(f => new int[] { indexed[f.Item1].Index, indexed[f.Item2].Index, indexed[f.Item3].Index }),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double TriangleArea(Point3d a, Point3d b, Point3d c) =>
        Vector3d.CrossProduct(b - a, c - a).Length * 0.5;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double TetrahedronVolume(Point3d a, Point3d b, Point3d c, Point3d d) =>
        Vector3d.Multiply(Vector3d.CrossProduct(b - a, c - a), d - a) / 6.0;

    /// <summary>
    /// Computes 2D Delaunay triangulation using Bowyer-Watson incremental algorithm.
    /// <para>Returns triangle vertex indices as triples for XY-projected points.</para>
    /// </summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<int[][]> DelaunayTriangulation2D(Point3d[] points, IGeometryContext context) =>
        points.Length < 3
            ? ResultFactory.Create<int[][]>(error: E.Geometry.InvalidCount.WithContext("DelaunayTriangulation2D requires at least 3 points"))
            : ComputeDelaunay2D(points: points, context: context);

    private static Result<int[][]> ComputeDelaunay2D(Point3d[] points, IGeometryContext context) {
        (double minX, double minY, double maxX, double maxY) = (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        double dx = (maxX - minX) * 2.0;
        double dy = (maxY - minY) * 2.0;
        Point3d[] superTriangle = [new Point3d(minX - dx, minY - dy, 0), new Point3d(maxX + dx, minY - dy, 0), new Point3d(minX + ((maxX - minX) * 0.5), maxY + dy, 0),];
        HashSet<(int, int, int)> triangles = [(points.Length, points.Length + 1, points.Length + 2),];
        Point3d[] allPoints = [.. points, .. superTriangle,];

        for (int i = 0; i < points.Length; i++) {
            (int, int, int)[] badTriangles = [.. triangles.Where(t => IsInCircumcircle(allPoints[t.Item1], allPoints[t.Item2], allPoints[t.Item3], points[i], context)),];
            HashSet<(int, int)> polygon = [];

            foreach ((int a, int b, int c) in badTriangles) {
                _ = polygon.Contains((b, a)) ? polygon.Remove((b, a)) : polygon.Add((a, b));
                _ = polygon.Contains((c, b)) ? polygon.Remove((c, b)) : polygon.Add((b, c));
                _ = polygon.Contains((a, c)) ? polygon.Remove((a, c)) : polygon.Add((c, a));
                _ = triangles.Remove((a, b, c));
            }

            foreach ((int a, int b) in polygon) {
                _ = triangles.Add((a, b, i));
            }
        }

        return ResultFactory.Create<int[][]>(value: [.. triangles.Where(t => t.Item1 < points.Length && t.Item2 < points.Length && t.Item3 < points.Length).Select(t => new int[] { t.Item1, t.Item2, t.Item3 }),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInCircumcircle(Point3d a, Point3d b, Point3d c, Point3d p, IGeometryContext context) {
        double ax = a.X - p.X;
        double ay = a.Y - p.Y;
        double bx = b.X - p.X;
        double by = b.Y - p.Y;
        double cx = c.X - p.X;
        double cy = c.Y - p.Y;
        double det = (((ax * ax) + (ay * ay)) * ((bx * cy) - (by * cx))) + (((bx * bx) + (by * by)) * ((cx * ay) - (cy * ax))) + (((cx * cx) + (cy * cy)) * ((ax * by) - (ay * bx)));
        return det > context.AbsoluteTolerance;
    }

    /// <summary>
    /// Computes 2D Voronoi diagram from Delaunay triangulation.
    /// <para>Returns Voronoi cell vertices for each input point.</para>
    /// </summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Point3d[][]> VoronoiDiagram2D(Point3d[] points, IGeometryContext context) =>
        points.Length < 3
            ? ResultFactory.Create<Point3d[][]>(error: E.Geometry.InvalidCount.WithContext("VoronoiDiagram2D requires at least 3 points"))
            : DelaunayTriangulation2D(points: points, context: context).Bind(triangles => ComputeVoronoi2D(points: points, triangles: triangles, _: context));

    private static Result<Point3d[][]> ComputeVoronoi2D(Point3d[] points, int[][] triangles, IGeometryContext _) =>
        ResultFactory.Create(value: triangles.Select(t => Circumcenter2D(points[t[0]], points[t[1]], points[t[2]])).ToArray() is Point3d[] circumcenters
            ? Enumerable.Range(0, points.Length).Select(i =>
                triangles.Select((t, ti) => (t, ti))
                    .Where(p => p.t.Contains(i))
                    .Select(p => circumcenters[p.ti])
                    .ToArray() is { Length: > 0 } cell
                        ? cell.OrderBy(p => Math.Atan2(p.Y - cell.Average(c => c.Y), p.X - cell.Average(c => c.X))).ToArray()
                        : []).ToArray()
            : []);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d Circumcenter2D(Point3d a, Point3d b, Point3d c) {
        double d = 2.0 * ((a.X * (b.Y - c.Y)) + (b.X * (c.Y - a.Y)) + (c.X * (a.Y - b.Y)));
        double aSq = (a.X * a.X) + (a.Y * a.Y);
        double bSq = (b.X * b.X) + (b.Y * b.Y);
        double cSq = (c.X * c.X) + (c.Y * c.Y);
        double ux = ((aSq * (b.Y - c.Y)) + (bSq * (c.Y - a.Y)) + (cSq * (a.Y - b.Y))) / d;
        double uy = ((aSq * (c.X - b.X)) + (bSq * (a.X - c.X)) + (cSq * (b.X - a.X))) / d;
        return new Point3d(ux, uy, a.Z);
    }
}
