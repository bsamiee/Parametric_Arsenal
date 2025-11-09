using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Arsenal.Rhino.Extraction;

/// <summary>Point extraction dispatch with FrozenDictionary handlers and geometry normalization.</summary>
internal static class ExtractionCore {
    /// <summary>Extraction algorithm handlers mapped by (kind, geometry type) for O(1) dispatch.</summary>
    private static readonly FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, object?, bool, IGeometryContext, Point3d[]>> _handlers =
        BuildHandlerRegistry();

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> Execute(GeometryBase geometry, object spec, IGeometryContext context) {
#pragma warning disable IDE0004 // Cast is redundant - required for type inference in switch expression
        (byte kind, object? param, bool includeEnds) = spec switch {
            int count => ((byte)10, (object)count, true),
            double length => ((byte)11, (object)length, true),
            (int count, bool ends) => ((byte)10, (object)count, ends),
            (double length, bool ends) => ((byte)11, (object)length, ends),
            Vector3d dir => ((byte)12, (object)dir, true),
            Continuity cont => ((byte)13, (object)cont, true),
            Extract.Semantic { Kind: byte k } => (k, (object?)null, true),
            _ => ((byte)0, (object?)null, false),
        };
#pragma warning restore IDE0004

        return kind is 0
            ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction)
            : ExecuteWithNormalization(geometry, kind, param, includeEnds, context);
    }

    [Pure]
    private static Result<IReadOnlyList<Point3d>> ExecuteWithNormalization(
        GeometryBase geometry,
        byte kind,
        object? param,
        bool includeEnds,
        IGeometryContext context) {
        (GeometryBase normalized, bool shouldDispose) = geometry switch {
            Extrusion ext when kind is 1 or 6 or 7 => (ext.ToBrep(splitKinkyFaces: true), true),
            SubD sd when kind is 1 or 6 or 7 => (sd.ToBrep(), true),
            _ => (geometry, false),
        };

        try {
            V mode = ExtractionConfig.GetValidationMode(kind: kind, geometryType: normalized.GetType());
            return ResultFactory.Create(value: normalized)
                .Validate(args: mode == V.None ? null : [context, mode,])
                .Bind(g => ResultFactory.Create(value: (IReadOnlyList<Point3d>)DispatchExtraction(geometry: g, kind: kind, param: param, includeEnds: includeEnds, context: context).AsReadOnly()));
        } finally {
            if (shouldDispose) {
                (normalized as IDisposable)?.Dispose();
            }
        }
    }

    [Pure]
    private static Point3d[] DispatchExtraction(GeometryBase geometry, byte kind, object? param, bool includeEnds, IGeometryContext context) {
        Type geometryType = geometry.GetType();
        return _handlers.TryGetValue(key: (kind, geometryType), value: out Func<GeometryBase, object?, bool, IGeometryContext, Point3d[]>? handler)
            ? handler(geometry, param, includeEnds, context)
            : _handlers
                .Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsInstanceOfType(geometry))
                .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? 1 : b.IsAssignableFrom(a) ? -1 : 0))
                .Select(kv => kv.Value)
                .FirstOrDefault()
                ?.Invoke(geometry, param, includeEnds, context) ?? [];
    }

    [Pure]
    private static FrozenDictionary<(byte Kind, Type GeometryType), Func<GeometryBase, object?, bool, IGeometryContext, Point3d[]>> BuildHandlerRegistry() =>
        new Dictionary<(byte, Type), Func<GeometryBase, object?, bool, IGeometryContext, Point3d[]>> {
            [(1, typeof(Brep))] = static (g, _, _, _) => g is Brep b ? VolumeMassProperties.Compute(b) switch { { Centroid: { IsValid: true } ct } => [ct, .. b.Vertices.Select(v => v.Location)],
                _ => [.. b.Vertices.Select(v => v.Location)],
            } : [],
            [(1, typeof(Curve))] = static (g, _, _, _) => g is Curve c ? AreaMassProperties.Compute(c) switch { { Centroid: { IsValid: true } ct } => [ct, c.PointAtStart, c.PointAt(c.Domain.ParameterAt(0.5)), c.PointAtEnd],
                _ => [c.PointAtStart, c.PointAt(c.Domain.ParameterAt(0.5)), c.PointAtEnd],
            } : [],
            [(1, typeof(Surface))] = static (g, _, _, _) => g is Surface s && (s.Domain(0), s.Domain(1)) is (Interval u, Interval v)
                ? AreaMassProperties.Compute(s) switch {
                    { Centroid: { IsValid: true } ct } => [ct, s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max)],
                    _ => [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max)],
                }
                : [],
            [(1, typeof(Mesh))] = static (g, _, _, _) => g is Mesh m ? VolumeMassProperties.Compute(m) switch { { Centroid: { IsValid: true } ct } => [ct, .. m.Vertices.ToPoint3dArray()],
                _ => m.Vertices.ToPoint3dArray(),
            } : [],
            [(1, typeof(PointCloud))] = static (g, _, _, _) => g is PointCloud { Count: > 0 } pc && pc.GetPoints() is Point3d[] pts
                ? [new Point3d(pts.Sum(static p => p.X) / pts.Length, pts.Sum(static p => p.Y) / pts.Length, pts.Sum(static p => p.Z) / pts.Length), .. pts]
                : [],
            [(2, typeof(Curve))] = static (g, _, _, _) => g is Curve c ? [c.PointAtStart, c.PointAtEnd] : [],
            [(2, typeof(Surface))] = static (g, _, _, _) => g is Surface s ? (s.Domain(0), s.Domain(1)) switch { (Interval u, Interval v) => [s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max)], } : [],
            [(2, typeof(GeometryBase))] = static (g, _, _, _) => g.GetBoundingBox(accurate: true).GetCorners(),
            [(3, typeof(NurbsCurve))] = static (g, _, _, _) => g is NurbsCurve nc ? [.. nc.GrevillePoints()] : [],
            [(3, typeof(NurbsSurface))] = static (g, _, _, _) => g is NurbsSurface { Points: NurbsSurfacePointList pts }
                ? [.. from u in Enumerable.Range(0, pts.CountU) from v in Enumerable.Range(0, pts.CountV) let gp = pts.GetGrevillePoint(u, v) select ((NurbsSurface)g).PointAt(gp.X, gp.Y),]
                : [],
            [(3, typeof(Curve))] = static (g, _, _, _) => g is Curve c && c.ToNurbsCurve() is NurbsCurve nc ? ((Func<NurbsCurve, Point3d[]>)(n => { try { return [.. n.GrevillePoints()]; } finally { n.Dispose(); } }))(nc) : [],
            [(3, typeof(Surface))] = static (g, _, _, _) => g is Surface s && s.ToNurbsSurface() is NurbsSurface ns && ns.Points is NurbsSurfacePointList pts
                ? ((Func<(NurbsSurface, NurbsSurfacePointList), Point3d[]>)(t => { try { return [.. from u in Enumerable.Range(0, t.Item2.CountU) from v in Enumerable.Range(0, t.Item2.CountV) let gp = t.Item2.GetGrevillePoint(u, v) select t.Item1.PointAt(gp.X, gp.Y),]; } finally { t.Item1.Dispose(); } }))((ns, pts))
                : [],
            [(4, typeof(NurbsCurve))] = static (g, _, _, _) => g is NurbsCurve nc ? nc.InflectionPoints() ?? [] : [],
            [(4, typeof(Curve))] = static (g, _, _, _) => g is Curve c && c.ToNurbsCurve() is NurbsCurve nc ? ((Func<NurbsCurve, Point3d[]>)(n => { try { return n.InflectionPoints() ?? []; } finally { n.Dispose(); } }))(nc) : [],
            [(5, typeof(Curve))] = static (g, _, _, ctx) => g is Curve c && ctx.AbsoluteTolerance is double tol
                ? c.TryGetCircle(out Circle circ, tol) ? [circ.PointAt(0), circ.PointAt(Math.PI / 2), circ.PointAt(Math.PI), circ.PointAt(3 * Math.PI / 2)]
                : c.TryGetEllipse(out Ellipse e, tol) ? [e.Center + (e.Plane.XAxis * e.Radius1), e.Center + (e.Plane.YAxis * e.Radius2), e.Center - (e.Plane.XAxis * e.Radius1), e.Center - (e.Plane.YAxis * e.Radius2),]
                : c.TryGetPolyline(out Polyline pl) ? [.. pl]
                : c.IsLinear(tol) ? [c.PointAtStart, c.PointAtEnd]
                : []
                : [],
            [(6, typeof(Brep))] = static (g, _, _, _) => g is Brep b ? [.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))] : [],
            [(6, typeof(Mesh))] = static (g, _, _, _) => g is Mesh m ? [.. Enumerable.Range(0, m.TopologyEdges.Count).Select(i => m.TopologyEdges.EdgeLine(i)).Where(static ln => ln.IsValid).Select(static ln => ln.PointAt(0.5)),] : [],
            [(6, typeof(Curve))] = static (g, _, _, _) => g is Curve c
                ? c.DuplicateSegments() is Curve[] { Length: > 0 } segs
                    ? ((Func<Curve[], Point3d[]>)(segments => {
                        try { return [.. segments.Select(seg => seg.PointAtNormalizedLength(0.5))]; } finally { foreach (Curve seg in segments) { seg.Dispose(); } }
                    }))(segs)
                    : c.TryGetPolyline(out Polyline pl)
                        ? [.. pl.GetSegments().Where(static ln => ln.IsValid).Select(static ln => ln.PointAt(0.5))]
                        : []
                : [],
            [(7, typeof(Brep))] = static (g, _, _, _) => g is Brep b ? [.. b.Faces.Select(f => f.DuplicateFace(duplicateMeshes: false) switch {
                Brep dup => ((Func<Brep, Point3d>)(d => { try { return AreaMassProperties.Compute(d)?.Centroid ?? Point3d.Unset; } finally { d.Dispose(); } }))(dup),
                _ => Point3d.Unset,
            }).Where(static p => p != Point3d.Unset),
            ] : [],
            [(7, typeof(Mesh))] = static (g, _, _, _) => g is Mesh m ? [.. Enumerable.Range(0, m.Faces.Count).Select(i => m.Faces.GetFaceCenter(i)).Where(static pt => pt.IsValid),] : [],
            [(10, typeof(Curve))] = static (g, p, ends, _) => g is Curve c && p is int count && c.DivideByCount(count, ends) is double[] ts ? [.. ts.Select(c.PointAt)] : [],
            [(10, typeof(Surface))] = static (g, p, ends, _) => g is Surface s && p is int d
                ? (s.Domain(0), s.Domain(1)) switch {
                    (Interval u, Interval v) => [.. from ui in Enumerable.Range(0, d)
                                                    from vi in Enumerable.Range(0, d)
                                                    let up = d == 1 ? 0.5 : ends ? ui / (double)(d - 1) : (ui + 0.5) / d
                                                    let vp = d == 1 ? 0.5 : ends ? vi / (double)(d - 1) : (vi + 0.5) / d
                                                    select s.PointAt(u.ParameterAt(up), v.ParameterAt(vp)),
                    ],
                }
                : [],
            [(11, typeof(Curve))] = static (g, p, ends, _) => g is Curve c && p is double length && c.DivideByLength(length, ends) is double[] ts ? [.. ts.Select(c.PointAt)] : [],
            [(12, typeof(Curve))] = static (g, p, _, _) => g is Curve c && p is Vector3d dir && c.ExtremeParameters(dir) is double[] ts ? [.. ts.Select(c.PointAt)] : [],
            [(13, typeof(Curve))] = static (g, p, _, _) => g is Curve c && p is Continuity cont
                ? ((Func<List<Point3d>>)(() => { List<Point3d> pts = []; double t0 = c.Domain.Min; while (c.GetNextDiscontinuity(cont, t0, c.Domain.Max, out double t)) { pts.Add(c.PointAt(t)); t0 = t; } return pts; }))() switch { { Count: > 0 } list => [.. list], _ => [], }
                : [],
        }.ToFrozenDictionary();
}
