using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Semantic extraction validation mode mapping with type inheritance fallback.</summary>
internal static class ExtractionConfig {
    /// <summary>Validation mode mapping for semantic extraction kinds and geometry types.</summary>
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

    /// <summary>Retrieves validation mode for extraction kind and geometry type with inheritance fallback.</summary>
    internal static V GetValidationMode(byte kind, Type geometryType) {
        (byte, Type) key = (kind, geometryType);
        return ValidationModes.TryGetValue(key, out V exact) ? exact
            : (kind, geometryType.Name) switch {
                (1, "Brep") => V.Standard | V.MassProperties,
                (1, "Curve" or "NurbsCurve" or "LineCurve" or "ArcCurve" or "PolyCurve" or "PolylineCurve") => V.Standard | V.AreaCentroid,
                (1, "Surface" or "NurbsSurface" or "PlaneSurface") => V.Standard | V.AreaCentroid,
                (1, "Mesh") => V.Standard | V.MassProperties,
                (1, "PointCloud") => V.Standard,
                (2, _) => V.BoundingBox,
                (3, _) when geometryType.Name.Contains("Curve", StringComparison.Ordinal) || geometryType.Name.Contains("Surface", StringComparison.Ordinal) => V.Standard,
                (4 or 10 or 11, _) when geometryType.Name.Contains("Curve", StringComparison.Ordinal) => V.Standard | V.Degeneracy,
                (5, _) when geometryType.Name.Contains("Curve", StringComparison.Ordinal) => V.Tolerance,
                (6, "Brep") => V.Standard | V.Topology,
                (6, "Mesh") => V.Standard | V.MeshSpecific,
                (6, _) when geometryType.Name.Contains("Curve", StringComparison.Ordinal) => V.Standard,
                (7, "Brep") => V.Standard | V.Topology,
                (7, "Mesh") => V.Standard | V.MeshSpecific,
                (12 or 13, _) when geometryType.Name.Contains("Curve", StringComparison.Ordinal) => V.Standard,
                (10, _) when geometryType.Name.Contains("Surface", StringComparison.Ordinal) => V.Standard,
                _ => V.Standard,
            };
    }
}
