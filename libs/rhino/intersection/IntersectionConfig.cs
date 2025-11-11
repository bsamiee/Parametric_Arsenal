using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Validation modes and parameters for intersection operations.</summary>
internal static class IntersectionConfig {
    /// <summary>Tangent angle threshold 5° for classification.</summary>
    internal const double TangentAngleThreshold = 0.087266;

    /// <summary>Grazing angle threshold 15° for crossing vs grazing.</summary>
    internal const double GrazingAngleThreshold = 0.261799;

    /// <summary>Near-miss tolerance multiplier 10× context tolerance.</summary>
    internal const double NearMissToleranceMultiplier = 10.0;

    /// <summary>Stability perturbation distance 0.1% of geometry size.</summary>
    internal const double StabilityPerturbationFactor = 0.001;

    /// <summary>Stability sample count 8 directions for perturbation.</summary>
    internal const int StabilitySampleCount = 8;

    /// <summary>Blend score for tangent intersections 1.0.</summary>
    internal const double TangentBlendScore = 1.0;
    /// <summary>Blend score for perpendicular intersections 0.5.</summary>
    internal const double PerpendicularBlendScore = 0.5;
    /// <summary>Blend score for tangent curve-surface 0.8.</summary>
    internal const double CurveSurfaceTangentBlendScore = 0.8;
    /// <summary>Blend score for perpendicular curve-surface 0.4.</summary>
    internal const double CurveSurfacePerpendicularBlendScore = 0.4;

    /// <summary>(TypeA, TypeB) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(Type, Type), (V ModeA, V ModeB)> ValidationModes =
        new Dictionary<(Type, Type), (V, V)> {
            [(typeof(Curve), typeof(Curve))] = (V.Standard | V.Degeneracy, V.Standard | V.Degeneracy),
            [(typeof(NurbsCurve), typeof(NurbsCurve))] = (V.Standard | V.Degeneracy | V.NurbsGeometry, V.Standard | V.Degeneracy | V.NurbsGeometry),
            [(typeof(PolyCurve), typeof(Curve))] = (V.Standard | V.Degeneracy | V.PolycurveStructure, V.Standard | V.Degeneracy),
            [(typeof(Curve), typeof(PolyCurve))] = (V.Standard | V.Degeneracy, V.Standard | V.Degeneracy | V.PolycurveStructure),
            [(typeof(Curve), typeof(Surface))] = (V.Standard | V.Degeneracy, V.Standard | V.UVDomain),
            [(typeof(Surface), typeof(Curve))] = (V.Standard | V.UVDomain, V.Standard | V.Degeneracy),
            [(typeof(Curve), typeof(NurbsSurface))] = (V.Standard | V.Degeneracy, V.Standard | V.NurbsGeometry | V.UVDomain),
            [(typeof(NurbsSurface), typeof(Curve))] = (V.Standard | V.NurbsGeometry | V.UVDomain, V.Standard | V.Degeneracy),
            [(typeof(Curve), typeof(Brep))] = (V.Standard | V.Degeneracy, V.Standard | V.Topology),
            [(typeof(Brep), typeof(Curve))] = (V.Standard | V.Topology, V.Standard | V.Degeneracy),
            [(typeof(Curve), typeof(Extrusion))] = (V.Standard | V.Degeneracy, V.Standard | V.ExtrusionGeometry),
            [(typeof(Extrusion), typeof(Curve))] = (V.Standard | V.ExtrusionGeometry, V.Standard | V.Degeneracy),
            [(typeof(Curve), typeof(BrepFace))] = (V.Standard | V.Degeneracy, V.Standard | V.Topology),
            [(typeof(BrepFace), typeof(Curve))] = (V.Standard | V.Topology, V.Standard | V.Degeneracy),
            [(typeof(Curve), typeof(Plane))] = (V.Standard | V.Degeneracy, V.Standard),
            [(typeof(Plane), typeof(Curve))] = (V.Standard, V.Standard | V.Degeneracy),
            [(typeof(Curve), typeof(Line))] = (V.Standard | V.Degeneracy, V.Standard),
            [(typeof(Line), typeof(Curve))] = (V.Standard, V.Standard | V.Degeneracy),
            [(typeof(Brep), typeof(Brep))] = (V.Standard | V.Topology, V.Standard | V.Topology),
            [(typeof(Brep), typeof(Plane))] = (V.Standard | V.Topology, V.Standard),
            [(typeof(Plane), typeof(Brep))] = (V.Standard, V.Standard | V.Topology),
            [(typeof(Brep), typeof(Surface))] = (V.Standard | V.Topology, V.Standard | V.UVDomain),
            [(typeof(Surface), typeof(Brep))] = (V.Standard | V.UVDomain, V.Standard | V.Topology),
            [(typeof(Extrusion), typeof(Extrusion))] = (V.Standard | V.ExtrusionGeometry, V.Standard | V.ExtrusionGeometry),
            [(typeof(Surface), typeof(Surface))] = (V.Standard | V.UVDomain, V.Standard | V.UVDomain),
            [(typeof(NurbsSurface), typeof(NurbsSurface))] = (V.Standard | V.NurbsGeometry | V.UVDomain, V.Standard | V.NurbsGeometry | V.UVDomain),
            [(typeof(Mesh), typeof(Mesh))] = (V.MeshSpecific, V.MeshSpecific),
            [(typeof(Mesh), typeof(Ray3d))] = (V.MeshSpecific, V.None),
            [(typeof(Ray3d), typeof(Mesh))] = (V.None, V.MeshSpecific),
            [(typeof(Mesh), typeof(Plane))] = (V.MeshSpecific, V.Standard),
            [(typeof(Plane), typeof(Mesh))] = (V.Standard, V.MeshSpecific),
            [(typeof(Mesh), typeof(Line))] = (V.MeshSpecific, V.Standard),
            [(typeof(Line), typeof(Mesh))] = (V.Standard, V.MeshSpecific),
            [(typeof(Mesh), typeof(PolylineCurve))] = (V.MeshSpecific, V.Standard | V.Degeneracy),
            [(typeof(PolylineCurve), typeof(Mesh))] = (V.Standard | V.Degeneracy, V.MeshSpecific),
            [(typeof(Line), typeof(Line))] = (V.Standard, V.Standard),
            [(typeof(Line), typeof(BoundingBox))] = (V.Standard, V.None),
            [(typeof(BoundingBox), typeof(Line))] = (V.None, V.Standard),
            [(typeof(Line), typeof(Plane))] = (V.Standard, V.Standard),
            [(typeof(Plane), typeof(Line))] = (V.Standard, V.Standard),
            [(typeof(Line), typeof(Sphere))] = (V.Standard, V.Standard),
            [(typeof(Sphere), typeof(Line))] = (V.Standard, V.Standard),
            [(typeof(Line), typeof(Cylinder))] = (V.Standard, V.Standard),
            [(typeof(Cylinder), typeof(Line))] = (V.Standard, V.Standard),
            [(typeof(Line), typeof(Circle))] = (V.Standard, V.Standard),
            [(typeof(Circle), typeof(Line))] = (V.Standard, V.Standard),
            [(typeof(Plane), typeof(Plane))] = (V.Standard, V.Standard),
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = (V.Standard, V.Standard),
            [(typeof(Plane), typeof(Circle))] = (V.Standard, V.Standard),
            [(typeof(Circle), typeof(Plane))] = (V.Standard, V.Standard),
            [(typeof(Plane), typeof(Sphere))] = (V.Standard, V.Standard),
            [(typeof(Sphere), typeof(Plane))] = (V.Standard, V.Standard),
            [(typeof(Plane), typeof(BoundingBox))] = (V.Standard, V.None),
            [(typeof(BoundingBox), typeof(Plane))] = (V.None, V.Standard),
            [(typeof(Sphere), typeof(Sphere))] = (V.Standard, V.Standard),
            [(typeof(Circle), typeof(Circle))] = (V.Standard, V.Standard),
            [(typeof(Arc), typeof(Arc))] = (V.Standard, V.Standard),
            [(typeof(Point3d[]), typeof(Brep[]))] = (V.None, V.None),
            [(typeof(Brep[]), typeof(Point3d[]))] = (V.None, V.None),
            [(typeof(Point3d[]), typeof(Mesh[]))] = (V.None, V.None),
            [(typeof(Mesh[]), typeof(Point3d[]))] = (V.None, V.None),
            [(typeof(Ray3d), typeof(GeometryBase[]))] = (V.None, V.None),
            [(typeof(GeometryBase[]), typeof(Ray3d))] = (V.None, V.None),
        }.ToFrozenDictionary();
}
