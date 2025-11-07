using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Configuration constants and dispatch tables for spatial operations.</summary>
internal static class SpatialConfig {
    /// <summary>RTree factory configuration mapping source types to construction strategies for optimal tree structure.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, RTree>> TreeFactories =
        new Dictionary<Type, Func<object, RTree>> {
            [typeof(Point3d[])] = s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(),
            [typeof(PointCloud)] = s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(),
            [typeof(Mesh)] = s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(),
            [typeof(Curve[])] = s => BuildGeometryArrayTree((Curve[])s),
            [typeof(Surface[])] = s => BuildGeometryArrayTree((Surface[])s),
            [typeof(Brep[])] = s => BuildGeometryArrayTree((Brep[])s),
        }.ToFrozenDictionary();

    /// <summary>Algorithm configuration mapping input/query type pairs to validation modes and buffer strategies.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (V Mode, int BufferSize)> AlgorithmConfig =
        new Dictionary<(Type, Type), (V, int)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (V.None, 2048),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (V.None, 2048),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (V.None, 2048),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (V.None, 2048),
            [(typeof(PointCloud), typeof(Sphere))] = (V.Degeneracy, 2048),
            [(typeof(PointCloud), typeof(BoundingBox))] = (V.Degeneracy, 2048),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (V.Degeneracy, 2048),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (V.Degeneracy, 2048),
            [(typeof(Mesh), typeof(Sphere))] = (V.MeshSpecific, 2048),
            [(typeof(Mesh), typeof(BoundingBox))] = (V.MeshSpecific, 2048),
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = (V.MeshSpecific, 4096),
            [(typeof(Curve[]), typeof(Sphere))] = (V.Degeneracy, 2048),
            [(typeof(Curve[]), typeof(BoundingBox))] = (V.Degeneracy, 2048),
            [(typeof(Surface[]), typeof(Sphere))] = (V.BoundingBox, 2048),
            [(typeof(Surface[]), typeof(BoundingBox))] = (V.BoundingBox, 2048),
            [(typeof(Brep[]), typeof(Sphere))] = (V.Topology, 2048),
            [(typeof(Brep[]), typeof(BoundingBox))] = (V.Topology, 2048),
        }.ToFrozenDictionary();

    private static RTree BuildGeometryArrayTree<T>(T[] geometries) where T : GeometryBase {
        RTree tree = new();
        for (int i = 0; i < geometries.Length; i++) {
            _ = tree.Insert(geometries[i].GetBoundingBox(accurate: true), i);
        }
        return tree;
    }
}
