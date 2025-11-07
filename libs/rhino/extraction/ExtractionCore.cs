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

/// <summary>Internal extraction algorithms with Rhino SDK geometry processing.</summary>
internal static class ExtractionCore {

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

        if (kind is 0) {
            return ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction);
        }

        (GeometryBase normalized, bool shouldDispose) = geometry switch {
            Extrusion ext when kind is 1 or 6 => (ext.ToBrep(splitKinkyFaces: true), true),
            SubD sd when kind is 1 or 6 => (sd.ToBrep(), true),
            _ => (geometry, false),
        };

        try {
            V mode = ExtractionConfig.ValidationModes.TryGetValue((kind, normalized.GetType()), out V exact) ? exact :
                ExtractionConfig.ValidationModes.Where(kv => kv.Key.Item1 == kind && kv.Key.Item2.IsInstanceOfType(normalized))
                    .OrderByDescending(kv => kv.Key.Item2, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                    .Select(kv => kv.Value)
                    .DefaultIfEmpty(V.Standard)
                    .First();

            return ResultFactory.Create(value: normalized)
                .Validate(args: mode == V.None ? null : [context, mode])
                .Bind(g => ResultFactory.Create(value: (IReadOnlyList<Point3d>)ExtractCore(g, kind, param, includeEnds, context).AsReadOnly()));
        } finally {
            if (shouldDispose) {
                (normalized as IDisposable)?.Dispose();
            }
        }
    }

    [Pure]
    private static Point3d[] ExtractCore(GeometryBase geometry, byte kind, object? param, bool includeEnds, IGeometryContext context) =>
        (kind, geometry, param) switch {
            (1, Brep b, _) => VolumeMassProperties.Compute(b) switch { { Centroid: { IsValid: true } ct } => [ct, .. b.Vertices.Select(v => v.Location)],
                _ => [.. b.Vertices.Select(v => v.Location)],
            },
            (1, Curve c, _) => (AreaMassProperties.Compute(c), c) switch {
                ( { Centroid: { IsValid: true } ct }, Curve crv) =>
                    [ct, crv.PointAtStart, crv.PointAt(crv.Domain.ParameterAt(0.5)), crv.PointAtEnd],
                (_, Curve crv) => [crv.PointAtStart, crv.PointAt(crv.Domain.ParameterAt(0.5)), crv.PointAtEnd],
            },
            (1, Surface s, _) => (AreaMassProperties.Compute(s), s, s.Domain(0), s.Domain(1)) switch {
                ( { Centroid: { IsValid: true } ct }, Surface sf, Interval u, Interval v) =>
                    [ct, sf.PointAt(u.Min, v.Min), sf.PointAt(u.Max, v.Min), sf.PointAt(u.Max, v.Max), sf.PointAt(u.Min, v.Max)],
                (_, Surface sf, Interval u, Interval v) =>
                    [sf.PointAt(u.Min, v.Min), sf.PointAt(u.Max, v.Min), sf.PointAt(u.Max, v.Max), sf.PointAt(u.Min, v.Max)],
            },
            (1, Mesh m, _) => (VolumeMassProperties.Compute(m), m) switch {
                ( { Centroid: { IsValid: true } ct }, Mesh mesh) => [ct, .. mesh.Vertices.ToPoint3dArray()],
                (_, Mesh mesh) => mesh.Vertices.ToPoint3dArray(),
            },
            (1, PointCloud pc, _) => pc.GetPoints() is Point3d[] pts && pts.Length > 0 ?
                [pts.Aggregate(Point3d.Origin, static (s, p) => s + p) / pts.Length, .. pts] : [],
            (2, Curve c, _) => [c.PointAtStart, c.PointAtEnd],
            (2, Surface s, _) => (s.Domain(0), s.Domain(1), s) switch {
                (Interval u, Interval v, Surface sf) =>
                    [sf.PointAt(u.Min, v.Min), sf.PointAt(u.Max, v.Min), sf.PointAt(u.Max, v.Max), sf.PointAt(u.Min, v.Max)],
            },
            (2, GeometryBase g, _) => g.GetBoundingBox(accurate: true).GetCorners(),
            (3, NurbsCurve nc, _) => [.. nc.GrevillePoints()],
            (3, NurbsSurface ns, _) => ns.Points is NurbsSurfacePointList pts ?
                [.. from u in Enumerable.Range(0, pts.CountU)
                    from v in Enumerable.Range(0, pts.CountV)
                    let gp = pts.GetGrevillePoint(u, v)
                    select ns.PointAt(gp.X, gp.Y),
                ] : [],
            (3, Curve c, _) => c.ToNurbsCurve() switch {
                NurbsCurve nc => ((Func<NurbsCurve, Point3d[]>)(n => { try { return [.. n.GrevillePoints()]; } finally { n.Dispose(); } }))(nc),
                _ => [],
            },
            (3, Surface s, _) => s.ToNurbsSurface() switch {
                NurbsSurface ns when ns.Points is NurbsSurfacePointList pts => ((Func<NurbsSurface, Point3d[]>)(n => {
                    try {
                        return [.. from u in Enumerable.Range(0, pts.CountU)
                                   from v in Enumerable.Range(0, pts.CountV)
                                   let gp = pts.GetGrevillePoint(u, v)
                                   select n.PointAt(gp.X, gp.Y),
                        ];
                    } finally { n.Dispose(); }
                }))(ns),
                _ => [],
            },
            (4, NurbsCurve nc, _) => nc.InflectionPoints() switch { Point3d[] { Length: > 0 } pts => pts, _ => [] },
            (4, Curve c, _) => c.ToNurbsCurve() switch {
                NurbsCurve nc => ((Func<NurbsCurve, Point3d[]>)(n => { try { return n.InflectionPoints() ?? []; } finally { n.Dispose(); } }))(nc),
                _ => [],
            },
            (5, Curve c, _) => (c, context.AbsoluteTolerance) switch {
                (Curve crv, double tol) when crv.TryGetCircle(out Circle circ, tol) =>
                    [circ.PointAt(0), circ.PointAt(Math.PI / 2), circ.PointAt(Math.PI), circ.PointAt(3 * Math.PI / 2)],
                (Curve crv, double tol) when crv.TryGetEllipse(out Ellipse e, tol) =>
                    [e.Center + (e.Plane.XAxis * e.Radius1),
                        e.Center + (e.Plane.YAxis * e.Radius2),
                        e.Center - (e.Plane.XAxis * e.Radius1),
                        e.Center - (e.Plane.YAxis * e.Radius2),
                    ],
                (Curve crv, double tol) when crv.TryGetPolyline(out Polyline pl) => [.. pl],
                (Curve crv, double tol) when crv.IsLinear(tol) => [crv.PointAtStart, crv.PointAtEnd],
                _ => [],
            },
            (6, Brep b, _) => [.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))],
            (6, Mesh m, _) => [.. Enumerable.Range(0, m.TopologyEdges.Count)
                .Select(i => m.TopologyEdges.EdgeLine(i))
                .Where(static ln => ln.IsValid)
                .Select(static ln => ln.PointAt(0.5)),
            ],
            (6, Curve c, _) => c.DuplicateSegments() is Curve[] { Length: > 0 } segs
                ? [.. segs.Select(static seg => seg.PointAtNormalizedLength(0.5))]
                : c.TryGetPolyline(out Polyline pl)
                    ? [.. pl.GetSegments().Where(static ln => ln.IsValid).Select(static ln => ln.PointAt(0.5))]
                    : [],
            (7, Brep b, _) => [.. b.Faces.Select(f => f.DuplicateFace(duplicateMeshes: false) switch {
                Brep dup => ((Func<Brep, Point3d>)(d => {
                    try {
                        return AreaMassProperties.Compute(d)?.Centroid ?? Point3d.Unset;
                    } finally { d.Dispose(); }
                }))(dup),
                _ => Point3d.Unset,
            }).Where(static p => p != Point3d.Unset),
            ],
            (7, Mesh m, _) => [.. Enumerable.Range(0, m.Faces.Count)
                .Select(i => m.Faces.GetFaceCenter(i))
                .Where(static pt => pt.IsValid),
            ],
            (10, Curve c, int count) => c.DivideByCount(count, includeEnds) switch {
                double[] ts => [.. ts.Select(c.PointAt)],
                _ => [],
            },
            (10, Surface s, int d) => (s.Domain(0), s.Domain(1), s) switch {
                (Interval u, Interval v, Surface sf) =>
                    [.. from ui in Enumerable.Range(0, d)
                        from vi in Enumerable.Range(0, d)
                        let up = d == 1 ? 0.5 : includeEnds ? ui / (double)(d - 1) : (ui + 0.5) / d
                        let vp = d == 1 ? 0.5 : includeEnds ? vi / (double)(d - 1) : (vi + 0.5) / d
                        select sf.PointAt(u.ParameterAt(up), v.ParameterAt(vp)),
                    ],
                _ => [],
            },
            (11, Curve c, double length) => c.DivideByLength(length, includeEnds) switch {
                double[] ts => [.. ts.Select(c.PointAt)],
                _ => [],
            },
            (12, Curve c, Vector3d dir) => c.ExtremeParameters(dir) switch {
                double[] ts => [.. ts.Select(c.PointAt)],
                _ => [],
            },
            (13, Curve c, Continuity cont) => ((Func<List<Point3d>>)(() => {
                List<Point3d> pts = [];
                double t0 = c.Domain.Min;
                while (c.GetNextDiscontinuity(cont, t0, c.Domain.Max, out double t)) {
                    pts.Add(c.PointAt(t));
                    t0 = t;
                }
                return pts;
            }))() switch { { Count: > 0 } list => [.. list], _ => [], },
            _ => [],
        };
}
