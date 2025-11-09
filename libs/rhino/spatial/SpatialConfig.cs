using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing validation modes and buffer allocation strategies.</summary>
internal static class SpatialConfig {
    /// <summary>Validation mode mapping for spatial input geometry types.</summary>
    internal static readonly FrozenDictionary<Type, V> ValidationModes =
        new Dictionary<Type, V> {
            [typeof(Point3d[])] = V.None,
            [typeof(PointCloud)] = V.Standard,
            [typeof(Mesh)] = V.MeshSpecific,
            [typeof(Curve[])] = V.Degeneracy,
            [typeof(Surface[])] = V.BoundingBox,
            [typeof(Brep[])] = V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Standard buffer: 2048 elements for basic sphere/box queries.</summary>
    internal const int DefaultBufferSize = 2048;

    /// <summary>Extended buffer: 4096 elements for mesh overlap and proximity queries.</summary>
    internal const int LargeBufferSize = 4096;
}
