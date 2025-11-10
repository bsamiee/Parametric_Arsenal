using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Dense spatial algorithm implementations with zero duplication.</summary>
internal static class SpatialCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d Centroid(IEnumerable<int> indices, Point3d[] pts) =>
        indices.ToArray() is int[] arr && arr.Length > 0
            ? arr.Aggregate((X: 0.0, Y: 0.0, Z: 0.0), (sum, idx) => (sum.X + pts[idx].X, sum.Y + pts[idx].Y, sum.Z + pts[idx].Z)) is (double x, double y, double z)
                ? new Point3d(x / arr.Length, y / arr.Length, z / arr.Length)
                : Point3d.Origin
            : Point3d.Origin;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d ExtractCentroid(GeometryBase g) => g switch {
        Curve c => ((Func<Point3d>)(() => { using AreaMassProperties amp = AreaMassProperties.Compute(c); return amp?.Centroid ?? c.GetBoundingBox(accurate: false).Center; }))(),
        Surface s => ((Func<Point3d>)(() => { using AreaMassProperties amp = AreaMassProperties.Compute(s); return amp?.Centroid ?? s.GetBoundingBox(accurate: false).Center; }))(),
        Brep b => ((Func<Point3d>)(() => { using VolumeMassProperties vmp = VolumeMassProperties.Compute(b); return vmp?.Centroid ?? b.GetBoundingBox(accurate: false).Center; }))(),
        Mesh m => ((Func<Point3d>)(() => { using VolumeMassProperties vmp = VolumeMassProperties.Compute(m); return vmp?.Centroid ?? m.GetBoundingBox(accurate: false).Center; }))(),
        _ => g.GetBoundingBox(accurate: false).Center,
    };

    internal static Result<(Point3d, double[])[]> Cluster<T>(T[] geometry, byte algorithm, int k, double epsilon, IGeometryContext context) where T : GeometryBase =>
        geometry.Length is 0
            ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Geometry.InvalidCount.WithContext("Cluster requires at least one geometry"))
            : !SpatialConfig.ClusterParams.TryGetValue(algorithm, out (int maxIter, int minPts) config) || !((algorithm is 0 or 2 && k > 0) || (algorithm is 1 && epsilon > 0))
                ? ResultFactory.Create<(Point3d, double[])[]>(error: algorithm is 0 or 2 ? E.Spatial.InvalidClusterK : E.Spatial.InvalidEpsilon)
                : ClusterInternal(geometry: geometry, algorithm: algorithm, k: k, epsilon: epsilon, config: config, context: context);

    private static Result<(Point3d, double[])[]> ClusterInternal<T>(T[] geometry, byte algorithm, int k, double epsilon, (int maxIter, int minPts) config, IGeometryContext context) where T : GeometryBase {
        Point3d[] pts = [.. geometry.Select(ExtractCentroid),];
        return ((algorithm is 0 or 2) && k > pts.Length)
            ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.KExceedsPointCount)
            : algorithm switch {
                0 => KMeansAssign(pts, k, context.AbsoluteTolerance, config.maxIter),
                1 => DBSCANAssign(pts, epsilon, config.minPts),
                2 => HierarchicalAssign(pts, k),
                _ => [],
            } is int[] assigns && (algorithm is 1 ? (assigns.Any(a => a >= 0) ? assigns.Where(a => a >= 0).Max() + 1 : 0) : k) is int nc && nc > 0
                ? ResultFactory.Create(value: Enumerable.Range(0, nc).Select(c => Enumerable.Range(0, pts.Length).Where(i => assigns[i] == c).ToArray() is int[] m && m.Length > 0 ? Centroid(m, pts) is Point3d centroid ? (centroid, [.. m.Select(i => pts[i].DistanceTo(centroid))]) : (Point3d.Origin, Array.Empty<double>()) : (Point3d.Origin, Array.Empty<double>())).ToArray())
                : ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed);
    }

    private static int[] KMeansAssign(Point3d[] pts, int k, double tol, int maxIter) {
        int[] assignments = new int[pts.Length];
        Point3d[] centroids = new Point3d[k];
        Random rng = new(SpatialConfig.KMeansSeed);

        // K-means++ initialization with squared distances
        centroids[0] = pts[rng.Next(pts.Length)];
        for (int i = 1; i < k; i++) {
            double[] distSq = [.. pts.Select(p => Enumerable.Range(0, i).Min(c => p.DistanceTo(centroids[c])) is double minDist ? minDist * minDist : 0.0),];
            double sum = distSq.Sum();
            centroids[i] = sum <= tol || sum <= 0.0
                ? pts[rng.Next(pts.Length)]
                : distSq.Select((d, idx) => (d, idx)).Aggregate((cumulative: 0.0, target: rng.NextDouble() * sum, result: pts.Length - 1), (acc, pair) =>
                    acc.cumulative >= acc.target ? acc : (acc.cumulative + pair.d, acc.target, pair.idx)).result is int selectedIdx && selectedIdx >= 0 && selectedIdx < pts.Length
                    ? pts[selectedIdx]
                    : pts[^1];
        }

        // Lloyd's algorithm
        for (int iter = 0; iter < maxIter; iter++) {
            // Assign to nearest centroid
            for (int i = 0; i < pts.Length; i++) {
                assignments[i] = Enumerable.Range(0, k).Select(j => (Dist: pts[i].DistanceTo(centroids[j]), Idx: j)).MinBy(pair => pair.Dist).Idx;
            }

            // Recompute centroids and check convergence
            (Point3d Sum, int Count)[] clusters = [.. Enumerable.Range(0, k).Select(_ => (Point3d.Origin, 0)),];
            for (int i = 0; i < pts.Length; i++) {
                clusters[assignments[i]] = (clusters[assignments[i]].Sum + pts[i], clusters[assignments[i]].Count + 1);
            }

            double maxShift = clusters.Select((cluster, i) => {
                Point3d newCentroid = cluster.Count > 0 ? cluster.Sum / cluster.Count : centroids[i];
                double shift = centroids[i].DistanceTo(newCentroid);
                centroids[i] = newCentroid;
                return shift;
            }).Max();

            if (maxShift <= tol) {
                break;
            }
        }

        return assignments;
    }

    private static int[] DBSCANAssign(Point3d[] pts, double eps, int minPts) {
        int[] assignments = [.. Enumerable.Repeat(-1, pts.Length),];
        bool[] visited = new bool[pts.Length];
        int clusterId = 0;
        int[] GetNeighbors(int idx) => [.. Enumerable.Range(0, pts.Length).Where(j => j != idx && pts[idx].DistanceTo(pts[j]) <= eps),];

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
    private static int[] HierarchicalAssign(Point3d[] pts, int k) =>
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

            double angle = dist > context.AbsoluteTolerance
                ? Vector3d.VectorAngle(dir, toGeom / dist)
                : 0.0;
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
        List<Point3d> BuildHalf(Point3d[] sequence) {
            List<Point3d> hull = [];
            for (int i = 0; i < sequence.Length; i++) {
                while (hull.Count >= 2 && CrossProduct2D(hull[^2], hull[^1], sequence[i]) <= context.AbsoluteTolerance) {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(sequence[i]);
            }
            return hull;
        }
        (List<Point3d> lower, List<Point3d> upper) = (BuildHalf(pts), BuildHalf([.. pts.AsEnumerable().Reverse(),]));
        Point3d[] result = [.. lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)),];
        return result.Length >= 3
            ? ResultFactory.Create<Point3d[]>(value: result)
            : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidGeometry.WithContext("ConvexHull2D failed: collinear or degenerate points"));
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
                : ResultFactory.Create<int[][]>(error: E.Geometry.InvalidGeometry.WithContext("ConvexHull3D failed: coplanar or degenerate points"));

    private static (bool Success, (int, int, int)[] Faces) ComputeInitialTetrahedron((Point3d Point, int Index)[] points, IGeometryContext context) {
        (int a, int b) = (0, points.Skip(1).Select((p, i) => (Dist: points[0].Point.DistanceTo(p.Point), Idx: i + 1)).OrderByDescending(x => x.Dist).First().Idx);
        int c = points.Skip(2).Select((p, i) => (Area: TriangleArea(points[a].Point, points[b].Point, p.Point), Idx: i + 2)).Where(x => x.Area > context.AbsoluteTolerance).OrderByDescending(x => x.Area).FirstOrDefault().Idx;
        return c > 0
            ? points.Skip(3).Select((p, i) => (Vol: TetrahedronVolume(points[a].Point, points[b].Point, points[c].Point, p.Point), Idx: i + 3)).Where(x => Math.Abs(x.Vol) > context.AbsoluteTolerance).OrderByDescending(x => Math.Abs(x.Vol)).FirstOrDefault() is (double v, int d) && d > 0
                ? (true, v > 0 ? [(a, b, c), (a, c, d), (a, d, b), (b, d, c),] : [(a, c, b), (a, d, c), (a, b, d), (b, c, d),])
                : (false, [])
            : (false, []);
    }

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
            }),];

            if (visibleFaces.Length is 0) {
                continue;
            }

            HashSet<(int, int)> horizon = [];
            foreach ((int a, int b, int c) in visibleFaces) {
                _ = faces.Remove((a, b, c));
                AddOrRemoveEdge(horizon, (a, b));
                AddOrRemoveEdge(horizon, (b, c));
                AddOrRemoveEdge(horizon, (c, a));
            }

            foreach ((int a, int b) in horizon) {
                _ = faces.Add((a, b, i));
            }
            _ = processed.Add(i);
        }

        return ResultFactory.Create<int[][]>(value: [.. faces.Select(f => new int[] { indexed[f.Item1].Index, indexed[f.Item2].Index, indexed[f.Item3].Index }),]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddOrRemoveEdge(HashSet<(int, int)> edges, (int a, int b) edge) =>
        _ = edges.Contains((edge.b, edge.a)) ? edges.Remove((edge.b, edge.a)) : edges.Add(edge);

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
                AddPolygonEdge(polygon, (a, b));
                AddPolygonEdge(polygon, (b, c));
                AddPolygonEdge(polygon, (c, a));
                _ = triangles.Remove((a, b, c));
            }

            foreach ((int a, int b) in polygon) {
                _ = triangles.Add((a, b, i));
            }
        }

        return ResultFactory.Create<int[][]>(value: [.. triangles.Where(t => t.Item1 < points.Length && t.Item2 < points.Length && t.Item3 < points.Length).Select(t => new int[] { t.Item1, t.Item2, t.Item3 }),]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddPolygonEdge(HashSet<(int, int)> edges, (int a, int b) edge) =>
        _ = edges.Contains((edge.b, edge.a)) ? edges.Remove((edge.b, edge.a)) : edges.Add(edge);

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

    private static Result<Point3d[][]> ComputeVoronoi2D(Point3d[] points, int[][] triangles, IGeometryContext _) {
        Dictionary<int, List<Point3d>> voronoiCells = Enumerable.Range(0, points.Length).ToDictionary(static i => i, static _ => new List<Point3d>());
        Point3d[] circumcenters = [.. triangles.Select(t => Circumcenter2D(points[t[0]], points[t[1]], points[t[2]])),];

        for (int i = 0; i < triangles.Length; i++) {
            int[] triangle = triangles[i];
            Point3d cc = circumcenters[i];
            voronoiCells[triangle[0]].Add(cc);
            voronoiCells[triangle[1]].Add(cc);
            voronoiCells[triangle[2]].Add(cc);
        }

        Point3d[][] result = [.. voronoiCells.Values.Select(cell => {
            Point3d center = new(cell.Average(p => p.X), cell.Average(p => p.Y), cell.Average(p => p.Z));
            return cell.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToArray();
        }),];

        return ResultFactory.Create<Point3d[][]>(value: result);
    }

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
