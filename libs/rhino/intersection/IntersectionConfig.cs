using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Type-pair validation mode mapping for 40+ intersection combinations.</summary>
internal static class IntersectionConfig {
    /// <summary>Validation mode mapping for intersection type pairs.</summary>
    internal static readonly FrozenDictionary<(Type, Type), V> ValidationModes =
        new Dictionary<(Type, Type), V> {
            [(typeof(Curve), typeof(Curve))] = V.Standard | V.Degeneracy,
            [(typeof(NurbsCurve), typeof(NurbsCurve))] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [(typeof(PolyCurve), typeof(Curve))] = V.Standard | V.Degeneracy | V.PolycurveStructure,
            [(typeof(Curve), typeof(Surface))] = V.Standard,
            [(typeof(Curve), typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry,
            [(typeof(Curve), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Curve), typeof(Extrusion))] = V.Standard | V.ExtrusionGeometry,
            [(typeof(Curve), typeof(BrepFace))] = V.Standard | V.Topology,
            [(typeof(Curve), typeof(Plane))] = V.Standard | V.Degeneracy,
            [(typeof(Curve), typeof(Line))] = V.Standard | V.Degeneracy,
            [(typeof(Brep), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Brep), typeof(Plane))] = V.Standard | V.Topology,
            [(typeof(Brep), typeof(Surface))] = V.Standard | V.Topology,
            [(typeof(Extrusion), typeof(Extrusion))] = V.Standard | V.ExtrusionGeometry,
            [(typeof(Surface), typeof(Surface))] = V.Standard | V.UVDomain,
            [(typeof(NurbsSurface), typeof(NurbsSurface))] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [(typeof(Mesh), typeof(Mesh))] = V.MeshSpecific,
            [(typeof(Mesh), typeof(Ray3d))] = V.MeshSpecific,
            [(typeof(Mesh), typeof(Plane))] = V.MeshSpecific,
            [(typeof(Mesh), typeof(Line))] = V.MeshSpecific,
            [(typeof(Mesh), typeof(PolylineCurve))] = V.MeshSpecific,
            [(typeof(Line), typeof(Line))] = V.Standard,
            [(typeof(Line), typeof(BoundingBox))] = V.Standard,
            [(typeof(Line), typeof(Plane))] = V.Standard,
            [(typeof(Line), typeof(Sphere))] = V.Standard,
            [(typeof(Line), typeof(Cylinder))] = V.Standard,
            [(typeof(Line), typeof(Circle))] = V.Standard,
            [(typeof(Plane), typeof(Plane))] = V.Standard,
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = V.Standard,
            [(typeof(Plane), typeof(Circle))] = V.Standard,
            [(typeof(Plane), typeof(Sphere))] = V.Standard,
            [(typeof(Plane), typeof(BoundingBox))] = V.Standard,
            [(typeof(Sphere), typeof(Sphere))] = V.Standard,
            [(typeof(Circle), typeof(Circle))] = V.Standard,
            [(typeof(Arc), typeof(Arc))] = V.Standard,
            [(typeof(Point3d[]), typeof(Brep[]))] = V.Standard | V.Topology,
            [(typeof(Point3d[]), typeof(Mesh[]))] = V.MeshSpecific,
            [(typeof(Ray3d), typeof(GeometryBase[]))] = V.Standard,
        }.ToFrozenDictionary();

    /// <summary>Validation mode mapping for trim operations by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, V> TrimValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy | V.PolycurveStructure,
            [typeof(LineCurve)] = V.Standard | V.Degeneracy,
            [typeof(ArcCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard | V.UVDomain,
            [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [typeof(BrepFace)] = V.Standard | V.Topology,
        }.ToFrozenDictionary();

    /// <summary>Validation mode mapping for split operations by geometry type.</summary>
    internal static readonly FrozenDictionary<Type, V> SplitValidationModes =
        new Dictionary<Type, V> {
            [typeof(Curve)] = V.Standard | V.Degeneracy,
            [typeof(NurbsCurve)] = V.Standard | V.Degeneracy | V.NurbsGeometry,
            [typeof(PolyCurve)] = V.Standard | V.Degeneracy | V.PolycurveStructure,
            [typeof(LineCurve)] = V.Standard | V.Degeneracy,
            [typeof(ArcCurve)] = V.Standard | V.Degeneracy,
            [typeof(PolylineCurve)] = V.Standard | V.Degeneracy,
            [typeof(Surface)] = V.Standard | V.UVDomain,
            [typeof(NurbsSurface)] = V.Standard | V.NurbsGeometry | V.UVDomain,
            [typeof(Brep)] = V.Standard | V.Topology,
            [typeof(BrepFace)] = V.Standard | V.Topology,
            [typeof(Extrusion)] = V.Standard | V.ExtrusionGeometry,
        }.ToFrozenDictionary();

    /// <summary>Retrieves validation mode for trim operations with inheritance fallback.</summary>
    internal static V GetTrimValidationMode(Type geometryType) =>
        TrimValidationModes.TryGetValue(geometryType, out V exact) ? exact :
            TrimValidationModes.Where(kv => kv.Key.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(V.Standard)
                .First();

    /// <summary>Retrieves validation mode for split operations with inheritance fallback.</summary>
    internal static V GetSplitValidationMode(Type geometryType) =>
        SplitValidationModes.TryGetValue(geometryType, out V exact) ? exact :
            SplitValidationModes.Where(kv => kv.Key.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(V.Standard)
                .First();
}
