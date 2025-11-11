using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Linq;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Arsenal.Rhino.Extraction;

/// <summary>Point extraction with FrozenDictionary dispatch and normalization.</summary>
internal static class ExtractionCore {
    /// <summary>(Kind, Type) to handler function mapping for O(1) dispatch.</summary>
    private static readonly FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>> _handlers =
        BuildHandlerRegistry();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> Execute(GeometryBase geometry, Extract.Request request, IGeometryContext context) =>
        ExecuteWithNormalization(geometry, request, context);

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExecuteWithNormalization(
        GeometryBase geometry,
        Extract.Request request,
        IGeometryContext context) {
        (GeometryBase? Candidate, bool ShouldDispose) conversion = (geometry, request.Kind) switch {
            (Extrusion extrusion, 1 or 6 or 7 or 10 or 11 or 12) => (extrusion.ToBrep(splitKinkyFaces: true), true),
            (SubD subd, 1 or 6 or 7 or 10 or 11 or 12) => (subd.ToBrep(), true),
            (GeometryBase geom, _) => (geom, false),
        };

        if (conversion.Candidate is null) {
            return ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Normalization failed"));
        }

        GeometryBase normalized = conversion.Candidate;

        try {
            V mode = ExtractionConfig.GetValidationMode(request.Kind, normalized.GetType());
            return ResultFactory.Create(value: normalized)
                .Validate(args: mode == V.None ? null : [context, mode,])
                .Bind(_ => DispatchExtraction(normalized, request, context).Map(points => (IReadOnlyList<Point3d>)points));
        } finally {
            if (conversion.ShouldDispose) {
                (normalized as IDisposable)?.Dispose();
            }
        }
    }

    [Pure]
    private static Result<Point3d[]> DispatchExtraction(GeometryBase geometry, Extract.Request request, IGeometryContext context) =>
        _handlers.TryGetValue((request.Kind, geometry.GetType()), out Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>? handler)
            ? handler(geometry, request, context)
            : _handlers.Where(kv => kv.Key.Kind == request.Kind && kv.Key.GeometryType.IsInstanceOfType(geometry))
                .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
                .Select(kv => kv.Value)
                .DefaultIfEmpty(static (_, _, _) => ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("No handler registered")))
                .First()(geometry, request, context);

    [Pure]
    private static FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>> BuildHandlerRegistry() =>
        new Dictionary<(byte, Type), Func<GeometryBase, Extract.Request, IGeometryContext, Result<Point3d[]>>> {
            [(1, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? VolumeMassProperties.Compute(brep) switch { { Centroid: { IsValid: true } centroid } => ResultFactory.Create(value: [centroid, .. brep.Vertices.Select(vertex => vertex.Location),]), _ => ResultFactory.Create(value: [.. brep.Vertices.Select(vertex => vertex.Location),]), }
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(1, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? AreaMassProperties.Compute(curve) switch { { Centroid: { IsValid: true } centroid } => ResultFactory.Create(value: [centroid, curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,]), _ => ResultFactory.Create(value: [curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,]), }
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(1, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface
                ? (AreaMassProperties.Compute(surface), surface.Domain(0), surface.Domain(1)) switch {
                    ({ Centroid: { IsValid: true } centroid }, Interval u, Interval v) => ResultFactory.Create(value: [centroid, surface.PointAt(u.Min, v.Min), surface.PointAt(u.Max, v.Min), surface.PointAt(u.Max, v.Max), surface.PointAt(u.Min, v.Max),]),
                    (_, Interval u, Interval v) => ResultFactory.Create(value: [surface.PointAt(u.Min, v.Min), surface.PointAt(u.Max, v.Min), surface.PointAt(u.Max, v.Max), surface.PointAt(u.Min, v.Max),]),
                }
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Surface")),
            [(1, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? VolumeMassProperties.Compute(mesh) switch { { Centroid: { IsValid: true } centroid } => ResultFactory.Create(value: [centroid, .. mesh.Vertices.ToPoint3dArray(),]), _ => ResultFactory.Create(value: mesh.Vertices.ToPoint3dArray()), }
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(1, typeof(PointCloud))] = static (geometry, _, _) => geometry is PointCloud cloud && cloud.GetPoints() is Point3d[] cloudPoints && cloudPoints.Length > 0
                ? ResultFactory.Create(value: [cloudPoints.Aggregate(Point3d.Origin, static (sum, point) => sum + point) / cloudPoints.Length, .. cloudPoints,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("PointCloud has no points")),
            [(2, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? ResultFactory.Create(value: [curve.PointAtStart, curve.PointAtEnd,])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(2, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create(value: [surface.PointAt(uDomain.Min, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Min), surface.PointAt(uDomain.Max, vDomain.Max), surface.PointAt(uDomain.Min, vDomain.Max),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable")),
            [(2, typeof(GeometryBase))] = static (geometry, _, _) => ResultFactory.Create(value: geometry.GetBoundingBox(accurate: true).GetCorners()),
            [(3, typeof(NurbsCurve))] = static (geometry, _, _) => geometry is NurbsCurve nurbsCurve && nurbsCurve.GrevillePoints() is Point3d[] greville
                ? ResultFactory.Create(value: greville)
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Greville extraction failed")),
            [(3, typeof(NurbsSurface))] = static (geometry, _, _) => geometry is NurbsSurface nurbsSurface && nurbsSurface.Points is NurbsSurfacePointList controlPoints
                ? ResultFactory.Create(value: [.. from int u in Enumerable.Range(0, controlPoints.CountU) from int v in Enumerable.Range(0, controlPoints.CountV) let greville = controlPoints.GetGrevillePoint(u, v) select nurbsSurface.PointAt(greville.X, greville.Y),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("NURBS surface Greville extraction failed")),
            [(3, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve && curve.ToNurbsCurve() is NurbsCurve nurbs
                ? (
                    using (nurbs)
                    {
                        return nurbs.GrevillePoints() is Point3d[] greville
                            ? ResultFactory.Create(value: greville)
                            : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Greville extraction failed"));
                    }
                )
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to convert curve to NURBS")),
            [(3, typeof(Surface))] = static (geometry, _, _) => geometry is Surface surface && surface.ToNurbsSurface() is NurbsSurface nurbs && nurbs.Points is not null
                ? ((Func<NurbsSurface, Result<Point3d[]>>)(n => {
                    using (n) {
                        return ResultFactory.Create(value: [.. from int u in Enumerable.Range(0, n.Points.CountU) from int v in Enumerable.Range(0, n.Points.CountV) let greville = n.Points.GetGrevillePoint(u, v) select n.PointAt(greville.X, greville.Y),]);
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
                    ? ResultFactory.Create(value: [circle.PointAt(0), circle.PointAt(Math.PI / 2), circle.PointAt(Math.PI), circle.PointAt(3 * Math.PI / 2),])
                    : curve.TryGetEllipse(out Ellipse ellipse, tolerance)
                        ? ResultFactory.Create(value: [ellipse.Center + (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center + (ellipse.Plane.YAxis * ellipse.Radius2), ellipse.Center - (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center - (ellipse.Plane.YAxis * ellipse.Radius2),])
                        : curve.TryGetPolyline(out Polyline polyline)
                            ? ResultFactory.Create(value: [.. polyline])
                            : curve.IsLinear(tolerance)
                                ? ResultFactory.Create(value: [curve.PointAtStart, curve.PointAtEnd,])
                                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant extraction unsupported"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant extraction requires curve")),
            [(6, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create(value: [.. brep.Edges.Select(edge => edge.PointAtNormalizedLength(0.5)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(6, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ResultFactory.Create(value: [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(index => mesh.TopologyEdges.EdgeLine(index)).Where(static line => line.IsValid).Select(static line => line.PointAt(0.5)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(6, typeof(Curve))] = static (geometry, _, _) => geometry is Curve curve
                ? curve.DuplicateSegments() is Curve[] { Length: > 0 } segments
                    ? ((Func<Curve[], Result<Point3d[]>>)(segArray => {
                        try {
                            return ResultFactory.Create(value: [.. segArray.Select(segment => segment.PointAtNormalizedLength(0.5)),]);
                        } finally {
                            foreach (Curve segment in segArray) {
                                segment.Dispose();
                            }
                        }
                    }))(segments)
                    : curve.TryGetPolyline(out Polyline polyline)
                        ? ResultFactory.Create(value: [.. polyline.GetSegments().Where(static line => line.IsValid).Select(static line => line.PointAt(0.5)),])
                        : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Unable to compute edge midpoints"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve")),
            [(7, typeof(Brep))] = static (geometry, _, _) => geometry is Brep brep
                ? ResultFactory.Create(value: [.. brep.Faces.Select(face => face.DuplicateFace(duplicateMeshes: false) switch {
                    Brep duplicate => ((Func<Brep, Point3d>)(dup => {
                        try {
                            Point3d? centroid = AreaMassProperties.Compute(dup)?.Centroid;
                            return centroid.HasValue && centroid.Value.IsValid ? centroid.Value : Point3d.Unset;
                        } finally {
                            dup.Dispose();
                        }
                    }))(duplicate),
                    _ => Point3d.Unset,
                }).Where(static point => point != Point3d.Unset),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(7, typeof(Mesh))] = static (geometry, _, _) => geometry is Mesh mesh
                ? ResultFactory.Create(value: [.. Enumerable.Range(0, mesh.Faces.Count).Select(index => mesh.Faces.GetFaceCenter(index)).Where(static pt => pt.IsValid),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Mesh")),
            [(10, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is int count
                ? curve.DivideByCount(count, request.IncludeEnds) is double[] parameters
                    ? ResultFactory.Create(value: [.. parameters.Select(curve.PointAt),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByCount failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and integer count")),
            [(10, typeof(Surface))] = static (geometry, request, _) => geometry is Surface surface && request.Parameter is int density && density > 0 && (surface.Domain(0), surface.Domain(1)) is (Interval uDomain, Interval vDomain)
                ? ResultFactory.Create(value: [.. from int ui in Enumerable.Range(0, density) from int vi in Enumerable.Range(0, density) let uParameter = density == 1 ? 0.5 : request.IncludeEnds ? ui / (double)(density - 1) : (ui + 0.5) / density let vParameter = density == 1 ? 0.5 : request.IncludeEnds ? vi / (double)(density - 1) : (vi + 0.5) / density select surface.PointAt(uDomain.ParameterAt(uParameter), vDomain.ParameterAt(vParameter)),])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Surface divide failed")),
            [(10, typeof(Brep))] = static (geometry, _, _) => geometry is Brep
                ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByCount unsupported for Brep"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Brep")),
            [(11, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is double length && length > 0
                ? curve.DivideByLength(length, request.IncludeEnds) is double[] parameters
                    ? ResultFactory.Create(value: [.. parameters.Select(curve.PointAt),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByLength failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and positive length")),
            [(11, typeof(Surface))] = static (geometry, _, _) => geometry is Surface
                ? ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("DivideByLength unsupported for Surface"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Surface")),
            [(12, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is Vector3d direction
                ? curve.ExtremeParameters(direction) is double[] parameters
                    ? ResultFactory.Create(value: [.. parameters.Select(curve.PointAt),])
                    : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Extreme parameter computation failed"))
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and direction")),
            [(13, typeof(Curve))] = static (geometry, request, _) => geometry is Curve curve && request.Parameter is Continuity continuity
                ? ResultFactory.Create(value: [.. ((Func<List<Point3d>>)(() => {
                    List<Point3d> discontinuities = [];
                    double parameter = curve.Domain.Min;
                    while (curve.GetNextDiscontinuity(continuity, parameter, curve.Domain.Max, out double next)) {
                        discontinuities.Add(curve.PointAt(next));
                        parameter = next;
                    }
                    return discontinuities;
                }))()])
                : ResultFactory.Create<Point3d[]>(error: E.Geometry.InvalidExtraction.WithContext("Expected Curve and continuity")),
        }.ToFrozenDictionary();
}
