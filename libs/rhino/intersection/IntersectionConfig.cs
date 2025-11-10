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
}
