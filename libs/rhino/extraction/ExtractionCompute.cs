using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Feature extraction algorithms: design features, primitive decomposition, pattern recognition.</summary>
internal static class ExtractionCompute {
    private static readonly double[] _zeroResidual = [0.0,];

    [Pure]
    internal static Result<Extraction.FeatureExtractionResult> ExtractFeatures(Brep brep, IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, ExtractionConfig.FeatureMetadata.ValidationMode,])
            .Ensure(b => b.Faces.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no faces"))
            .Ensure(b => b.Edges.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no edges"))
            .Bind(validBrep => ExtractFeaturesInternal(brep: validBrep, context: context));

    [Pure]
    private static Extraction.Feature ClassifyEdge(BrepEdge edge, Brep brep) =>
        (edge.Domain.Min, edge.Domain.Max, Enumerable.Range(0, ExtractionConfig.FilletCurvatureSampleCount)
            .Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.FilletCurvatureSampleCount - 1.0))))
            .Where(v => v.IsValid)
            .Select(v => v.Length).ToArray()) is (double tMin, double tMax, double[] curvatures) && curvatures.Length >= 2
                ? ClassifyEdgeFromCurvature(edge: edge, brep: brep, curvatures: curvatures, tMin: tMin, tMax: tMax)
                : new Extraction.GenericEdge(Length: edge.EdgeCurve.GetLength());

    private static Result<Extraction.PatternDetectionResult> TryDetectGridPattern(Point3d[] centers, IGeometryContext context) =>
        (centers[0], Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[0]).ToArray()) is (Point3d origin, Vector3d[] relativeVectors)
            && relativeVectors.Where(v => v.Length > context.AbsoluteTolerance).ToArray() is Vector3d[] candidates && candidates.Length >= 2
            && FindGridBasis(candidates: candidates, context: context) is (Vector3d u, Vector3d v, bool success) && success
            && relativeVectors.All(vec => IsGridPoint(vector: vec, u: u, v: v, context: context))
                ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Pattern: new Extraction.GridPattern(SymmetryTransform: Transform.PlaneToPlane(Plane.WorldXY, new Plane(origin, u, v))), Confidence: 0.9))
                : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected);

    [Pure]
    internal static Result<Extraction.PatternDetectionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(gs => gs.Length >= ExtractionConfig.PatternMinInstances, error: E.Geometry.NoPatternDetected.WithContext($"Need at least {ExtractionConfig.PatternMinInstances.ToString(System.Globalization.CultureInfo.InvariantCulture)} instances"))
            .Ensure(gs => gs.All(g => g is not null), error: E.Validation.GeometryInvalid.WithContext("Array contains null geometries"))
            .Bind(gs => ResultFactory.Create(value: (IEnumerable<GeometryBase>)gs)
                .TraverseElements(geometry => ResultFactory.Create(value: geometry)
                    .Validate(args: [context, ExtractionConfig.PatternMetadata.ValidationMode,])
                    .Map(validated => validated.GetBoundingBox(accurate: false).Center))
                .Bind(centers => DetectPatternType(centers: [.. centers], context: context)));

    [Pure]
    private static Extraction.Feature ClassifyEdgeFromCurvature(
        BrepEdge edge,
        Brep brep,
        double[] curvatures,
        double tMin,
        double tMax) =>
        (curvatures.Average(), !edge.GetNextDiscontinuity(continuityType: Continuity.G2_locus_continuous, t0: tMin, t1: tMax, t: out double _)) is (double mean, bool isG2)
            && Math.Sqrt(curvatures.Sum(k => (k - mean) * (k - mean)) / curvatures.Length) / (mean > RhinoMath.ZeroTolerance ? mean : 1.0) is double coeffVar
            && isG2 && coeffVar < ExtractionConfig.FilletCurvatureVariationThreshold && mean > RhinoMath.ZeroTolerance
                ? new Extraction.Fillet(Radius: 1.0 / mean)
                : ClassifyEdgeByDihedral(edge: edge, brep: brep, mean: mean);

    private static bool IsGridPoint(Vector3d vector, Vector3d u, Vector3d v, IGeometryContext context) {
        double uLen = u.Length;
        double vLen = v.Length;

        return uLen > context.AbsoluteTolerance
            && vLen > context.AbsoluteTolerance
            && Vector3d.Multiply(vector, u) / (uLen * uLen) is double a
            && Vector3d.Multiply(vector, v) / (vLen * vLen) is double b
            && Math.Abs(a - Math.Round(a)) < ExtractionConfig.GridPointDeviationThreshold
            && Math.Abs(b - Math.Round(b)) < ExtractionConfig.GridPointDeviationThreshold;
    }

    private static Result<Extraction.PatternDetectionResult> TryDetectScalingPattern(Point3d[] centers, IGeometryContext context) =>
        new Point3d(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z)) is Point3d centroid
            && centers.Select(c => centroid.DistanceTo(c)).ToArray() is double[] distances
            && Enumerable.Range(0, distances.Length - 1).Select(i => distances[i] > context.AbsoluteTolerance ? distances[i + 1] / distances[i] : 0.0).Where(r => r > context.AbsoluteTolerance).ToArray() is double[] validRatios
            && validRatios.Length >= 2
            && (validRatios.Length switch {
                0 => double.MaxValue,
                1 => 0.0,
                int n => validRatios.Average() is double mean ? validRatios.Sum(v => (v - mean) * (v - mean)) / n : 0.0,
            }) is double variance && variance < ExtractionConfig.ScalingVarianceThreshold
                ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Pattern: new Extraction.ScalingPattern(SymmetryTransform: Transform.Scale(anchor: centroid, scaleFactor: validRatios.Average())), Confidence: 0.7))
                : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected);

    [Pure]
    private static Extraction.Feature ClassifyEdgeByDihedral(BrepEdge edge, Brep brep, double mean) {
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
            : new Extraction.GenericEdge(Length: edge.EdgeCurve.GetLength());
    }

    private static Result<Extraction.PatternDetectionResult> DetectPatternType(Point3d[] centers, IGeometryContext context) {
        Vector3d[] deltas = new Vector3d[centers.Length - 1];
        for (int i = 0; i < deltas.Length; i++) {
            deltas[i] = centers[i + 1] - centers[i];
        }
        return deltas.Length > 0
            && deltas[0].Length > context.AbsoluteTolerance
            && deltas.All(d => (d - deltas[0]).Length < context.AbsoluteTolerance)
            ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Pattern: new Extraction.LinearPattern(SymmetryTransform: Transform.Translation(deltas[0])), Confidence: 1.0))
            : TryDetectRadialPattern(centers: centers, context: context) is Result<Extraction.PatternDetectionResult> radialResult && radialResult.IsSuccess
                ? radialResult
                : TryDetectGridPattern(centers: centers, context: context) is Result<Extraction.PatternDetectionResult> gridResult && gridResult.IsSuccess
                    ? gridResult
                    : TryDetectScalingPattern(centers: centers, context: context) is Result<Extraction.PatternDetectionResult> scaleResult && scaleResult.IsSuccess
                        ? scaleResult
                        : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected.WithContext("No linear, radial, grid, or scaling pattern detected"));
    }

    [Pure]
    private static (bool IsHole, double Area) ClassifyHole(BrepLoop loop, IGeometryContext context) {
        using Curve? c = loop.To3dCurve();

        return c switch {
            null => (false, 0.0),
            _ when !c.IsClosed => (false, 0.0),
            _ when c.TryGetCircle(out Circle circ, tolerance: context.AbsoluteTolerance)
                => (true, Math.PI * circ.Radius * circ.Radius),
            _ when c.TryGetEllipse(out Ellipse ell, tolerance: context.AbsoluteTolerance)
                => (true, Math.PI * ell.Radius1 * ell.Radius2),
            _ when c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHolePolySides => ((Func<(bool, double)>)(() => {
                using AreaMassProperties? mp = AreaMassProperties.Compute(c);
                return (true, mp is { Area: double a } ? a : 0.0);
            }))(),
            _ => (false, 0.0),
        };
    }

    [Pure]
    private static Extraction.Feature ClassifyEdgeByAngle(
        BrepEdge edge,
        Vector3d normal0,
        Vector3d normal1,
        double mean) {
        double dihedralAngle = Math.Abs(Vector3d.VectorAngle(normal0, normal1));
        bool isSmooth = dihedralAngle > ExtractionConfig.SmoothEdgeAngleThreshold;
        bool isSharp = dihedralAngle < ExtractionConfig.SharpEdgeAngleThreshold;
        bool isChamfer = !isSmooth && !isSharp;
        double length = edge.EdgeCurve.GetLength();

        return (isChamfer, isSmooth, isSharp, mean > RhinoMath.ZeroTolerance) switch {
            (true, _, _, _) => new Extraction.Chamfer(Angle: dihedralAngle),
            (_, true, _, true) => new Extraction.VariableRadiusFillet(AverageRadius: 1.0 / mean),
            (_, _, true, _) => new Extraction.GenericEdge(Length: length),
            _ => new Extraction.GenericEdge(Length: length),
        };
    }

    [Pure]
    internal static Result<Extraction.PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context, ExtractionConfig.PrimitiveMetadata.ValidationMode,])
            .Bind(validGeometry => validGeometry switch {
                Surface surface => ResultFactory.Create(value: surface)
                    .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,])
                    .Bind(validSurface => ClassifySurface(surface: validSurface, context: context) switch {
                        Extraction.UnknownPrimitive => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(
                            error: E.Geometry.NoPrimitivesDetected),
                        Extraction.Primitive prim => ResultFactory.Create(
                            value: new Extraction.PrimitiveDecompositionResult(Primitives: [prim,], Residuals: _zeroResidual)),
                    }),
                Brep brep => ResultFactory.Create(value: brep)
                    .Validate(args: [context, V.Standard | V.BrepGranular,])
                    .Ensure(b => b.Faces.Count > 0, error: E.Geometry.DecompositionFailed.WithContext("Brep has no faces"))
                    .Bind(validBrep => DecomposeBrepFaces(brep: validBrep, context: context)),
                GeometryBase other => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(
                    error: E.Geometry.DecompositionFailed.WithContext($"Unsupported geometry type: {other.GetType().Name}")),
            });

    [Pure]
    private static Result<Extraction.FeatureExtractionResult> ExtractFeaturesInternal(Brep brep, IGeometryContext context) {
        BrepEdge[] validEdges = [.. brep.Edges.Where(e => e.EdgeCurve is not null),];
        Extraction.Feature[] edgeFeatures = new Extraction.Feature[validEdges.Length];

        for (int i = 0; i < validEdges.Length; i++) {
            edgeFeatures[i] = ClassifyEdge(edge: validEdges[i], brep: brep);
        }

        Extraction.Feature[] holeFeatures = [.. brep.Loops
            .Where(l => l.LoopType == BrepLoopType.Inner)
            .Select(l => ClassifyHole(loop: l, context: context))
            .Where(h => h.IsHole)
            .Select(h => (Extraction.Feature)new Extraction.Hole(h.Area)),
        ];

        Extraction.Feature[] allFeatures = [.. edgeFeatures.Concat(holeFeatures),];
        double confidence = brep.Edges.Count > 0
            ? 1.0 - (brep.Edges.Count(e => e.EdgeCurve is null) / (double)brep.Edges.Count)
            : 0.0;

        return ResultFactory.Create(value: new Extraction.FeatureExtractionResult(Features: allFeatures, Confidence: confidence));
    }

    [Pure]
    private static Extraction.Primitive ClassifySurface(Surface surface, IGeometryContext context) =>
        surface.TryGetPlane(out Plane pl, tolerance: context.AbsoluteTolerance)
            ? new Extraction.PlanarPrimitive(Frame: pl, Origin: pl.Origin)
            : surface.TryGetCylinder(out Cylinder cyl, tolerance: context.AbsoluteTolerance)
                && cyl.Radius > RhinoMath.ZeroTolerance
                && cyl.TotalHeight > RhinoMath.ZeroTolerance
                ? new Extraction.CylindricalPrimitive(Frame: new Plane(cyl.CircleAt(0.0).Center, cyl.Axis), Radius: cyl.Radius, Height: cyl.TotalHeight)
                : surface.TryGetSphere(out Sphere sph, tolerance: context.AbsoluteTolerance)
                    && sph.Radius > RhinoMath.ZeroTolerance
                    ? new Extraction.SphericalPrimitive(Frame: new Plane(sph.Center, Vector3d.ZAxis), Radius: sph.Radius)
                    : surface.TryGetCone(out Cone cone, tolerance: context.AbsoluteTolerance)
                        && cone.Radius > RhinoMath.ZeroTolerance
                        && cone.Height > RhinoMath.ZeroTolerance
                        ? new Extraction.ConicalPrimitive(Frame: new Plane(cone.BasePoint, cone.Axis), BaseRadius: cone.Radius, Height: cone.Height, Angle: Math.Atan(cone.Radius / cone.Height))
                        : surface.TryGetTorus(out Torus torus, tolerance: context.AbsoluteTolerance)
                            && torus.MajorRadius > RhinoMath.ZeroTolerance
                            && torus.MinorRadius > RhinoMath.ZeroTolerance
                            ? new Extraction.ToroidalPrimitive(Frame: torus.Plane, MajorRadius: torus.MajorRadius, MinorRadius: torus.MinorRadius)
                            : surface switch {
                                Extrusion ext when ext.IsValid && ext.PathLineCurve() is LineCurve lc =>
                                    new Extraction.ExtrusionPrimitive(Frame: new Plane(ext.PathStart, lc.Line.Direction), Length: lc.Line.Length),
                                _ => ClassifySurfaceByCurvature(surface: surface),
                            };

    [Pure]
    private static Extraction.Primitive ClassifySurfaceByCurvature(Surface surface) {
        (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1));
        int sampleCount = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.CurvatureSampleCount));
        int maxSamples = sampleCount * sampleCount;
        SurfaceCurvature[] curvatures = new SurfaceCurvature[maxSamples];
        int validCount = 0;
        double sampleDivisor = sampleCount > 1 ? sampleCount - 1.0 : 1.0;

        for (int i = 0; i < sampleCount; i++) {
            double up = u.ParameterAt(sampleCount > 1 ? i / sampleDivisor : 0.5);
            for (int j = 0; j < sampleCount; j++) {
                double vp = v.ParameterAt(sampleCount > 1 ? j / sampleDivisor : 0.5);
                SurfaceCurvature? curv = surface.CurvatureAt(u: up, v: vp);
                if (curv is not null) {
                    curvatures[validCount++] = curv;
                }
            }
        }

        return validCount < ExtractionConfig.MinCurvatureSamples
            ? new Extraction.UnknownPrimitive(Frame: Plane.WorldXY)
            : TestPrincipalCurvatureConstancy(surface: surface, curvatures: curvatures.AsSpan(0, validCount).ToArray());
    }

    private static (Vector3d U, Vector3d V, bool Success) FindGridBasis(Vector3d[] candidates, IGeometryContext context) {
        Vector3d u = candidates.Length > 0 ? candidates[0] : Vector3d.Zero;
        double minLengthSq = u.SquareLength;
        for (int i = 1; i < candidates.Length; i++) {
            double lengthSq = candidates[i].SquareLength;
            if (lengthSq < minLengthSq) {
                u = candidates[i];
                minLengthSq = lengthSq;
            }
        }

        double uLen = u.Length;
        if (uLen <= context.AbsoluteTolerance) {
            return (Vector3d.Zero, Vector3d.Zero, false);
        }

        Vector3d uDir = u / uLen;
        for (int i = 0; i < candidates.Length; i++) {
            Vector3d candidate = candidates[i];
            double candidateLen = candidate.Length;
            if (candidateLen > context.AbsoluteTolerance
                && Math.Abs(Vector3d.Multiply(uDir, candidate / candidateLen)) < ExtractionConfig.GridOrthogonalityThreshold) {
                return (u, candidate, true);
            }
        }
        return (Vector3d.Zero, Vector3d.Zero, false);
    }

    private static Result<Extraction.PatternDetectionResult> TryDetectRadialPattern(Point3d[] centers, IGeometryContext context) {
        Point3d centroid = new(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z));
        double meanDistance = centers.Average(c => centroid.DistanceTo(c));
        Vector3d normal = Plane.FitPlaneToPoints(points: centers, plane: out Plane bestFit) == PlaneFitResult.Success
            ? bestFit.Normal
            : (centers[0] - centroid) is Vector3d v1 && v1.Length > RhinoMath.ZeroTolerance
                ? ((Func<Vector3d>)(() => {
                    Vector3d v1n = v1 / v1.Length;
                    for (int i = 1; i < centers.Length; i++) {
                        Vector3d v2 = centers[i] - centroid;
                        if (v2.Length > RhinoMath.ZeroTolerance) {
                            Vector3d normalCand = Vector3d.CrossProduct(v1n, v2);
                            if (normalCand.Length > RhinoMath.ZeroTolerance) {
                                return normalCand / normalCand.Length;
                            }
                        }
                    }
                    return Vector3d.ZAxis;
                }))()
                : Vector3d.ZAxis;
        return meanDistance > context.AbsoluteTolerance
            && centers.All(c => RhinoMath.EpsilonEquals(centroid.DistanceTo(c), meanDistance, meanDistance * ExtractionConfig.RadialDistanceVariationThreshold))
            && centers.Select(c => c - centroid).ToArray() is Vector3d[] radii
            && Enumerable.Range(0, radii.Length - 1).Select(i => Vector3d.VectorAngle(radii[i], radii[i + 1])).ToArray() is double[] angles
            && angles.Average() is double meanAngle
            && angles.All(a => RhinoMath.EpsilonEquals(a, meanAngle, ExtractionConfig.RadialAngleVariationThreshold))
                ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Pattern: new Extraction.RadialPattern(SymmetryTransform: Transform.Rotation(meanAngle, normal, centroid)), Confidence: 0.9))
                : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected);
    }

    [Pure]
    private static Result<Extraction.PrimitiveDecompositionResult> DecomposeBrepFaces(Brep brep, IGeometryContext context) =>
        Enumerable.Range(0, brep.Faces.Count)
            .Select(i => brep.Faces[i].DuplicateSurface() switch {
                null => (false, null, 0.0,
                    (SystemError?)E.Geometry.DecompositionFailed.WithContext($"Failed to duplicate face {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
                Surface surface => ((Func<(bool, Extraction.Primitive?, double, SystemError?)>)(() => {
                    using (surface) {
                        Result<Surface> validated = ResultFactory.Create(value: surface)
                            .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,]);
                        SystemError? err = validated.Errors.Count > 0 ? validated.Errors[0] : null;
                        return !validated.IsSuccess
                            ? (false, null, 0.0, err)
                            : ClassifySurface(surface: surface, context: context) switch {
                                Extraction.UnknownPrimitive => (false, null, 0.0, null),
                                Extraction.Primitive prim => (true, prim, ComputeSurfaceResidual(surface: surface, primitive: prim), null),
                            };
                    }
                }))(),
            })
            .Aggregate<(bool Success, Extraction.Primitive? Primitive, double Residual, SystemError? Error),
                Result<Extraction.PrimitiveDecompositionResult>>(
                ResultFactory.Create(value: new Extraction.PrimitiveDecompositionResult(Primitives: [], Residuals: [])),
                (result, item) => item.Error.HasValue
                    ? ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: item.Error.Value)
                    : result.IsSuccess && item.Success && item.Primitive is not null
                        ? result.Map(r => new Extraction.PrimitiveDecompositionResult(
                            Primitives: [.. r.Primitives, item.Primitive,],
                            Residuals: [.. r.Residuals, item.Residual,]))
                        : result)
            .Bind(r => r.Primitives.Length > 0
                ? ResultFactory.Create(value: r)
                : ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(
                    error: E.Geometry.NoPrimitivesDetected.WithContext("No faces classified as primitives")));

    [Pure]
    private static Extraction.Primitive TestPrincipalCurvatureConstancy(
        Surface surface,
        SurfaceCurvature[] curvatures) {
        int n = curvatures.Length;
        double gaussianSum = 0.0;
        double gaussianSumSq = 0.0;
        double meanSum = 0.0;
        double meanSumSq = 0.0;
        for (int i = 0; i < n; i++) {
            double g = curvatures[i].Gaussian;
            double m = curvatures[i].Mean;
            gaussianSum += g;
            gaussianSumSq += g * g;
            meanSum += m;
            meanSumSq += m * m;
        }
        double gaussianMean = gaussianSum / n;
        double meanMean = meanSum / n;
        double gaussianVar = (gaussianSumSq / n) - (gaussianMean * gaussianMean);
        double meanVar = (meanSumSq / n) - (meanMean * meanMean);
        bool gaussianConstant = Math.Abs(gaussianMean) > RhinoMath.ZeroTolerance
            ? gaussianVar / (gaussianMean * gaussianMean) < ExtractionConfig.CurvatureVariationThreshold
            : gaussianVar < RhinoMath.SqrtEpsilon;
        bool meanConstant = Math.Abs(meanMean) > RhinoMath.ZeroTolerance
            ? meanVar / (meanMean * meanMean) < ExtractionConfig.CurvatureVariationThreshold
            : meanVar < RhinoMath.SqrtEpsilon;

        return (gaussianConstant, meanConstant, Math.Abs(gaussianMean) < RhinoMath.SqrtEpsilon) switch {
            (true, _, true) => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? new Extraction.PlanarPrimitive(Frame: frame, Origin: frame.Origin)
                : new Extraction.UnknownPrimitive(Frame: Plane.WorldXY),
            (false, true, false) when meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? new Extraction.CylindricalPrimitive(Frame: frame, Radius: 1.0 / (2.0 * meanMean), Height: surface.GetBoundingBox(accurate: false).Diagonal.Length)
                : new Extraction.UnknownPrimitive(Frame: Plane.WorldXY),
            (true, true, false) when gaussianMean > RhinoMath.ZeroTolerance && meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? new Extraction.SphericalPrimitive(Frame: frame, Radius: 1.0 / Math.Sqrt(gaussianMean))
                : new Extraction.UnknownPrimitive(Frame: Plane.WorldXY),
            _ => new Extraction.UnknownPrimitive(Frame: Plane.WorldXY),
        };
    }

    [Pure]
    private static double ComputeSurfaceResidual(Surface surface, Extraction.Primitive primitive) {
        (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1));
        int samplesPerDir = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount));
        int totalSamples = samplesPerDir * samplesPerDir;
        double sumSquaredDistances = 0.0;
        double sampleDivisor = samplesPerDir > 1 ? samplesPerDir - 1.0 : 1.0;

        for (int i = 0; i < samplesPerDir; i++) {
            double up = u.ParameterAt(i / sampleDivisor);
            for (int j = 0; j < samplesPerDir; j++) {
                double vp = v.ParameterAt(j / sampleDivisor);
                Point3d surfacePoint = surface.PointAt(u: up, v: vp);
                Point3d primitivePoint = primitive switch {
                    Extraction.PlanarPrimitive pl => pl.Frame.ClosestPoint(surfacePoint),
                    Extraction.CylindricalPrimitive cyl => ((Func<Point3d>)(() => {
                        Vector3d toPoint = surfacePoint - cyl.Frame.Origin;
                        double axisProjection = Vector3d.Multiply(toPoint, cyl.Frame.ZAxis);
                        Point3d axisPoint = cyl.Frame.Origin + (cyl.Frame.ZAxis * axisProjection);
                        Vector3d radialDir = surfacePoint - axisPoint;
                        return radialDir.Length > RhinoMath.ZeroTolerance
                            ? axisPoint + ((radialDir / radialDir.Length) * cyl.Radius)
                            : axisPoint + (cyl.Frame.XAxis * cyl.Radius);
                    }))(),
                    Extraction.SphericalPrimitive sph => ((Func<Point3d>)(() => {
                        Vector3d dir = surfacePoint - sph.Frame.Origin;
                        return dir.Length > RhinoMath.ZeroTolerance
                            ? sph.Frame.Origin + ((dir / dir.Length) * sph.Radius)
                            : sph.Frame.Origin + new Vector3d(sph.Radius, 0, 0);
                    }))(),
                    Extraction.ConicalPrimitive cone => ((Func<Point3d>)(() => {
                        Vector3d toPoint = surfacePoint - cone.Frame.Origin;
                        double axisProjection = Vector3d.Multiply(toPoint, cone.Frame.ZAxis);
                        double coneRadius = cone.BaseRadius * (1.0 - (axisProjection / cone.Height));
                        Point3d axisPoint = cone.Frame.Origin + (cone.Frame.ZAxis * axisProjection);
                        Vector3d radialDir = surfacePoint - axisPoint;
                        return radialDir.Length > RhinoMath.ZeroTolerance
                            ? axisPoint + ((radialDir / radialDir.Length) * coneRadius)
                            : axisPoint + (cone.Frame.XAxis * coneRadius);
                    }))(),
                    Extraction.ToroidalPrimitive tor => ((Func<Point3d>)(() => {
                        Vector3d toPoint = surfacePoint - tor.Frame.Origin;
                        Vector3d radialInPlane = toPoint - (tor.Frame.ZAxis * Vector3d.Multiply(toPoint, tor.Frame.ZAxis));
                        Point3d majorCirclePoint = radialInPlane.Length > RhinoMath.ZeroTolerance
                            ? tor.Frame.Origin + ((radialInPlane / radialInPlane.Length) * tor.MajorRadius)
                            : tor.Frame.Origin + (tor.Frame.XAxis * tor.MajorRadius);
                        Vector3d toMinor = surfacePoint - majorCirclePoint;
                        return toMinor.Length > RhinoMath.ZeroTolerance
                            ? majorCirclePoint + ((toMinor / toMinor.Length) * tor.MinorRadius)
                            : majorCirclePoint + (tor.Frame.ZAxis * tor.MinorRadius);
                    }))(),
                    Extraction.ExtrusionPrimitive ext => ext.Frame.ClosestPoint(surfacePoint),
                    _ => surfacePoint,
                };
                sumSquaredDistances += surfacePoint.DistanceToSquared(primitivePoint);
            }
        }

        return Math.Sqrt(sumSquaredDistances / totalSamples);
    }
}
