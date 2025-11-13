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
    [Pure]
    internal static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeatures(Brep brep, IGeometryContext context) =>
        ResultFactory.Create(value: brep)
            .Validate(args: [context, V.Standard | V.Topology | V.BrepGranular,])
            .Ensure(b => b.Faces.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no faces"))
            .Ensure(b => b.Edges.Count > 0, error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no edges"))
            .Bind(validBrep => ExtractFeaturesInternal(brep: validBrep, context: context));

    [Pure]
    private static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeaturesInternal(Brep brep, IGeometryContext context) {
        BrepEdge[] validEdges = [.. brep.Edges.Where(e => e.EdgeCurve is not null),];
        (byte Type, double Param)[] edgeFeatures = new (byte, double)[validEdges.Length];

        for (int i = 0; i < validEdges.Length; i++) {
            edgeFeatures[i] = ClassifyEdge(edge: validEdges[i], brep: brep);
        }

        (byte Type, double Param)[] holeFeatures = [.. brep.Loops
            .Where(l => l.LoopType == BrepLoopType.Inner)
            .Select(l => ClassifyHole(loop: l, context: context))
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
        (edge.Domain.Min, edge.Domain.Max, Enumerable.Range(0, ExtractionConfig.FilletCurvatureSampleCount)
            .Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.FilletCurvatureSampleCount - 1.0))))
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
            && Math.Sqrt(curvatures.Sum(k => (k - mean) * (k - mean)) / curvatures.Length) / (mean > RhinoMath.ZeroTolerance ? mean : 1.0) is double coeffVar
            && isG2 && coeffVar < ExtractionConfig.FilletCurvatureVariationThreshold && mean > RhinoMath.ZeroTolerance
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
        bool isSmooth = dihedralAngle > ExtractionConfig.SmoothEdgeAngleThreshold;
        bool isSharp = dihedralAngle < ExtractionConfig.SharpEdgeAngleThreshold;
        bool isChamfer = !isSmooth && !isSharp;

        return isChamfer
            ? (Type: ExtractionConfig.FeatureTypeChamfer, Param: dihedralAngle)
            : mean > RhinoMath.ZeroTolerance
                ? (Type: ExtractionConfig.FeatureTypeVariableRadiusFillet, Param: 1.0 / mean)
                : (Type: ExtractionConfig.FeatureTypeGenericEdge, Param: edge.EdgeCurve.GetLength());
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

    private static readonly double[] _zeroResidual = [0.0,];

    [Pure]
    internal static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        ResultFactory.Create(value: geometry)
            .Validate(args: [context, V.Standard | V.BoundingBox,])
            .Bind(validGeometry => validGeometry switch {
                Surface surface => ResultFactory.Create(value: surface)
                    .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,])
                    .Bind(validSurface => ClassifySurface(surface: validSurface, context: context) switch {
                        (true, byte type, Plane frame, double[] pars) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                            value: ([(Type: type, Frame: frame, Params: pars),], _zeroResidual)),
                        _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                            error: E.Geometry.NoPrimitivesDetected),
                    }),
                Brep brep => ResultFactory.Create(value: brep)
                    .Validate(args: [context, V.Standard | V.BrepGranular,])
                    .Ensure(b => b.Faces.Count > 0, error: E.Geometry.DecompositionFailed.WithContext("Brep has no faces"))
                    .Bind(validBrep => DecomposeBrepFaces(brep: validBrep, context: context)),
                GeometryBase other => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                    error: E.Geometry.DecompositionFailed.WithContext($"Unsupported geometry type: {other.GetType().Name}")),
            });

    [Pure]
    private static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeBrepFaces(Brep brep, IGeometryContext context) =>
        Enumerable.Range(0, brep.Faces.Count)
            .Select(i => brep.Faces[i].DuplicateSurface() switch {
                null => (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, Array.Empty<double>(), 0.0,
                    (SystemError?)E.Geometry.DecompositionFailed.WithContext($"Failed to duplicate face {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
                Surface surface => ((Func<(bool, byte, Plane, double[], double, SystemError?)>)(() => {
                    using (surface) {
                        Result<Surface> validatedSurface = ResultFactory.Create(value: surface)
                            .Validate(args: [context, V.Standard | V.SurfaceContinuity | V.UVDomain,]);
                        SystemError? firstError = validatedSurface.Errors.Count > 0 ? validatedSurface.Errors[0] : null;
                        return !validatedSurface.IsSuccess
                            ? (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, Array.Empty<double>(), 0.0, firstError)
                            : ClassifySurface(surface: surface, context: context) switch {
                                (true, byte type, Plane frame, double[] pars) =>
                                    (true, type, frame, pars, ComputeSurfaceResidual(surface: surface, type: type, frame: frame, pars: pars), null),
                                (bool Success, byte Type, Plane Frame, double[] Params) classification => (classification.Success, classification.Type, classification.Frame, classification.Params, 0.0, null),
                            };
                    }
                }))(),
            })
            .Aggregate<(bool Success, byte Type, Plane Frame, double[] Params, double Residual, SystemError? Error),
                Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>>(
                ResultFactory.Create(value: (Primitives: Array.Empty<(byte, Plane, double[])>(), Residuals: Array.Empty<double>())),
                (result, item) => item.Error.HasValue
                    ? ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: item.Error.Value)
                    : result.IsSuccess && item.Success
                        ? result.Map<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(r => (
                            Primitives: [.. r.Primitives, (item.Type, item.Frame, item.Params),],
                            Residuals: [.. r.Residuals, item.Residual,]))
                        : result)
            .Bind(r => r.Primitives.Length > 0
                ? ResultFactory.Create(value: r)
                : ResultFactory.Create<((byte, Plane, double[])[], double[])>(
                    error: E.Geometry.NoPrimitivesDetected.WithContext("No faces classified as primitives")));

    [Pure]
    private static (bool Success, byte Type, Plane Frame, double[] Params) ClassifySurface(Surface surface, IGeometryContext context) =>
        surface.TryGetPlane(out Plane pl, tolerance: context.AbsoluteTolerance)
            ? (true, ExtractionConfig.PrimitiveTypePlane, pl, [pl.OriginX, pl.OriginY, pl.OriginZ,])
            : surface.TryGetCylinder(out Cylinder cyl, tolerance: context.AbsoluteTolerance)
                && cyl.Radius > RhinoMath.ZeroTolerance
                && cyl.TotalHeight > RhinoMath.ZeroTolerance
                ? (true, ExtractionConfig.PrimitiveTypeCylinder, new Plane(cyl.CircleAt(0.0).Center, cyl.Axis), [cyl.Radius, cyl.TotalHeight,])
                : surface.TryGetSphere(out Sphere sph, tolerance: context.AbsoluteTolerance)
                    && sph.Radius > RhinoMath.ZeroTolerance
                    ? (true, ExtractionConfig.PrimitiveTypeSphere, new Plane(sph.Center, Vector3d.ZAxis), [sph.Radius,])
                    : surface.TryGetCone(out Cone cone, tolerance: context.AbsoluteTolerance)
                        && cone.Radius > RhinoMath.ZeroTolerance
                        && cone.Height > RhinoMath.ZeroTolerance
                        ? (true, ExtractionConfig.PrimitiveTypeCone, new Plane(cone.BasePoint, cone.Axis), [cone.Radius, cone.Height, Math.Atan(cone.Radius / cone.Height),])
                        : surface.TryGetTorus(out Torus torus, tolerance: context.AbsoluteTolerance)
                            && torus.MajorRadius > RhinoMath.ZeroTolerance
                            && torus.MinorRadius > RhinoMath.ZeroTolerance
                            ? (true, ExtractionConfig.PrimitiveTypeTorus, torus.Plane, [torus.MajorRadius, torus.MinorRadius,])
                            : surface switch {
                                Extrusion ext when ext.IsValid && ext.PathLineCurve() is LineCurve lc =>
                                    (true, ExtractionConfig.PrimitiveTypeExtrusion, new Plane(ext.PathStart, lc.Line.Direction), [lc.Line.Length,]),
                                _ => ClassifySurfaceByCurvature(surface: surface),
                            };

    [Pure]
    private static (bool Success, byte Type, Plane Frame, double[] Params) ClassifySurfaceByCurvature(Surface surface) {
        (Interval uDomain, Interval vDomain) = (surface.Domain(0), surface.Domain(1));
        int sampleCount = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.CurvatureSampleCount));
        SurfaceCurvature[] curvatures = [.. from int i in Enumerable.Range(0, sampleCount)
            from int j in Enumerable.Range(0, sampleCount)
            let u = uDomain.ParameterAt(i / (double)(sampleCount - 1))
            let v = vDomain.ParameterAt(j / (double)(sampleCount - 1))
            let curv = surface.CurvatureAt(u: u, v: v)
            where curv is not null
            select curv,
        ];

        return curvatures.Length < ExtractionConfig.MinCurvatureSamples
            ? (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, [])
            : TestPrincipalCurvatureConstancy(surface: surface, curvatures: curvatures);
    }

    [Pure]
    private static (bool Success, byte Type, Plane Frame, double[] Params) TestPrincipalCurvatureConstancy(
        Surface surface,
        SurfaceCurvature[] curvatures) {
        double[] gaussianCurvatures = [.. curvatures.Select(static c => c.Gaussian),];
        double[] meanCurvatures = [.. curvatures.Select(static c => c.Mean),];
        double gaussianMean = gaussianCurvatures.Average();
        double meanMean = meanCurvatures.Average();
        double gaussianVar = gaussianCurvatures.Sum(g => (g - gaussianMean) * (g - gaussianMean)) / gaussianCurvatures.Length;
        double meanVar = meanCurvatures.Sum(m => (m - meanMean) * (m - meanMean)) / meanCurvatures.Length;
        bool gaussianConstant = Math.Abs(gaussianMean) > RhinoMath.ZeroTolerance
            ? gaussianVar / (gaussianMean * gaussianMean) < ExtractionConfig.CurvatureVariationThreshold
            : gaussianVar < RhinoMath.SqrtEpsilon;
        bool meanConstant = Math.Abs(meanMean) > RhinoMath.ZeroTolerance
            ? meanVar / (meanMean * meanMean) < ExtractionConfig.CurvatureVariationThreshold
            : meanVar < RhinoMath.SqrtEpsilon;

        return (gaussianConstant, meanConstant, Math.Abs(gaussianMean) < RhinoMath.SqrtEpsilon) switch {
            (true, _, true) => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? (true, ExtractionConfig.PrimitiveTypePlane, frame, [frame.OriginX, frame.OriginY, frame.OriginZ,])
                : (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, []),
            (false, true, false) when meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? (true, ExtractionConfig.PrimitiveTypeCylinder, frame, [1.0 / meanMean, surface.GetBoundingBox(accurate: false).Diagonal.Length,])
                : (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, []),
            (true, true, false) when gaussianMean > RhinoMath.ZeroTolerance && meanMean > RhinoMath.ZeroTolerance => surface.FrameAt(u: surface.Domain(0).Mid, v: surface.Domain(1).Mid, out Plane frame)
                ? (true, ExtractionConfig.PrimitiveTypeSphere, frame, [1.0 / Math.Sqrt(Math.Abs(gaussianMean)),])
                : (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, []),
            _ => (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, []),
        };
    }

    [Pure]
    private static double ComputeSurfaceResidual(Surface surface, byte type, Plane frame, double[] pars) =>
        (surface.Domain(0), surface.Domain(1), (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount))) is (Interval u, Interval v, int samplesPerDir)
            ? Math.Sqrt(Enumerable.Range(0, samplesPerDir).SelectMany(i => Enumerable.Range(0, samplesPerDir).Select(j =>
                surface.PointAt(u: u.ParameterAt(i / (double)(samplesPerDir - 1)), v: v.ParameterAt(j / (double)(samplesPerDir - 1))) is Point3d sp
                    ? sp.DistanceToSquared(type switch {
                        ExtractionConfig.PrimitiveTypePlane when pars.Length >= 3 => frame.ClosestPoint(sp),
                        ExtractionConfig.PrimitiveTypeCylinder when pars.Length >= 2 => ProjectPointToCylinder(point: sp, cylinderPlane: frame, radius: pars[0]),
                        ExtractionConfig.PrimitiveTypeSphere when pars.Length >= 1 => ProjectPointToSphere(point: sp, center: frame.Origin, radius: pars[0]),
                        ExtractionConfig.PrimitiveTypeCone when pars.Length >= 3 => ProjectPointToCone(point: sp, conePlane: frame, baseRadius: pars[0], height: pars[1]),
                        ExtractionConfig.PrimitiveTypeTorus when pars.Length >= 2 => ProjectPointToTorus(point: sp, torusPlane: frame, majorRadius: pars[0], minorRadius: pars[1]),
                        ExtractionConfig.PrimitiveTypeExtrusion when pars.Length >= 1 => frame.ClosestPoint(sp),
                        _ => sp,
                    })
                    : 0.0)).Sum() / (samplesPerDir * samplesPerDir))
            : 0.0;

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
    internal static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        ResultFactory.Create(value: geometries)
            .Ensure(gs => gs.Length >= ExtractionConfig.PatternMinInstances, error: E.Geometry.NoPatternDetected.WithContext($"Need at least {ExtractionConfig.PatternMinInstances.ToString(System.Globalization.CultureInfo.InvariantCulture)} instances"))
            .Ensure(gs => gs.All(g => g is not null), error: E.Validation.GeometryInvalid.WithContext("Array contains null geometries"))
            .Bind(gs => ResultFactory.Create(value: (IEnumerable<GeometryBase>)gs)
                .TraverseElements(geometry => ResultFactory.Create(value: geometry)
                    .Validate(args: [context, V.Standard | V.BoundingBox,])
                    .Map(validated => validated.GetBoundingBox(accurate: false).Center))
                .Bind(centers => DetectPatternType(centers: [.. centers], context: context)));

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> DetectPatternType(Point3d[] centers, IGeometryContext context) =>
        Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[i]).ToArray() is Vector3d[] deltas
            && deltas[0].Length > context.AbsoluteTolerance
            && deltas.All(d => (d - deltas[0]).Length < context.AbsoluteTolerance)
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
            && distances.All(d => Math.Abs(d - meanDistance) / meanDistance < ExtractionConfig.RadialDistanceVariationThreshold);

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
            && angles.Average() is double meanAngle && angles.All(a => Math.Abs(a - meanAngle) < ExtractionConfig.RadialAngleVariationThreshold)
                ? ResultFactory.Create(value: (Type: ExtractionConfig.PatternTypeRadial, SymmetryTransform: Transform.Rotation(meanAngle, normal, centroid), Confidence: 0.9))
                : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected);

    [Pure]
    private static Vector3d ComputeBestFitPlaneNormal(Point3d[] points, Point3d centroid) =>
        Plane.FitPlaneToPoints(points: points, plane: out Plane bestFit) == PlaneFitResult.Success
            ? bestFit.Normal
            : (points[0] - centroid) is Vector3d v1 && v1.Length > RhinoMath.ZeroTolerance
                ? Enumerable.Range(1, points.Length - 1)
                    .Select(i => (points[i] - centroid))
                    .Where(v2 => v2.Length > RhinoMath.ZeroTolerance)
                    .Select(v2 => Vector3d.CrossProduct(v1, v2))
                    .FirstOrDefault(normal => normal.Length > RhinoMath.ZeroTolerance) is Vector3d n && n.Length > RhinoMath.ZeroTolerance
                        ? n / n.Length
                        : Vector3d.ZAxis
                : Vector3d.ZAxis;

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
            && Math.Abs(Vector3d.Multiply(uDir, c / c.Length)) < ExtractionConfig.GridOrthogonalityThreshold);

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
            && Math.Abs(a - Math.Round(a)) < ExtractionConfig.GridPointDeviationThreshold
            && Math.Abs(b - Math.Round(b)) < ExtractionConfig.GridPointDeviationThreshold;
    }

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> TryDetectScalingPattern(Point3d[] centers, IGeometryContext context) =>
        new Point3d(centers.Average(p => p.X), centers.Average(p => p.Y), centers.Average(p => p.Z)) is Point3d centroid
            && centers.Select(c => centroid.DistanceTo(c)).ToArray() is double[] distances
            && Enumerable.Range(0, distances.Length - 1).Select(i => distances[i] > context.AbsoluteTolerance ? distances[i + 1] / distances[i] : 0.0).Where(r => r > context.AbsoluteTolerance).ToArray() is double[] validRatios
            && validRatios.Length >= 2 && ComputeVariance(values: validRatios) is double variance && variance < ExtractionConfig.ScalingVarianceThreshold
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
