using System.Buffers;
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
    [Pure]
    internal static Result<(Point3d Centroid, double[] Radii)[]> ClusterKMeans<T>(T[] geometry, int k, IGeometryContext context) where T : GeometryBase {
        Point3d[] points = ExtractCentroids(geometry: geometry);
        return k > points.Length
            ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.KExceedsPointCount)
            : ResultFactory.Create(value: ComputeClusterResults(assignments: KMeansAssign(pts: points, k: k, tol: context.AbsoluteTolerance, maxIter: SpatialConfig.KMeansMaxIterations), allPoints: points));
    }

    [Pure]
    internal static Result<(Point3d Centroid, double[] Radii)[]> ClusterDBSCAN<T>(T[] geometry, double epsilon, int minPoints) where T : GeometryBase {
        Point3d[] points = ExtractCentroids(geometry: geometry);
        int[] assignments = DBSCANAssign(pts: points, eps: epsilon, minPts: minPoints);
        int clusterCount = assignments.Where(static a => a >= 0).DefaultIfEmpty(-1).Max() + 1;
        return clusterCount <= 0
            ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed.WithContext("DBSCAN found no clusters"))
            : ResultFactory.Create(value: ComputeClusterResults(assignments: assignments, allPoints: points));
    }

    [Pure]
    internal static Result<(Point3d Centroid, double[] Radii)[]> ClusterHierarchical<T>(T[] geometry, int k) where T : GeometryBase {
        Point3d[] points = ExtractCentroids(geometry: geometry);
        return k > points.Length
            ? ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.KExceedsPointCount)
            : ResultFactory.Create(value: ComputeClusterResults(assignments: HierarchicalAssign(pts: points, k: k), allPoints: points));
    }

    [Pure]
    private static Point3d[] ExtractCentroids<T>(T[] geometry) where T : GeometryBase {
        Point3d[] centroids = new Point3d[geometry.Length];
        for (int i = 0; i < geometry.Length; i++) {
            centroids[i] = geometry[i] switch {
                Mesh m => VolumeMassProperties.Compute(m) is { Centroid: { IsValid: true } c }
                    ? c
                    : m.GetBoundingBox(accurate: false).Center,
                Brep b => VolumeMassProperties.Compute(b) is { Centroid: { IsValid: true } c }
                    ? c
                    : b.GetBoundingBox(accurate: false).Center,
                Surface s => AreaMassProperties.Compute(s) is { Centroid: { IsValid: true } c }
                    ? c
                    : s.GetBoundingBox(accurate: false).Center,
                Curve c => AreaMassProperties.Compute(c) is { Centroid: { IsValid: true } ct }
                    ? ct
                    : c.GetBoundingBox(accurate: false).Center,
                _ => geometry[i].GetBoundingBox(accurate: false).Center,
            };
        }
        return centroids;
    }

    [Pure]
    private static (Point3d Centroid, double[] Radii)[] ComputeClusterResults(int[] assignments, Point3d[] allPoints) {
        int clusterCount = assignments.Max() + 1;
        (Point3d Centroid, double[] Radii)[] results = new (Point3d, double[])[clusterCount];
        
        for (int c = 0; c < clusterCount; c++) {
            int[] members = [.. Enumerable.Range(0, allPoints.Length).Where(i => assignments[i] == c),];
            if (members.Length is 0) {
                results[c] = (Point3d.Origin, []);
                continue;
            }
            
            Vector3d sum = Vector3d.Zero;
            for (int mi = 0; mi < members.Length; mi++) {
                sum += new Vector3d(allPoints[members[mi]]);
            }
            Point3d centroid = Point3d.Origin + (sum / members.Length);
            double[] radii = new double[members.Length];
            for (int mi = 0; mi < members.Length; mi++) {
                radii[mi] = allPoints[members[mi]].DistanceTo(centroid);
            }
            results[c] = (centroid, radii);
        }
        
        return results;
    }

    internal static int[] KMeansAssign(Point3d[] pts, int k, double tol, int maxIter) {
        int[] assignments = new int[pts.Length];
        Point3d[] centroids = new Point3d[k];
        Random rng = new(SpatialConfig.KMeansSeed);
        double[] distSq = ArrayPool<double>.Shared.Rent(pts.Length);

        try {
            // K-means++ initialization - fused distance calculation and sum
            centroids[0] = pts[rng.Next(pts.Length)];
            for (int i = 1; i < k; i++) {
                double sum = 0.0;
                for (int j = 0; j < pts.Length; j++) {
                    double minDist = pts[j].DistanceTo(centroids[0]);
                    for (int c = 1; c < i; c++) {
                        double dist = pts[j].DistanceTo(centroids[c]);
                        minDist = dist < minDist ? dist : minDist;
                    }
                    distSq[j] = minDist * minDist;
                    sum += distSq[j];
                }

                centroids[i] = sum <= tol || sum <= 0.0 ? pts[rng.Next(pts.Length)] : ((Func<Point3d>)(() => {
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

            // Lloyd's algorithm - fused assignment and accumulation
            (Vector3d Sum, int Count)[] clusters = new (Vector3d, int)[k];
            for (int iter = 0; iter < maxIter; iter++) {
                // Reset clusters and assign to nearest centroid in single pass
                for (int i = 0; i < k; i++) {
                    clusters[i] = (Vector3d.Zero, 0);
                }
                
                for (int i = 0; i < pts.Length; i++) {
                    int nearest = 0;
                    double minDist = pts[i].DistanceTo(centroids[0]);
                    for (int j = 1; j < k; j++) {
                        double dist = pts[i].DistanceTo(centroids[j]);
                        (nearest, minDist) = dist < minDist ? (j, dist) : (nearest, minDist);
                    }
                    assignments[i] = nearest;
                    clusters[nearest] = (clusters[nearest].Sum + new Vector3d(pts[i]), clusters[nearest].Count + 1);
                }

                // Recompute centroids and check convergence
                double maxShift = 0.0;
                for (int i = 0; i < k; i++) {
                    Point3d newCentroid = clusters[i].Count > 0 ? (Point3d)(clusters[i].Sum / clusters[i].Count) : centroids[i];
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
        using RTree? tree = pts.Length > SpatialConfig.DBSCANRTreeThreshold ? RTree.CreateFromPointArray(pts) : null;

        int[] GetNeighbors(int idx) {
            if (tree is not null) {
                List<int> buffer = [];
                _ = tree.Search(new Sphere(pts[idx], eps), (_, args) => {
                    if (args.Id != idx) {
                        buffer.Add(args.Id);
                    }
                });
                return [.. buffer,];
            }
            return [.. Enumerable.Range(0, pts.Length).Where(j => j != idx && pts[idx].DistanceTo(pts[j]) <= eps),];
        }

        for (int i = 0; i < pts.Length; i++) {
            if (visited[i]) {
                continue;
            }

            int[] neighbors = GetNeighbors(i);

            if ((neighbors.Length + 1) < minPts) {
                continue;
            }

            visited[i] = true;
            assignments[i] = clusterId;
            Queue<int> queue = new(neighbors);

            while (queue.Count > 0) {
                int cur = queue.Dequeue();
                if (visited[cur]) {
                    continue;
                }

                visited[cur] = true;
                int[] curNeighbors = GetNeighbors(cur);

                if ((curNeighbors.Length + 1) >= minPts) {
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
    internal static int[] HierarchicalAssign(Point3d[] pts, int k) {
        int n = pts.Length;
        int[] assignments = [.. Enumerable.Range(0, n),];
        HashSet<int> activeClusters = [.. Enumerable.Range(0, n),];

        for (int mergeIteration = 0; mergeIteration < n - k; mergeIteration++) {
            (int cluster1, int cluster2, double minDist) = (0, 1, double.MaxValue);
            int[] activeArray = [.. activeClusters.Order(),];

            for (int i = 0; i < activeArray.Length; i++) {
                int c1 = activeArray[i];
                int repr1 = Array.FindIndex(assignments, idx => assignments[idx] == c1);
                if (repr1 == -1) {
                    continue;
                }

                for (int j = i + 1; j < activeArray.Length; j++) {
                    int c2 = activeArray[j];
                    int repr2 = Array.FindIndex(assignments, idx => assignments[idx] == c2);
                    if (repr2 == -1) {
                        continue;
                    }

                    double dist = pts[repr1].DistanceTo(pts[repr2]);
                    (cluster1, cluster2, minDist) = dist < minDist ? (c1, c2, dist) : (cluster1, cluster2, minDist);
                }
            }

            for (int i = 0; i < n; i++) {
                assignments[i] = assignments[i] == cluster2 ? cluster1 : assignments[i];
            }
            _ = activeClusters.Remove(cluster2);
        }

        // Normalize cluster IDs to [0, k-1] to prevent gaps that cause empty clusters in ComputeClusterResults.
        int[] uniqueClusters = [.. activeClusters.Order(),];
        for (int i = 0; i < n; i++) {
            assignments[i] = Array.IndexOf(uniqueClusters, assignments[i]);
        }

        return assignments;
    }

    [Pure]
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
                    Point3d[] samples3D = [.. Enumerable.Range(0, sampleCount).Select(i => boundary.PointAtNormalizedLength((double)i / sampleCount)),];
                    Point3d[] samples2D = [.. samples3D.Select(p => { Point3d pt = p; pt.Transform(toPlane); return pt; }),];
                    return VoronoiDiagram2D(points: samples2D, context: context).Bind(cells => {
                        (Curve?, double)[] skeleton = [.. cells
                            .SelectMany((cell, _) => cell.Length > 0
                                ? Enumerable.Range(0, cell.Length).Select(j => (P1: cell[j], P2: cell[(j + 1) % cell.Length]))
                                : [])
                            .Select(edge => (edge.P1, edge.P2, Mid: new Point3d((edge.P1.X + edge.P2.X) * 0.5, (edge.P1.Y + edge.P2.Y) * 0.5, (edge.P1.Z + edge.P2.Z) * 0.5)))
                            .Select(edge => { Point3d mid3D = edge.Mid; mid3D.Transform(fromPlane); return (edge.P1, edge.P2, edge.Mid, Mid3D: mid3D); })
                            .Where(edge => boundary.Contains(edge.Mid3D, plane, effectiveTolerance) is PointContainment.Inside)
                            .GroupBy(edge => (edge.P1, edge.P2) switch {
                                (Point3d p1, Point3d p2) when p1.X < p2.X - context.AbsoluteTolerance || (Math.Abs(p1.X - p2.X) <= context.AbsoluteTolerance && p1.Y < p2.Y - context.AbsoluteTolerance) => (Min: p1, Max: p2),
                                _ => (Min: edge.P2, Max: edge.P1),
                            })
                            .Select(static g => g.First())
                            .Select(edge => { Point3d p1_3D = edge.P1; p1_3D.Transform(fromPlane); Point3d p2_3D = edge.P2; p2_3D.Transform(fromPlane); return (P1_3D: p1_3D, P2_3D: p2_3D, edge.Mid3D); })
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

    /// <summary>Computes 2D convex hull of XY-coplanar points using Andrew's monotone chain algorithm.</summary>
    [Pure]
    internal static Result<Point3d[]> ConvexHull2D(Point3d[] points, IGeometryContext context) =>
        points.Length < 3 ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidCount.WithContext("ConvexHull2D requires at least 3 points"))
            : points.Length > 1 && points[0].Z is double z0 && points.Skip(1).All(p => RhinoMath.EpsilonEquals(p.Z, z0, epsilon: context.AbsoluteTolerance))
                ? ((Func<Result<Point3d[]>>)(() => {
                    Point3d[] pts = [.. points.OrderBy(static p => p.X).ThenBy(static p => p.Y),];
                    double crossProductTolerance = context.AbsoluteTolerance * context.AbsoluteTolerance;
                    List<Point3d> lower = [];
                    for (int i = 0; i < pts.Length; i++) {
                        while (lower.Count >= 2 && (((lower[^1].X - lower[^2].X) * (pts[i].Y - lower[^2].Y)) - ((lower[^1].Y - lower[^2].Y) * (pts[i].X - lower[^2].X))) <= crossProductTolerance) {
                            lower.RemoveAt(lower.Count - 1);
                        }
                        lower.Add(pts[i]);
                    }
                    List<Point3d> upper = [];
                    for (int i = pts.Length - 1; i >= 0; i--) {
                        while (upper.Count >= 2 && (((upper[^1].X - upper[^2].X) * (pts[i].Y - upper[^2].Y)) - ((upper[^1].Y - upper[^2].Y) * (pts[i].X - upper[^2].X))) <= crossProductTolerance) {
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
    [Pure]
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
                _ = horizon.Remove((b, a)) || horizon.Add((a, b));
                _ = horizon.Remove((c, b)) || horizon.Add((b, c));
                _ = horizon.Remove((a, c)) || horizon.Add((c, a));
            }

            foreach ((int a, int b) in horizon) {
                _ = faces.Add((a, b, i));
            }
            _ = processed.Add(i);
        }

        return ResultFactory.Create<int[][]>(value: [.. faces.Select(f => new int[] { indexed[f.Item1].Index, indexed[f.Item2].Index, indexed[f.Item3].Index }),]);
    }

    /// <summary>Computes 2D Delaunay triangulation using Bowyer-Watson algorithm, returning triangle vertex indices as triples.</summary>
    [Pure]
    internal static Result<int[][]> DelaunayTriangulation2D(Point3d[] points, IGeometryContext context) {
        if (points.Length < 3) { return ResultFactory.Create<int[][]>(error: E.Geometry.InvalidCount.WithContext("DelaunayTriangulation2D requires at least 3 points")); }

        (double z0, double minX, double minY, double maxX, double maxY) = (points[0].Z, points[0].X, points[0].Y, points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++) {
            double x = points[i].X;
            double y = points[i].Y;
            if (!RhinoMath.EpsilonEquals(points[i].Z, z0, epsilon: context.AbsoluteTolerance)) { return ResultFactory.Create<int[][]>(error: E.Geometry.InvalidOrientationPlane.WithContext("DelaunayTriangulation2D requires all points to have the same Z coordinate")); }

            (minX, minY, maxX, maxY) = (x < minX ? x : minX, y < minY ? y : minY, x > maxX ? x : maxX, y > maxY ? y : maxY);
        }
        double extentX = maxX - minX;
        double extentY = maxY - minY;
        double extentTolerance = Math.Max(context.AbsoluteTolerance, RhinoMath.ZeroTolerance);
        if (extentX <= extentTolerance || extentY <= extentTolerance) { return ResultFactory.Create<int[][]>(error: E.Validation.DegenerateGeometry.WithContext("DelaunayTriangulation2D requires non-collinear points")); }

        (double dx, double dy) = (extentX * SpatialConfig.DelaunaySuperTriangleScale, extentY * SpatialConfig.DelaunaySuperTriangleScale);
        Point3d[] superTriangle = [new Point3d(minX - dx, minY - dy, z0), new Point3d(maxX + dx, minY - dy, z0), new Point3d(minX + (extentX * SpatialConfig.DelaunaySuperTriangleCenterWeight), maxY + dy, z0),];
        HashSet<(int, int, int)> triangles = [(points.Length, points.Length + 1, points.Length + 2),];
        Point3d[] allPoints = [.. points, .. superTriangle,];
        for (int i = 0; i < points.Length; i++) {
            (int, int, int)[] badTriangles = [.. triangles.Where(t => IsInCircumcircle(allPoints[t.Item1], allPoints[t.Item2], allPoints[t.Item3], points[i], context)),];
            HashSet<(int, int)> polygon = [];
            foreach ((int a, int b, int c) in badTriangles) {
                _ = polygon.Remove((b, a)) || polygon.Add((a, b));
                _ = polygon.Remove((c, b)) || polygon.Add((b, c));
                _ = polygon.Remove((a, c)) || polygon.Add((c, a));
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
        // Compute twice the signed area of triangle (a,b,c) to check for degeneracy
        double orientation = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        double orientationTolerance = context.AbsoluteTolerance * context.AbsoluteTolerance;
        // Check for degenerate triangle: if orientation is near zero, the points are collinear and the incircle predicate is not meaningful.
        if (Math.Abs(orientation) <= orientationTolerance) {
            return false;
        }

        // Incircle determinant test using squared distances
        double det = (((ax * ax) + (ay * ay)) * ((bx * cy) - (by * cx))) + (((bx * bx) + (by * by)) * ((cx * ay) - (cy * ax))) + (((cx * cx) + (cy * cy)) * ((ax * by) - (ay * bx)));
        // Adjust determinant sign for counter-clockwise orientation to maintain consistent incircle test semantics.
        double adjustedDet = orientation > 0.0 ? det : -det;
        double determinantTolerance = orientationTolerance * orientationTolerance;
        return adjustedDet > determinantTolerance;
    }

    /// <summary>Computes 2D Voronoi diagram from Delaunay triangulation, returning cell vertices for each input point.</summary>
    [Pure]
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
                            double centerX = 0.0;
                            double centerY = 0.0;
                            for (int ci = 0; ci < cell.Count; ci++) {
                                centerX += cell[ci].X;
                                centerY += cell[ci].Y;
                            }
                            centerX /= cell.Count;
                            centerY /= cell.Count;
                            return [.. cell.OrderBy(p => Math.Atan2(p.Y - centerY, p.X - centerX)),];
                        }))()
                        : [];
                }
                return ResultFactory.Create(value: cells);
            }))());
}
