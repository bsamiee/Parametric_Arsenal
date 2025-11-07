using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Configuration constants and validation dispatch for intersection operations.</summary>
internal static class IntersectionConfig {
    /// <summary>Validation mode configuration mapping geometry type pairs to validation flags.</summary>
    internal static readonly FrozenDictionary<(Type TypeA, Type TypeB), V> ValidationModes =
        new Dictionary<(Type, Type), V> {
            [(typeof(Curve), typeof(Curve))] = V.Standard | V.Degeneracy,
            [(typeof(Curve), typeof(Surface))] = V.Standard,
            [(typeof(Curve), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Curve), typeof(BrepFace))] = V.Standard | V.Topology,
            [(typeof(Curve), typeof(Plane))] = V.Standard | V.Degeneracy,
            [(typeof(Curve), typeof(Line))] = V.Standard | V.Degeneracy,
            [(typeof(Brep), typeof(Brep))] = V.Standard | V.Topology,
            [(typeof(Brep), typeof(Plane))] = V.Standard | V.Topology,
            [(typeof(Brep), typeof(Surface))] = V.Standard | V.Topology,
            [(typeof(Surface), typeof(Surface))] = V.Standard,
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
}
