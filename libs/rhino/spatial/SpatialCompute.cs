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
        (new Random(SpatialConfig.KMeansSeed), new Point3d[k], new int[pts.Length]) switch {
            (Random rng, Point3d[] c, int[] a) => (c[0] = pts[rng.Next(pts.Length)]) is Point3d && Enumerable.Range(1, k - 1).Aggregate(c, (centroids, i) => pts.Select(p => Enumerable.Range(0, i).Min(j => p.DistanceTo(centroids[j]))).Select(d => d * d).ToArray() is double[] d2 && d2.Sum() is double sum && Enumerable.Range(0, pts.Length).Aggregate((0.0, pts.Length - 1), (state, j) => (state.Item1 + d2[j]) >= rng.NextDouble() * sum && state.Item2 == pts.Length - 1 ? (state.Item1 + d2[j], j) : (state.Item1 + d2[j], state.Item2)).Item2 is int idx ? (centroids[i] = pts[idx]) is Point3d && centroids : centroids) is Point3d[] init && Enumerable.Range(0, maxIter).Aggregate((init, a), (state, _) => Enumerable.Range(0, pts.Length).Select(i => state.Item2[i] = Enumerable.Range(0, k).OrderBy(j => pts[i].DistanceTo(state.Item1[j])).First()).ToArray() is int[] && Enumerable.Range(0, k).Select(j => Enumerable.Range(0, pts.Length).Where(i => state.Item2[i] == j) is IEnumerable<int> m ? Centroid(m, pts) : state.Item1[j]).ToArray() is Point3d[] nc && Enumerable.Range(0, k).Max(j => state.Item1[j].DistanceTo(nc[j])) < tol ? (state.Item1, state.Item2) : (nc, state.Item2)).Item2,
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int[] DBSCANAssign(Point3d[] pts, double eps, int minPts) =>
        (Enumerable.Repeat(-1, pts.Length).ToArray(), new bool[pts.Length], 0) switch {
            (int[] a, bool[] v, int cid) => Enumerable.Range(0, pts.Length).Aggregate((a, cid), (state, i) => state.Item1[i] switch {
                _ when v[i] => state,
                _ => (v[i] = true) is bool && Enumerable.Range(0, pts.Length).Where(j => j != i && pts[i].DistanceTo(pts[j]) <= eps).ToArray() is int[] n && n.Length < minPts ? state : (state.Item1[i] = state.Item2) is int && new Queue<int>(n).Aggregate(state, (s, cur) => !v[cur] && (v[cur] = true) is bool && Enumerable.Range(0, pts.Length).Where(j => j != cur && pts[cur].DistanceTo(pts[j]) <= eps).Count() >= minPts ? Enumerable.Range(0, pts.Length).Where(j => j != cur && pts[cur].DistanceTo(pts[j]) <= eps && s.Item1[j] is -1).Aggregate(s, (ss, nb) => { new Queue<int>(n).Enqueue(nb); return (ss.Item1[cur] = ss.Item1[cur] is -1 ? ss.Item2 : ss.Item1[cur]) is int && ss; }) : ((s.Item1[cur] = s.Item1[cur] is -1 ? s.Item2 : s.Item1[cur]) is int && s)) is (int[], int) && (state.Item1, state.Item2 + 1),
            }).Item1,
        };

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
        (Enumerable.Range(0, geometry.Length).Aggregate(new RTree(), (tree, i) => { _ = tree.Insert(geometry[i].GetBoundingBox(accurate: true), i); return tree; }), direction / direction.Length, new List<(int, double, double)>()) switch {
            (RTree tree, Vector3d dir, List<(int, double, double)> results) => tree.Search(new BoundingBox(new Point3d(-maxDist, -maxDist, -maxDist), new Point3d(maxDist, maxDist, maxDist)), (_, args) => (geometry[args.Id].GetBoundingBox(accurate: false).Center - Point3d.Origin, 0.0, 0.0) is (Vector3d toGeom, double dist, double angle) && (toGeom.Length, dist > context.AbsoluteTolerance ? Vector3d.VectorAngle(dir, toGeom / toGeom.Length) : 0.0) is (double d, double a) && d * (1.0 + (angleWeight * a)) <= maxDist ? results.Add((args.Id, d, a)) : false) is bool && ResultFactory.Create(value: results.OrderBy(static r => r.Item2).ToArray()),
        };
}
