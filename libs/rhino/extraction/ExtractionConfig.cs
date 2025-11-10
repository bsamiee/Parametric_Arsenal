using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
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

    /// <summary>Surface primitive classifiers: (Surface, Tolerance) → (Success, Type, Frame, Params).</summary>
    internal static readonly FrozenDictionary<byte, Func<Surface, double, (bool Success, byte Type, Plane Frame, double[] Params)>> PrimitiveClassifiers =
        new Dictionary<byte, Func<Surface, double, (bool, byte, Plane, double[])>> {
            [PrimitiveTypePlane] = static (s, tol) => s.TryGetPlane(out Plane pl, tolerance: tol)
                ? (true, PrimitiveTypePlane, pl, [pl.OriginX, pl.OriginY, pl.OriginZ,])
                : (false, PrimitiveTypeUnknown, Plane.WorldXY, []),
            [PrimitiveTypeCylinder] = static (s, tol) => s.TryGetCylinder(out Cylinder cyl, tolerance: tol) && cyl.Radius > tol
                ? (true, PrimitiveTypeCylinder, new Plane(cyl.CircleAt(0.0).Center, cyl.Axis), [cyl.Radius, cyl.TotalHeight,])
                : (false, PrimitiveTypeUnknown, Plane.WorldXY, []),
            [PrimitiveTypeSphere] = static (s, tol) => s.TryGetSphere(out Sphere sph, tolerance: tol) && sph.Radius > tol
                ? (true, PrimitiveTypeSphere, new Plane(sph.Center, Vector3d.ZAxis), [sph.Radius,])
                : (false, PrimitiveTypeUnknown, Plane.WorldXY, []),
            [PrimitiveTypeCone] = static (s, tol) => s.TryGetCone(out Cone cone, tolerance: tol) && cone.Radius > tol && cone.Height > tol
                ? (true, PrimitiveTypeCone, new Plane(cone.BasePoint, cone.Axis), [cone.Radius, cone.Height, Math.Atan(cone.Radius / cone.Height),])
                : (false, PrimitiveTypeUnknown, Plane.WorldXY, []),
            [PrimitiveTypeTorus] = static (s, tol) => s.TryGetTorus(out Torus torus, tolerance: tol) && torus.MajorRadius > tol && torus.MinorRadius > tol
                ? (true, PrimitiveTypeTorus, torus.Plane, [torus.MajorRadius, torus.MinorRadius,])
                : (false, PrimitiveTypeUnknown, Plane.WorldXY, []),
            [PrimitiveTypeExtrusion] = static (s, _) => s is Extrusion ext && ext.IsValid && ext.PathLineCurve() is LineCurve lc
                ? (true, PrimitiveTypeExtrusion, new Plane(ext.PathStart, lc.Line.Direction), [lc.Line.Length,])
                : (false, PrimitiveTypeUnknown, Plane.WorldXY, []),
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

    /// <summary>Edge feature classifiers: evaluates edge geometry to determine feature type and parameter.</summary>
    internal static readonly FrozenDictionary<string, Func<BrepEdge, Brep, (double[] Curvatures, double Mean)?, (byte Type, double Param)>> EdgeClassifiers =
        new Dictionary<string, Func<BrepEdge, Brep, (double[], double)?, (byte, double)>>(StringComparer.Ordinal) {
            ["Fillet"] = static (edge, _, data) => data is (double[] curvs, double mean)
                && !edge.GetNextDiscontinuity(continuityType: Continuity.G2_locus_continuous, t0: edge.Domain.Min, t1: edge.Domain.Max, t: out double _)
                && Math.Sqrt(curvs.Sum(k => (k - mean) * (k - mean)) / curvs.Length) / (mean > Epsilon ? mean : 1.0) is double coeffVar
                && coeffVar < Thresholds["FilletCurvatureVariation"] && mean > Epsilon
                    ? (FeatureTypeFillet, 1.0 / mean)
                    : (FeatureTypeGenericEdge, edge.EdgeCurve.GetLength()),
            ["Chamfer"] = static (edge, brep, data) => (data is (double[] curvs, double mean), edge.AdjacentFaces()) switch {
                (true, int[] { Length: 2 } faces) when edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d mid
                    && brep.Faces[faces[0]].ClosestPoint(testPoint: mid, u: out double u0, v: out double v0)
                    && brep.Faces[faces[1]].ClosestPoint(testPoint: mid, u: out double u1, v: out double v1)
                    && Math.Abs(Vector3d.VectorAngle(brep.Faces[faces[0]].NormalAt(u: u0, v: v0), brep.Faces[faces[1]].NormalAt(u: u1, v: v1))) is double angle
                    && angle < Thresholds["SmoothEdgeAngle"] && angle > Thresholds["SharpEdgeAngle"]
                    => (FeatureTypeChamfer, angle),
                (true, _) when data.HasValue && data.Value.Item2 > Epsilon
                    => (FeatureTypeVariableRadiusFillet, 1.0 / data.Value.Item2),
                _ => (FeatureTypeGenericEdge, edge.EdgeCurve.GetLength()),
            },
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Pattern detectors: (Centers, Context) → Result with pattern type, transform, confidence.</summary>
    internal static readonly FrozenDictionary<byte, Func<Point3d[], IGeometryContext, Result<(byte Type, Transform Transform, double Confidence)>>> PatternDetectors =
        new Dictionary<byte, Func<Point3d[], IGeometryContext, Result<(byte, Transform, double)>>> {
            [PatternTypeLinear] = static (centers, ctx) => Enumerable.Range(0, centers.Length - 1)
                .Select(i => centers[i + 1] - centers[i]).ToArray() is Vector3d[] deltas
                && deltas.All(d => (d - deltas[0]).Length < ctx.AbsoluteTolerance)
                    ? ResultFactory.Create(value: (Type: PatternTypeLinear, Transform: Transform.Translation(deltas[0]), Confidence: 1.0))
                    : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected),
            [PatternTypeRadial] = static (centers, ctx) => new Point3d(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z)) is Point3d centroid
                && centers.Select(c => centroid.DistanceTo(c)).ToArray() is double[] dists
                && dists.Average() is double mean && mean > ctx.AbsoluteTolerance
                && dists.All(d => Math.Abs(d - mean) / mean < Thresholds["RadialDistanceVariation"])
                && centers.Select(c => c - centroid).ToArray() is Vector3d[] radii
                && ComputeBestFitPlaneNormal(points: centers, centroid: centroid) is Vector3d normal
                && Enumerable.Range(0, radii.Length - 1).Select(i => Vector3d.VectorAngle(radii[i], radii[i + 1])).ToArray() is double[] angles
                && angles.Average() is double meanAngle && angles.All(a => Math.Abs(a - meanAngle) < Thresholds["RadialAngleVariation"])
                    ? ResultFactory.Create(value: (Type: PatternTypeRadial, Transform: Transform.Rotation(meanAngle, normal, centroid), Confidence: 0.9))
                    : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected),
            [PatternTypeGrid] = static (centers, ctx) => (centers[0], Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[0]).ToArray()) is (Point3d origin, Vector3d[] relVecs)
                && relVecs.Where(v => v.Length > ctx.AbsoluteTolerance).ToArray() is Vector3d[] cands && cands.Length >= 2
                && FindGridBasis(candidates: cands, context: ctx) is (Vector3d u, Vector3d v, true)
                && relVecs.All(vec => IsGridPoint(vector: vec, u: u, v: v, context: ctx))
                    ? ResultFactory.Create(value: (Type: PatternTypeGrid, Transform: Transform.PlaneToPlane(Plane.WorldXY, new Plane(origin, u, v)), Confidence: 0.9))
                    : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected),
            [PatternTypeScaling] = static (centers, ctx) => new Point3d(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z)) is Point3d centroid
                && centers.Select(c => centroid.DistanceTo(c)).ToArray() is double[] dists
                && Enumerable.Range(0, dists.Length - 1).Select(i => dists[i] > ctx.AbsoluteTolerance ? dists[i + 1] / dists[i] : 0.0).Where(r => r > ctx.AbsoluteTolerance).ToArray() is double[] ratios
                && ratios.Length >= 2 && ComputeVariance(values: ratios) is double var && var < Thresholds["ScalingVariance"]
                    ? ResultFactory.Create(value: (Type: PatternTypeScaling, Transform: Transform.Scale(anchor: centroid, scaleFactor: ratios.Average()), Confidence: 0.7))
                    : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected),
        }.ToFrozenDictionary();

    [Pure]
    private static Vector3d ComputeBestFitPlaneNormal(Point3d[] points, Point3d centroid) {
        Vector3d v1 = (points[0] - centroid);
        v1 = v1.Length > Epsilon ? v1 / v1.Length : Vector3d.XAxis;
        for (int i = 1; i < points.Length; i++) {
            Vector3d v2 = (points[i] - centroid);
            v2 = v2.Length > Epsilon ? v2 / v2.Length : Vector3d.YAxis;
            Vector3d normal = Vector3d.CrossProduct(v1, v2);
            if (normal.Length > Epsilon) {
                return normal / normal.Length;
            }
        }
        return Vector3d.ZAxis;
    }

    [Pure]
    private static (Vector3d U, Vector3d V, bool Success) FindGridBasis(Vector3d[] candidates, IGeometryContext context) {
        Vector3d[] ordered = [.. candidates.OrderBy(c => c.SquareLength),];
        Vector3d u = ordered[0];
        Vector3d uDir = u / u.Length;
        Vector3d vCand = ordered.Skip(1).FirstOrDefault(c => c.Length > context.AbsoluteTolerance && Math.Abs(Vector3d.Multiply(uDir, c / c.Length)) < Thresholds["GridOrthogonality"]);
        return vCand.Length > context.AbsoluteTolerance ? (u, vCand, true) : (Vector3d.Zero, Vector3d.Zero, false);
    }

    [Pure]
    private static bool IsGridPoint(Vector3d vector, Vector3d u, Vector3d v, IGeometryContext context) {
        double uLen = u.Length;
        double vLen = v.Length;
        return uLen > context.AbsoluteTolerance && vLen > context.AbsoluteTolerance
            && Vector3d.Multiply(vector, u) / (uLen * uLen) is double a
            && Vector3d.Multiply(vector, v) / (vLen * vLen) is double b
            && Math.Abs(a - Math.Round(a)) < Thresholds["GridPointDeviation"]
            && Math.Abs(b - Math.Round(b)) < Thresholds["GridPointDeviation"];
    }

    [Pure]
    private static double ComputeVariance(double[] values) =>
        values.Length switch {
            0 => double.MaxValue,
            1 => 0.0,
            int n => values.Average() is double mean ? values.Sum(v => (v - mean) * (v - mean)) / n : 0.0,
        };

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
