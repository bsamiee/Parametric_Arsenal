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
        !SpatialConfig.ClusterParams.TryGetValue(algorithm, out (int maxIter, int minPts) config) || !((algorithm is 0 or 2 && k > 0) || (algorithm is 1 && epsilon > 0))
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
            centroids[i] = sum <= tol
                ? pts[rng.Next(pts.Length)]
                : distSq.Select((d, idx) => (d, idx)).Aggregate((cumulative: 0.0, target: rng.NextDouble() * sum, result: pts.Length - 1), (acc, pair) =>
                    acc.cumulative >= acc.target ? acc : (acc.cumulative + pair.d, acc.target, pair.idx)).result is int selectedIdx
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
        (brep.Faces.Count is 1 && brep.Faces[0].IsPlanar(tolerance: context.AbsoluteTolerance), brep.Edges.Where(static e => e.Valence == EdgeAdjacency.Naked).Select(static e => e.DuplicateCurve()).Where(static c => c is not null).ToArray()) switch {
            (false, _) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.NonPlanarNotSupported),
            (true, { Length: 0 }) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("No boundary")),
            (true, Curve[] edges) when Curve.JoinCurves(edges, joinTolerance: tolerance, preserveDirection: false).FirstOrDefault() is Curve joined && joined.IsClosed && joined.TryGetPlane(out Plane plane, tolerance: tolerance) && joined.Offset(plane: plane, distance: tolerance * SpatialConfig.MedialAxisOffsetMultiplier, tolerance: tolerance, CurveOffsetCornerStyle.Sharp) is { Length: > 0 } offsets => ResultFactory.Create(value: (offsets, offsets.Select(static c => c.GetLength()).ToArray())),
            _ => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("Not closed planar or offset failed")),
        };

    internal static Result<(int, double, double)[]> ProximityField(GeometryBase[] geometry, Vector3d direction, double maxDist, double angleWeight, IGeometryContext context) =>
        direction.Length <= context.AbsoluteTolerance
            ? ResultFactory.Create<(int, double, double)[]>(error: E.Spatial.ZeroLengthDirection)
            : ProximityFieldCompute(geometry: geometry, direction: direction, maxDist: maxDist, angleWeight: angleWeight, context: context);

    private static Result<(int, double, double)[]> ProximityFieldCompute(GeometryBase[] geometry, Vector3d direction, double maxDist, double angleWeight, IGeometryContext context) {
        using RTree tree = new();
        BoundingBox bounds = BoundingBox.Empty;

        for (int i = 0; i < geometry.Length; i++) {
            BoundingBox bbox = geometry[i].GetBoundingBox(accurate: true);
            _ = tree.Insert(bbox, i);
            bounds.Union(bbox);
        }

        Vector3d dir = direction / direction.Length;
        BoundingBox searchBox = bounds;
        searchBox.Inflate(maxDist);

        List<(int, double, double)> results = [];
        _ = tree.Search(searchBox, (_, args) => {
            Point3d center = geometry[args.Id].GetBoundingBox(accurate: false).Center;
            Vector3d toGeom = center - Point3d.Origin;
            double dist = toGeom.Length;

            if (dist > maxDist) {
                return;
            }

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
    internal static Result<Point3d[]> ConvexHull2D(Point3d[] points) =>
        points.Length < 3 ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidCount.WithContext("ConvexHull2D requires at least 3 points"))
            : points.Length > 1 && points[0].Z is double z0 && points.Skip(1).All(p => Math.Abs(p.Z - z0) < 1e-8) ? BuildConvexHull2D(points: points)
            : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidOrientationPlane.WithContext("ConvexHull2D requires all points to have the same Z coordinate (coplanar in XY plane)"));

    private static Result<Point3d[]> BuildConvexHull2D(Point3d[] points) {
        Point3d[] pts = [.. points.OrderBy(static p => p.X).ThenBy(static p => p.Y),];
        static List<Point3d> BuildHalf(Point3d[] sequence) {
            List<Point3d> hull = [];
            for (int i = 0; i < sequence.Length; i++) {
                while (hull.Count >= 2 && CrossProduct2D(hull[^2], hull[^1], sequence[i]) <= 0.0) {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(sequence[i]);
            }
            return hull;
        }
        (List<Point3d> lower, List<Point3d> upper) = (BuildHalf(pts), BuildHalf([.. pts.AsEnumerable().Reverse(),]));
        return ResultFactory.Create<Point3d[]>(value: [.. lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CrossProduct2D(Point3d o, Point3d a, Point3d b) =>
        ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
}
