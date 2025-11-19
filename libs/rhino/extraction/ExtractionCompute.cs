using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Pure algorithmic implementations for feature extraction, primitive decomposition, and pattern recognition.</summary>
internal static class ExtractionCompute {
    // ═══════════════════════════════════════════════════════════════════════════════
    // Feature Extraction
    // ═══════════════════════════════════════════════════════════════════════════════

    [Pure]
    internal static Result<Extraction.FeatureResult> ExtractFeatures(Brep brep, IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Ensure(b => b.Faces.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no faces"))
            .Ensure(b => b.Edges.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no edges"))
            .Bind(validBrep => ExtractFeaturesInternal(brep: validBrep, context: context));

    [Pure]
    private static Result<Extraction.FeatureResult> ExtractFeaturesInternal(Brep brep, IGeometryContext context) =>
        ([.. brep.Edges.Where(static e => e.EdgeCurve is not null).Select(edge => ClassifyEdge(edge: edge, brep: brep)),
          .. brep.Loops
            .Where(static l => l.LoopType == BrepLoopType.Inner)
            .Select(l => ClassifyHole(loop: l, context: context))
            .Where(static h => h.IsHole)
            .Select(static h => new Extraction.Feature(Kind: Extraction.FeatureKind.Hole, Parameter: h.Area)),
        ]) is Extraction.Feature[] allFeatures
            ? ResultFactory.Create(value: new Extraction.FeatureResult(
                Features: allFeatures,
                Confidence: brep.Edges.Count > 0
                    ? 1.0 - (brep.Edges.Count(static e => e.EdgeCurve is null) / (double)brep.Edges.Count)
                    : 0.0))
            : ResultFactory.Create<Extraction.FeatureResult>(error: E.Geometry.FeatureExtractionFailed);

    [Pure]
    private static Extraction.Feature ClassifyEdge(BrepEdge edge, Brep brep) =>
        (edge.Domain.Min, edge.Domain.Max, Enumerable.Range(0, ExtractionConfig.FilletCurvatureSampleCount)
            .Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.FilletCurvatureSampleCount - 1.0))))
            .Where(v => v.IsValid)
            .Select(v => v.Length).ToArray()) is (double tMin, double tMax, double[] curvatures) && curvatures.Length >= 2
                ? ClassifyEdgeFromCurvature(edge: edge, brep: brep, curvatures: curvatures, tMin: tMin, tMax: tMax)
                : new Extraction.Feature(Kind: Extraction.FeatureKind.GenericEdge, Parameter: edge.EdgeCurve.GetLength());

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
                ? new Extraction.Feature(Kind: Extraction.FeatureKind.Fillet, Parameter: 1.0 / mean)
                : ClassifyEdgeByDihedral(edge: edge, brep: brep, mean: mean);

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
            : new Extraction.Feature(Kind: Extraction.FeatureKind.GenericEdge, Parameter: edge.EdgeCurve.GetLength());
    }

    [Pure]
    private static Extraction.Feature ClassifyEdgeByAngle(
        BrepEdge edge,
        Vector3d normal0,
        Vector3d normal1,
        double mean) =>
        (Math.Abs(Vector3d.VectorAngle(normal0, normal1)), edge.EdgeCurve.GetLength()) is (double dihedralAngle, double length)
            ? (dihedralAngle > ExtractionConfig.SmoothEdgeAngleThreshold, dihedralAngle < ExtractionConfig.SharpEdgeAngleThreshold, mean > RhinoMath.ZeroTolerance) switch {
                (false, false, _) => new Extraction.Feature(Kind: Extraction.FeatureKind.Chamfer, Parameter: dihedralAngle),
                (true, _, true) => new Extraction.Feature(Kind: Extraction.FeatureKind.VariableRadiusFillet, Parameter: 1.0 / mean),
                (_, true, _) => new Extraction.Feature(Kind: Extraction.FeatureKind.GenericEdge, Parameter: length),
                _ => new Extraction.Feature(Kind: Extraction.FeatureKind.GenericEdge, Parameter: length),
            }
            : new Extraction.Feature(Kind: Extraction.FeatureKind.GenericEdge, Parameter: 0.0);

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
            _ when c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHolePolySides => (
                true,
                ((Func<double>)(() => {
                    using AreaMassProperties? massProperties = AreaMassProperties.Compute(c);
                    return massProperties is { Area: double holeArea } ? holeArea : 0.0;
                }))()
            ),
            _ => (false, 0.0),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Primitive Decomposition
    // ═══════════════════════════════════════════════════════════════════════════════

    private static readonly double[] _zeroResidual = [0.0,];

    [Pure]
    internal static Result<Extraction.PrimitiveResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ResultFactory.Create(value: geometry)
            .Bind(validGeometry => validGeometry switch {
                Surface surface => ResultFactory.Create(value: surface)
                    .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,])
                    .Bind(validSurface => ClassifySurface(surface: validSurface, context: context) switch {
                        (true, Extraction.PrimitiveKind kind, Plane frame, double[] pars) => ResultFactory.Create(
                            value: new Extraction.PrimitiveResult(
                                Primitives: [new Extraction.Primitive(Kind: kind, Frame: frame, Parameters: pars),],
                                Residuals: _zeroResidual)),
                        _ => ResultFactory.Create<Extraction.PrimitiveResult>(error: E.Geometry.NoPrimitivesDetected),
                    }),
                Brep brep => ResultFactory.Create(value: brep)
                    .Validate(args: [context, V.Standard | V.BrepGranular,])
                    .Ensure(b => b.Faces.Count > 0, error: E.Geometry.DecompositionFailed.WithContext("Brep has no faces"))
                    .Bind(validBrep => DecomposeBrepFaces(brep: validBrep, context: context)),
                GeometryBase other => ResultFactory.Create<Extraction.PrimitiveResult>(
                    error: E.Geometry.DecompositionFailed.WithContext($"Unsupported geometry type: {other.GetType().Name}")),
            });

    [Pure]
    private static Result<Extraction.PrimitiveResult> DecomposeBrepFaces(Brep brep, IGeometryContext context) =>
        Enumerable.Range(0, brep.Faces.Count)
            .Select(i => brep.Faces[i].DuplicateSurface() switch {
                null => (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, Array.Empty<double>(), 0.0,
                    (SystemError?)E.Geometry.DecompositionFailed.WithContext($"Failed to duplicate face {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
                Surface surface => ((Func<(bool, Extraction.PrimitiveKind, Plane, double[], double, SystemError?)>)(() => {
                    using (surface) {
                        Result<Surface> validatedSurface = ResultFactory.Create(value: surface)
                            .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,]);
                        SystemError? firstError = validatedSurface.Errors.Count > 0 ? validatedSurface.Errors[0] : null;
                        return !validatedSurface.IsSuccess
                            ? (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, Array.Empty<double>(), 0.0, firstError)
                            : ClassifySurface(surface: surface, context: context) switch {
                                (true, Extraction.PrimitiveKind kind, Plane frame, double[] pars) =>
                                    (true, kind, frame, pars, ComputeSurfaceResidual(surface: surface, kind: kind, frame: frame, pars: pars), null),
                                (bool Success, Extraction.PrimitiveKind Kind, Plane Frame, double[] Params) classification =>
                                    (classification.Success, classification.Kind, classification.Frame, classification.Params, 0.0, null),
                            };
                    }
                }))(),
            })
            .Aggregate<(bool Success, Extraction.PrimitiveKind Kind, Plane Frame, double[] Params, double Residual, SystemError? Error),
                Result<Extraction.PrimitiveResult>>(
                ResultFactory.Create(value: new Extraction.PrimitiveResult(Primitives: [], Residuals: [])),
                (result, item) => item.Error.HasValue
                    ? ResultFactory.Create<Extraction.PrimitiveResult>(error: item.Error.Value)
                    : result.IsSuccess && item.Success
                        ? result.Map(r => new Extraction.PrimitiveResult(
                            Primitives: [.. r.Primitives, new Extraction.Primitive(Kind: item.Kind, Frame: item.Frame, Parameters: item.Params),],
                            Residuals: [.. r.Residuals, item.Residual,]))
                        : result)
            .Bind(r => r.Primitives.Count > 0
                ? ResultFactory.Create(value: r)
                : ResultFactory.Create<Extraction.PrimitiveResult>(
                    error: E.Geometry.NoPrimitivesDetected.WithContext("No faces classified as primitives")));

    [Pure]
    private static (bool Success, Extraction.PrimitiveKind Kind, Plane Frame, double[] Params) ClassifySurface(Surface surface, IGeometryContext context) =>
        surface.TryGetPlane(out Plane pl, tolerance: context.AbsoluteTolerance)
            ? (true, Extraction.PrimitiveKind.Plane, pl, [pl.OriginX, pl.OriginY, pl.OriginZ,])
            : surface.TryGetCylinder(out Cylinder cyl, tolerance: context.AbsoluteTolerance)
                && cyl.Radius > RhinoMath.ZeroTolerance
                && cyl.TotalHeight > RhinoMath.ZeroTolerance
                ? (true, Extraction.PrimitiveKind.Cylinder, new Plane(cyl.CircleAt(0.0).Center, cyl.Axis), [cyl.Radius, cyl.TotalHeight,])
                : surface.TryGetSphere(out Sphere sph, tolerance: context.AbsoluteTolerance)
                    && sph.Radius > RhinoMath.ZeroTolerance
                    ? (true, Extraction.PrimitiveKind.Sphere, new Plane(sph.Center, Vector3d.ZAxis), [sph.Radius,])
                    : surface.TryGetCone(out Cone cone, tolerance: context.AbsoluteTolerance)
                        && cone.Radius > RhinoMath.ZeroTolerance
                        && cone.Height > RhinoMath.ZeroTolerance
                        ? (true, Extraction.PrimitiveKind.Cone, new Plane(cone.BasePoint, cone.Axis), [cone.Radius, cone.Height, Math.Atan(cone.Radius / cone.Height),])
                        : surface.TryGetTorus(out Torus torus, tolerance: context.AbsoluteTolerance)
                            && torus.MajorRadius > RhinoMath.ZeroTolerance
                            && torus.MinorRadius > RhinoMath.ZeroTolerance
                            ? (true, Extraction.PrimitiveKind.Torus, torus.Plane, [torus.MajorRadius, torus.MinorRadius,])
                            : surface switch {
                                Extrusion ext when ext.IsValid && ext.PathLineCurve() is LineCurve lc =>
                                    (true, Extraction.PrimitiveKind.Extrusion, new Plane(ext.PathStart, lc.Line.Direction), [lc.Line.Length,]),
                                _ => ClassifySurfaceByCurvature(surface: surface),
                            };

    [Pure]
    private static (bool Success, Extraction.PrimitiveKind Kind, Plane Frame, double[] Params) ClassifySurfaceByCurvature(Surface surface) =>
        ((int)Math.Ceiling(Math.Sqrt(ExtractionConfig.CurvatureSampleCount)), surface.Domain(0), surface.Domain(1)) is (int sampleCount, Interval uDomain, Interval vDomain)
            && (sampleCount > 1 ? sampleCount - 1.0 : 1.0) is double sampleDivisor
            && (from int i in Enumerable.Range(0, sampleCount)
                from int j in Enumerable.Range(0, sampleCount)
                let u = uDomain.ParameterAt(sampleCount > 1 ? i / sampleDivisor : 0.5)
                let v = vDomain.ParameterAt(sampleCount > 1 ? j / sampleDivisor : 0.5)
                let curv = surface.CurvatureAt(u: u, v: v)
                where curv is not null
                select curv).ToArray() is SurfaceCurvature[] curvatures
                ? curvatures.Length < ExtractionConfig.MinCurvatureSamples
                    ? (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, [])
                    : TestPrincipalCurvatureConstancy(surface: surface, curvatures: curvatures)
                : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []);

    [Pure]
    private static (bool Success, Extraction.PrimitiveKind Kind, Plane Frame, double[] Params) TestPrincipalCurvatureConstancy(
        Surface surface,
        SurfaceCurvature[] curvatures) =>
        curvatures.Aggregate(
            seed: (GaussianSum: 0.0, GaussianSumSq: 0.0, MeanSum: 0.0, MeanSumSq: 0.0),
            func: (acc, c) => (acc.GaussianSum + c.Gaussian, acc.GaussianSumSq + (c.Gaussian * c.Gaussian), acc.MeanSum + c.Mean, acc.MeanSumSq + (c.Mean * c.Mean))) is (double gSum, double gSumSq, double mSum, double mSumSq)
            && curvatures.Length is int n && n > 0
            && (gSum / n, mSum / n) is (double gaussianMean, double meanMean)
            && ((gSumSq / n) - (gaussianMean * gaussianMean), (mSumSq / n) - (meanMean * meanMean)) is (double gaussianVar, double meanVar)
            && (Math.Abs(gaussianMean) > RhinoMath.ZeroTolerance
                ? gaussianVar / (gaussianMean * gaussianMean) < ExtractionConfig.CurvatureVariationThreshold
                : gaussianVar < RhinoMath.SqrtEpsilon) is bool gaussianConstant
            && (Math.Abs(meanMean) > RhinoMath.ZeroTolerance
                ? meanVar / (meanMean * meanMean) < ExtractionConfig.CurvatureVariationThreshold
                : meanVar < RhinoMath.SqrtEpsilon) is bool meanConstant
                ? (gaussianConstant, meanConstant, Math.Abs(gaussianMean) < RhinoMath.SqrtEpsilon) switch {
                    (true, _, true) => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                        ? (true, Extraction.PrimitiveKind.Plane, frame, [frame.OriginX, frame.OriginY, frame.OriginZ,])
                        : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
                    (false, true, false) when meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                        ? (true, Extraction.PrimitiveKind.Cylinder, frame, [1.0 / (2.0 * meanMean), surface.GetBoundingBox(accurate: false).Diagonal.Length,])
                        : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
                    (true, true, false) when gaussianMean > RhinoMath.ZeroTolerance && meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                        ? (true, Extraction.PrimitiveKind.Sphere, frame, [1.0 / Math.Sqrt(gaussianMean),])
                        : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
                    _ => (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
                }
                : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []);

    [Pure]
    private static double ComputeSurfaceResidual(Surface surface, Extraction.PrimitiveKind kind, Plane frame, double[] pars) =>
        ((int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount)), surface.Domain(0), surface.Domain(1)) is (int samplesPerDir, Interval uDomain, Interval vDomain)
            && (samplesPerDir > 1 ? samplesPerDir - 1.0 : 1.0) is double sampleDivisor
            ? Math.Sqrt((from int i in Enumerable.Range(0, samplesPerDir)
                from int j in Enumerable.Range(0, samplesPerDir)
                let u = uDomain.ParameterAt(i / sampleDivisor)
                let v = vDomain.ParameterAt(j / sampleDivisor)
                let surfacePoint = surface.PointAt(u: u, v: v)
                let primitivePoint = kind switch {
                    Extraction.PrimitiveKind.Plane when pars.Length >= 3 => frame.ClosestPoint(surfacePoint),
                    Extraction.PrimitiveKind.Cylinder when pars.Length >= 2 => ProjectPointToCylinder(point: surfacePoint, cylinderPlane: frame, radius: pars[0]),
                    Extraction.PrimitiveKind.Sphere when pars.Length >= 1 => ProjectPointToSphere(point: surfacePoint, center: frame.Origin, radius: pars[0]),
                    Extraction.PrimitiveKind.Cone when pars.Length >= 3 => ProjectPointToCone(point: surfacePoint, conePlane: frame, baseRadius: pars[0], height: pars[1]),
                    Extraction.PrimitiveKind.Torus when pars.Length >= 2 => ProjectPointToTorus(point: surfacePoint, torusPlane: frame, majorRadius: pars[0], minorRadius: pars[1]),
                    Extraction.PrimitiveKind.Extrusion when pars.Length >= 1 => frame.ClosestPoint(surfacePoint),
                    _ => surfacePoint,
                }
                select surfacePoint.DistanceToSquared(primitivePoint)).Sum() / (samplesPerDir * samplesPerDir))
            : 0.0;

    [Pure]
    private static Point3d ProjectPointToCylinder(Point3d point, Plane cylinderPlane, double radius) =>
        (point - cylinderPlane.Origin, Vector3d.Multiply(point - cylinderPlane.Origin, cylinderPlane.ZAxis)) is (Vector3d toPoint, double axisProjection)
            && (cylinderPlane.Origin + (cylinderPlane.ZAxis * axisProjection)) is Point3d axisPoint
            && (point - axisPoint) is Vector3d radialDir
            ? radialDir.Length > RhinoMath.ZeroTolerance
                ? axisPoint + ((radialDir / radialDir.Length) * radius)
                : axisPoint + (cylinderPlane.XAxis * radius)
            : point;

    [Pure]
    private static Point3d ProjectPointToSphere(Point3d point, Point3d center, double radius) =>
        (point - center) is Vector3d dir
            ? dir.Length > RhinoMath.ZeroTolerance
                ? center + ((dir / dir.Length) * radius)
                : center + new Vector3d(radius, 0, 0)
            : point;

    [Pure]
    private static Point3d ProjectPointToCone(Point3d point, Plane conePlane, double baseRadius, double height) =>
        (point - conePlane.Origin, Vector3d.Multiply(point - conePlane.Origin, conePlane.ZAxis)) is (Vector3d toPoint, double axisProjection)
            && (baseRadius * (1.0 - (axisProjection / height))) is double coneRadius
            && (conePlane.Origin + (conePlane.ZAxis * axisProjection)) is Point3d axisPoint
            && (point - axisPoint) is Vector3d radialDir
            ? radialDir.Length > RhinoMath.ZeroTolerance
                ? axisPoint + ((radialDir / radialDir.Length) * coneRadius)
                : axisPoint + (conePlane.XAxis * coneRadius)
            : point;

    [Pure]
    private static Point3d ProjectPointToTorus(Point3d point, Plane torusPlane, double majorRadius, double minorRadius) =>
        (point - torusPlane.Origin) is Vector3d toPoint
            && (toPoint - (torusPlane.ZAxis * Vector3d.Multiply(toPoint, torusPlane.ZAxis))) is Vector3d radialInPlane
            && (radialInPlane.Length > RhinoMath.ZeroTolerance
                ? torusPlane.Origin + ((radialInPlane / radialInPlane.Length) * majorRadius)
                : torusPlane.Origin + (torusPlane.XAxis * majorRadius)) is Point3d majorCirclePoint
            && (point - majorCirclePoint) is Vector3d toMinor
            ? toMinor.Length > RhinoMath.ZeroTolerance
                ? majorCirclePoint + ((toMinor / toMinor.Length) * minorRadius)
                : majorCirclePoint + (torusPlane.ZAxis * minorRadius)
            : point;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Pattern Extraction
    // ═══════════════════════════════════════════════════════════════════════════════

    [Pure]
    internal static Result<Extraction.PatternResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(gs => gs.Length >= ExtractionConfig.PatternMinInstances, error: E.Geometry.NoPatternDetected.WithContext($"Need at least {ExtractionConfig.PatternMinInstances.ToString(System.Globalization.CultureInfo.InvariantCulture)} instances"))
            .Ensure(gs => gs.All(g => g is not null), error: E.Validation.GeometryInvalid.WithContext("Array contains null geometries"))
            .Bind(gs => ResultFactory.Create(value: (IEnumerable<GeometryBase>)gs)
                .TraverseElements(geometry => ResultFactory.Create(value: geometry)
                    .Validate(args: [context, V.Standard | V.BoundingBox,])
                    .Map(validated => validated.GetBoundingBox(accurate: false).Center))
                .Bind(centers => DetectPatternType(centers: [.. centers], context: context)));

    private static Result<Extraction.PatternResult> DetectPatternType(Point3d[] centers, IGeometryContext context) =>
        ((Func<Vector3d[]>)(() => {
            Vector3d[] deltas = new Vector3d[centers.Length - 1];
            for (int i = 0; i < deltas.Length; i++) {
                deltas[i] = centers[i + 1] - centers[i];
            }
            return deltas;
        }))() is Vector3d[] deltas
            && deltas.Length > 0
            && deltas[0].Length > context.AbsoluteTolerance
            && deltas.All(d => (d - deltas[0]).Length < context.AbsoluteTolerance)
            ? ResultFactory.Create(value: new Extraction.PatternResult(
                Kind: Extraction.PatternKind.Linear,
                SymmetryTransform: Transform.Translation(deltas[0]),
                Confidence: 1.0))
            : TryDetectRadialPattern(centers: centers, context: context) is Result<Extraction.PatternResult> radialResult && radialResult.IsSuccess
                ? radialResult
                : TryDetectGridPattern(centers: centers, context: context) is Result<Extraction.PatternResult> gridResult && gridResult.IsSuccess
                    ? gridResult
                    : TryDetectScalingPattern(centers: centers, context: context) is Result<Extraction.PatternResult> scaleResult && scaleResult.IsSuccess
                        ? scaleResult
                        : ResultFactory.Create<Extraction.PatternResult>(error: E.Geometry.NoPatternDetected.WithContext("No linear, radial, grid, or scaling pattern detected"));

    [Pure]
    private static Result<Extraction.PatternResult> TryDetectRadialPattern(Point3d[] centers, IGeometryContext context) =>
        new Point3d(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z)) is Point3d centroid
            && centers.Average(c => centroid.DistanceTo(c)) is double meanDistance
            && meanDistance > context.AbsoluteTolerance
            && centers.All(c => RhinoMath.EpsilonEquals(centroid.DistanceTo(c), meanDistance, meanDistance * ExtractionConfig.RadialDistanceVariationThreshold))
            && (centers.Select(c => c - centroid).ToArray(), ComputeBestFitPlaneNormal(points: centers, centroid: centroid)) is (Vector3d[] radii, Vector3d normal)
            && Enumerable.Range(0, radii.Length - 1).Select(i => Vector3d.VectorAngle(radii[i], radii[i + 1])).ToArray() is double[] angles
            && angles.Average() is double meanAngle
            && angles.All(a => RhinoMath.EpsilonEquals(a, meanAngle, ExtractionConfig.RadialAngleVariationThreshold))
                ? ResultFactory.Create(value: new Extraction.PatternResult(
                    Kind: Extraction.PatternKind.Radial,
                    SymmetryTransform: Transform.Rotation(meanAngle, normal, centroid),
                    Confidence: 0.9))
                : ResultFactory.Create<Extraction.PatternResult>(error: E.Geometry.NoPatternDetected);

    [Pure]
    private static Vector3d ComputeBestFitPlaneNormal(Point3d[] points, Point3d centroid) =>
        Plane.FitPlaneToPoints(points: points, plane: out Plane bestFit) == PlaneFitResult.Success
            ? bestFit.Normal
            : (points[0] - centroid) is Vector3d v1 && v1.Length > RhinoMath.ZeroTolerance
                && (v1 / v1.Length) is Vector3d v1n
                ? points.Skip(1)
                    .Select(p => p - centroid)
                    .Where(v2 => v2.Length > RhinoMath.ZeroTolerance)
                    .Select(v2 => Vector3d.CrossProduct(v1n, v2))
                    .FirstOrDefault(normal => normal.Length > RhinoMath.ZeroTolerance) is Vector3d found && found.Length > RhinoMath.ZeroTolerance
                        ? found / found.Length
                        : Vector3d.ZAxis
                : Vector3d.ZAxis;

    private static Result<Extraction.PatternResult> TryDetectGridPattern(Point3d[] centers, IGeometryContext context) =>
        (centers[0], Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[0]).ToArray()) is (Point3d origin, Vector3d[] relativeVectors)
            && relativeVectors.Where(v => v.Length > context.AbsoluteTolerance).ToArray() is Vector3d[] candidates && candidates.Length >= 2
            && FindGridBasis(candidates: candidates, context: context) is (Vector3d u, Vector3d v, bool success) && success
            && relativeVectors.All(vec => IsGridPoint(vector: vec, u: u, v: v, context: context))
                ? ResultFactory.Create(value: new Extraction.PatternResult(
                    Kind: Extraction.PatternKind.Grid,
                    SymmetryTransform: Transform.PlaneToPlane(Plane.WorldXY, new Plane(origin, u, v)),
                    Confidence: 0.9))
                : ResultFactory.Create<Extraction.PatternResult>(error: E.Geometry.NoPatternDetected);

    [Pure]
    private static (Vector3d U, Vector3d V, bool Success) FindGridBasis(Vector3d[] candidates, IGeometryContext context) =>
        candidates.Length == 0
            ? (Vector3d.Zero, Vector3d.Zero, false)
            : candidates.Aggregate(
                seed: (U: candidates[0], MinSq: candidates[0].SquareLength),
                func: (acc, c) => c.SquareLength < acc.MinSq ? (c, c.SquareLength) : acc).U is Vector3d u
                && u.Length is double uLen && uLen > context.AbsoluteTolerance
                && (u / uLen) is Vector3d uDir
                && candidates.FirstOrDefault(c => c.Length > context.AbsoluteTolerance
                    && Math.Abs(Vector3d.Multiply(uDir, c / c.Length)) < ExtractionConfig.GridOrthogonalityThreshold) is Vector3d v
                && v.Length > context.AbsoluteTolerance
                    ? (u, v, true)
                    : (Vector3d.Zero, Vector3d.Zero, false);

    [Pure]
    private static bool IsGridPoint(Vector3d vector, Vector3d u, Vector3d v, IGeometryContext context) =>
        u.Length is double uLen && v.Length is double vLen
            && uLen > context.AbsoluteTolerance
            && vLen > context.AbsoluteTolerance
            && Vector3d.Multiply(vector, u) / (uLen * uLen) is double a
            && Vector3d.Multiply(vector, v) / (vLen * vLen) is double b
            && Math.Abs(a - Math.Round(a)) < ExtractionConfig.GridPointDeviationThreshold
            && Math.Abs(b - Math.Round(b)) < ExtractionConfig.GridPointDeviationThreshold;

    private static Result<Extraction.PatternResult> TryDetectScalingPattern(Point3d[] centers, IGeometryContext context) =>
        new Point3d(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z)) is Point3d centroid
            && centers.Select(c => centroid.DistanceTo(c)).ToArray() is double[] distances
            && Enumerable.Range(0, distances.Length - 1).Select(i => distances[i] > context.AbsoluteTolerance ? distances[i + 1] / distances[i] : 0.0).Where(r => r > context.AbsoluteTolerance).ToArray() is double[] validRatios
            && validRatios.Length >= 2 && ComputeVariance(values: validRatios) is double variance && variance < ExtractionConfig.ScalingVarianceThreshold
                ? ResultFactory.Create(value: new Extraction.PatternResult(
                    Kind: Extraction.PatternKind.Scaling,
                    SymmetryTransform: Transform.Scale(anchor: centroid, scaleFactor: validRatios.Average()),
                    Confidence: 0.7))
                : ResultFactory.Create<Extraction.PatternResult>(error: E.Geometry.NoPatternDetected);

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
