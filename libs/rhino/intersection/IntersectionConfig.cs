using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Validation metadata and constants for intersection operations.</summary>
[Pure]
internal static class IntersectionConfig {
    /// <summary>Metadata describing validation and operation naming for a geometry pair.</summary>
    internal sealed record IntersectionPairMetadata(
        V FirstValidation,
        V SecondValidation,
        string OperationName);

    /// <summary>Validation metadata table keyed by canonical geometry type pairs.</summary>
    internal static readonly FrozenDictionary<(Type, Type), IntersectionPairMetadata> PairMetadata =
        new Dictionary<(Type, Type), IntersectionPairMetadata> {
            [(typeof(Curve), typeof(Curve))] = new(
                FirstValidation: V.Standard | V.Degeneracy,
                SecondValidation: V.Standard | V.Degeneracy,
                OperationName: "Intersection.CurveCurve"),
            [(typeof(Curve), typeof(BrepFace))] = new(
                FirstValidation: V.Standard | V.Degeneracy,
                SecondValidation: V.Standard | V.Topology,
                OperationName: "Intersection.CurveBrepFace"),
            [(typeof(Curve), typeof(Surface))] = new(
                FirstValidation: V.Standard | V.Degeneracy,
                SecondValidation: V.Standard | V.UVDomain,
                OperationName: "Intersection.CurveSurface"),
            [(typeof(Curve), typeof(Plane))] = new(
                FirstValidation: V.Standard | V.Degeneracy,
                SecondValidation: V.Standard,
                OperationName: "Intersection.CurvePlane"),
            [(typeof(Curve), typeof(Line))] = new(
                FirstValidation: V.Standard | V.Degeneracy,
                SecondValidation: V.Standard,
                OperationName: "Intersection.CurveLine"),
            [(typeof(Curve), typeof(Brep))] = new(
                FirstValidation: V.Standard | V.Degeneracy,
                SecondValidation: V.Standard | V.Topology,
                OperationName: "Intersection.CurveBrep"),
            [(typeof(Brep), typeof(Brep))] = new(
                FirstValidation: V.Standard | V.Topology,
                SecondValidation: V.Standard | V.Topology,
                OperationName: "Intersection.BrepBrep"),
            [(typeof(Brep), typeof(Plane))] = new(
                FirstValidation: V.Standard | V.Topology,
                SecondValidation: V.Standard,
                OperationName: "Intersection.BrepPlane"),
            [(typeof(Brep), typeof(Surface))] = new(
                FirstValidation: V.Standard | V.Topology,
                SecondValidation: V.Standard | V.UVDomain,
                OperationName: "Intersection.BrepSurface"),
            [(typeof(Surface), typeof(Surface))] = new(
                FirstValidation: V.Standard | V.UVDomain,
                SecondValidation: V.Standard | V.UVDomain,
                OperationName: "Intersection.SurfaceSurface"),
            [(typeof(Mesh), typeof(Mesh))] = new(
                FirstValidation: V.MeshSpecific,
                SecondValidation: V.MeshSpecific,
                OperationName: "Intersection.MeshMesh"),
            [(typeof(Mesh), typeof(Ray3d))] = new(
                FirstValidation: V.MeshSpecific,
                SecondValidation: V.None,
                OperationName: "Intersection.MeshRay"),
            [(typeof(Mesh), typeof(Plane))] = new(
                FirstValidation: V.MeshSpecific,
                SecondValidation: V.Standard,
                OperationName: "Intersection.MeshPlane"),
            [(typeof(Mesh), typeof(Line))] = new(
                FirstValidation: V.MeshSpecific,
                SecondValidation: V.Standard,
                OperationName: "Intersection.MeshLine"),
            [(typeof(Mesh), typeof(PolylineCurve))] = new(
                FirstValidation: V.MeshSpecific,
                SecondValidation: V.Standard | V.Degeneracy,
                OperationName: "Intersection.MeshPolyline"),
            [(typeof(Line), typeof(Line))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.LineLine"),
            [(typeof(Line), typeof(BoundingBox))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.None,
                OperationName: "Intersection.LineBoundingBox"),
            [(typeof(Line), typeof(Plane))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.LinePlane"),
            [(typeof(Line), typeof(Sphere))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.LineSphere"),
            [(typeof(Line), typeof(Cylinder))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.LineCylinder"),
            [(typeof(Line), typeof(Circle))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.LineCircle"),
            [(typeof(Plane), typeof(Plane))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.PlanePlane"),
            [(typeof(ValueTuple<Plane, Plane>), typeof(Plane))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.PlaneTriple"),
            [(typeof(Plane), typeof(Circle))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.PlaneCircle"),
            [(typeof(Plane), typeof(Sphere))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.PlaneSphere"),
            [(typeof(Plane), typeof(BoundingBox))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.None,
                OperationName: "Intersection.PlaneBoundingBox"),
            [(typeof(Sphere), typeof(Sphere))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.SphereSphere"),
            [(typeof(Circle), typeof(Circle))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.CircleCircle"),
            [(typeof(Arc), typeof(Arc))] = new(
                FirstValidation: V.Standard,
                SecondValidation: V.Standard,
                OperationName: "Intersection.ArcArc"),
            [(typeof(Point3d[]), typeof(Brep[]))] = new(
                FirstValidation: V.None,
                SecondValidation: V.None,
                OperationName: "Intersection.ProjectPointsBrep"),
            [(typeof(Point3d[]), typeof(Mesh[]))] = new(
                FirstValidation: V.None,
                SecondValidation: V.None,
                OperationName: "Intersection.ProjectPointsMesh"),
            [(typeof(Ray3d), typeof(GeometryBase[]))] = new(
                FirstValidation: V.None,
                SecondValidation: V.None,
                OperationName: "Intersection.RayShoot"),
        }.ToFrozenDictionary();

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
