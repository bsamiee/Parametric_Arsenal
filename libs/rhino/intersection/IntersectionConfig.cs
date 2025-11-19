using System.Collections.Frozen;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Validation modes and parameters for intersection operations using RhinoDoc tolerances via GeometryContext.FromDocument(doc).</summary>
internal static class IntersectionConfig {
    /// <summary>Classification analysis metadata containing angle thresholds and blend quality scores.</summary>
    internal sealed record ClassificationMetadata(
        double TangentAngleThreshold,
        double GrazingAngleThreshold,
        double TangentBlendScore,
        double PerpendicularBlendScore,
        double CurveSurfaceTangentBlendScore,
        double CurveSurfacePerpendicularBlendScore);

    /// <summary>Near-miss analysis metadata containing tolerance multiplier and sample count parameters.</summary>
    internal sealed record NearMissMetadata(
        double ToleranceMultiplier,
        int MaxVertexSamples,
        int MinCurveSamples,
        int MinBrepSamples);

    /// <summary>Stability analysis metadata containing perturbation factor and spherical sample count.</summary>
    internal sealed record StabilityMetadata(
        double PerturbationFactor,
        int SampleCount);

    /// <summary>Classification analysis configuration with angle thresholds in radians and blend scores.</summary>
    internal static readonly ClassificationMetadata Classification = new(
        TangentAngleThreshold: RhinoMath.ToRadians(5.0),
        GrazingAngleThreshold: RhinoMath.ToRadians(15.0),
        TangentBlendScore: 1.0,
        PerpendicularBlendScore: 0.5,
        CurveSurfaceTangentBlendScore: 0.8,
        CurveSurfacePerpendicularBlendScore: 0.4);

    /// <summary>Near-miss analysis configuration with tolerance multiplier and sample count limits.</summary>
    internal static readonly NearMissMetadata NearMiss = new(
        ToleranceMultiplier: 10.0,
        MaxVertexSamples: 1000,
        MinCurveSamples: 3,
        MinBrepSamples: 8);

    /// <summary>Stability analysis configuration with perturbation factor and spherical sample count.</summary>
    internal static readonly StabilityMetadata Stability = new(
        PerturbationFactor: 0.001,
        SampleCount: 8);

    /// <summary>(TypeA, TypeB) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(Type, Type), (V ModeA, V ModeB)> ValidationModes =
        new (Type TypeA, Type TypeB, V ModeA, V ModeB)[] {
            (typeof(Curve), typeof(Curve), V.Standard | V.Degeneracy, V.Standard | V.Degeneracy),
            (typeof(NurbsCurve), typeof(NurbsCurve), V.Standard | V.Degeneracy | V.NurbsGeometry, V.Standard | V.Degeneracy | V.NurbsGeometry),
            (typeof(PolyCurve), typeof(Curve), V.Standard | V.Degeneracy | V.PolycurveStructure, V.Standard | V.Degeneracy),
            (typeof(Curve), typeof(Surface), V.Standard | V.Degeneracy, V.Standard | V.UVDomain),
            (typeof(Curve), typeof(NurbsSurface), V.Standard | V.Degeneracy, V.Standard | V.NurbsGeometry | V.UVDomain),
            (typeof(Curve), typeof(Brep), V.Standard | V.Degeneracy, V.Standard | V.Topology),
            (typeof(Curve), typeof(Extrusion), V.Standard | V.Degeneracy, V.Standard | V.ExtrusionGeometry),
            (typeof(Curve), typeof(BrepFace), V.Standard | V.Degeneracy, V.Standard | V.Topology),
            (typeof(Curve), typeof(Plane), V.Standard | V.Degeneracy, V.Standard),
            (typeof(Curve), typeof(Line), V.Standard | V.Degeneracy, V.Standard),
            (typeof(Brep), typeof(Brep), V.Standard | V.Topology, V.Standard | V.Topology),
            (typeof(Brep), typeof(Plane), V.Standard | V.Topology, V.Standard),
            (typeof(Brep), typeof(Surface), V.Standard | V.Topology, V.Standard | V.UVDomain),
            (typeof(Extrusion), typeof(Extrusion), V.Standard | V.ExtrusionGeometry, V.Standard | V.ExtrusionGeometry),
            (typeof(Surface), typeof(Surface), V.Standard | V.UVDomain, V.Standard | V.UVDomain),
            (typeof(NurbsSurface), typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain, V.Standard | V.NurbsGeometry | V.UVDomain),
            (typeof(Mesh), typeof(Mesh), V.MeshSpecific, V.MeshSpecific),
            (typeof(Mesh), typeof(Ray3d), V.MeshSpecific, V.None),
            (typeof(Mesh), typeof(Plane), V.MeshSpecific, V.Standard),
            (typeof(Mesh), typeof(Line), V.MeshSpecific, V.Standard),
            (typeof(Mesh), typeof(PolylineCurve), V.MeshSpecific, V.Standard | V.Degeneracy),
            (typeof(Line), typeof(Line), V.Standard, V.Standard),
            (typeof(Line), typeof(BoundingBox), V.Standard, V.None),
            (typeof(Line), typeof(Plane), V.Standard, V.Standard),
            (typeof(Line), typeof(Sphere), V.Standard, V.Standard),
            (typeof(Line), typeof(Cylinder), V.Standard, V.Standard),
            (typeof(Line), typeof(Circle), V.Standard, V.Standard),
            (typeof(Plane), typeof(Plane), V.Standard, V.Standard),
            (typeof(ValueTuple<Plane, Plane>), typeof(Plane), V.Standard, V.Standard),
            (typeof(Plane), typeof(Circle), V.Standard, V.Standard),
            (typeof(Plane), typeof(Sphere), V.Standard, V.Standard),
            (typeof(Plane), typeof(BoundingBox), V.Standard, V.None),
            (typeof(Sphere), typeof(Sphere), V.Standard, V.Standard),
            (typeof(Circle), typeof(Circle), V.Standard, V.Standard),
            (typeof(Arc), typeof(Arc), V.Standard, V.Standard),
            (typeof(Point3d[]), typeof(Brep[]), V.None, V.None),
            (typeof(Point3d[]), typeof(Mesh[]), V.None, V.None),
            (typeof(Ray3d), typeof(GeometryBase[]), V.None, V.None),
        }
        .SelectMany<(Type TypeA, Type TypeB, V ModeA, V ModeB), KeyValuePair<(Type, Type), (V ModeA, V ModeB)>>(static p => p.TypeA == p.TypeB
            ? [KeyValuePair.Create((p.TypeA, p.TypeB), (p.ModeA, p.ModeB)),]
            : [KeyValuePair.Create((p.TypeA, p.TypeB), (p.ModeA, p.ModeB)), KeyValuePair.Create((p.TypeB, p.TypeA), (p.ModeB, p.ModeA)),])
        .ToFrozenDictionary();
}
