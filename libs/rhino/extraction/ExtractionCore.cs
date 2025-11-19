using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Linq;
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

/// <summary>Point extraction with FrozenDictionary dispatch and normalization.</summary>
internal static class ExtractionCore {
    private static readonly IComparer<Type> _specificityComparer = Comparer<Type>.Create(static (a, b) =>
        a == b ? 0 : a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0);

    /// <summary>(Kind, Type) to handler function mapping for O(1) dispatch.</summary>
    private static readonly (FrozenDictionary<(Extraction.PointOperationKind Kind, Type GeometryType), Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>>> Map,
        FrozenDictionary<Extraction.PointOperationKind, (Type GeometryType, Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>> Handler)[]> Fallbacks) _pointRegistry = BuildHandlerRegistry();

    private static readonly FrozenDictionary<(Extraction.PointOperationKind Kind, Type GeometryType), Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>>> _handlers = _pointRegistry.Map;
    private static readonly FrozenDictionary<Extraction.PointOperationKind, (Type GeometryType, Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>> Handler)[]> _handlerFallbacks = _pointRegistry.Fallbacks;

    /// <summary>(Kind, Type) to curve handler function mapping for O(1) dispatch.</summary>
    private static readonly (FrozenDictionary<(Extraction.CurveOperationKind Kind, Type GeometryType), Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>>> Map,
        FrozenDictionary<Extraction.CurveOperationKind, (Type GeometryType, Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>> Handler)[]> Fallbacks) _curveRegistry = BuildCurveHandlerRegistry();

    private static readonly FrozenDictionary<(Extraction.CurveOperationKind Kind, Type GeometryType), Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>>> _curveHandlers = _curveRegistry.Map;
    private static readonly FrozenDictionary<Extraction.CurveOperationKind, (Type GeometryType, Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>> Handler)[]> _curveHandlerFallbacks = _curveRegistry.Fallbacks;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExecutePoints(GeometryBase geometry, Extraction.PointRequest request, IGeometryContext context) =>
        NormalizePointRequest(request, context)
            .Bind(spec => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<GeometryBase, Result<IReadOnlyList<Point3d>>>)(item => ExecutePointOperation(item, spec, context)),
                config: new OperationConfig<GeometryBase, Point3d> {
                    Context = context,
                    ValidationMode = V.None,
                    OperationName = ExtractionConfig.ResolvePointOperationName(spec.Kind),
                }));

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExecuteCurves(GeometryBase geometry, Extraction.CurveRequest request, IGeometryContext context) =>
        NormalizeCurveRequest(request)
            .Bind(spec => UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<GeometryBase, Result<IReadOnlyList<Curve>>>)(item => ExecuteCurveOperation(item, spec, context)),
                config: new OperationConfig<GeometryBase, Curve> {
                    Context = context,
                    ValidationMode = V.None,
                    OperationName = ExtractionConfig.ResolveCurveOperationName(spec.Kind),
                }));

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExecutePointOperation(GeometryBase geometry, PointOperationSpec spec, IGeometryContext context) =>
        NormalizeGeometry(geometry, spec.Kind)
            .Bind(normalized => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                GeometryBase candidate = normalized.Geometry;
                V mode = ExtractionConfig.ResolvePointValidation(spec.Kind, candidate.GetType());
                Result<IReadOnlyList<Point3d>> result = ResultFactory.Create(value: candidate)
                    .Validate(args: mode == V.None ? null : [context, mode,])
                    .Bind(_ => DispatchPointOperation(candidate, spec, context).Map(points => (IReadOnlyList<Point3d>)points));
                return normalized.ShouldDispose && candidate is IDisposable disposable
                    ? result.Tap(onSuccess: _ => disposable.Dispose(), onFailure: _ => disposable.Dispose())
                    : result;
            }))());

    [Pure]
    private static Result<IReadOnlyList<Curve>> ExecuteCurveOperation(GeometryBase geometry, CurveOperationSpec spec, IGeometryContext context) {
        V mode = ExtractionConfig.ResolveCurveValidation(spec.Kind, geometry.GetType());
        return ResultFactory.Create(value: geometry)
            .Validate(args: mode == V.None ? null : [context, mode,])
            .Bind(_ => DispatchCurveOperation(geometry, spec, context).Map(curves => (IReadOnlyList<Curve>)curves));
    }

    [Pure]
    private static Result<Point3d[]> DispatchPointOperation(GeometryBase geometry, PointOperationSpec spec, IGeometryContext context) =>
        _handlers.TryGetValue((spec.Kind, geometry.GetType()), out Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>>? handler)
            ? handler(geometry, spec, context)
            : _handlerFallbacks.TryGetValue(spec.Kind, out (Type GeometryType, Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>> Handler)[]? fallbacks)
                ? InvokeFallback(geometry, spec, context, fallbacks)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("No handler registered"));

    [Pure]
    private static Result<Curve[]> DispatchCurveOperation(GeometryBase geometry, CurveOperationSpec spec, IGeometryContext context) =>
        _curveHandlers.TryGetValue((spec.Kind, geometry.GetType()), out Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>>? handler)
            ? handler(geometry, spec, context)
            : _curveHandlerFallbacks.TryGetValue(spec.Kind, out (Type GeometryType, Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>> Handler)[]? fallbacks)
                ? InvokeFallback(geometry, spec, context, fallbacks)
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("No curve handler registered"));

    private static Result<IReadOnlyList<Curve>> InvokeFallback(
        GeometryBase geometry,
        CurveOperationSpec spec,
        IGeometryContext context,
        (Type GeometryType, Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>> Handler)[] fallbacks) {
        int match = Array.FindIndex(fallbacks, candidate => candidate.GeometryType.IsInstanceOfType(geometry));
        return match >= 0
            ? fallbacks[match].Handler(geometry, spec, context).Map(curves => (IReadOnlyList<Curve>)curves)
            : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("No curve handler registered"));
    }

    private static Result<Point3d[]> InvokeFallback(
        GeometryBase geometry,
        PointOperationSpec spec,
        IGeometryContext context,
        (Type GeometryType, Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>> Handler)[] fallbacks) {
        int match = Array.FindIndex(fallbacks, candidate => candidate.GeometryType.IsInstanceOfType(geometry));
        return match >= 0
            ? fallbacks[match].Handler(geometry, spec, context)
            : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("No handler registered"));
    }

    private static Result<PointOperationSpec> NormalizePointRequest(Extraction.PointRequest request, IGeometryContext context) =>
        request switch {
            Extraction.SemanticPoint semantic when semantic.Semantic == Extraction.PointSemantic.OsculatingFrames && semantic.SampleCount.HasValue && semantic.SampleCount.Value < 2
                => ResultFactory.Create<PointOperationSpec>(error: E.Geometry.InvalidCount.WithContext("Osculating frame count must be >= 2")),
            Extraction.SemanticPoint semantic when semantic.Semantic == Extraction.PointSemantic.OsculatingFrames => ResultFactory.Create(value: new PointOperationSpec(
                semantic.Semantic.Operation,
                semantic.SampleCount ?? ExtractionConfig.DefaultOsculatingFrameCount,
                includeEnds: true)),
            Extraction.SemanticPoint semantic => ResultFactory.Create(value: new PointOperationSpec(semantic.Semantic.Operation, null, includeEnds: true)),
            Extraction.DivideByCount divide when divide.Count <= 0 => ResultFactory.Create<PointOperationSpec>(error: E.Geometry.InvalidCount),
            Extraction.DivideByCount divide => ResultFactory.Create(value: new PointOperationSpec(
                Extraction.PointOperationKind.DivideByCount,
                divide.Count,
                divide.IncludeEndpoints)),
            Extraction.DivideByLength divide when divide.Length <= 0 => ResultFactory.Create<PointOperationSpec>(error: E.Geometry.InvalidLength),
            Extraction.DivideByLength divide => ResultFactory.Create(value: new PointOperationSpec(
                Extraction.PointOperationKind.DivideByLength,
                divide.Length,
                divide.IncludeEndpoints)),
            Extraction.DirectionalExtrema extrema when extrema.Direction.Length <= context.AbsoluteTolerance
                => ResultFactory.Create<PointOperationSpec>(error: E.Geometry.InvalidDirection),
            Extraction.DirectionalExtrema extrema => ResultFactory.Create(value: new PointOperationSpec(
                Extraction.PointOperationKind.DirectionalExtrema,
                extrema.Direction,
                includeEnds: true)),
            Extraction.Discontinuities discontinuities => ResultFactory.Create(value: new PointOperationSpec(
                Extraction.PointOperationKind.Discontinuities,
                discontinuities.Continuity,
                includeEnds: true)),
            _ => ResultFactory.Create<PointOperationSpec>(error: E.Geometry.InvalidExtraction),
        };

    private static Result<CurveOperationSpec> NormalizeCurveRequest(Extraction.CurveRequest request) =>
        request switch {
            Extraction.SemanticCurves semantic when semantic.Semantic == Extraction.CurveSemantic.FeatureEdges => ResultFactory.Create(value: new CurveOperationSpec(
                semantic.Semantic.Operation,
                ExtractionConfig.FeatureEdgeAngleThreshold,
                includeEnds: false)),
            Extraction.SemanticCurves semantic => ResultFactory.Create(value: new CurveOperationSpec(
                semantic.Semantic.Operation,
                null,
                includeEnds: true)),
            Extraction.UniformIsocurves uniform when uniform.Count < ExtractionConfig.MinIsocurveCount || uniform.Count > ExtractionConfig.MaxIsocurveCount
                => ResultFactory.Create<CurveOperationSpec>(error: E.Geometry.InvalidCount),
            Extraction.UniformIsocurves uniform => ResultFactory.Create(value: new CurveOperationSpec(
                Extraction.CurveOperationKind.UniformIsocurves,
                uniform.Count,
                includeEnds: true)),
            Extraction.DirectionalIsocurves dir when dir.Count < ExtractionConfig.MinIsocurveCount || dir.Count > ExtractionConfig.MaxIsocurveCount
                => ResultFactory.Create<CurveOperationSpec>(error: E.Geometry.InvalidCount),
            Extraction.DirectionalIsocurves dir => ResultFactory.Create(value: new CurveOperationSpec(
                Extraction.CurveOperationKind.DirectionalIsocurves,
                (dir.Count, ToDirectionByte(dir.Direction)),
                includeEnds: true)),
            Extraction.ParameterIsocurves parameters when parameters.Parameters.Count == 0
                => ResultFactory.Create<CurveOperationSpec>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty")),
            Extraction.ParameterIsocurves parameters => ResultFactory.Create(value: new CurveOperationSpec(
                Extraction.CurveOperationKind.ParameterIsocurves,
                parameters.Parameters.ToArray(),
                includeEnds: false)),
            Extraction.DirectionalParameterIsocurves parameters when parameters.Parameters.Count == 0
                => ResultFactory.Create<CurveOperationSpec>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty")),
            Extraction.DirectionalParameterIsocurves parameters => ResultFactory.Create(value: new CurveOperationSpec(
                Extraction.CurveOperationKind.ParameterDirectionalIsocurves,
                (parameters.Parameters.ToArray(), ToDirectionByte(parameters.Direction)),
                includeEnds: false)),
            Extraction.FeatureEdgesByAngle edges when edges.AngleThreshold <= 0 => ResultFactory.Create<CurveOperationSpec>(error: E.Geometry.InvalidAngle),
            Extraction.FeatureEdgesByAngle edges => ResultFactory.Create(value: new CurveOperationSpec(
                Extraction.CurveOperationKind.CustomFeatureEdges,
                edges.AngleThreshold,
                includeEnds: false)),
            _ => ResultFactory.Create<CurveOperationSpec>(error: E.Geometry.InvalidExtraction.WithContext("Unsupported curve request")),
        };

    private static Result<NormalizedGeometry> NormalizeGeometry(GeometryBase geometry, Extraction.PointOperationKind kind) =>
        (geometry, kind) switch {
            (Extrusion extrusion, Extraction.PointOperationKind.Analytical or Extraction.PointOperationKind.EdgeMidpoints or Extraction.PointOperationKind.FaceCentroids or Extraction.PointOperationKind.DivideByCount or Extraction.PointOperationKind.DivideByLength or Extraction.PointOperationKind.DirectionalExtrema)
                => extrusion.ToBrep(splitKinkyFaces: true) is Brep brep
                    ? ResultFactory.Create(value: new NormalizedGeometry(brep, true))
                    : ResultFactory.Create<NormalizedGeometry>(error: E.Geometry.InvalidExtraction.WithContext("Normalization failed")),
            (SubD subd, Extraction.PointOperationKind.Analytical or Extraction.PointOperationKind.EdgeMidpoints or Extraction.PointOperationKind.FaceCentroids or Extraction.PointOperationKind.DivideByCount or Extraction.PointOperationKind.DivideByLength or Extraction.PointOperationKind.DirectionalExtrema)
                => subd.ToBrep() is Brep brep
                    ? ResultFactory.Create(value: new NormalizedGeometry(brep, true))
                    : ResultFactory.Create<NormalizedGeometry>(error: E.Geometry.InvalidExtraction.WithContext("Normalization failed")),
            _ => ResultFactory.Create(value: new NormalizedGeometry(geometry, false)),
        };

    private static byte ToDirectionByte(Extraction.IsocurveDirection direction) => direction switch {
        Extraction.IsocurveDirection.U => 0,
        Extraction.IsocurveDirection.V => 1,
        _ => 2,
    };

    [Pure]
    private static (FrozenDictionary<(Extraction.PointOperationKind Kind, Type GeometryType), Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>>> Map,
        FrozenDictionary<Extraction.PointOperationKind, (Type GeometryType, Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>> Handler)[]> Fallbacks) BuildHandlerRegistry() {
        Dictionary<(Extraction.PointOperationKind Kind, Type GeometryType), Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>>> map = new() {
            [(Extraction.PointOperationKind.Analytical, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ((Func<Result<Point3d[]>>)(() => {
                    using VolumeMassProperties? massProperties = VolumeMassProperties.Compute(brep);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, .. brep.Vertices.Select(static vertex => vertex.Location),])
                        : ResultFactory.Create<Point3d[]>(value: [.. brep.Vertices.Select(static vertex => vertex.Location),]);
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(Extraction.PointOperationKind.Analytical, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? ((Func<Result<Point3d[]>>)(() => {
                    using AreaMassProperties? massProperties = AreaMassProperties.Compute(curve);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,])
                        : ResultFactory.Create<Point3d[]>(value: [curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,]);
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(Extraction.PointOperationKind.Analytical, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface
                ? ((Func<Result<Point3d[]>>)(() => {
                    using AreaMassProperties? massProperties = AreaMassProperties.Compute(surface);
                    Interval uDomain = surface.Domain(0);
                    Interval vDomain = surface.Domain(1);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),])
                        : ResultFactory.Create<Point3d[]>(value: [surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),]);
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Surface")),
            [(Extraction.PointOperationKind.Analytical, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ((Func<Result<Point3d[]>>)(() => {
                    using VolumeMassProperties? massProperties = VolumeMassProperties.Compute(mesh);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, .. mesh.Vertices.ToPoint3dArray(),])
                        : ResultFactory.Create(value: mesh.Vertices.ToPoint3dArray());
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(Extraction.PointOperationKind.Analytical, typeof(PointCloud))] = static (geometry, _, _) => geometry is PointCloud cloud && cloud.GetPoints() is Point3d[] cloudPoints && cloudPoints.Length > 0
                ? ResultFactory.Create<Point3d[]>(value: [cloudPoints.Aggregate(Point3d.Origin, static (sum, point) => sum + point) / cloudPoints.Length, .. cloudPoints,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("PointCloud has no points")),
            [(Extraction.PointOperationKind.Extremal, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? ResultFactory.Create<Point3d[]>(value: [curve.PointAtStart, curve.PointAtEnd,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(Extraction.PointOperationKind.Extremal, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Point3d[]>(value: [surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(Extraction.PointOperationKind.Extremal, typeof(GeometryBase))] = static (geometry, _, _) => ResultFactory.Create(value: geometry.GetBoundingBox(accurate: true).GetCorners()),
            [(Extraction.PointOperationKind.Greville, typeof(NurbsCurve))] = static (geometry, _, _) => geometry is NurbsCurve nurbsCurve && nurbsCurve.GrevillePoints() is Point3dList greville
                ? ResultFactory.Create<Point3d[]>(value: [.. greville,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Greville extraction failed")),
            [(Extraction.PointOperationKind.Greville, typeof(NurbsSurface))] = static (geometry, _, _) => geometry is NurbsSurface nurbsSurface && nurbsSurface.Points is NurbsSurfacePointList controlPoints
                ? ResultFactory.Create<Point3d[]>(value: [.. from int u in Enumerable.Range(0, controlPoints.CountU) from int v in Enumerable.Range(0, controlPoints.CountV) let greville = controlPoints.GetGrevillePoint(u, v) select nurbsSurface.PointAt(greville.X, greville.Y),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("NURBS surface Greville extraction failed")),
            [(Extraction.PointOperationKind.Greville, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve && curve.ToNurbsCurve() is NurbsCurve nurbs
                ? ((Func<Result<Point3d[]>>)(() => {
                    try {
                        return nurbs.GrevillePoints() is Point3dList greville
                            ? ResultFactory.Create<Point3d[]>(value: [.. greville,])
                            : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Greville extraction failed"));
                    } finally {
                        nurbs.Dispose();
                    }
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to convert curve to NURBS")),
            [(Extraction.PointOperationKind.Greville, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && surface.ToNurbsSurface() is NurbsSurface nurbs && nurbs.Points is not null
                ? ((Func<NurbsSurface, Result<Point3d[]>>)(n => {
                    try {
                        return ResultFactory.Create<Point3d[]>(value: [.. from int u in Enumerable.Range(0, n.Points.CountU) from int v in Enumerable.Range(0, n.Points.CountV) let greville = n.Points.GetGrevillePoint(u, v) select n.PointAt(greville.X, greville.Y),]);
                    } finally {
                        n.Dispose();
                    }
                }))(nurbs)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to derive NURBS surface")),
            [(Extraction.PointOperationKind.Inflection, typeof(NurbsCurve))] = static (geometry, _, _) => geometry is NurbsCurve nurbs && nurbs.InflectionPoints() is Point3d[] inflections
                ? ResultFactory.Create(value: inflections)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Inflection computation failed")),
            [(Extraction.PointOperationKind.Inflection, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve && curve.ToNurbsCurve() is NurbsCurve nurbs
                ? ((Func<NurbsCurve, Result<Point3d[]>>)(n => {
                    try {
                        Point3d[]? inflections = n.InflectionPoints();
                        return inflections is not null ? ResultFactory.Create(value: inflections) : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Inflection computation failed"));
                    } finally {
                        n.Dispose();
                    }
                }))(nurbs)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to convert curve to NurbsCurve")),
            [(Extraction.PointOperationKind.Quadrant, typeof(Curve))] = static (geometry, _, context) => geometry is Curve curve && context.AbsoluteTolerance is double tolerance
                ? curve.TryGetCircle(out Circle circle, tolerance)
                    ? ResultFactory.Create<Point3d[]>(value: [circle.PointAt(0), circle.PointAt(RhinoMath.HalfPI), circle.PointAt(Math.PI), circle.PointAt(3 * RhinoMath.HalfPI),])
                    : curve.TryGetEllipse(out Ellipse ellipse, tolerance)
                        ? ResultFactory.Create<Point3d[]>(value: [ellipse.Center + (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center + (ellipse.Plane.YAxis * ellipse.Radius2), ellipse.Center - (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center - (ellipse.Plane.YAxis * ellipse.Radius2),])
                        : curve.TryGetPolyline(out Polyline polyline)
                            ? ResultFactory.Create<Point3d[]>(value: [.. polyline])
                            : curve.IsLinear(tolerance)
                                ? ResultFactory.Create<Point3d[]>(value: [curve.PointAtStart, curve.PointAtEnd,])
                                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant extraction unsupported"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant extraction requires curve")),
            [(Extraction.PointOperationKind.EdgeMidpoints, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create<Point3d[]>(value: [.. brep.Edges.Select(static edge => edge.PointAtNormalizedLength(0.5)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(Extraction.PointOperationKind.EdgeMidpoints, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ResultFactory.Create<Point3d[]>(value: [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(index => mesh.TopologyEdges.EdgeLine(index)).Where(static line => line.IsValid).Select(static line => line.PointAt(0.5)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(Extraction.PointOperationKind.EdgeMidpoints, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? curve.DuplicateSegments() is Curve[] { Length: > 0 } segments
                    ? ((Func<Curve[], Result<Point3d[]>>)(segArray => ResultFactory.Create<Point3d[]>(value: [.. segArray.Select(static segment => {
                        try {
                            return segment.PointAtNormalizedLength(0.5);
                        } finally {
                            segment.Dispose();
                        }
                    }),
                    ])))(segments)
                    : curve.TryGetPolyline(out Polyline polyline)
                        ? ResultFactory.Create<Point3d[]>(value: [.. polyline.GetSegments().Where(static line => line.IsValid).Select(static line => line.PointAt(0.5)),])
                        : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to compute edge midpoints"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(Extraction.PointOperationKind.FaceCentroids, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create<Point3d[]>(value: [.. brep.Faces.Select(face => face.DuplicateFace(duplicateMeshes: false) switch {
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
                }).Where(static point => point != Point3d.Unset),
                ])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(Extraction.PointOperationKind.FaceCentroids, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ResultFactory.Create<Point3d[]>(value: [.. Enumerable.Range(0, mesh.Faces.Count).Select(index => mesh.Faces.GetFaceCenter(index)).Where(static pt => pt.IsValid),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(Extraction.PointOperationKind.DivideByCount, typeof(Curve))] = static (geometry, spec, _) => geometry is Curve curve && spec.Parameter is int count
                ? curve.DivideByCount(count, spec.IncludeEnds) is double[] parameters
                    ? ResultFactory.Create<Point3d[]>(value: [.. parameters.Select(t => curve.PointAt(t)),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByCount failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and integer count")),
            [(Extraction.PointOperationKind.DivideByCount, typeof(Surface))] = static (geometry, spec, _) => geometry is Surface surface && spec.Parameter is int density && density > 0 && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Point3d[]>(value: [.. from int ui in Enumerable.Range(0, density) from int vi in Enumerable.Range(0, density) let uParameter = density == 1 ? 0.5 : spec.IncludeEnds ? ui / (double)(density - 1) : (ui + 0.5) / density let vParameter = density == 1 ? 0.5 : spec.IncludeEnds ? vi / (double)(density - 1) : (vi + 0.5) / density select surface.PointAt(uDomain.ParameterAt(uParameter), vDomain.ParameterAt(vParameter)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface divide failed")),
            [(Extraction.PointOperationKind.DivideByCount, typeof(Brep))] = static (geometry, _, _) => geometry is Brep
                ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByCount unsupported for Brep"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(Extraction.PointOperationKind.DivideByLength, typeof(Curve))] = static (geometry, spec, _) => geometry is Curve curve && spec.Parameter is double length && length > 0
                ? curve.DivideByLength(length, spec.IncludeEnds) is double[] parameters
                    ? ResultFactory.Create<Point3d[]>(value: [.. parameters.Select(t => curve.PointAt(t)),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByLength failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and positive length")),
            [(Extraction.PointOperationKind.DivideByLength, typeof(Surface))] = static (geometry, _, _) => geometry is Surface
                ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByLength unsupported for Surface"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Surface")),
            [(Extraction.PointOperationKind.DirectionalExtrema, typeof(Curve))] = static (geometry, spec, _) => geometry is Curve curve && spec.Parameter is Vector3d direction
                ? curve.ExtremeParameters(direction) is double[] parameters
                    ? ResultFactory.Create<Point3d[]>(value: [.. parameters.Select(t => curve.PointAt(t)),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Extreme parameter computation failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and direction")),
            [(Extraction.PointOperationKind.Discontinuities, typeof(Curve))] = static (geometry, spec, _) => geometry is Curve curve && spec.Parameter is Continuity continuity
                ? ResultFactory.Create<Point3d[]>(value: [.. ((Func<List<Point3d>>)(() => {
                    List<Point3d> discontinuities = [];
                    double parameter = curve.Domain.Min;
                    while (curve.GetNextDiscontinuity(continuity, parameter, curve.Domain.Max, out double next)) {
                        discontinuities.Add(curve.PointAt(next));
                        parameter = next;
                    }
                    return discontinuities;
                }))(),
                ])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and continuity")),
            [(Extraction.PointOperationKind.OsculatingFrames, typeof(Curve))] = static (geometry, spec, _) => geometry is Curve curve
                ? (spec.Parameter switch {
                    int count when count >= 2 => count,
                    null => ExtractionConfig.DefaultOsculatingFrameCount,
                    _ => 0,
                }) is int frameCount && frameCount >= 2
                    ? curve.GetPerpendicularFrames(parameters: [.. Enumerable.Range(0, frameCount).Select(i => curve.Domain.ParameterAt(i / (double)(frameCount - 1))),]) is Plane[] frames && frames.Length > 0
                        ? ResultFactory.Create<Point3d[]>(value: [.. frames.Select(static frame => frame.Origin),])
                        : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("GetPerpendicularFrames failed"))
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected frame count >= 2"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
        };
        FrozenDictionary<Extraction.PointOperationKind, (Type GeometryType, Func<GeometryBase, PointOperationSpec, IGeometryContext, Result<Point3d[]>> Handler)[]> fallbacks = map
            .GroupBy(static entry => entry.Key.Kind)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(static entry => entry.Key.GeometryType, _specificityComparer)
                    .Select(static entry => (entry.Key.GeometryType, entry.Value))
                    .ToArray())
            .ToFrozenDictionary();
        return (map.ToFrozenDictionary(), fallbacks);
    }

    [Pure]
    private static (FrozenDictionary<(Extraction.CurveOperationKind Kind, Type GeometryType), Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>>> Map,
        FrozenDictionary<Extraction.CurveOperationKind, (Type GeometryType, Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>> Handler)[]> Fallbacks) BuildCurveHandlerRegistry() {
        Dictionary<(Extraction.CurveOperationKind Kind, Type GeometryType), Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>>> map = new() {
            [(Extraction.CurveOperationKind.Boundary, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) && new Curve?[] { surface.IsoCurve(direction: 0, constantParameter: u.Min), surface.IsoCurve(direction: 1, constantParameter: v.Min), surface.IsoCurve(direction: 0, constantParameter: u.Max), surface.IsoCurve(direction: 1, constantParameter: v.Max), } is Curve?[] curves
                ? ResultFactory.Create<Curve[]>(value: [.. curves.OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(Extraction.CurveOperationKind.Boundary, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create<Curve[]>(value: [.. brep.DuplicateEdgeCurves(nakedOnly: false),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(Extraction.CurveOperationKind.IsocurveU, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && surface.Domain(1) is Interval vDomain
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(Extraction.CurveOperationKind.IsocurveV, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && surface.Domain(0) is Interval uDomain
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(Extraction.CurveOperationKind.IsocurveUV, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(), .. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(Extraction.CurveOperationKind.FeatureEdges, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create<Curve[]>(value: [.. brep.Edges.Where(edge => edge.AdjacentFaces() is int[] adjacentFaces && adjacentFaces.Length == 2 && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d midPoint && brep.Faces[adjacentFaces[0]].ClosestPoint(testPoint: midPoint, u: out double u0, v: out double v0) && brep.Faces[adjacentFaces[1]].ClosestPoint(testPoint: midPoint, u: out double u1, v: out double v1) && Math.Abs(Vector3d.VectorAngle(brep.Faces[adjacentFaces[0]].NormalAt(u: u0, v: v0), brep.Faces[adjacentFaces[1]].NormalAt(u: u1, v: v1))) >= ExtractionConfig.FeatureEdgeAngleThreshold).Select(edge => edge.DuplicateCurve()).OfType<Curve>()])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(Extraction.CurveOperationKind.UniformIsocurves, typeof(Surface))] = static (geometry, spec, _) => geometry is Surface surface && spec.Parameter is int count && count >= ExtractionConfig.MinIsocurveCount && count <= ExtractionConfig.MaxIsocurveCount && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(), .. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid count or surface")),
            [(Extraction.CurveOperationKind.DirectionalIsocurves, typeof(Surface))] = static (geometry, spec, _) => geometry is Surface surface && spec.Parameter is (int count, byte direction) && count >= ExtractionConfig.MinIsocurveCount && count <= ExtractionConfig.MaxIsocurveCount
                ? direction switch {
                    0 => surface.Domain(1) is Interval vDomain ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("V domain unavailable")),
                    1 => surface.Domain(0) is Interval uDomain ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("U domain unavailable")),
                    2 => (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: v.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(), .. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: u.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
                    _ => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidDirection),
                }
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid parameters")),
            [(Extraction.CurveOperationKind.ParameterIsocurves, typeof(Surface))] = static (geometry, spec, _) => geometry is Surface surface && spec.Parameter is double[] parameters && parameters.Length > 0 && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(t))).OfType<Curve>(), .. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(t))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid parameters or surface")),
            [(Extraction.CurveOperationKind.ParameterDirectionalIsocurves, typeof(Surface))] = static (geometry, spec, _) => geometry is Surface surface && spec.Parameter is (double[] parameters, byte direction) && parameters.Length > 0
                ? direction switch {
                    0 => surface.Domain(1) is Interval vDomain ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(t))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("V domain unavailable")),
                    1 => surface.Domain(0) is Interval uDomain ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(t))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("U domain unavailable")),
                    2 => (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: v.ParameterAt(t))).OfType<Curve>(), .. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: u.ParameterAt(t))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
                    _ => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidDirection),
                }
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid parameters")),
            [(Extraction.CurveOperationKind.CustomFeatureEdges, typeof(Brep))] = static (geometry, spec, _) => geometry is Brep brep && spec.Parameter is double angleThreshold
                ? ResultFactory.Create<Curve[]>(value: [.. brep.Edges.Where(edge => edge.AdjacentFaces() is int[] adjacentFaces && adjacentFaces.Length == 2 && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d midPoint && brep.Faces[adjacentFaces[0]].ClosestPoint(testPoint: midPoint, u: out double u0, v: out double v0) && brep.Faces[adjacentFaces[1]].ClosestPoint(testPoint: midPoint, u: out double u1, v: out double v1) && Math.Abs(Vector3d.VectorAngle(brep.Faces[adjacentFaces[0]].NormalAt(u: u0, v: v0), brep.Faces[adjacentFaces[1]].NormalAt(u: u1, v: v1))) >= angleThreshold).Select(edge => edge.DuplicateCurve()).OfType<Curve>()])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid angle or brep")),
        };
        FrozenDictionary<Extraction.CurveOperationKind, (Type GeometryType, Func<GeometryBase, CurveOperationSpec, IGeometryContext, Result<Curve[]>> Handler)[]> fallbacks = map
            .GroupBy(static entry => entry.Key.Kind)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static entry => entry.Key.GeometryType, _specificityComparer)
                    .Select(static entry => (entry.Key.GeometryType, entry.Value))
                    .ToArray())
            .ToFrozenDictionary();
        return (map.ToFrozenDictionary(), fallbacks);
    }

    private sealed record PointOperationSpec(Extraction.PointOperationKind Kind, object? Parameter, bool IncludeEnds);
    private sealed record CurveOperationSpec(Extraction.CurveOperationKind Kind, object? Parameter, bool IncludeEnds);
    private sealed record NormalizedGeometry(GeometryBase Geometry, bool ShouldDispose);
}
