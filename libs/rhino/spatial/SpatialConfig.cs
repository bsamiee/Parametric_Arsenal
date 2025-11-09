using System.Collections.Frozen;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial algorithm dispatch with FrozenDictionary.</summary>
internal static class SpatialConfig {
    internal const int DefaultBufferSize = 2048;
    internal const int LargeBufferSize = 4096;

    /// <summary>Cluster assignment: 0=KMeans++, 1=DBSCAN, 2=Hierarchical.</summary>
    internal static readonly FrozenDictionary<byte, Func<Point3d[], int, double, double, int[]>> ClusterAssign =
        new Dictionary<byte, Func<Point3d[], int, double, double, int[]>> {
            [0] = static (pts, k, _, tol) => {
                Random rng = new(42);
                Point3d[] c = new Point3d[k];
                c[0] = pts[rng.Next(pts.Length)];
                for (int i = 1; i < k; i++) {
                    double[] d2 = pts.Select(p => Enumerable.Range(0, i).Min(j => p.DistanceTo(c[j]))).Select(d => d * d).ToArray();
                    double t = rng.NextDouble() * d2.Sum();
                    int idx = 0;
                    for (double sum = 0; idx < pts.Length && (sum += d2[idx]) < t; idx++) { }
                    c[i] = pts[idx < pts.Length ? idx : pts.Length - 1];
                }
                int[] a = new int[pts.Length];
                for (int iter = 0; iter < 100; iter++) {
                    for (int i = 0; i < pts.Length; i++) {
                        a[i] = Enumerable.Range(0, k).OrderBy(j => pts[i].DistanceTo(c[j])).First();
                    }
                    Point3d[] nc = Enumerable.Range(0, k).Select(j => {
                        IEnumerable<int> m = Enumerable.Range(0, pts.Length).Where(i => a[i] == j);
                        return m.Any() ? new Point3d(m.Average(i => pts[i].X), m.Average(i => pts[i].Y), m.Average(i => pts[i].Z)) : c[j];
                    }).ToArray();
                    if (Enumerable.Range(0, k).Max(j => c[j].DistanceTo(nc[j])) < tol) {
                        break;
                    }
                    c = nc;
                }
                return a;
            },
            [1] = static (pts, _, eps, _) => {
                int[] a = Enumerable.Repeat(-1, pts.Length).ToArray();
                bool[] v = new bool[pts.Length];
                int cid = 0;
                for (int i = 0; i < pts.Length; i++) {
                    if (v[i]) {
                        continue;
                    }
                    v[i] = true;
                    int[] n = Enumerable.Range(0, pts.Length).Where(j => j != i && pts[i].DistanceTo(pts[j]) <= eps).ToArray();
                    if (n.Length < 4) {
                        continue;
                    }
                    a[i] = cid;
                    Queue<int> q = new(n);
                    while (q.Count > 0) {
                        int cur = q.Dequeue();
                        if (!v[cur]) {
                            v[cur] = true;
                            foreach (int nb in Enumerable.Range(0, pts.Length).Where(j => j != cur && pts[cur].DistanceTo(pts[j]) <= eps && !q.Contains(j) && a[j] is -1)) {
                                q.Enqueue(nb);
                            }
                        }
                        if (a[cur] is -1) {
                            a[cur] = cid;
                        }
                    }
                    cid++;
                }
                return a;
            },
            [2] = static (pts, k, _, _) => {
                int[] a = Enumerable.Range(0, pts.Length).ToArray();
                for (int nc = pts.Length; nc > k; nc--) {
                    (int c1, int c2, double _) = Enumerable.Range(0, pts.Length).SelectMany(i => Enumerable.Range(i + 1, pts.Length - i - 1).Where(j => a[i] != a[j]).Select(j => (a[i], a[j], pts[i].DistanceTo(pts[j])))).OrderBy(t => t.Item3).First();
                    for (int i = 0; i < a.Length; i++) {
                        a[i] = a[i] == c2 ? c1 : a[i] > c2 ? a[i] - 1 : a[i];
                    }
                }
                return a;
            },
        }.ToFrozenDictionary();

    internal static (Point3d, double[])[] BuildClusters(Point3d[] pts, int[] assigns, int numClusters) =>
        Enumerable.Range(0, numClusters).Select(c => {
            IEnumerable<int> m = Enumerable.Range(0, pts.Length).Where(i => assigns[i] == c);
            Point3d ct = m.Any() ? new Point3d(m.Average(i => pts[i].X), m.Average(i => pts[i].Y), m.Average(i => pts[i].Z)) : Point3d.Origin;
            return (ct, m.Select(i => pts[i].DistanceTo(ct)).ToArray());
        }).ToArray();
}
