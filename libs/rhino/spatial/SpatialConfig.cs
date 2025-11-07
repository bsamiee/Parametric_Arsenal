using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Configuration constants and algorithm dispatch for spatial indexing operations.</summary>
internal static class SpatialConfig {
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
}
