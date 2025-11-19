using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino;
using Rhino.Collections;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Arsenal.Rhino.Extraction;

/// <summary>Orchestration layer for extraction operations with metadata-driven dispatch.</summary>
internal static class ExtractionCore {
    // ═══════════════════════════════════════════════════════════════════════════════
    // Point Extraction
    // ═══════════════════════════════════════════════════════════════════════════════

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExecutePoints<T>(
        T geometry,
        Extraction.PointOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        !ExtractionConfig.PointOperations.TryGetValue(operation.GetType(), out ExtractionConfig.ExtractionOperationMetadata? meta)
            ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Unknown operation: {operation.GetType().Name}"))
            : ExecutePointsWithNormalization(geometry: geometry, operation: operation, meta: meta, context: context);

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExecutePointsWithNormalization<T>(
        T geometry,
        Extraction.PointOperation operation,
        ExtractionConfig.ExtractionOperationMetadata meta,
        IGeometryContext context) where T : GeometryBase {
        (GeometryBase? candidate, bool shouldDispose) = (geometry, operation) switch {
            (Extrusion extrusion, Extraction.Analytical or Extraction.EdgeMidpoints or Extraction.FaceCentroids or Extraction.ByCount or Extraction.ByLength or Extraction.ByDirection) =>
                (extrusion.ToBrep(splitKinkyFaces: true), true),
            (SubD subd, Extraction.Analytical or Extraction.EdgeMidpoints or Extraction.FaceCentroids or Extraction.ByCount or Extraction.ByLength or Extraction.ByDirection) =>
                (subd.ToBrep(), true),
            (GeometryBase geom, _) => (geom, false),
        };

        return candidate is null
            ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Geometry normalization failed"))
            : ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                GeometryBase normalized = candidate;
                V validationMode = ExtractionConfig.GetPointValidationMode(operation.GetType(), normalized.GetType());
                Result<IReadOnlyList<Point3d>> result = UnifiedOperation.Apply(
                    input: normalized,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<Point3d>>>)(item =>
                        DispatchPointExtraction(geometry: item, operation: operation, context: context)),
                    config: new OperationConfig<GeometryBase, Point3d> {
                        Context = context,
                        ValidationMode = validationMode,
                        OperationName = meta.OperationName,
                        EnableDiagnostics = false,
                    });
                return (shouldDispose, normalized) switch {
                    (true, IDisposable disposable) => result.Tap(
                        onSuccess: _ => disposable.Dispose(),
                        onFailure: _ => disposable.Dispose()),
                    _ => result,
                };
            }))();
    }

    [Pure]
    private static Result<IReadOnlyList<Point3d>> DispatchPointExtraction(
        GeometryBase geometry,
        Extraction.PointOperation operation,
        IGeometryContext context) =>
        operation switch {
            Extraction.Analytical => ExtractAnalytical(geometry: geometry),
            Extraction.Extremal => ExtractExtremal(geometry: geometry),
            Extraction.Greville => ExtractGreville(geometry: geometry),
            Extraction.Inflection => ExtractInflection(geometry: geometry),
            Extraction.Quadrant => ExtractQuadrant(geometry: geometry, context: context),
            Extraction.EdgeMidpoints => ExtractEdgeMidpoints(geometry: geometry),
            Extraction.FaceCentroids => ExtractFaceCentroids(geometry: geometry),
            Extraction.OsculatingFrames op => ExtractOsculatingFrames(geometry: geometry, frameCount: op.FrameCount),
            Extraction.ByCount op => ExtractByCount(geometry: geometry, count: op.Count, includeEnds: op.IncludeEnds),
            Extraction.ByLength op => ExtractByLength(geometry: geometry, length: op.Length, includeEnds: op.IncludeEnds),
            Extraction.ByDirection op => ExtractByDirection(geometry: geometry, direction: op.Direction),
            Extraction.ByContinuity op => ExtractByContinuity(geometry: geometry, continuity: op.Continuity),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Unhandled operation: {operation.GetType().Name}")),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<IReadOnlyList<Point3d>>> ExecutePointsMultiple<T>(
        IReadOnlyList<T> geometries,
        Extraction.PointOperation operation,
        IGeometryContext context,
        bool accumulateErrors,
        bool enableParallel) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => ExecutePoints(geometry: item, operation: operation, context: context)),]
            : [.. geometries.Select(item => ExecutePoints(geometry: item, operation: operation, context: context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Curve Extraction
    // ═══════════════════════════════════════════════════════════════════════════════

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExecuteCurves<T>(
        T geometry,
        Extraction.CurveOperation operation,
        IGeometryContext context) where T : GeometryBase =>
        !ExtractionConfig.CurveOperations.TryGetValue(operation.GetType(), out ExtractionConfig.ExtractionOperationMetadata? meta)
            ? ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Unknown curve operation: {operation.GetType().Name}"))
            : UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<Curve>>>)(item =>
                    DispatchCurveExtraction(geometry: item, operation: operation, context: context)),
                config: new OperationConfig<T, Curve> {
                    Context = context,
                    ValidationMode = ExtractionConfig.GetCurveValidationMode(operation.GetType(), geometry.GetType()),
                    OperationName = meta.OperationName,
                    EnableDiagnostics = false,
                });

    [Pure]
    private static Result<IReadOnlyList<Curve>> DispatchCurveExtraction(
        GeometryBase geometry,
        Extraction.CurveOperation operation,
        IGeometryContext context) =>
        operation switch {
            Extraction.Boundary => ExtractBoundary(geometry: geometry),
            Extraction.FeatureEdges op => ExtractFeatureEdges(geometry: geometry, angleThreshold: op.AngleThreshold ?? ExtractionConfig.FeatureEdgeAngleThreshold),
            Extraction.IsocurveCount op => ExtractIsocurveCount(geometry: geometry, count: op.Count, direction: op.Direction),
            Extraction.IsocurveParams op => ExtractIsocurveParams(geometry: geometry, parameters: op.Parameters, direction: op.Direction),
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Unhandled curve operation: {operation.GetType().Name}")),
        };

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<IReadOnlyList<Curve>>> ExecuteCurvesMultiple<T>(
        IReadOnlyList<T> geometries,
        Extraction.CurveOperation operation,
        IGeometryContext context,
        bool accumulateErrors,
        bool enableParallel) where T : GeometryBase {
        Result<IReadOnlyList<Curve>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => ExecuteCurves(geometry: item, operation: operation, context: context)),]
            : [.. geometries.Select(item => ExecuteCurves(geometry: item, operation: operation, context: context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Analysis Operations
    // ═══════════════════════════════════════════════════════════════════════════════

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Extraction.FeatureResult> ExecuteFeatureExtraction(Brep brep, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: brep,
            operation: (Func<Brep, Result<IReadOnlyList<Extraction.FeatureResult>>>)(item =>
                ExtractionCompute.ExtractFeatures(brep: item, context: context)
                    .Map(result => (IReadOnlyList<Extraction.FeatureResult>)[result,])),
            config: new OperationConfig<Brep, Extraction.FeatureResult> {
                Context = context,
                ValidationMode = ExtractionConfig.FeatureExtractionMetadata.ValidationMode,
                OperationName = ExtractionConfig.FeatureExtractionMetadata.OperationName,
                EnableDiagnostics = false,
            })
        .Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Extraction.PrimitiveResult> ExecutePrimitiveDecomposition(GeometryBase geometry, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<GeometryBase, Result<IReadOnlyList<Extraction.PrimitiveResult>>>)(item =>
                ExtractionCompute.DecomposeToPrimitives(geometry: item, context: context)
                    .Map(result => (IReadOnlyList<Extraction.PrimitiveResult>)[result,])),
            config: new OperationConfig<GeometryBase, Extraction.PrimitiveResult> {
                Context = context,
                ValidationMode = ExtractionConfig.PrimitiveDecompositionMetadata.ValidationMode,
                OperationName = ExtractionConfig.PrimitiveDecompositionMetadata.OperationName,
                EnableDiagnostics = false,
            })
        .Map(static results => results[0]);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Extraction.PatternResult> ExecutePatternExtraction(GeometryBase[] geometries, IGeometryContext context) =>
        ExtractionCompute.ExtractPatterns(geometries: geometries, context: context);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Point Extraction Implementations
    // ═══════════════════════════════════════════════════════════════════════════════

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractAnalytical(GeometryBase geometry) =>
        geometry switch {
            Brep brep => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using VolumeMassProperties? massProperties = VolumeMassProperties.Compute(brep);
                return massProperties is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, .. brep.Vertices.Select(static vertex => vertex.Location),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. brep.Vertices.Select(static vertex => vertex.Location),]);
            }))(),
            Curve curve => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using AreaMassProperties? massProperties = AreaMassProperties.Compute(curve);
                return massProperties is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(value: [curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,]);
            }))(),
            Surface surface => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using AreaMassProperties? massProperties = AreaMassProperties.Compute(surface);
                Interval uDomain = surface.Domain(0);
                Interval vDomain = surface.Domain(1);
                return massProperties is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(value: [surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),]);
            }))(),
            Mesh mesh => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using VolumeMassProperties? massProperties = VolumeMassProperties.Compute(mesh);
                return massProperties is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, .. mesh.Vertices.ToPoint3dArray(),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(value: mesh.Vertices.ToPoint3dArray());
            }))(),
            PointCloud cloud when cloud.GetPoints() is Point3d[] points && points.Length > 0 =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [points.Aggregate(Point3d.Origin, static (sum, point) => sum + point) / points.Length, .. points,]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Analytical extraction unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractExtremal(GeometryBase geometry) =>
        geometry switch {
            Curve curve => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [curve.PointAtStart, curve.PointAtEnd,]),
            Surface surface when (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(value: geometry.GetBoundingBox(accurate: true).GetCorners()),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractGreville(GeometryBase geometry) =>
        geometry switch {
            NurbsCurve nurbsCurve when nurbsCurve.GrevillePoints() is Point3dList greville =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. greville,]),
            NurbsSurface nurbsSurface when nurbsSurface.Points is NurbsSurfacePointList controlPoints =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. from int u in Enumerable.Range(0, controlPoints.CountU) from int v in Enumerable.Range(0, controlPoints.CountV) let greville = controlPoints.GetGrevillePoint(u, v) select nurbsSurface.PointAt(greville.X, greville.Y),]),
            Curve curve when curve.ToNurbsCurve() is NurbsCurve nurbs =>
                ((Func<NurbsCurve, Result<IReadOnlyList<Point3d>>>)(n => {
                    try {
                        return n.GrevillePoints() is Point3dList greville
                            ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. greville,])
                            : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Greville extraction failed"));
                    } finally {
                        n.Dispose();
                    }
                }))(nurbs),
            Surface surface when surface.ToNurbsSurface() is NurbsSurface nurbs && nurbs.Points is not null =>
                ((Func<NurbsSurface, Result<IReadOnlyList<Point3d>>>)(n => {
                    try {
                        return ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. from int u in Enumerable.Range(0, n.Points.CountU) from int v in Enumerable.Range(0, n.Points.CountV) let greville = n.Points.GetGrevillePoint(u, v) select n.PointAt(greville.X, greville.Y),]);
                    } finally {
                        n.Dispose();
                    }
                }))(nurbs),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Greville extraction unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractInflection(GeometryBase geometry) =>
        geometry switch {
            NurbsCurve nurbs when nurbs.InflectionPoints() is Point3d[] inflections =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: inflections),
            Curve curve when curve.ToNurbsCurve() is NurbsCurve nurbs =>
                ((Func<NurbsCurve, Result<IReadOnlyList<Point3d>>>)(n => {
                    try {
                        Point3d[]? inflections = n.InflectionPoints();
                        return inflections is not null
                            ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: inflections)
                            : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Inflection computation failed"));
                    } finally {
                        n.Dispose();
                    }
                }))(nurbs),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Inflection extraction unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractQuadrant(GeometryBase geometry, IGeometryContext context) =>
        geometry is Curve curve && context.AbsoluteTolerance is double tolerance
            ? curve.TryGetCircle(out Circle circle, tolerance)
                ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [circle.PointAt(0), circle.PointAt(RhinoMath.HalfPI), circle.PointAt(Math.PI), circle.PointAt(3 * RhinoMath.HalfPI),])
                : curve.TryGetEllipse(out Ellipse ellipse, tolerance)
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [ellipse.Center + (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center + (ellipse.Plane.YAxis * ellipse.Radius2), ellipse.Center - (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center - (ellipse.Plane.YAxis * ellipse.Radius2),])
                    : curve.TryGetPolyline(out Polyline polyline)
                        ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. polyline,])
                        : curve.IsLinear(tolerance)
                            ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [curve.PointAtStart, curve.PointAtEnd,])
                            : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant extraction unsupported for this curve type"))
            : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant extraction requires curve"));

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractEdgeMidpoints(GeometryBase geometry) =>
        geometry switch {
            Brep brep => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. brep.Edges.Select(static edge => edge.PointAtNormalizedLength(0.5)),]),
            Mesh mesh => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(index => mesh.TopologyEdges.EdgeLine(index)).Where(static line => line.IsValid).Select(static line => line.PointAt(0.5)),]),
            Curve curve => curve.DuplicateSegments() is Curve[] { Length: > 0 } segments
                ? ((Func<Curve[], Result<IReadOnlyList<Point3d>>>)(segArray => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. segArray.Select(static segment => {
                    try {
                        return segment.PointAtNormalizedLength(0.5);
                    } finally {
                        segment.Dispose();
                    }
                }),])))(segments)
                : curve.TryGetPolyline(out Polyline polyline)
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. polyline.GetSegments().Where(static line => line.IsValid).Select(static line => line.PointAt(0.5)),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Unable to compute edge midpoints")),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"EdgeMidpoints extraction unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractFaceCentroids(GeometryBase geometry) =>
        geometry switch {
            Brep brep => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. brep.Faces.Select(face => face.DuplicateFace(duplicateMeshes: false) switch {
                Brep duplicate => ((Func<Brep, Point3d>)(dup => {
                    try {
                        Point3d? centroid = ((Func<Point3d?>)(() => {
                            using AreaMassProperties? massProperties = AreaMassProperties.Compute(dup);
                            return massProperties?.Centroid;
                        }))();
                        return centroid.HasValue && centroid.Value.IsValid ? centroid.Value : Point3d.Unset;
                    } finally {
                        dup.Dispose();
                    }
                }))(duplicate),
                _ => Point3d.Unset,
            }).Where(static point => point != Point3d.Unset),]),
            Mesh mesh => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. Enumerable.Range(0, mesh.Faces.Count).Select(index => mesh.Faces.GetFaceCenter(index)).Where(static pt => pt.IsValid),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"FaceCentroids extraction unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractOsculatingFrames(GeometryBase geometry, int? frameCount) =>
        geometry is Curve curve
            ? (frameCount switch {
                int count when count >= 2 => count,
                null => ExtractionConfig.DefaultOsculatingFrameCount,
                _ => 0,
            }) is int actualCount && actualCount >= 2
                ? curve.GetPerpendicularFrames(parameters: [.. Enumerable.Range(0, actualCount).Select(i => curve.Domain.ParameterAt(i / (double)(actualCount - 1))),]) is Plane[] frames && frames.Length > 0
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. frames.Select(static frame => frame.Origin),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("GetPerpendicularFrames failed"))
                : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Expected frame count >= 2"))
            : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("OsculatingFrames requires curve"));

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractByCount(GeometryBase geometry, int count, bool includeEnds) =>
        geometry switch {
            Curve curve => curve.DivideByCount(count, includeEnds) is double[] parameters
                ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. parameters.Select(t => curve.PointAt(t)),])
                : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("DivideByCount failed")),
            Surface surface when count > 0 && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. from int ui in Enumerable.Range(0, count) from int vi in Enumerable.Range(0, count) let uParameter = count == 1 ? 0.5 : includeEnds ? ui / (double)(count - 1) : (ui + 0.5) / count let vParameter = count == 1 ? 0.5 : includeEnds ? vi / (double)(count - 1) : (vi + 0.5) / count select surface.PointAt(uDomain.ParameterAt(uParameter), vDomain.ParameterAt(vParameter)),]),
            Brep => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("ByCount unsupported for Brep")),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"ByCount unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractByLength(GeometryBase geometry, double length, bool includeEnds) =>
        geometry switch {
            Curve curve => curve.DivideByLength(length, includeEnds) is double[] parameters
                ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. parameters.Select(t => curve.PointAt(t)),])
                : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("DivideByLength failed")),
            Surface => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("ByLength unsupported for Surface")),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"ByLength unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractByDirection(GeometryBase geometry, Vector3d direction) =>
        geometry is Curve curve
            ? curve.ExtremeParameters(direction) is double[] parameters
                ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. parameters.Select(t => curve.PointAt(t)),])
                : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Extreme parameter computation failed"))
            : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("ByDirection requires curve"));

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExtractByContinuity(GeometryBase geometry, Continuity continuity) =>
        geometry is Curve curve
            ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. Enumerable.Range(0, 1000)
                .Aggregate(
                    seed: (Points: new List<Point3d>(), Parameter: curve.Domain.Min),
                    func: (state, _) => curve.GetNextDiscontinuity(continuity, state.Parameter, curve.Domain.Max, out double next)
                        ? (Points: [.. state.Points, curve.PointAt(next),], Parameter: next)
                        : state)
                .Points,])
            : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("ByContinuity requires curve"));

    // ═══════════════════════════════════════════════════════════════════════════════
    // Curve Extraction Implementations
    // ═══════════════════════════════════════════════════════════════════════════════

    [Pure]
    private static Result<IReadOnlyList<Curve>> ExtractBoundary(GeometryBase geometry) =>
        geometry switch {
            Surface surface when (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) =>
                ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. new Curve?[] {
                    surface.IsoCurve(direction: 0, constantParameter: u.Min),
                    surface.IsoCurve(direction: 1, constantParameter: v.Min),
                    surface.IsoCurve(direction: 0, constantParameter: u.Max),
                    surface.IsoCurve(direction: 1, constantParameter: v.Max),
                }.OfType<Curve>(),]),
            Brep brep => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. brep.DuplicateEdgeCurves(nakedOnly: false),]),
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Boundary extraction unsupported for {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<IReadOnlyList<Curve>> ExtractFeatureEdges(GeometryBase geometry, double angleThreshold) =>
        geometry is Brep brep
            ? ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. brep.Edges
                .Where(edge => edge.AdjacentFaces() is int[] adjacentFaces
                    && adjacentFaces.Length == 2
                    && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d midPoint
                    && brep.Faces[adjacentFaces[0]].ClosestPoint(testPoint: midPoint, u: out double u0, v: out double v0)
                    && brep.Faces[adjacentFaces[1]].ClosestPoint(testPoint: midPoint, u: out double u1, v: out double v1)
                    && Math.Abs(Vector3d.VectorAngle(brep.Faces[adjacentFaces[0]].NormalAt(u: u0, v: v0), brep.Faces[adjacentFaces[1]].NormalAt(u: u1, v: v1))) >= angleThreshold)
                .Select(edge => edge.DuplicateCurve())
                .OfType<Curve>(),])
            : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("FeatureEdges requires Brep"));

    [Pure]
    private static Result<IReadOnlyList<Curve>> ExtractIsocurveCount(GeometryBase geometry, int count, Extraction.SurfaceDirection direction) =>
        geometry is Surface surface && count >= ExtractionConfig.MinIsocurveCount && count <= ExtractionConfig.MaxIsocurveCount
            ? (surface.Domain(0), surface.Domain(1)) switch {
                (Interval uDomain, Interval vDomain) => direction switch {
                    Extraction.SurfaceDirection.U => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                    Extraction.SurfaceDirection.V => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                    Extraction.SurfaceDirection.UV => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(), .. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                    _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidDirection),
                },
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            }
            : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Invalid count or geometry type: {geometry.GetType().Name}"));

    [Pure]
    private static Result<IReadOnlyList<Curve>> ExtractIsocurveParams(GeometryBase geometry, double[] parameters, Extraction.SurfaceDirection direction) =>
        geometry is Surface surface && parameters.Length > 0
            ? (surface.Domain(0), surface.Domain(1)) switch {
                (Interval uDomain, Interval vDomain) => direction switch {
                    Extraction.SurfaceDirection.U => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(t))).OfType<Curve>(),]),
                    Extraction.SurfaceDirection.V => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(t))).OfType<Curve>(),]),
                    Extraction.SurfaceDirection.UV => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(t))).OfType<Curve>(), .. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(t))).OfType<Curve>(),]),
                    _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidDirection),
                },
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            }
            : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Invalid parameters or geometry type: {geometry.GetType().Name}"));
}
