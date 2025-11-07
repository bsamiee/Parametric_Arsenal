using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration constants and validation dispatch for extraction operations.</summary>
internal static class ExtractionConfig {
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(GeometryBase))] = V.Standard,
            [(1, typeof(Brep))] = V.Standard | V.MassProperties,
            [(1, typeof(Curve))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Mesh))] = V.Standard | V.MassProperties,
            [(1, typeof(PointCloud))] = V.Standard,
            [(2, typeof(GeometryBase))] = V.BoundingBox,
            [(3, typeof(NurbsCurve))] = V.Standard,
            [(3, typeof(NurbsSurface))] = V.Standard,
            [(3, typeof(Curve))] = V.Standard,
            [(3, typeof(Surface))] = V.Standard,
            [(4, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(5, typeof(Curve))] = V.Tolerance,
            [(6, typeof(Brep))] = V.Standard | V.Topology,
            [(6, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(6, typeof(Curve))] = V.Standard,
            [(7, typeof(Brep))] = V.Standard | V.Topology,
            [(7, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(10, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(10, typeof(Surface))] = V.Standard,
            [(11, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(12, typeof(Curve))] = V.Standard,
            [(13, typeof(Curve))] = V.Standard,
        }.ToFrozenDictionary();

    internal static V GetValidationMode(byte kind, Type geometryType) =>
        ValidationModes.TryGetValue((kind, geometryType), out V exact)
            ? exact
            : ValidationModes
                .Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(V.Standard)
                .First();
}
