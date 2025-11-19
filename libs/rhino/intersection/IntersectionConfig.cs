using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Unified metadata, constants, and dispatch tables for intersection operations.</summary>
[Pure]
internal static class IntersectionConfig {
    /// <summary>Intersection operation metadata containing validation modes and operation name.</summary>
    internal sealed record IntersectionOperationMetadata(
        V ModeA,
        V ModeB,
        string OperationName);

    /// <summary>Unified operations dispatch table: (TypeA, TypeB) → metadata.</summary>
    internal static readonly FrozenDictionary<(Type, Type), IntersectionOperationMetadata> Operations =
        new (Type TypeA, Type TypeB, V ModeA, V ModeB, string OperationName)[] {
            (typeof(Curve), typeof(Curve), V.Standard | V.Degeneracy, V.Standard | V.Degeneracy, "Intersection.CurveCurve"),
            (typeof(NurbsCurve), typeof(NurbsCurve), V.Standard | V.Degeneracy | V.NurbsGeometry, V.Standard | V.Degeneracy | V.NurbsGeometry, "Intersection.NurbsCurveNurbsCurve"),
            (typeof(PolyCurve), typeof(Curve), V.Standard | V.Degeneracy | V.PolycurveStructure, V.Standard | V.Degeneracy, "Intersection.PolyCurveCurve"),
            (typeof(Curve), typeof(Surface), V.Standard | V.Degeneracy, V.Standard | V.UVDomain, "Intersection.CurveSurface"),
            (typeof(Curve), typeof(NurbsSurface), V.Standard | V.Degeneracy, V.Standard | V.NurbsGeometry | V.UVDomain, "Intersection.CurveNurbsSurface"),
            (typeof(Curve), typeof(Brep), V.Standard | V.Degeneracy, V.Standard | V.Topology, "Intersection.CurveBrep"),
            (typeof(Curve), typeof(Extrusion), V.Standard | V.Degeneracy, V.Standard | V.ExtrusionGeometry, "Intersection.CurveExtrusion"),
            (typeof(Curve), typeof(BrepFace), V.Standard | V.Degeneracy, V.Standard | V.Topology, "Intersection.CurveBrepFace"),
            (typeof(Curve), typeof(Plane), V.Standard | V.Degeneracy, V.Standard, "Intersection.CurvePlane"),
            (typeof(Curve), typeof(Line), V.Standard | V.Degeneracy, V.Standard, "Intersection.CurveLine"),
            (typeof(Brep), typeof(Brep), V.Standard | V.Topology, V.Standard | V.Topology, "Intersection.BrepBrep"),
            (typeof(Brep), typeof(Plane), V.Standard | V.Topology, V.Standard, "Intersection.BrepPlane"),
            (typeof(Brep), typeof(Surface), V.Standard | V.Topology, V.Standard | V.UVDomain, "Intersection.BrepSurface"),
            (typeof(Extrusion), typeof(Extrusion), V.Standard | V.ExtrusionGeometry, V.Standard | V.ExtrusionGeometry, "Intersection.ExtrusionExtrusion"),
            (typeof(Surface), typeof(Surface), V.Standard | V.UVDomain, V.Standard | V.UVDomain, "Intersection.SurfaceSurface"),
            (typeof(NurbsSurface), typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain, V.Standard | V.NurbsGeometry | V.UVDomain, "Intersection.NurbsSurfaceNurbsSurface"),
            (typeof(Mesh), typeof(Mesh), V.MeshSpecific, V.MeshSpecific, "Intersection.MeshMesh"),
            (typeof(Mesh), typeof(Ray3d), V.MeshSpecific, V.None, "Intersection.MeshRay"),
            (typeof(Mesh), typeof(Plane), V.MeshSpecific, V.Standard, "Intersection.MeshPlane"),
            (typeof(Mesh), typeof(Line), V.MeshSpecific, V.Standard, "Intersection.MeshLine"),
            (typeof(Mesh), typeof(PolylineCurve), V.MeshSpecific, V.Standard | V.Degeneracy, "Intersection.MeshPolyline"),
            (typeof(Line), typeof(Line), V.Standard, V.Standard, "Intersection.LineLine"),
            (typeof(Line), typeof(BoundingBox), V.Standard, V.None, "Intersection.LineBox"),
            (typeof(Line), typeof(Plane), V.Standard, V.Standard, "Intersection.LinePlane"),
            (typeof(Line), typeof(Sphere), V.Standard, V.Standard, "Intersection.LineSphere"),
            (typeof(Line), typeof(Cylinder), V.Standard, V.Standard, "Intersection.LineCylinder"),
            (typeof(Line), typeof(Circle), V.Standard, V.Standard, "Intersection.LineCircle"),
            (typeof(Plane), typeof(Plane), V.Standard, V.Standard, "Intersection.PlanePlane"),
            (typeof(ValueTuple<Plane, Plane>), typeof(Plane), V.Standard, V.Standard, "Intersection.PlanePlanePlane"),
            (typeof(Plane), typeof(Circle), V.Standard, V.Standard, "Intersection.PlaneCircle"),
            (typeof(Plane), typeof(Sphere), V.Standard, V.Standard, "Intersection.PlaneSphere"),
            (typeof(Plane), typeof(BoundingBox), V.Standard, V.None, "Intersection.PlaneBox"),
            (typeof(Sphere), typeof(Sphere), V.Standard, V.Standard, "Intersection.SphereSphere"),
            (typeof(Circle), typeof(Circle), V.Standard, V.Standard, "Intersection.CircleCircle"),
            (typeof(Arc), typeof(Arc), V.Standard, V.Standard, "Intersection.ArcArc"),
            (typeof(Point3d[]), typeof(Brep[]), V.None, V.None, "Intersection.PointsToBreps"),
            (typeof(Point3d[]), typeof(Mesh[]), V.None, V.None, "Intersection.PointsToMeshes"),
            (typeof(Ray3d), typeof(GeometryBase[]), V.None, V.None, "Intersection.RayShoot"),
        }
        .SelectMany<(Type TypeA, Type TypeB, V ModeA, V ModeB, string OperationName), KeyValuePair<(Type, Type), IntersectionOperationMetadata>>(
            static p => p.TypeA == p.TypeB
                ? [KeyValuePair.Create((p.TypeA, p.TypeB), new IntersectionOperationMetadata(p.ModeA, p.ModeB, p.OperationName)),]
                : [
                    KeyValuePair.Create((p.TypeA, p.TypeB), new IntersectionOperationMetadata(p.ModeA, p.ModeB, p.OperationName)),
                    KeyValuePair.Create((p.TypeB, p.TypeA), new IntersectionOperationMetadata(p.ModeB, p.ModeA, p.OperationName)),
                ])
        .ToFrozenDictionary();

    /// <summary>Classification metadata.</summary>
    internal static readonly IntersectionOperationMetadata ClassificationMetadata = new(
        ModeA: V.Standard,
        ModeB: V.Standard,
        OperationName: "Intersection.Classify");

    /// <summary>Near-miss metadata.</summary>
    internal static readonly IntersectionOperationMetadata NearMissMetadata = new(
        ModeA: V.Standard,
        ModeB: V.Standard,
        OperationName: "Intersection.NearMiss");

    /// <summary>Stability metadata.</summary>
    internal static readonly IntersectionOperationMetadata StabilityMetadata = new(
        ModeA: V.Standard,
        ModeB: V.Standard,
        OperationName: "Intersection.Stability");

    /// <summary>Angle thresholds for intersection classification.</summary>
    internal static readonly double TangentAngleThreshold = RhinoMath.ToRadians(5.0);
    internal static readonly double GrazingAngleThreshold = RhinoMath.ToRadians(15.0);

    /// <summary>Tolerance multiplier for near-miss detection threshold.</summary>
    internal const double NearMissToleranceMultiplier = 10.0;

    /// <summary>Stability analysis parameters.</summary>
    internal const double StabilityPerturbationFactor = 0.001;
    internal const int StabilitySampleCount = 8;

    /// <summary>Maximum vertex sample count for mesh near-miss detection.</summary>
    internal const int MaxNearMissSamples = 1000;

    /// <summary>Minimum sample count for curve near-miss detection.</summary>
    internal const int MinCurveNearMissSamples = 3;

    /// <summary>Minimum sample count for Brep near-miss detection.</summary>
    internal const int MinBrepNearMissSamples = 8;

    /// <summary>Blend quality scores for intersection types.</summary>
    internal const double TangentBlendScore = 1.0;
    internal const double PerpendicularBlendScore = 0.5;
    internal const double CurveSurfaceTangentBlendScore = 0.8;
    internal const double CurveSurfacePerpendicularBlendScore = 0.4;
}
