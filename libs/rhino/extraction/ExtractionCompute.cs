using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Feature extraction algorithms: design features, primitive decomposition, pattern recognition.</summary>
internal static class ExtractionCompute {
    [Pure]
    internal static Result<Extraction.FeatureExtractionResult> ExtractFeatures(Brep brep, IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, V.Standard | V.Topology | V.BrepGranular,])
            .Ensure(b => b.Faces.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no faces"))
            .Ensure(b => b.Edges.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no edges"))
            .Bind(validBrep => ExtractFeaturesInternal(brep: validBrep, context: context));

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
            .OfType<Extraction.Feature>(),
        ];

        Extraction.Feature[] allFeatures = [.. edgeFeatures.Concat(holeFeatures),];
        double confidence = brep.Edges.Count > 0
            ? 1.0 - (brep.Edges.Count(e => e.EdgeCurve is null) / (double)brep.Edges.Count)
            : 0.0;

        return ResultFactory.Create(value: new Extraction.FeatureExtractionResult(allFeatures, confidence));
    }

    [Pure]
    private static Extraction.Feature ClassifyEdge(BrepEdge edge, Brep brep) =>
        (edge.Domain.Min, edge.Domain.Max, Enumerable.Range(0, ExtractionConfig.FilletCurvatureSampleCount)
            .Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.FilletCurvatureSampleCount - 1.0))))
            .Where(v => v.IsValid)
            .Select(v => v.Length).ToArray()) is (double tMin, double tMax, double[] curvatures) && curvatures.Length >= 2
                ? ClassifyEdgeFromCurvature(edge: edge, brep: brep, curvatures: curvatures, tMin: tMin, tMax: tMax)
                : new Extraction.Feature(Extraction.FeatureKind.GenericEdge, edge.EdgeCurve.GetLength());

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
                ? new Extraction.Feature(Extraction.FeatureKind.Fillet, 1.0 / mean)
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
            : new Extraction.Feature(Extraction.FeatureKind.GenericEdge, edge.EdgeCurve.GetLength());
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
            (true, _, _, _) => new Extraction.Feature(Extraction.FeatureKind.Chamfer, dihedralAngle),
            (_, true, _, true) => new Extraction.Feature(Extraction.FeatureKind.VariableRadiusFillet, 1.0 / mean),
            _ => new Extraction.Feature(Extraction.FeatureKind.GenericEdge, length),
        };
    }

    [Pure]
    private static Extraction.Feature? ClassifyHole(BrepLoop loop, IGeometryContext context) {
        using Curve? c = loop.To3dCurve();

        return c switch {
            null => null,
            _ when !c.IsClosed => null,
            _ when c.TryGetCircle(out Circle circ, tolerance: context.AbsoluteTolerance)
                => new Extraction.Feature(Extraction.FeatureKind.Hole, Math.PI * circ.Radius * circ.Radius),
            _ when c.TryGetEllipse(out Ellipse ell, tolerance: context.AbsoluteTolerance)
                => new Extraction.Feature(Extraction.FeatureKind.Hole, Math.PI * ell.Radius1 * ell.Radius2),
            _ when c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHolePolySides =>
                ((Func<Extraction.Feature?>)(() => {
                    using AreaMassProperties? massProperties = AreaMassProperties.Compute(c);
                    return massProperties is { Area: double holeArea } ? new Extraction.Feature(Extraction.FeatureKind.Hole, holeArea) : null;
                }))(),
            _ => null,
        };
    }

    private static readonly double[] _zeroResidual = [0.0,];

    [Pure]
    internal static Result<Extraction.PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context, V.Standard | V.BoundingBox,])
            .Bind(validGeometry => validGeometry switch {
                Surface surface => ResultFactory.Create(value: surface)
                    .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,])
                    .Bind(validSurface => ClassifySurface(surface: validSurface, context: context) switch {
                        (true, Extraction.PrimitiveKind kind, Plane frame, double[] pars) => ResultFactory.Create(value: new Extraction.PrimitiveDecompositionResult(
                            new[] { new Extraction.Primitive(kind, frame, pars), },
                            _zeroResidual)),
                        _ => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.NoPrimitivesDetected),
                    }),
                Brep brep => ResultFactory.Create(value: brep)
                    .Validate(args: [context, V.Standard | V.BrepGranular,])
                    .Ensure(b => b.Faces.Count > 0, error: E.Geometry.DecompositionFailed.WithContext("Brep has no faces"))
                    .Bind(validBrep => DecomposeBrepFaces(brep: validBrep, context: context)),
                GeometryBase other => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(
                    error: E.Geometry.DecompositionFailed.WithContext($"Unsupported geometry type: {other.GetType().Name}")),
            });

    [Pure]
    private static Result<Extraction.PrimitiveDecompositionResult> DecomposeBrepFaces(Brep brep, IGeometryContext context) {
        List<Extraction.Primitive> primitives = [];
        List<double> residuals = [];
        for (int i = 0; i < brep.Faces.Count; i++) {
            Surface? surface = brep.Faces[i].DuplicateSurface();
            if (surface is null) {
                return ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(
                    error: E.Geometry.DecompositionFailed.WithContext($"Failed to duplicate face {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
            }

            using (surface) {
                Result<Surface> validatedSurface = ResultFactory.Create(value: surface)
                    .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,]);
                if (!validatedSurface.IsSuccess) {
                    SystemError error = validatedSurface.Errors[0];
                    return ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: error);
                }

                (bool success, Extraction.PrimitiveKind kind, Plane frame, double[] pars) = ClassifySurface(surface: surface, context: context);
                if (success) {
                    primitives.Add(new Extraction.Primitive(kind, frame, pars));
                    residuals.Add(ComputeSurfaceResidual(surface: surface, kind: kind, frame: frame, pars: pars));
                }
            }
        }

        return primitives.Count > 0
            ? ResultFactory.Create(value: new Extraction.PrimitiveDecompositionResult(primitives.ToArray(), residuals.ToArray()))
            : ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.NoPrimitivesDetected.WithContext("No faces classified as primitives"));
    }

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
    private static (bool Success, Extraction.PrimitiveKind Kind, Plane Frame, double[] Params) ClassifySurfaceByCurvature(Surface surface) {
        (Interval uDomain, Interval vDomain) = (surface.Domain(0), surface.Domain(1));
        int sampleCount = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.CurvatureSampleCount));
        int maxSamples = sampleCount * sampleCount;
        SurfaceCurvature[] curvatures = new SurfaceCurvature[maxSamples];
        int validCount = 0;
        double sampleDivisor = sampleCount > 1 ? sampleCount - 1.0 : 1.0;

        for (int i = 0; i < sampleCount; i++) {
            double u = uDomain.ParameterAt(sampleCount > 1 ? i / sampleDivisor : 0.5);
            for (int j = 0; j < sampleCount; j++) {
                double v = vDomain.ParameterAt(sampleCount > 1 ? j / sampleDivisor : 0.5);
                SurfaceCurvature? curv = surface.CurvatureAt(u: u, v: v);
                if (curv is not null) {
                    curvatures[validCount++] = curv;
                }
            }
        }

        return validCount < ExtractionConfig.MinCurvatureSamples
            ? (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, [])
            : TestPrincipalCurvatureConstancy(surface: surface, curvatures: curvatures.AsSpan(0, validCount).ToArray());
    }

    [Pure]
    private static (bool Success, Extraction.PrimitiveKind Kind, Plane Frame, double[] Params) TestPrincipalCurvatureConstancy(
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
                ? (true, Extraction.PrimitiveKind.Plane, frame, [frame.OriginX, frame.OriginY, frame.OriginZ,])
                : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
            (false, true, false) when meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? (true, Extraction.PrimitiveKind.Cylinder, frame, [1.0 / (2.0 * meanMean), surface.GetBoundingBox(accurate: false).Diagonal.Length,])
                : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
            (true, true, false) when gaussianMean > RhinoMath.ZeroTolerance && meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? (true, Extraction.PrimitiveKind.Sphere, frame, [1.0 / Math.Sqrt(gaussianMean),])
                : (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
            _ => (false, Extraction.PrimitiveKind.Unknown, Plane.WorldXY, []),
        };
    }

    [Pure]
    private static double ComputeSurfaceResidual(Surface surface, Extraction.PrimitiveKind kind, Plane frame, double[] pars) {
        (Interval uDomain, Interval vDomain) = (surface.Domain(0), surface.Domain(1));
        int samplesPerDir = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount));
        int totalSamples = samplesPerDir * samplesPerDir;
        double sumSquaredDistances = 0.0;
        double sampleDivisor = samplesPerDir > 1 ? samplesPerDir - 1.0 : 1.0;

        for (int i = 0; i < samplesPerDir; i++) {
            double u = uDomain.ParameterAt(i / sampleDivisor);
            for (int j = 0; j < samplesPerDir; j++) {
                double v = vDomain.ParameterAt(j / sampleDivisor);
                Point3d surfacePoint = surface.PointAt(u: u, v: v);
                Point3d primitivePoint = kind switch {
                    _ when kind == Extraction.PrimitiveKind.Plane && pars.Length >= 3 => frame.ClosestPoint(surfacePoint),
                    _ when kind == Extraction.PrimitiveKind.Cylinder && pars.Length >= 2 => ProjectPointToCylinder(point: surfacePoint, cylinderPlane: frame, radius: pars[0]),
                    _ when kind == Extraction.PrimitiveKind.Sphere && pars.Length >= 1 => ProjectPointToSphere(point: surfacePoint, center: frame.Origin, radius: pars[0]),
                    _ when kind == Extraction.PrimitiveKind.Cone && pars.Length >= 3 => ProjectPointToCone(point: surfacePoint, conePlane: frame, baseRadius: pars[0], height: pars[1]),
                    _ when kind == Extraction.PrimitiveKind.Torus && pars.Length >= 2 => ProjectPointToTorus(point: surfacePoint, torusPlane: frame, majorRadius: pars[0], minorRadius: pars[1]),
                    _ when kind == Extraction.PrimitiveKind.Extrusion && pars.Length >= 1 => frame.ClosestPoint(surfacePoint),
                    _ => surfacePoint,
                };
                sumSquaredDistances += surfacePoint.DistanceToSquared(primitivePoint);
            }
        }

        return Math.Sqrt(sumSquaredDistances / totalSamples);
    }

    [Pure]
    private static Point3d ProjectPointToCylinder(Point3d point, Plane cylinderPlane, double radius) {
        Vector3d toPoint = point - cylinderPlane.Origin;
        double axisProjection = Vector3d.Multiply(toPoint, cylinderPlane.ZAxis);
        Point3d axisPoint = cylinderPlane.Origin + (cylinderPlane.ZAxis * axisProjection);
        Vector3d radialDir = point - axisPoint;
        return radialDir.Length > RhinoMath.ZeroTolerance
            ? axisPoint + ((radialDir / radialDir.Length) * radius)
            : axisPoint + (cylinderPlane.XAxis * radius);
    }

    [Pure]
    private static Point3d ProjectPointToSphere(Point3d point, Point3d center, double radius) {
        Vector3d dir = point - center;
        return dir.Length > RhinoMath.ZeroTolerance
            ? center + ((dir / dir.Length) * radius)
            : center + new Vector3d(radius, 0, 0);
    }

    [Pure]
    private static Point3d ProjectPointToCone(Point3d point, Plane conePlane, double baseRadius, double height) {
        Vector3d toPoint = point - conePlane.Origin;
        double axisProjection = Vector3d.Multiply(toPoint, conePlane.ZAxis);
        double coneRadius = baseRadius * (1.0 - (axisProjection / height));
        Point3d axisPoint = conePlane.Origin + (conePlane.ZAxis * axisProjection);
        Vector3d radialDir = point - axisPoint;
        return radialDir.Length > RhinoMath.ZeroTolerance
            ? axisPoint + ((radialDir / radialDir.Length) * coneRadius)
            : axisPoint + (conePlane.XAxis * coneRadius);
    }

    [Pure]
    private static Point3d ProjectPointToTorus(Point3d point, Plane torusPlane, double majorRadius, double minorRadius) {
        Vector3d toPoint = point - torusPlane.Origin;
        Vector3d radialInPlane = toPoint - (torusPlane.ZAxis * Vector3d.Multiply(toPoint, torusPlane.ZAxis));
        Point3d majorCirclePoint = radialInPlane.Length > RhinoMath.ZeroTolerance
            ? torusPlane.Origin + ((radialInPlane / radialInPlane.Length) * majorRadius)
            : torusPlane.Origin + (torusPlane.XAxis * majorRadius);
        Vector3d toMinor = point - majorCirclePoint;
        return toMinor.Length > RhinoMath.ZeroTolerance
            ? majorCirclePoint + ((toMinor / toMinor.Length) * minorRadius)
            : majorCirclePoint + (torusPlane.ZAxis * minorRadius);
    }

    [Pure]
    internal static Result<Extraction.PatternDetectionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(gs => gs.Length >= ExtractionConfig.PatternMinInstances, error: E.Geometry.NoPatternDetected.WithContext($"Need at least {ExtractionConfig.PatternMinInstances.ToString(System.Globalization.CultureInfo.InvariantCulture)} instances"))
            .Ensure(gs => gs.All(g => g is not null), error: E.Validation.GeometryInvalid.WithContext("Array contains null geometries"))
            .Bind(gs => ResultFactory.Create(value: (IEnumerable<GeometryBase>)gs)
                .TraverseElements(geometry => ResultFactory.Create(value: geometry)
                    .Validate(args: [context, V.Standard | V.BoundingBox,])
                    .Map(validated => validated.GetBoundingBox(accurate: false).Center))
                .Bind(centers => DetectPatternType(centers: [.. centers], context: context)));

    private static Result<Extraction.PatternDetectionResult> DetectPatternType(Point3d[] centers, IGeometryContext context) =>
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
            ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Extraction.PatternKind.Linear, Transform.Translation(deltas[0]), 1.0))
            : TryPatternVariants(centers, context);

    private static Result<Extraction.PatternDetectionResult> TryPatternVariants(Point3d[] centers, IGeometryContext context) {
        Result<Extraction.PatternDetectionResult> radial = TryDetectRadialPattern(centers: centers, context: context);
        if (radial.IsSuccess) {
            return radial;
        }

        Result<Extraction.PatternDetectionResult> grid = TryDetectGridPattern(centers: centers, context: context);
        if (grid.IsSuccess) {
            return grid;
        }

        Result<Extraction.PatternDetectionResult> scaling = TryDetectScalingPattern(centers: centers, context: context);
        return scaling.IsSuccess
            ? scaling
            : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected.WithContext("No linear, radial, grid, or scaling pattern detected"));
    }

    private static Result<Extraction.PatternDetectionResult> TryDetectRadialPattern(Point3d[] centers, IGeometryContext context) {
        Point3d centroid = new(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z));
        double meanDistance = centers.Average(c => centroid.DistanceTo(c));
        return meanDistance > context.AbsoluteTolerance
            && centers.All(c => RhinoMath.EpsilonEquals(centroid.DistanceTo(c), meanDistance, meanDistance * ExtractionConfig.RadialDistanceVariationThreshold))
            && (centers.Select(c => c - centroid).ToArray(), ComputeBestFitPlaneNormal(points: centers, centroid: centroid)) is (Vector3d[] radii, Vector3d normal)
            && Enumerable.Range(0, radii.Length - 1).Select(i => Vector3d.VectorAngle(radii[i], radii[i + 1])).ToArray() is double[] angles
            && angles.Average() is double meanAngle
            && angles.All(a => RhinoMath.EpsilonEquals(a, meanAngle, ExtractionConfig.RadialAngleVariationThreshold))
                ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Extraction.PatternKind.Radial, Transform.Rotation(meanAngle, normal, centroid), 0.9))
                : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected);
    }

    [Pure]
    private static Vector3d ComputeBestFitPlaneNormal(Point3d[] points, Point3d centroid) =>
        Plane.FitPlaneToPoints(points: points, plane: out Plane bestFit) == PlaneFitResult.Success
            ? bestFit.Normal
            : (points[0] - centroid) is Vector3d v1 && v1.Length > RhinoMath.ZeroTolerance
                ? ((Func<Vector3d>)(() => {
                    Vector3d v1n = v1 / v1.Length;
                    for (int i = 1; i < points.Length; i++) {
                        Vector3d v2 = points[i] - centroid;
                        if (v2.Length > RhinoMath.ZeroTolerance) {
                            Vector3d normal = Vector3d.CrossProduct(v1n, v2);
                            if (normal.Length > RhinoMath.ZeroTolerance) {
                                return normal / normal.Length;
                            }
                        }
                    }
                    return Vector3d.ZAxis;
                }))()
                : Vector3d.ZAxis;

    private static Result<Extraction.PatternDetectionResult> TryDetectGridPattern(Point3d[] centers, IGeometryContext context) =>
        (centers[0], Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[0]).ToArray()) is (Point3d origin, Vector3d[] relativeVectors)
            && relativeVectors.Where(v => v.Length > context.AbsoluteTolerance).ToArray() is Vector3d[] candidates && candidates.Length >= 2
            && FindGridBasis(candidates: candidates, context: context) is (Vector3d u, Vector3d v, bool success) && success
            && relativeVectors.All(vec => IsGridPoint(vector: vec, u: u, v: v, context: context))
                ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Extraction.PatternKind.Grid, Transform.PlaneToPlane(Plane.WorldXY, new Plane(origin, u, v)), 0.9))
                : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected);

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
            && validRatios.Length >= 2 && ComputeVariance(values: validRatios) is double variance && variance < ExtractionConfig.ScalingVarianceThreshold
                ? ResultFactory.Create(value: new Extraction.PatternDetectionResult(Extraction.PatternKind.Scaling, Transform.Scale(anchor: centroid, scaleFactor: validRatios.Average()), 0.7))
                : ResultFactory.Create<Extraction.PatternDetectionResult>(error: E.Geometry.NoPatternDetected);

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
