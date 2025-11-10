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
            ? ((Func<Point3d>)(() => {
                (double sumX, double sumY, double sumZ) = (0.0, 0.0, 0.0);
                foreach (int idx in arr) {
                    sumX += pts[idx].X;
                    sumY += pts[idx].Y;
                    sumZ += pts[idx].Z;
                }
                return new Point3d(sumX / arr.Length, sumY / arr.Length, sumZ / arr.Length);
            }))()
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
            double[] distSq = new double[pts.Length];
            for (int j = 0; j < pts.Length; j++) {
                double minDist = double.MaxValue;
                for (int c = 0; c < i; c++) {
                    double d = pts[j].DistanceTo(centroids[c]);
                    minDist = Math.Min(d, minDist);
                }
                distSq[j] = minDist * minDist;
            }

            double sum = distSq.Sum();
            centroids[i] = sum <= tol
                ? pts[rng.Next(pts.Length)]
                : ((Func<Point3d>)(() => {
                    double target = rng.NextDouble() * sum;
                    double cumulative = 0.0;
                    for (int j = 0; j < pts.Length; j++) {
                        cumulative += distSq[j];
                        if (cumulative >= target) {
                            return pts[j];
                        }
                    }
                    return pts[^1];
                }))();
        }

        // Lloyd's algorithm
        for (int iter = 0; iter < maxIter; iter++) {
            // Assign to nearest centroid
            for (int i = 0; i < pts.Length; i++) {
                double minDist = double.MaxValue;
                for (int j = 0; j < k; j++) {
                    double d = pts[i].DistanceTo(centroids[j]);
                    if (d < minDist) {
                        minDist = d;
                        assignments[i] = j;
                    }
                }
            }

            // Recompute centroids and check convergence
            Point3d[] newCentroids = new Point3d[k];
            int[] counts = new int[k];
            for (int i = 0; i < pts.Length; i++) {
                newCentroids[assignments[i]] += pts[i];
                counts[assignments[i]]++;
            }

            double maxShift = 0;
            for (int i = 0; i < k; i++) {
                newCentroids[i] = counts[i] > 0 ? newCentroids[i] / counts[i] : centroids[i];
                double shift = centroids[i].DistanceTo(newCentroids[i]);
                maxShift = Math.Max(shift, maxShift);
                centroids[i] = newCentroids[i];
            }

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

        for (int i = 0; i < pts.Length; i++) {
            if (visited[i]) {
                continue;
            }

            visited[i] = true;

            // Find epsilon-neighborhood
            int[] neighbors = [.. Enumerable.Range(0, pts.Length).Where(j => j != i && pts[i].DistanceTo(pts[j]) <= eps),];

            if (neighbors.Length < minPts) {
                continue;
            }

            // Start new cluster
            assignments[i] = clusterId;
            Queue<int> queue = new(neighbors);

            while (queue.Count > 0) {
                int cur = queue.Dequeue();
                if (visited[cur]) {
                    continue;
                }

                visited[cur] = true;

                // Find cur's neighbors
                int[] curNeighbors = [.. Enumerable.Range(0, pts.Length).Where(j => j != cur && pts[cur].DistanceTo(pts[j]) <= eps),];

                // If core point, expand cluster
                if (curNeighbors.Length >= minPts) {
                    foreach (int nb in curNeighbors) {
                        if (assignments[nb] == -1) {
                            queue.Enqueue(nb);
                        }
                    }
                }

                // Assign to cluster if unassigned
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
        Enumerable.Range(0, pts.Length - k).Aggregate(Enumerable.Range(0, pts.Length).ToArray(), (a, _) => Enumerable.Range(0, pts.Length).SelectMany(i => Enumerable.Range(i + 1, pts.Length - i - 1).Where(j => a[i] != a[j]).Select(j => (a[i], a[j], pts[i].DistanceTo(pts[j])))).OrderBy(t => t.Item3).First() is (int c1, int c2, double) ? [.. Enumerable.Range(0, a.Length).Select(i => a[i] == c2 ? c1 : a[i] > c2 ? a[i] - 1 : a[i])] : a);

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
    internal static Result<Point3d[]> ConvexHull2D(Point3d[] points) {
        const double Epsilon = 1e-8;
        double z0 = points.Length > 0 ? points[0].Z : 0.0;
        bool allSameZ = true;
        for (int i = 1; i < points.Length; i++) {
            if (System.Math.Abs(points[i].Z - z0) > Epsilon) {
                allSameZ = false;
                break;
            }
        }
        return points.Length < 3
            ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidCount.WithContext("ConvexHull2D requires at least 3 points"))
            : !allSameZ
                ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidPlane.WithContext("ConvexHull2D requires all points to have the same Z coordinate (coplanar in XY plane)"))
                : BuildConvexHull2D(points: points);
    }

    private static Result<Point3d[]> BuildConvexHull2D(Point3d[] points) {
        Point3d[] pts = [.. points.OrderBy(static p => p.X).ThenBy(static p => p.Y),];

        List<Point3d> lower = [];
        for (int i = 0; i < pts.Length; i++) {
            while (lower.Count >= 2 && CrossProduct2D(lower[^2], lower[^1], pts[i]) <= 0.0) {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(pts[i]);
        }

        List<Point3d> upper = [];
        for (int i = pts.Length - 1; i >= 0; i--) {
            while (upper.Count >= 2 && CrossProduct2D(upper[^2], upper[^1], pts[i]) <= 0.0) {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(pts[i]);
        }

        return ResultFactory.Create<Point3d[]>(value: [.. lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)),]);
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CrossProduct2D(Point3d o, Point3d a, Point3d b) =>
        ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
}
