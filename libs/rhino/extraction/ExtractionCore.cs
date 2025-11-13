using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
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
    private static readonly (FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>> Map,
        FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>> Handler)[]> Fallbacks) _pointRegistry = BuildHandlerRegistry();

    private static readonly FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>> _handlers = _pointRegistry.Map;
    private static readonly FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>> Handler)[]> _handlerFallbacks = _pointRegistry.Fallbacks;

    /// <summary>(Kind, Type) to curve handler function mapping for O(1) dispatch.</summary>
    private static readonly (FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>>> Map,
        FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>> Handler)[]> Fallbacks) _curveRegistry = BuildCurveHandlerRegistry();

    private static readonly FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>>> _curveHandlers = _curveRegistry.Map;
    private static readonly FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>> Handler)[]> _curveHandlerFallbacks = _curveRegistry.Fallbacks;

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> Execute(GeometryBase geometry, Extract.Request request, IGeometryContext context) =>
        ExecuteWithNormalization(geometry, request, context);

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExecuteCurves(GeometryBase geometry, Extract.Request request, IGeometryContext context) =>
        _curveHandlers.TryGetValue((request.Kind, geometry.GetType()), out Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>>? handler)
            ? handler(geometry, request, context).Map(curves => (IReadOnlyList<Curve>)curves)
            : _curveHandlerFallbacks.TryGetValue(request.Kind, out (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>> Handler)[]? fallbacks)
                ? InvokeFallback(geometry, request, context, fallbacks)
                : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("No curve handler registered"));

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExecuteWithNormalization(
        GeometryBase geometry,
        Extract.Request request,
        IGeometryContext context) {
        (GeometryBase? candidate, bool shouldDispose) = (geometry, request.Kind) switch {
            (Extrusion extrusion, 1 or 6 or 7 or 10 or 11 or 12) => (extrusion.ToBrep(splitKinkyFaces: true), true),
            (SubD subd, 1 or 6 or 7 or 10 or 11 or 12) => (subd.ToBrep(), true),
            (GeometryBase geom, _) => (geom, false),
        };

        return candidate is null
            ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Normalization failed"))
            : ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                GeometryBase normalized = candidate;
                V mode = ExtractionConfig.GetValidationMode(request.Kind, normalized.GetType());
                Result<IReadOnlyList<Point3d>> result = ResultFactory.Create(value: normalized)
                    .Validate(args: mode == V.None ? null : [context, mode,])
                    .Bind(_ => DispatchExtraction(normalized, request, context).Map(points => (IReadOnlyList<Point3d>)points));
                return (shouldDispose, normalized) switch {
                    (true, IDisposable disposable) => result.Tap(onSuccess: _ => disposable.Dispose(), onFailure: _ => disposable.Dispose()),
                    _ => result,
                };
            }))();
    }

    [Pure]
    private static Result<Point3d[]> DispatchExtraction(GeometryBase geometry, Extract.Request request, IGeometryContext context) =>
        _handlers.TryGetValue((request.Kind, geometry.GetType()), out Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>? handler)
            ? handler(geometry, request, context)
            : _handlerFallbacks.TryGetValue(request.Kind, out (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>> Handler)[]? fallbacks)
                ? InvokeFallback(geometry, request, context, fallbacks)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("No handler registered"));

    private static Result<IReadOnlyList<Curve>> InvokeFallback(
        GeometryBase geometry,
        Extract.Request request,
        IGeometryContext context,
        (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>> Handler)[] fallbacks) {
        int match = Array.FindIndex(fallbacks, candidate => candidate.GeometryType.IsInstanceOfType(geometry));
        return match >= 0
            ? fallbacks[match].Handler(geometry, request, context).Map(curves => (IReadOnlyList<Curve>)curves)
            : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("No curve handler registered"));
    }

    private static Result<Point3d[]> InvokeFallback(
        GeometryBase geometry,
        Extract.Request request,
        IGeometryContext context,
        (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>> Handler)[] fallbacks) {
        int match = Array.FindIndex(fallbacks, candidate => candidate.GeometryType.IsInstanceOfType(geometry));
        return match >= 0
            ? fallbacks[match].Handler(geometry, request, context)
            : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("No handler registered"));
    }

    [Pure]
    private static (FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>> Map,
        FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>> Handler)[]> Fallbacks) BuildHandlerRegistry() {
        Dictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>> map = new() {
            [(1, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ((Func<Result<Point3d[]>>)(() => {
                    using VolumeMassProperties? massProperties = VolumeMassProperties.Compute(brep);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, .. brep.Vertices.Select(vertex => vertex.Location),])
                        : ResultFactory.Create<Point3d[]>(value: [.. brep.Vertices.Select(vertex => vertex.Location),]);
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(1, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? ((Func<Result<Point3d[]>>)(() => {
                    using AreaMassProperties? massProperties = AreaMassProperties.Compute(curve);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,])
                        : ResultFactory.Create<Point3d[]>(value: [curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,]);
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(1, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface
                ? ((Func<Result<Point3d[]>>)(() => {
                    using AreaMassProperties? massProperties = AreaMassProperties.Compute(surface);
                    Interval uDomain = surface.Domain(0);
                    Interval vDomain = surface.Domain(1);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),])
                        : ResultFactory.Create<Point3d[]>(value: [surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),]);
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Surface")),
            [(1, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ((Func<Result<Point3d[]>>)(() => {
                    using VolumeMassProperties? massProperties = VolumeMassProperties.Compute(mesh);
                    return massProperties is { Centroid: { IsValid: true } centroid }
                        ? ResultFactory.Create<Point3d[]>(value: [centroid, .. mesh.Vertices.ToPoint3dArray(),])
                        : ResultFactory.Create(value: mesh.Vertices.ToPoint3dArray());
                }))()
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(1, typeof(PointCloud))] = static (geometry, _, _) => geometry is PointCloud cloud && cloud.GetPoints() is Point3d[] cloudPoints && cloudPoints.Length > 0
                ? ResultFactory.Create<Point3d[]>(value: [cloudPoints.Aggregate(Point3d.Origin, static (sum, point) => sum + point) / cloudPoints.Length, .. cloudPoints,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("PointCloud has no points")),
            [(2, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? ResultFactory.Create<Point3d[]>(value: [curve.PointAtStart, curve.PointAtEnd,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(2, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Point3d[]>(value: [surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(2, typeof(GeometryBase))] = static (geometry, _, _) => ResultFactory.Create(value: geometry.GetBoundingBox(accurate: true).GetCorners()),
            [(3, typeof(NurbsCurve))] = static (geometry, _, _) => geometry is NurbsCurve nurbsCurve && nurbsCurve.GrevillePoints() is Point3dList greville
                ? ResultFactory.Create<Point3d[]>(value: [.. greville,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Greville extraction failed")),
            [(3, typeof(NurbsSurface))] = static (geometry, _, _) => geometry is NurbsSurface nurbsSurface && nurbsSurface.Points is NurbsSurfacePointList controlPoints
                ? ResultFactory.Create<Point3d[]>(value: [.. from int u in Enumerable.Range(0, controlPoints.CountU) from int v in Enumerable.Range(0, controlPoints.CountV) let greville = controlPoints.GetGrevillePoint(u, v) select nurbsSurface.PointAt(greville.X, greville.Y),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("NURBS surface Greville extraction failed")),
            [(3, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve && curve.ToNurbsCurve() is NurbsCurve nurbs
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
            [(3, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && surface.ToNurbsSurface() is NurbsSurface nurbs && nurbs.Points is not null
                ? ((Func<NurbsSurface, Result<Point3d[]>>)(n => {
                    try {
                        return ResultFactory.Create<Point3d[]>(value: [.. from int u in Enumerable.Range(0, n.Points.CountU) from int v in Enumerable.Range(0, n.Points.CountV) let greville = n.Points.GetGrevillePoint(u, v) select n.PointAt(greville.X, greville.Y),]);
                    } finally {
                        n.Dispose();
                    }
                }))(nurbs)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to derive NURBS surface")),
            [(4, typeof(NurbsCurve))] = static (geometry, _, _) => geometry is NurbsCurve nurbs && nurbs.InflectionPoints() is Point3d[] inflections
                ? ResultFactory.Create(value: inflections)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Inflection computation failed")),
            [(4, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve && curve.ToNurbsCurve() is NurbsCurve nurbs
                ? ((Func<NurbsCurve, Result<Point3d[]>>)(n => {
                    try {
                        Point3d[]? inflections = n.InflectionPoints();
                        return inflections is not null ? ResultFactory.Create(value: inflections) : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Inflection computation failed"));
                    } finally {
                        n.Dispose();
                    }
                }))(nurbs)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to convert curve to NurbsCurve")),
            [(5, typeof(Curve))] = static (geometry, _, context) => geometry is Curve curve && context.AbsoluteTolerance is double tolerance
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
            [(6, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create<Point3d[]>(value: [.. brep.Edges.Select(edge => edge.PointAtNormalizedLength(0.5)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(6, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ResultFactory.Create<Point3d[]>(value: [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(index => mesh.TopologyEdges.EdgeLine(index)).Where(static line => line.IsValid).Select(static line => line.PointAt(0.5)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(6, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
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
            [(7, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
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
            [(7, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ResultFactory.Create<Point3d[]>(value: [.. Enumerable.Range(0, mesh.Faces.Count).Select(index => mesh.Faces.GetFaceCenter(index)).Where(static pt => pt.IsValid),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(10, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is int count
                ? curve.DivideByCount(count, request.IncludeEnds) is double[] parameters
                    ? ResultFactory.Create<Point3d[]>(value: [.. parameters.Select(curve.PointAt),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByCount failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and integer count")),
            [(10, typeof(Surface))] = static (geometry, request, _) => geometry is Surface surface && request.Parameter is int density && density > 0 && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Point3d[]>(value: [.. from int ui in Enumerable.Range(0, density) from int vi in Enumerable.Range(0, density) let uParameter = density == 1 ? 0.5 : request.IncludeEnds ? ui / (double)(density - 1) : (ui + 0.5) / density let vParameter = density == 1 ? 0.5 : request.IncludeEnds ? vi / (double)(density - 1) : (vi + 0.5) / density select surface.PointAt(uDomain.ParameterAt(uParameter), vDomain.ParameterAt(vParameter)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface divide failed")),
            [(10, typeof(Brep))] = static (geometry, _, _) => geometry is Brep
                ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByCount unsupported for Brep"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(11, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is double length && length > 0
                ? curve.DivideByLength(length, request.IncludeEnds) is double[] parameters
                    ? ResultFactory.Create<Point3d[]>(value: [.. parameters.Select(curve.PointAt),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByLength failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and positive length")),
            [(11, typeof(Surface))] = static (geometry, _, _) => geometry is Surface
                ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByLength unsupported for Surface"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Surface")),
            [(12, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is Vector3d direction
                ? curve.ExtremeParameters(direction) is double[] parameters
                    ? ResultFactory.Create<Point3d[]>(value: [.. parameters.Select(curve.PointAt),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Extreme parameter computation failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and direction")),
            [(13, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is Continuity continuity
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
            [(8, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve
                ? (request.Parameter switch {
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
        FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>> Handler)[]> fallbacks = map
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
    private static (FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>>> Map,
        FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>> Handler)[]> Fallbacks) BuildCurveHandlerRegistry() {
        Dictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>>> map = new() {
            [(20, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) && new Curve?[] { surface.IsoCurve(direction: 0, constantParameter: u.Min), surface.IsoCurve(direction: 1, constantParameter: v.Min), surface.IsoCurve(direction: 0, constantParameter: u.Max), surface.IsoCurve(direction: 1, constantParameter: v.Max), } is Curve?[] curves
                ? ResultFactory.Create<Curve[]>(value: [.. curves.OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(20, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create<Curve[]>(value: [.. brep.DuplicateEdgeCurves(nakedOnly: false),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(21, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && surface.Domain(1) is Interval vDomain
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(22, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && surface.Domain(0) is Interval uDomain
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(23, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(), .. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(24, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create<Curve[]>(value: [.. brep.Edges.Where(edge => edge.AdjacentFaces() is int[] adjacentFaces && adjacentFaces.Length == 2 && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d midPoint && brep.Faces[adjacentFaces[0]].ClosestPoint(testPoint: midPoint, u: out double u0, v: out double v0) && brep.Faces[adjacentFaces[1]].ClosestPoint(testPoint: midPoint, u: out double u1, v: out double v1) && Math.Abs(Vector3d.VectorAngle(brep.Faces[adjacentFaces[0]].NormalAt(u: u0, v: v0), brep.Faces[adjacentFaces[1]].NormalAt(u: u1, v: v1))) >= ExtractionConfig.FeatureEdgeAngleThreshold).Select(edge => edge.DuplicateCurve()).OfType<Curve>()])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(30, typeof(Surface))] = static (geometry, request, _) => geometry is Surface surface && request.Parameter is int count && count >= ExtractionConfig.MinIsocurveCount && count <= ExtractionConfig.MaxIsocurveCount && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(), .. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid count or surface")),
            [(31, typeof(Surface))] = static (geometry, request, _) => geometry is Surface surface && request.Parameter is (int count, byte direction) && count >= ExtractionConfig.MinIsocurveCount && count <= ExtractionConfig.MaxIsocurveCount
                ? direction switch {
                    0 => surface.Domain(1) is Interval vDomain ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("V domain unavailable")),
                    1 => surface.Domain(0) is Interval uDomain ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("U domain unavailable")),
                    2 => (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) ? ResultFactory.Create<Curve[]>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 0, constantParameter: v.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(), .. Enumerable.Range(0, count).Select(i => surface.IsoCurve(direction: 1, constantParameter: u.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
                    _ => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidDirection),
                }
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid parameters")),
            [(32, typeof(Surface))] = static (geometry, request, _) => geometry is Surface surface && request.Parameter is double[] parameters && parameters.Length > 0 && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(t))).OfType<Curve>(), .. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(t))).OfType<Curve>(),])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid parameters or surface")),
            [(33, typeof(Surface))] = static (geometry, request, _) => geometry is Surface surface && request.Parameter is (double[] parameters, byte direction) && parameters.Length > 0
                ? direction switch {
                    0 => surface.Domain(1) is Interval vDomain ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: vDomain.ParameterAt(t))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("V domain unavailable")),
                    1 => surface.Domain(0) is Interval uDomain ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: uDomain.ParameterAt(t))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("U domain unavailable")),
                    2 => (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) ? ResultFactory.Create<Curve[]>(value: [.. parameters.Select(t => surface.IsoCurve(direction: 0, constantParameter: v.ParameterAt(t))).OfType<Curve>(), .. parameters.Select(t => surface.IsoCurve(direction: 1, constantParameter: u.ParameterAt(t))).OfType<Curve>(),]) : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
                    _ => ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidDirection),
                }
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid parameters")),
            [(34, typeof(Brep))] = static (geometry, request, _) => geometry is Brep brep && request.Parameter is double angleThreshold
                ? ResultFactory.Create<Curve[]>(value: [.. brep.Edges.Where(edge => edge.AdjacentFaces() is int[] adjacentFaces && adjacentFaces.Length == 2 && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d midPoint && brep.Faces[adjacentFaces[0]].ClosestPoint(testPoint: midPoint, u: out double u0, v: out double v0) && brep.Faces[adjacentFaces[1]].ClosestPoint(testPoint: midPoint, u: out double u1, v: out double v1) && Math.Abs(Vector3d.VectorAngle(brep.Faces[adjacentFaces[0]].NormalAt(u: u0, v: v0), brep.Faces[adjacentFaces[1]].NormalAt(u: u1, v: v1))) >= angleThreshold).Select(edge => edge.DuplicateCurve()).OfType<Curve>()])
                : ResultFactory.Create<Curve[]>(error: E.Geometry.InvalidExtraction.WithContext("Invalid angle or brep")),
        };
        FrozenDictionary<byte, (Type GeometryType, Func<GeometryBase, Extract.Request, IGeometryContext, Result<Curve[]>> Handler)[]> fallbacks = map
            .GroupBy(static entry => entry.Key.Kind)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(static entry => entry.Key.GeometryType, _specificityComparer)
                    .Select(static entry => (entry.Key.GeometryType, entry.Value))
                    .ToArray())
            .ToFrozenDictionary();
        return (map.ToFrozenDictionary(), fallbacks);
    }
}
