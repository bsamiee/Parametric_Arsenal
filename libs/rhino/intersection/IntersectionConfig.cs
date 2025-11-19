using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Intersection;

/// <summary>Intersection metadata registry containing validation flags and tolerances.</summary>
internal static class IntersectionConfig {
    internal sealed record PairStrategyMetadata(string OperationName, V ModeA, V ModeB);

    internal sealed record OperationMetadata(string OperationName, V ValidationMode);

    private static readonly (Type TypeA, Type TypeB, V ModeA, V ModeB)[] StrategyDefinitions = [
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
    ];

    internal static readonly FrozenDictionary<(Type, Type), PairStrategyMetadata> PairStrategies =
        StrategyDefinitions
            .SelectMany(CreateEntries)
            .ToFrozenDictionary(entry => entry.Key, entry => entry.Value);

    internal static readonly OperationMetadata IntersectionOperation = new("Intersect.Execute", V.None);
    internal static readonly OperationMetadata ClassificationOperation = new("Intersect.Classify", V.None);
    internal static readonly OperationMetadata NearMissOperation = new("Intersect.NearMiss", V.None);
    internal static readonly OperationMetadata StabilityOperation = new("Intersect.Stability", V.None);

    internal static readonly double TangentAngleThreshold = RhinoMath.ToRadians(5.0);
    internal static readonly double GrazingAngleThreshold = RhinoMath.ToRadians(15.0);
    internal const double NearMissToleranceMultiplier = 10.0;
    internal const double StabilityPerturbationFactor = 0.001;
    internal const int StabilitySampleCount = 8;
    internal const int MaxNearMissSamples = 1000;
    internal const int MinCurveNearMissSamples = 3;
    internal const int MinBrepNearMissSamples = 8;
    internal const double TangentBlendScore = 1.0;
    internal const double PerpendicularBlendScore = 0.5;
    internal const double CurveSurfaceTangentBlendScore = 0.8;
    internal const double CurveSurfacePerpendicularBlendScore = 0.4;

    private static IEnumerable<KeyValuePair<(Type, Type), PairStrategyMetadata>> CreateEntries((Type TypeA, Type TypeB, V ModeA, V ModeB) definition) =>
        definition.TypeA == definition.TypeB
            ? [KeyValuePair.Create((definition.TypeA, definition.TypeB), new PairStrategyMetadata(CreateOperationName(definition.TypeA, definition.TypeB), definition.ModeA, definition.ModeB)),]
            : [
                KeyValuePair.Create((definition.TypeA, definition.TypeB), new PairStrategyMetadata(CreateOperationName(definition.TypeA, definition.TypeB), definition.ModeA, definition.ModeB)),
                KeyValuePair.Create((definition.TypeB, definition.TypeA), new PairStrategyMetadata(CreateOperationName(definition.TypeB, definition.TypeA), definition.ModeB, definition.ModeA)),
            ];

    private static string CreateOperationName(Type left, Type right) =>
        string.Create(CultureInfo.InvariantCulture, $"Intersect.{left.Name}.{right.Name}");
}
