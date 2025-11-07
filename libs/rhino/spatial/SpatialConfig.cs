using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Configuration constants and validation modes for spatial indexing operations.</summary>
internal static class SpatialConfig {
    /// <summary>Default buffer size for ArrayPool allocations in range search operations.</summary>
    internal const int DefaultBufferSize = 2048;

    /// <summary>Large buffer size for operations with expected high result counts.</summary>
    internal const int LargeBufferSize = 4096;

    /// <summary>Validation mode configuration mapping input/query type pairs to validation strategies.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), V> ValidationModes =
        new Dictionary<(Type, Type), V> {
            [(typeof(Point3d[]), typeof(Sphere))] = V.None,
            [(typeof(Point3d[]), typeof(BoundingBox))] = V.None,
            [(typeof(Point3d[]), typeof(ValueTuple<Point3d[], int>))] = V.None,
            [(typeof(Point3d[]), typeof(ValueTuple<Point3d[], double>))] = V.None,
            [(typeof(PointCloud), typeof(Sphere))] = V.Degeneracy,
            [(typeof(PointCloud), typeof(BoundingBox))] = V.Degeneracy,
            [(typeof(PointCloud), typeof(ValueTuple<Point3d[], int>))] = V.Degeneracy,
            [(typeof(PointCloud), typeof(ValueTuple<Point3d[], double>))] = V.Degeneracy,
            [(typeof(Mesh), typeof(Sphere))] = V.MeshSpecific,
            [(typeof(Mesh), typeof(BoundingBox))] = V.MeshSpecific,
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = V.MeshSpecific,
            [(typeof(Curve[]), typeof(Sphere))] = V.Degeneracy,
            [(typeof(Curve[]), typeof(BoundingBox))] = V.Degeneracy,
            [(typeof(Surface[]), typeof(Sphere))] = V.BoundingBox,
            [(typeof(Surface[]), typeof(BoundingBox))] = V.BoundingBox,
            [(typeof(Brep[]), typeof(Sphere))] = V.Topology,
            [(typeof(Brep[]), typeof(BoundingBox))] = V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Buffer size configuration mapping input/query type pairs to optimal ArrayPool buffer sizes.</summary>
    internal static readonly FrozenDictionary<(Type Input, Type Query), int> BufferSizes =
        new Dictionary<(Type, Type), int> {
            [(typeof(Point3d[]), typeof(Sphere))] = DefaultBufferSize,
            [(typeof(Point3d[]), typeof(BoundingBox))] = DefaultBufferSize,
            [(typeof(Point3d[]), typeof(ValueTuple<Point3d[], int>))] = DefaultBufferSize,
            [(typeof(Point3d[]), typeof(ValueTuple<Point3d[], double>))] = DefaultBufferSize,
            [(typeof(PointCloud), typeof(Sphere))] = DefaultBufferSize,
            [(typeof(PointCloud), typeof(BoundingBox))] = DefaultBufferSize,
            [(typeof(PointCloud), typeof(ValueTuple<Point3d[], int>))] = DefaultBufferSize,
            [(typeof(PointCloud), typeof(ValueTuple<Point3d[], double>))] = DefaultBufferSize,
            [(typeof(Mesh), typeof(Sphere))] = DefaultBufferSize,
            [(typeof(Mesh), typeof(BoundingBox))] = DefaultBufferSize,
            [(typeof(ValueTuple<Mesh, Mesh>), typeof(double))] = LargeBufferSize,
            [(typeof(Curve[]), typeof(Sphere))] = DefaultBufferSize,
            [(typeof(Curve[]), typeof(BoundingBox))] = DefaultBufferSize,
            [(typeof(Surface[]), typeof(Sphere))] = DefaultBufferSize,
            [(typeof(Surface[]), typeof(BoundingBox))] = DefaultBufferSize,
            [(typeof(Brep[]), typeof(Sphere))] = DefaultBufferSize,
            [(typeof(Brep[]), typeof(BoundingBox))] = DefaultBufferSize,
        }.ToFrozenDictionary();
}
