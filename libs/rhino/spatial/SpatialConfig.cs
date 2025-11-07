using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Configuration constants and dispatch tables for spatial indexing operations.</summary>
internal static class SpatialConfig {
    /// <summary>Default buffer size for RTree search operations.</summary>
    internal const int DefaultBufferSize = 2048;

    /// <summary>Large buffer size for mesh overlap and complex proximity queries.</summary>
    internal const int LargeBufferSize = 4096;

    /// <summary>RTree factory configuration mapping source types to construction strategies for optimal tree structure.</summary>
    internal static readonly FrozenDictionary<Type, Func<object, RTree>> TreeFactories =
        new Dictionary<Type, Func<object, RTree>> {
            [typeof(Point3d[])] = s => RTree.CreateFromPointArray((Point3d[])s) ?? new RTree(),
            [typeof(PointCloud)] = s => RTree.CreatePointCloudTree((PointCloud)s) ?? new RTree(),
            [typeof(Mesh)] = s => RTree.CreateMeshFaceTree((Mesh)s) ?? new RTree(),
            [typeof(Curve[])] = s => SpatialCore.BuildGeometryArrayTree((Curve[])s),
            [typeof(Surface[])] = s => SpatialCore.BuildGeometryArrayTree((Surface[])s),
            [typeof(Brep[])] = s => SpatialCore.BuildGeometryArrayTree((Brep[])s),
        }.ToFrozenDictionary();

    /// <summary>Algorithm configuration mapping input/query type pairs to validation modes and buffer strategies.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), (V Mode, int BufferSize)> AlgorithmConfig =
        new Dictionary<(Type, Type), (V, int)> {
            [(typeof(Point3d[]), typeof(Sphere))] = (V.None, DefaultBufferSize),
            [(typeof(Point3d[]), typeof(BoundingBox))] = (V.None, DefaultBufferSize),
            [(typeof(Point3d[]), typeof((Point3d[], int)))] = (V.None, DefaultBufferSize),
            [(typeof(Point3d[]), typeof((Point3d[], double)))] = (V.None, DefaultBufferSize),
            [(typeof(PointCloud), typeof(Sphere))] = (V.Degeneracy, DefaultBufferSize),
            [(typeof(PointCloud), typeof(BoundingBox))] = (V.Degeneracy, DefaultBufferSize),
            [(typeof(PointCloud), typeof((Point3d[], int)))] = (V.Degeneracy, DefaultBufferSize),
            [(typeof(PointCloud), typeof((Point3d[], double)))] = (V.Degeneracy, DefaultBufferSize),
            [(typeof(Mesh), typeof(Sphere))] = (V.MeshSpecific, DefaultBufferSize),
            [(typeof(Mesh), typeof(BoundingBox))] = (V.MeshSpecific, DefaultBufferSize),
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = (V.MeshSpecific, LargeBufferSize),
            [(typeof(Curve[]), typeof(Sphere))] = (V.Degeneracy, DefaultBufferSize),
            [(typeof(Curve[]), typeof(BoundingBox))] = (V.Degeneracy, DefaultBufferSize),
            [(typeof(Surface[]), typeof(Sphere))] = (V.BoundingBox, DefaultBufferSize),
            [(typeof(Surface[]), typeof(BoundingBox))] = (V.BoundingBox, DefaultBufferSize),
            [(typeof(Brep[]), typeof(Sphere))] = (V.Topology, DefaultBufferSize),
            [(typeof(Brep[]), typeof(BoundingBox))] = (V.Topology, DefaultBufferSize),
        }.ToFrozenDictionary();
}
