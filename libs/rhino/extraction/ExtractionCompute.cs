using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Feature extraction algorithms: design features, primitive decomposition, pattern recognition.</summary>
internal static class ExtractionCompute {
    [Pure]
    internal static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeatures(Brep brep) =>
        !brep.IsValid
            ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Validation.GeometryInvalid)
            : brep.Faces.Count is 0
                ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no faces"))
                : brep.Edges.Count is 0
                    ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no edges"))
                    : ExtractFeaturesInternal(brep: brep);

    [Pure]
    private static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeaturesInternal(Brep brep) {
        BrepEdge[] validEdges = [.. brep.Edges.Where(e => e.EdgeCurve is not null),];
        (byte Type, double Param)[] edgeFeatures = new (byte, double)[validEdges.Length];

        for (int i = 0; i < validEdges.Length; i++) {
            edgeFeatures[i] = ClassifyEdge(edge: validEdges[i], brep: brep);
        }

        (byte Type, double Param)[] holeFeatures = [.. brep.Loops
            .Where(l => l.LoopType == BrepLoopType.Inner)
            .Select(l => ClassifyHole(loop: l))
            .Where(h => h.IsHole)
            .Select(h => (Type: ExtractionConfig.FeatureTypeHole, Param: h.Area)),
        ];

        (byte Type, double Param)[] allFeatures = [.. edgeFeatures.Concat(holeFeatures),];
        double confidence = brep.Edges.Count > 0
            ? 1.0 - (brep.Edges.Count(e => e.EdgeCurve is null) / (double)brep.Edges.Count)
            : 0.0;

        return ResultFactory.Create(value: (Features: allFeatures, Confidence: confidence));
    }

    [Pure]
    private static (byte Type, double Param) ClassifyEdge(BrepEdge edge, Brep brep) =>
        (edge.Domain.Min, edge.Domain.Max, Enumerable.Range(0, ExtractionConfig.Params["FilletCurvatureSamples"])
            .Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.Params["FilletCurvatureSamples"] - 1.0))))
            .Where(v => v.IsValid)
            .Select(v => v.Length).ToArray()) is (double tMin, double tMax, double[] curvatures) && curvatures.Length >= 2
                ? ClassifyEdgeFromCurvature(edge: edge, brep: brep, curvatures: curvatures, tMin: tMin, tMax: tMax)
                : (Type: ExtractionConfig.FeatureTypeGenericEdge, Param: edge.EdgeCurve.GetLength());

    [Pure]
    private static (byte Type, double Param) ClassifyEdgeFromCurvature(
        BrepEdge edge,
        Brep brep,
        double[] curvatures,
        double tMin,
        double tMax) =>
        (curvatures.Average(), !edge.GetNextDiscontinuity(continuityType: Continuity.G2_locus_continuous, t0: tMin, t1: tMax, t: out double _)) is (double mean, bool isG2)
            && Math.Sqrt(curvatures.Sum(k => (k - mean) * (k - mean)) / curvatures.Length) / (mean > ExtractionConfig.Epsilon ? mean : 1.0) is double coeffVar
            && isG2 && coeffVar < ExtractionConfig.Thresholds["FilletCurvatureVariation"] && mean > ExtractionConfig.Epsilon
                ? (Type: ExtractionConfig.FeatureTypeFillet, Param: 1.0 / mean)
                : ClassifyEdgeByDihedral(edge: edge, brep: brep, mean: mean);

    [Pure]
    private static (byte Type, double Param) ClassifyEdgeByDihedral(BrepEdge edge, Brep brep, double mean) {
        int[] adjacentFaces = edge.AdjacentFaces();

        return adjacentFaces.Length is 2
            && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d midPoint
            && brep.Faces[adjacentFaces[0]].ClosestPoint(testPoint: midPoint, u: out double u0, v: out double v0)
            && brep.Faces[adjacentFaces[1]].ClosestPoint(testPoint: midPoint, u: out double u1, v: out double v1)
            ? ClassifyEdgeByAngle(
                edge: edge,
                normal0: brep.Faces[adjacentFaces[0]].NormalAt(u: u0, v: v0),
                normal1: brep.Faces[adjacentFaces[1]].NormalAt(u: u1, v: v1),
                mean: mean)
            : (Type: ExtractionConfig.FeatureTypeGenericEdge, Param: edge.EdgeCurve.GetLength());
    }

    [Pure]
    private static (byte Type, double Param) ClassifyEdgeByAngle(
        BrepEdge edge,
        Vector3d normal0,
        Vector3d normal1,
        double mean) {
        double dihedralAngle = Math.Abs(Vector3d.VectorAngle(normal0, normal1));
        bool isSmooth = dihedralAngle > ExtractionConfig.Thresholds["SmoothEdgeAngle"];
        bool isSharp = dihedralAngle < ExtractionConfig.Thresholds["SharpEdgeAngle"];
        bool isChamfer = !isSmooth && !isSharp;

        return isChamfer
            ? (Type: ExtractionConfig.FeatureTypeChamfer, Param: dihedralAngle)
            : mean > ExtractionConfig.Epsilon
                ? (Type: ExtractionConfig.FeatureTypeVariableRadiusFillet, Param: 1.0 / mean)
                : (Type: ExtractionConfig.FeatureTypeGenericEdge, Param: edge.EdgeCurve.GetLength());
    }

    [Pure]
    private static (bool IsHole, double Area) ClassifyHole(BrepLoop loop) {
        using Curve? c = loop.To3dCurve();

        if (c?.IsClosed is not true) {
            return (false, 0.0);
        }

        double area = 0.0;
        bool isHole = false;

        if (c.TryGetCircle(out Circle circ, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])) {
            area = Math.PI * circ.Radius * circ.Radius;
            isHole = true;
        } else if (c.TryGetEllipse(out Ellipse ell, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])) {
            area = Math.PI * ell.Radius1 * ell.Radius2;
            isHole = true;
        } else if (c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.Params["MinHolePolySides"]) {
            using AreaMassProperties? amp = AreaMassProperties.Compute(c);
            area = amp?.Area ?? 0.0;
            isHole = true;
        }

        return (isHole, area);
    }

    private static readonly double[] _zeroResidual = [0.0,];

    [Pure]
    internal static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry) =>
        geometry switch {
            Surface s => ClassifySurface(surface: s) switch {
                (true, byte type, Plane frame, double[] pars) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                    value: ([(Type: type, Frame: frame, Params: pars),], _zeroResidual)),
                _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                    error: E.Geometry.NoPrimitivesDetected),
            },
            Brep b when b.Faces.Count is 0 => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                error: E.Geometry.DecompositionFailed.WithContext("Brep has no faces")),
            Brep b => DecomposeBrepFaces(brep: b),
            _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                error: E.Geometry.DecompositionFailed.WithContext($"Unsupported geometry type: {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeBrepFaces(Brep brep) {
        (bool Success, byte Type, Plane Frame, double[] Params, double Residual)[] classified =
            new (bool, byte, Plane, double[], double)[brep.Faces.Count];

        for (int i = 0; i < brep.Faces.Count; i++) {
            classified[i] = brep.Faces[i].DuplicateSurface() switch {
                null => (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, [], 0.0),
                Surface surf => ((Func<(bool, byte, Plane, double[], double)>)(() => {
                    (bool success, byte type, Plane frame, double[] pars) = ClassifySurface(surface: surf);
                    double residual = success ? ComputeSurfaceResidual(surface: surf, type: type, frame: frame, pars: pars) : 0.0;
                    surf.Dispose();
                    return (success, type, frame, pars, residual);
                }))(),
            };
        }

        (byte Type, Plane Frame, double[] Params)[] primitives = [.. classified
            .Where(c => c.Success)
            .Select(c => (c.Type, c.Frame, c.Params)),
        ];

        double[] residuals = [.. classified
            .Where(c => c.Success)
            .Select(c => c.Residual),
        ];

        return primitives.Length > 0
            ? ResultFactory.Create(value: (Primitives: primitives, Residuals: residuals))
            : ResultFactory.Create<((byte, Plane, double[])[], double[])>(
                error: E.Geometry.NoPrimitivesDetected.WithContext("No faces classified as primitives"));
    }

    [Pure]
    private static (bool Success, byte Type, Plane Frame, double[] Params) ClassifySurface(Surface surface) =>
        surface.TryGetPlane(out Plane pl, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])
            ? (true, ExtractionConfig.PrimitiveTypePlane, pl, [pl.OriginX, pl.OriginY, pl.OriginZ,])
            : surface.TryGetCylinder(out Cylinder cyl, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])
                && cyl.Radius > ExtractionConfig.Thresholds["PrimitiveFit"]
                ? (true, ExtractionConfig.PrimitiveTypeCylinder, new Plane(cyl.CircleAt(0.0).Center, cyl.Axis), [cyl.Radius, cyl.TotalHeight,])
                : surface.TryGetSphere(out Sphere sph, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])
                    && sph.Radius > ExtractionConfig.Thresholds["PrimitiveFit"]
                    ? (true, ExtractionConfig.PrimitiveTypeSphere, new Plane(sph.Center, Vector3d.ZAxis), [sph.Radius,])
                    : surface.TryGetCone(out Cone cone, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])
                        && cone.Radius > ExtractionConfig.Thresholds["PrimitiveFit"]
                        && cone.Height > ExtractionConfig.Thresholds["PrimitiveFit"]
                        ? (true, ExtractionConfig.PrimitiveTypeCone, new Plane(cone.BasePoint, cone.Axis), [cone.Radius, cone.Height, Math.Atan(cone.Radius / cone.Height),])
                        : surface.TryGetTorus(out Torus torus, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])
                            && torus.MajorRadius > ExtractionConfig.Thresholds["PrimitiveFit"]
                            && torus.MinorRadius > ExtractionConfig.Thresholds["PrimitiveFit"]
                            ? (true, ExtractionConfig.PrimitiveTypeTorus, torus.Plane, [torus.MajorRadius, torus.MinorRadius,])
                            : surface switch {
                                Extrusion ext when ext.IsValid && ext.PathLineCurve() is LineCurve lc =>
                                    (true, ExtractionConfig.PrimitiveTypeExtrusion, new Plane(ext.PathStart, lc.Line.Direction), [lc.Line.Length,]),
                                _ => (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, []),
                            };

    [Pure]
    private static double ComputeSurfaceResidual(Surface surface, byte type, Plane frame, double[] pars) =>
        (surface.Domain(0), surface.Domain(1), (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.Params["PrimitiveResidualSamples"]))) is (Interval u, Interval v, int samplesPerDir)
            && ExtractionConfig.PrimitiveProjectors.TryGetValue(type, out Func<Point3d, Plane, double[], Point3d>? projector)
            ? Math.Sqrt(Enumerable.Range(0, samplesPerDir).SelectMany(i => Enumerable.Range(0, samplesPerDir).Select(j =>
                surface.PointAt(u: u.ParameterAt(i / (double)(samplesPerDir - 1)), v: v.ParameterAt(j / (double)(samplesPerDir - 1))) is Point3d sp
                    ? sp.DistanceToSquared(projector(sp, frame, pars))
                    : 0.0)).Sum() / (samplesPerDir * samplesPerDir))
            : 0.0;

    [Pure]
    internal static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        geometries.Length < ExtractionConfig.Params["PatternMinInstances"]
            ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(
                error: E.Geometry.NoPatternDetected.WithContext($"Need at least {ExtractionConfig.Params["PatternMinInstances"].ToString(System.Globalization.CultureInfo.InvariantCulture)} instances"))
            : DetectPatternType(centers: [.. geometries.Select(g => g.GetBoundingBox(accurate: false).Center),], context: context);

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> DetectPatternType(Point3d[] centers, IGeometryContext context) =>
        Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[i]).ToArray() is Vector3d[] deltas && deltas.All(d => (d - deltas[0]).Length < context.AbsoluteTolerance)
            ? ResultFactory.Create(value: (Type: ExtractionConfig.PatternTypeLinear, SymmetryTransform: Transform.Translation(deltas[0]), Confidence: 1.0))
            : TryDetectRadialPattern(centers: centers, context: context) is Result<(byte, Transform, double)> radialResult && radialResult.IsSuccess
                ? radialResult
                : TryDetectGridPattern(centers: centers, context: context) is Result<(byte, Transform, double)> gridResult && gridResult.IsSuccess
                    ? gridResult
                    : TryDetectScalingPattern(centers: centers, context: context) is Result<(byte, Transform, double)> scaleResult && scaleResult.IsSuccess
                        ? scaleResult
                        : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected.WithContext("No linear, radial, grid, or scaling pattern detected"));

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> TryDetectRadialPattern(Point3d[] centers, IGeometryContext context) {
        Point3d centroid = new(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z));
        double[] distances = new double[centers.Length];

        for (int i = 0; i < centers.Length; i++) {
            distances[i] = centroid.DistanceTo(centers[i]);
        }

        double meanDistance = distances.Average();
        bool allDistancesEqual = meanDistance > context.AbsoluteTolerance
            && distances.All(d => Math.Abs(d - meanDistance) / meanDistance < ExtractionConfig.Thresholds["RadialDistanceVariation"]);

        return !allDistancesEqual
            ? ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected)
            : ComputeRadialPattern(centers: centers, centroid: centroid, _: meanDistance, __: context);
    }

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ComputeRadialPattern(
        Point3d[] centers,
        Point3d centroid,
        double _,
        IGeometryContext __) =>
        (centers.Select(c => c - centroid).ToArray(), ComputeBestFitPlaneNormal(points: centers, centroid: centroid)) is (Vector3d[] radii, Vector3d normal)
            && Enumerable.Range(0, radii.Length - 1).Select(i => Vector3d.VectorAngle(radii[i], radii[i + 1])).ToArray() is double[] angles
            && angles.Average() is double meanAngle && angles.All(a => Math.Abs(a - meanAngle) < ExtractionConfig.Thresholds["RadialAngleVariation"])
                ? ResultFactory.Create(value: (Type: ExtractionConfig.PatternTypeRadial, SymmetryTransform: Transform.Rotation(meanAngle, normal, centroid), Confidence: 0.9))
                : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected);

    [Pure]
    private static Vector3d ComputeBestFitPlaneNormal(Point3d[] points, Point3d centroid) {
        Vector3d v1 = (points[0] - centroid);
        v1 = v1.Length > ExtractionConfig.Epsilon ? v1 / v1.Length : Vector3d.XAxis;

        for (int i = 1; i < points.Length; i++) {
            Vector3d v2 = (points[i] - centroid);
            v2 = v2.Length > ExtractionConfig.Epsilon ? v2 / v2.Length : Vector3d.YAxis;
            Vector3d normal = Vector3d.CrossProduct(v1, v2);

            if (normal.Length > ExtractionConfig.Epsilon) {
                return normal / normal.Length;
            }
        }

        return Vector3d.ZAxis;
    }

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> TryDetectGridPattern(Point3d[] centers, IGeometryContext context) =>
        (centers[0], Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[0]).ToArray()) is (Point3d origin, Vector3d[] relativeVectors)
            && relativeVectors.Where(v => v.Length > context.AbsoluteTolerance).ToArray() is Vector3d[] candidates && candidates.Length >= 2
            && FindGridBasis(candidates: candidates, context: context) is (Vector3d u, Vector3d v, bool success) && success
            && relativeVectors.All(vec => IsGridPoint(vector: vec, u: u, v: v, context: context))
                ? ResultFactory.Create(value: (Type: ExtractionConfig.PatternTypeGrid, SymmetryTransform: Transform.PlaneToPlane(Plane.WorldXY, new Plane(origin, u, v)), Confidence: 0.9))
                : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected);

    private static (Vector3d U, Vector3d V, bool Success) FindGridBasis(Vector3d[] candidates, IGeometryContext context) {
        Vector3d[] orderedCandidates = [.. candidates.OrderBy(c => c.SquareLength),];
        Vector3d u = orderedCandidates[0];
        Vector3d uDir = u / u.Length;
        Vector3d vCandidate = orderedCandidates.Skip(1).FirstOrDefault(c =>
            c.Length > context.AbsoluteTolerance
            && Math.Abs(Vector3d.Multiply(uDir, c / c.Length)) < ExtractionConfig.Thresholds["GridOrthogonality"]);

        return vCandidate.Length > context.AbsoluteTolerance
            ? (U: u, V: vCandidate, Success: true)
            : (U: Vector3d.Zero, V: Vector3d.Zero, Success: false);
    }

    private static bool IsGridPoint(Vector3d vector, Vector3d u, Vector3d v, IGeometryContext context) {
        double uLen = u.Length;
        double vLen = v.Length;

        return uLen > context.AbsoluteTolerance
            && vLen > context.AbsoluteTolerance
            && Vector3d.Multiply(vector, u) / (uLen * uLen) is double a
            && Vector3d.Multiply(vector, v) / (vLen * vLen) is double b
            && Math.Abs(a - Math.Round(a)) < ExtractionConfig.Thresholds["GridPointDeviation"]
            && Math.Abs(b - Math.Round(b)) < ExtractionConfig.Thresholds["GridPointDeviation"];
    }

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> TryDetectScalingPattern(Point3d[] centers, IGeometryContext context) =>
        new Point3d(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z)) is Point3d centroid
            && centers.Select(c => centroid.DistanceTo(c)).ToArray() is double[] distances
            && Enumerable.Range(0, distances.Length - 1).Select(i => distances[i] > context.AbsoluteTolerance ? distances[i + 1] / distances[i] : 0.0).Where(r => r > context.AbsoluteTolerance).ToArray() is double[] validRatios
            && validRatios.Length >= 2 && ComputeVariance(values: validRatios) is double variance && variance < ExtractionConfig.Thresholds["ScalingVariance"]
                ? ResultFactory.Create(value: (Type: ExtractionConfig.PatternTypeScaling, SymmetryTransform: Transform.Scale(anchor: centroid, scaleFactor: validRatios.Average()), Confidence: 0.7))
                : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected);

    [Pure]
    private static double ComputeVariance(double[] values) =>
        values.Length switch {
            0 => double.MaxValue,
            1 => 0.0,
            int n => values.Average() is double mean
                ? values.Sum(v => (v - mean) * (v - mean)) / n
                : 0.0,
        };
}
