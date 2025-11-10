using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Configuration constants for semantic extraction: type identifiers, detection thresholds, and validation mode mappings.</summary>
internal static class ExtractionConfig {
    /// <summary>Epsilon tolerance for zero comparisons and near-zero checks.</summary>
    internal const double Epsilon = 1e-10;

    /// <summary>Classification type metadata: type identifier → (Category, Name) for unified type system.</summary>
    internal static readonly FrozenDictionary<byte, (byte Category, string Name)> TypeMetadata =
        new Dictionary<byte, (byte, string)> {
            [0] = (0, "Fillet"),
            [1] = (0, "Chamfer"),
            [2] = (0, "Hole"),
            [3] = (0, "GenericEdge"),
            [4] = (0, "VariableRadiusFillet"),
            [10] = (1, "Plane"),
            [11] = (1, "Cylinder"),
            [12] = (1, "Sphere"),
            [13] = (1, "Unknown"),
            [14] = (1, "Cone"),
            [15] = (1, "Torus"),
            [16] = (1, "Extrusion"),
            [20] = (2, "Linear"),
            [21] = (2, "Radial"),
            [22] = (2, "Grid"),
            [23] = (2, "Scaling"),
        }.ToFrozenDictionary();

    /// <summary>Feature type identifiers (Category 0).</summary>
    internal const byte FeatureTypeFillet = 0;
    internal const byte FeatureTypeChamfer = 1;
    internal const byte FeatureTypeHole = 2;
    internal const byte FeatureTypeGenericEdge = 3;
    internal const byte FeatureTypeVariableRadiusFillet = 4;

    /// <summary>Primitive type identifiers (Category 1).</summary>
    internal const byte PrimitiveTypePlane = 10;
    internal const byte PrimitiveTypeCylinder = 11;
    internal const byte PrimitiveTypeSphere = 12;
    internal const byte PrimitiveTypeUnknown = 13;
    internal const byte PrimitiveTypeCone = 14;
    internal const byte PrimitiveTypeTorus = 15;
    internal const byte PrimitiveTypeExtrusion = 16;

    /// <summary>Pattern type identifiers (Category 2).</summary>
    internal const byte PatternTypeLinear = 20;
    internal const byte PatternTypeRadial = 21;
    internal const byte PatternTypeGrid = 22;
    internal const byte PatternTypeScaling = 23;

    /// <summary>Detection thresholds and parameters for classification algorithms.</summary>
    internal static readonly FrozenDictionary<string, double> Thresholds =
        new Dictionary<string, double>(StringComparer.Ordinal) {
            ["FilletCurvatureVariation"] = 0.15,
            ["G2Continuity"] = 0.01,
            ["SharpEdgeAngle"] = 0.349,
            ["SmoothEdgeAngle"] = 2.967,
            ["PrimitiveFit"] = 0.001,
            ["RadialDistanceVariation"] = 0.05,
            ["RadialAngleVariation"] = 0.05,
            ["GridOrthogonality"] = 0.1,
            ["GridPointDeviation"] = 0.1,
            ["ScalingVariance"] = 0.1,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Integer parameters for algorithm configuration.</summary>
    internal static readonly FrozenDictionary<string, int> Params =
        new Dictionary<string, int>(StringComparer.Ordinal) {
            ["FilletCurvatureSamples"] = 5,
            ["MinHolePolySides"] = 16,
            ["PrimitiveResidualSamples"] = 20,
            ["PatternMinInstances"] = 3,
        }.ToFrozenDictionary(StringComparer.Ordinal);
    /// <summary>(Kind, Type) tuple to validation mode mapping.</summary>
    internal static readonly FrozenDictionary<(byte Kind, Type GeometryType), V> ValidationModes =
        new Dictionary<(byte, Type), V> {
            [(1, typeof(Brep))] = V.Standard | V.MassProperties,
            [(1, typeof(Curve))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Surface))] = V.Standard | V.AreaCentroid,
            [(1, typeof(Mesh))] = V.Standard | V.MassProperties,
            [(2, typeof(GeometryBase))] = V.BoundingBox,
            [(3, typeof(GeometryBase))] = V.Standard,
            [(4, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(5, typeof(Curve))] = V.Tolerance,
            [(6, typeof(Brep))] = V.Standard | V.Topology,
            [(6, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(7, typeof(Brep))] = V.Standard | V.Topology,
            [(7, typeof(Mesh))] = V.Standard | V.MeshSpecific,
            [(10, typeof(Curve))] = V.Standard | V.Degeneracy,
            [(10, typeof(Surface))] = V.Standard,
            [(11, typeof(Curve))] = V.Standard | V.Degeneracy,
        }.ToFrozenDictionary();

    /// <summary>Primitive projection functions: type → (Point, Plane, Params) → Point.</summary>
    internal static readonly FrozenDictionary<byte, Func<Point3d, Plane, double[], Point3d>> PrimitiveProjectors =
        new Dictionary<byte, Func<Point3d, Plane, double[], Point3d>> {
            [PrimitiveTypePlane] = static (pt, frame, _) => frame.ClosestPoint(pt),
            [PrimitiveTypeCylinder] = static (pt, frame, pars) => pars.Length >= 1 ? ProjectToCylinder(pt, frame, pars[0]) : pt,
            [PrimitiveTypeSphere] = static (pt, frame, pars) => pars.Length >= 1 ? ProjectToSphere(pt, frame.Origin, pars[0]) : pt,
            [PrimitiveTypeCone] = static (pt, frame, pars) => pars.Length >= 2 ? ProjectToCone(pt, frame, pars[0], pars[1]) : pt,
            [PrimitiveTypeTorus] = static (pt, frame, pars) => pars.Length >= 2 ? ProjectToTorus(pt, frame, pars[0], pars[1]) : pt,
            [PrimitiveTypeExtrusion] = static (pt, frame, _) => frame.ClosestPoint(pt),
        }.ToFrozenDictionary();

    [Pure]
    private static Point3d ProjectToCylinder(Point3d point, Plane frame, double radius) {
        Vector3d toPoint = point - frame.Origin;
        double axisProjection = Vector3d.Multiply(toPoint, frame.ZAxis);
        Point3d axisPoint = frame.Origin + (frame.ZAxis * axisProjection);
        Vector3d radialDir = point - axisPoint;
        return radialDir.Length > Epsilon ? axisPoint + ((radialDir / radialDir.Length) * radius) : axisPoint + (frame.XAxis * radius);
    }

    [Pure]
    private static Point3d ProjectToSphere(Point3d point, Point3d center, double radius) {
        Vector3d dir = point - center;
        return dir.Length > Epsilon ? center + ((dir / dir.Length) * radius) : center + new Vector3d(radius, 0, 0);
    }

    [Pure]
    private static Point3d ProjectToCone(Point3d point, Plane frame, double baseRadius, double height) {
        Vector3d toPoint = point - frame.Origin;
        double axisProjection = Vector3d.Multiply(toPoint, frame.ZAxis);
        double coneRadius = baseRadius * (1.0 - (axisProjection / height));
        Point3d axisPoint = frame.Origin + (frame.ZAxis * axisProjection);
        Vector3d radialDir = point - axisPoint;
        return radialDir.Length > Epsilon ? axisPoint + ((radialDir / radialDir.Length) * coneRadius) : axisPoint + (frame.XAxis * coneRadius);
    }

    [Pure]
    private static Point3d ProjectToTorus(Point3d point, Plane frame, double majorRadius, double minorRadius) {
        Vector3d toPoint = point - frame.Origin;
        Vector3d radialInPlane = toPoint - (frame.ZAxis * Vector3d.Multiply(toPoint, frame.ZAxis));
        Point3d majorCirclePoint = radialInPlane.Length > Epsilon
            ? frame.Origin + ((radialInPlane / radialInPlane.Length) * majorRadius)
            : frame.Origin + (frame.XAxis * majorRadius);
        Vector3d toMinor = point - majorCirclePoint;
        return toMinor.Length > Epsilon ? majorCirclePoint + ((toMinor / toMinor.Length) * minorRadius) : majorCirclePoint + (frame.ZAxis * minorRadius);
    }

    /// <summary>Gets validation mode with inheritance fallback for (kind, type) pair.</summary>
    internal static V GetValidationMode(byte kind, Type geometryType) =>
        ValidationModes.TryGetValue((kind, geometryType), out V exact) ? exact : ValidationModes.Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType)).OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0)).Select(kv => kv.Value).DefaultIfEmpty(V.Standard).First();
}
