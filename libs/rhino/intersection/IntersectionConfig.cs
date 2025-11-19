using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Unified metadata, constants, and dispatch tables for intersection operations.</summary>
[Pure]
internal static class IntersectionConfig {
    /// <summary>Unified operation metadata for all intersection operations.</summary>
    internal sealed record IntersectionPairMetadata(
        V ValidationModeA,
        V ValidationModeB,
        string OperationName);

    /// <summary>Singular unified intersection pair dispatch table: (TypeA, TypeB) → metadata.</summary>
    internal static readonly FrozenDictionary<(Type, Type), IntersectionPairMetadata> PairOperations =
        new (Type TypeA, Type TypeB, V ModeA, V ModeB, string OpName)[] {
            (typeof(Curve), typeof(Curve), V.Standard | V.Degeneracy, V.Standard | V.Degeneracy, "Intersection.Curve.Curve"),
            (typeof(NurbsCurve), typeof(NurbsCurve), V.Standard | V.Degeneracy | V.NurbsGeometry, V.Standard | V.Degeneracy | V.NurbsGeometry, "Intersection.NurbsCurve.NurbsCurve"),
            (typeof(PolyCurve), typeof(Curve), V.Standard | V.Degeneracy | V.PolycurveStructure, V.Standard | V.Degeneracy, "Intersection.PolyCurve.Curve"),
            (typeof(Curve), typeof(Surface), V.Standard | V.Degeneracy, V.Standard | V.UVDomain, "Intersection.Curve.Surface"),
            (typeof(Curve), typeof(NurbsSurface), V.Standard | V.Degeneracy, V.Standard | V.NurbsGeometry | V.UVDomain, "Intersection.Curve.NurbsSurface"),
            (typeof(Curve), typeof(Brep), V.Standard | V.Degeneracy, V.Standard | V.Topology, "Intersection.Curve.Brep"),
            (typeof(Curve), typeof(Extrusion), V.Standard | V.Degeneracy, V.Standard | V.ExtrusionGeometry, "Intersection.Curve.Extrusion"),
            (typeof(Curve), typeof(BrepFace), V.Standard | V.Degeneracy, V.Standard | V.Topology, "Intersection.Curve.BrepFace"),
            (typeof(Curve), typeof(Plane), V.Standard | V.Degeneracy, V.Standard, "Intersection.Curve.Plane"),
            (typeof(Curve), typeof(Line), V.Standard | V.Degeneracy, V.Standard, "Intersection.Curve.Line"),
            (typeof(Brep), typeof(Brep), V.Standard | V.Topology, V.Standard | V.Topology, "Intersection.Brep.Brep"),
            (typeof(Brep), typeof(Plane), V.Standard | V.Topology, V.Standard, "Intersection.Brep.Plane"),
            (typeof(Brep), typeof(Surface), V.Standard | V.Topology, V.Standard | V.UVDomain, "Intersection.Brep.Surface"),
            (typeof(Extrusion), typeof(Extrusion), V.Standard | V.ExtrusionGeometry, V.Standard | V.ExtrusionGeometry, "Intersection.Extrusion.Extrusion"),
            (typeof(Surface), typeof(Surface), V.Standard | V.UVDomain, V.Standard | V.UVDomain, "Intersection.Surface.Surface"),
            (typeof(NurbsSurface), typeof(NurbsSurface), V.Standard | V.NurbsGeometry | V.UVDomain, V.Standard | V.NurbsGeometry | V.UVDomain, "Intersection.NurbsSurface.NurbsSurface"),
            (typeof(Mesh), typeof(Mesh), V.MeshSpecific, V.MeshSpecific, "Intersection.Mesh.Mesh"),
            (typeof(Mesh), typeof(Ray3d), V.MeshSpecific, V.None, "Intersection.Mesh.Ray3d"),
            (typeof(Mesh), typeof(Plane), V.MeshSpecific, V.Standard, "Intersection.Mesh.Plane"),
            (typeof(Mesh), typeof(Line), V.MeshSpecific, V.Standard, "Intersection.Mesh.Line"),
            (typeof(Mesh), typeof(PolylineCurve), V.MeshSpecific, V.Standard | V.Degeneracy, "Intersection.Mesh.PolylineCurve"),
            (typeof(Line), typeof(Line), V.Standard, V.Standard, "Intersection.Line.Line"),
            (typeof(Line), typeof(BoundingBox), V.Standard, V.None, "Intersection.Line.BoundingBox"),
            (typeof(Line), typeof(Plane), V.Standard, V.Standard, "Intersection.Line.Plane"),
            (typeof(Line), typeof(Sphere), V.Standard, V.Standard, "Intersection.Line.Sphere"),
            (typeof(Line), typeof(Cylinder), V.Standard, V.Standard, "Intersection.Line.Cylinder"),
            (typeof(Line), typeof(Circle), V.Standard, V.Standard, "Intersection.Line.Circle"),
            (typeof(Plane), typeof(Plane), V.Standard, V.Standard, "Intersection.Plane.Plane"),
            (typeof(ValueTuple<Plane, Plane>), typeof(Plane), V.Standard, V.Standard, "Intersection.PlanePlane.Plane"),
            (typeof(Plane), typeof(Circle), V.Standard, V.Standard, "Intersection.Plane.Circle"),
            (typeof(Plane), typeof(Sphere), V.Standard, V.Standard, "Intersection.Plane.Sphere"),
            (typeof(Plane), typeof(BoundingBox), V.Standard, V.None, "Intersection.Plane.BoundingBox"),
            (typeof(Sphere), typeof(Sphere), V.Standard, V.Standard, "Intersection.Sphere.Sphere"),
            (typeof(Circle), typeof(Circle), V.Standard, V.Standard, "Intersection.Circle.Circle"),
            (typeof(Arc), typeof(Arc), V.Standard, V.Standard, "Intersection.Arc.Arc"),
            (typeof(Point3d[]), typeof(Brep[]), V.None, V.Standard | V.Topology, "Intersection.PointProjection.Breps"),
            (typeof(Point3d[]), typeof(Mesh[]), V.None, V.MeshSpecific, "Intersection.PointProjection.Meshes"),
            (typeof(Ray3d), typeof(GeometryBase[]), V.None, V.None, "Intersection.RayShoot"),
        }
        .SelectMany<(Type TypeA, Type TypeB, V ModeA, V ModeB, string OpName), KeyValuePair<(Type, Type), IntersectionPairMetadata>>(
            static p => p.TypeA == p.TypeB
                ? [KeyValuePair.Create((p.TypeA, p.TypeB), new IntersectionPairMetadata(p.ModeA, p.ModeB, p.OpName)),]
                : [
                    KeyValuePair.Create((p.TypeA, p.TypeB), new IntersectionPairMetadata(p.ModeA, p.ModeB, p.OpName)),
                    KeyValuePair.Create((p.TypeB, p.TypeA), new IntersectionPairMetadata(p.ModeB, p.ModeA, p.OpName)),
                ])
        .ToFrozenDictionary();

    /// <summary>Classification operation metadata.</summary>
    internal static readonly IntersectionPairMetadata ClassificationOperation = new(
        ValidationModeA: V.Standard,
        ValidationModeB: V.Standard,
        OperationName: "Intersection.Classify");

    /// <summary>Near-miss operation metadata.</summary>
    internal static readonly IntersectionPairMetadata NearMissOperation = new(
        ValidationModeA: V.Standard,
        ValidationModeB: V.Standard,
        OperationName: "Intersection.NearMiss");

    /// <summary>Stability operation metadata.</summary>
    internal static readonly IntersectionPairMetadata StabilityOperation = new(
        ValidationModeA: V.Standard,
        ValidationModeB: V.Standard,
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
