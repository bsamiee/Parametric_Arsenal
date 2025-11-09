using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Advanced spatial algorithms with FrozenDictionary dispatch.</summary>
internal static class SpatialCompute {
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Point3d Centroid, double[] Radii)[]> Cluster<T>(T[] geometry, byte algorithm, int k, double epsilon, IGeometryContext context) where T : GeometryBase =>
        SpatialConfig.ClusterAssign.TryGetValue(algorithm, out Func<Point3d[], int, double, double, int[]>? assign) switch {
            true when (algorithm is 0 or 2 && k > 0) || (algorithm is 1 && epsilon > 0) => assign(geometry.Select(g => g switch {
                Curve c when AreaMassProperties.Compute(c) is { } amp => amp.Centroid,
                Surface s when AreaMassProperties.Compute(s) is { } amp => amp.Centroid,
                Brep b when VolumeMassProperties.Compute(b) is { } vmp => vmp.Centroid,
                Mesh m when VolumeMassProperties.Compute(m) is { } vmp => vmp.Centroid,
                GeometryBase gb => gb.GetBoundingBox(accurate: false).Center,
            }).ToArray(), k, epsilon, context.AbsoluteTolerance) is int[] a && (algorithm is 1 ? a.Max() + 1 : k) is int nc
                ? ResultFactory.Create(value: SpatialConfig.BuildClusters(geometry.Select(g => g switch {
                    Curve c when AreaMassProperties.Compute(c) is { } amp => amp.Centroid,
                    Surface s when AreaMassProperties.Compute(s) is { } amp => amp.Centroid,
                    Brep b when VolumeMassProperties.Compute(b) is { } vmp => vmp.Centroid,
                    Mesh m when VolumeMassProperties.Compute(m) is { } vmp => vmp.Centroid,
                    GeometryBase gb => gb.GetBoundingBox(accurate: false).Center,
                }).ToArray(), a, nc))
                : ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed),
            true => ResultFactory.Create<(Point3d, double[])[]>(error: algorithm is 0 or 2 ? E.Spatial.InvalidClusterK : E.Spatial.InvalidEpsilon),
            false => ResultFactory.Create<(Point3d, double[])[]>(error: E.Spatial.ClusteringFailed),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(Curve[] Skeleton, double[] Stability)> MedialAxis(Brep brep, double tolerance, IGeometryContext context) =>
        brep.Faces.Count is 1 && brep.Faces[0].IsPlanar(tolerance: context.AbsoluteTolerance)
            ? brep.Edges.Where(static e => e.Valence == EdgeAdjacency.Naked).Select(static e => e.DuplicateCurve()).Where(static c => c is not null).ToArray() is Curve[] edges && edges.Length > 0
                ? Curve.JoinCurves(edges, joinTolerance: tolerance, preserveDirection: false).FirstOrDefault() is Curve joined && joined.IsClosed && joined.TryGetPlane(out Plane plane, tolerance: tolerance)
                    ? joined.Offset(plane: plane, distance: tolerance * 10.0, tolerance: tolerance, CurveOffsetCornerStyle.Sharp) is Curve[] offsets && offsets.Length > 0
                        ? ResultFactory.Create(value: (offsets, offsets.Select(c => c.GetLength()).ToArray()))
                        : ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("Offset failed"))
                    : ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("Not closed planar"))
                : ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.MedialAxisFailed.WithContext("No boundary"))
            : ResultFactory.Create<(Curve[], double[])>(error: E.Spatial.NonPlanarNotSupported);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<(int Index, double Distance, double Angle)[]> ProximityField(GeometryBase[] geometry, Vector3d direction, double maxDist, double angleWeight, IGeometryContext context) {
        RTree tree = new();
        for (int i = 0; i < geometry.Length; i++) {
            _ = tree.Insert(geometry[i].GetBoundingBox(accurate: true), i);
        }
        Vector3d dir = direction / direction.Length;
        List<(int, double, double)> results = [];
        tree.Search(new BoundingBox(new Point3d(-maxDist, -maxDist, -maxDist), new Point3d(maxDist, maxDist, maxDist)), (_, args) => {
            Point3d center = geometry[args.Id].GetBoundingBox(accurate: false).Center;
            Vector3d toGeom = center - Point3d.Origin;
            double dist = toGeom.Length;
            double angle = dist > context.AbsoluteTolerance ? Vector3d.VectorAngle(dir, toGeom / dist) : 0.0;
            double weighted = dist * (1.0 + (angleWeight * angle));
            if (weighted <= maxDist) {
                results.Add((args.Id, dist, angle));
            }
        });
        return ResultFactory.Create(value: results.OrderBy(static r => r.Item2).ToArray());
    }
}
