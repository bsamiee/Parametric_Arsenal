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
        indices.Any() ? new Point3d(indices.Average(i => pts[i].X), indices.Average(i => pts[i].Y), indices.Average(i => pts[i].Z)) : Point3d.Origin;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point3d ExtractCentroid(GeometryBase g) => g switch {
        Curve c when AreaMassProperties.Compute(c) is { } amp => amp.Centroid,
        Surface s when AreaMassProperties.Compute(s) is { } amp => amp.Centroid,
        Brep b when VolumeMassProperties.Compute(b) is { } vmp => vmp.Centroid,
        Mesh m when VolumeMassProperties.Compute(m) is { } vmp => vmp.Centroid,
        _ => g.GetBoundingBox(accurate: false).Center,
    };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d, double[])[]> Cluster<T>(T[] geometry, byte algorithm, int k, double epsilon, IGeometryContext context) where T : GeometryBase =>
        SpatialConfig.ClusterParams.TryGetValue(algorithm, out (int maxIter, int minPts) config) && ((algorithm is 0 or 2 && k > 0) || (algorithm is 1 && epsilon > 0)) switch {
            true => geometry.Select(ExtractCentroid).ToArray() is Point3d[] pts && algorithm switch {
                0 => KMeansAssign(pts, k, context.AbsoluteTolerance, config.maxIter),
                1 => DBSCANAssign(pts, epsilon, config.minPts),
                2 => HierarchicalAssign(pts, k),
                _ => [],
            } is int[] assigns && (algorithm is 1 ? assigns.Max() + 1 : k) is int nc && nc > 0
                ? ResultFactory.Create(value: Enumerable.Range(0, nc).Select(c => Enumerable.Range(0, pts.Length).Where(i => assigns[i] == c) is IEnumerable<int> m ? (Centroid(m, pts), m.Select(i => pts[i].DistanceTo(Centroid(m, pts))).ToArray()) : (Point3d.Origin, [])).ToArray())
                : ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed),
            false => ResultFactory.Create<(Point3d, double[])[]>(error: algorithm is 0 or 2 ? E.Spatial.InvalidClusterK : E.Spatial.InvalidEpsilon),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int[] KMeansAssign(Point3d[] pts, int k, double tol, int maxIter) =>
        Enumerable.Range(1, k - 1).Aggregate(
            (Rng: new Random(SpatialConfig.KMeansSeed), Centroids: new[] { pts[new Random(SpatialConfig.KMeansSeed).Next(pts.Length)] }.Concat(Enumerable.Repeat(Point3d.Origin, k - 1)).ToArray()),
            (state, i) => pts.Select(p => Enumerable.Range(0, i).Min(j => p.DistanceTo(state.Centroids[j]))).Select(d => d * d).ToArray() is double[] d2
                ? d2.Sum() is double sum && state.Rng.NextDouble() * sum is double target
                    ? state.Centroids.Select((c, idx) => idx == i ? pts[Enumerable.Range(0, pts.Length).Aggregate((Cumul: 0.0, Sel: pts.Length - 1), (s, j) => (s.Cumul + d2[j] >= target && s.Sel == pts.Length - 1) ? (s.Cumul + d2[j], j) : (s.Cumul + d2[j], s.Sel)).Sel] : c).ToArray() is Point3d[] newCentroids ? (state.Rng, newCentroids) : state
                    : state
                : state) is (Random _, Point3d[] init)
            ? Enumerable.Range(0, maxIter).Aggregate(
                (Centroids: init, Assignments: new int[pts.Length], MaxShift: double.MaxValue),
                (s, _) => s.MaxShift < tol ? s
                    : Enumerable.Range(0, pts.Length).Select(i => Enumerable.Range(0, k).OrderBy(j => pts[i].DistanceTo(s.Centroids[j])).First()).ToArray() is int[] assignments
                        ? Enumerable.Range(0, k).Select(j => Centroid(Enumerable.Range(0, pts.Length).Where(i => assignments[i] == j), pts)).ToArray() is Point3d[] newCentroids
                            ? (newCentroids, assignments, Enumerable.Range(0, k).Max(j => s.Centroids[j].DistanceTo(newCentroids[j])))
                            : s
                        : s).Assignments
            : new int[pts.Length];

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int[] DBSCANAssign(Point3d[] pts, double eps, int minPts) =>
        Enumerable.Range(0, pts.Length).Aggregate(
            (Assignments: Enumerable.Repeat(-1, pts.Length).ToArray(), Visited: new bool[pts.Length], ClusterId: 0),
            (state, i) => state.Visited[i] switch {
                true => state,
                false => (state.Visited[i] = true) is bool && Enumerable.Range(0, pts.Length).Where(j => j != i && pts[i].DistanceTo(pts[j]) <= eps).ToArray() is int[] neighbors
                    ? neighbors.Length < minPts ? state
                        : (state.Assignments[i] = state.ClusterId) is int && Enumerable.Range(0, neighbors.Length).Aggregate((State: state, Queue: new Queue<int>(neighbors)), (ctx, _) => ctx.Queue.Count is 0 ? ctx
                            : ctx.Queue.Dequeue() is int cur && ctx.State.Visited[cur] switch {
                                true => (ctx.State, ctx.Queue),
                                false => (ctx.State.Visited[cur] = true) is bool && Enumerable.Range(0, pts.Length).Where(j => j != cur && pts[cur].DistanceTo(pts[j]) <= eps).ToArray() is int[] curNbrs
                                    ? curNbrs.Length >= minPts ? curNbrs.Where(nb => ctx.State.Assignments[nb] is -1).Aggregate(ctx, (c, nb) => (c.State, c.Queue.Enqueue(nb) is ValueTuple ? c.Queue : c.Queue)) : ctx
                                    : ctx
                            } is (State: (int[] a, bool[] v, int cid), Queue: Queue<int> q) && (a, v, ((a[cur] = a[cur] is -1 ? cid : a[cur]) is int && cid))) is ((int[] finalA, bool[] finalV, int finalCid), Queue<int> _)
                            ? (finalA, finalV, finalCid + 1)
                            : state
                    : state,
            }).Assignments;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int[] HierarchicalAssign(Point3d[] pts, int k) =>
        Enumerable.Range(0, pts.Length - k).Aggregate(Enumerable.Range(0, pts.Length).ToArray(), (a, _) => Enumerable.Range(0, pts.Length).SelectMany(i => Enumerable.Range(i + 1, pts.Length - i - 1).Where(j => a[i] != a[j]).Select(j => (a[i], a[j], pts[i].DistanceTo(pts[j])))).OrderBy(t => t.Item3).First() is (int c1, int c2, double _) ? Enumerable.Range(0, a.Length).Select(i => a[i] == c2 ? c1 : a[i] > c2 ? a[i] - 1 : a[i]).ToArray() : a);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Curve[], double[])> MedialAxis(Brep brep, double tolerance, IGeometryContext context) =>
        (brep.Faces.Count is 1 && brep.Faces[0].IsPlanar(tolerance: context.AbsoluteTolerance), brep.Edges.Where(static e => e.Valence == EdgeAdjacency.Naked).Select(static e => e.DuplicateCurve()).Where(static c => c is not null).ToArray()) switch {
            (false, _) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.NonPlanarNotSupported),
            (true, { Length: 0 }) => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("No boundary")),
            (true, Curve[] edges) when Curve.JoinCurves(edges, joinTolerance: tolerance, preserveDirection: false).FirstOrDefault() is Curve joined && joined.IsClosed && joined.TryGetPlane(out Plane plane, tolerance: tolerance) && joined.Offset(plane: plane, distance: tolerance * SpatialConfig.MedialAxisOffsetMultiplier, tolerance: tolerance, CurveOffsetCornerStyle.Sharp) is { Length: > 0 } offsets => ResultFactory.Create(value: (offsets, offsets.Select(static c => c.GetLength()).ToArray())),
            _ => ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("Not closed planar or offset failed")),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(int, double, double)[]> ProximityField(GeometryBase[] geometry, Vector3d direction, double maxDist, double angleWeight, IGeometryContext context) =>
        Enumerable.Range(0, geometry.Length).Aggregate((Tree: new RTree(), Bounds: BoundingBox.Empty), (state, i) => geometry[i].GetBoundingBox(accurate: true) is BoundingBox bbox
            ? (state.Tree.Insert(bbox, i) is bool && state.Tree, state.Bounds.Union(bbox) is BoundingBox && state.Bounds.Union(bbox))
            : state) is (RTree tree, BoundingBox bounds) && (direction / direction.Length, bounds, new List<(int, double, double)>()) is (Vector3d dir, BoundingBox searchBounds, List<(int, double, double)> results)
                ? searchBounds.Inflate(maxDist) is BoundingBox && tree.Search(searchBounds, (_, args) => geometry[args.Id].GetBoundingBox(accurate: false).Center - Point3d.Origin is Vector3d toGeom
                    ? toGeom.Length is double dist && (dist > context.AbsoluteTolerance ? Vector3d.VectorAngle(dir, toGeom / dist) : 0.0) is double angle && dist * (1.0 + (angleWeight * angle)) is double weightedDist
                        ? weightedDist <= maxDist ? results.Add((args.Id, dist, angle)) is ValueTuple && false : false
                        : false
                    : false) is bool && ResultFactory.Create(value: results.OrderBy(static r => r.Item2).ToArray())
                : ResultFactory.Create<(int, double, double)[]>(error: E.Spatial.ProximityFieldFailed);
}
