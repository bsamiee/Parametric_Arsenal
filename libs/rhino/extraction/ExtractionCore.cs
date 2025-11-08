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
    /// <summary>Extraction strategy function signature for polymorphic dispatch.</summary>
    private delegate Point3d[] ExtractionFunc(GeometryBase geometry, object? param, bool includeEnds, IGeometryContext context);

    /// <summary>Dispatch table mapping (kind, geometry type) to extraction strategies with validation modes.</summary>
    private static readonly FrozenDictionary<(byte Kind, Type GeometryType), (V ValidationMode, ExtractionFunc Extractor)> _dispatch =
        new Dictionary<(byte, Type), (V, ExtractionFunc)> {
            [(1, typeof(Brep))] = (V.Standard | V.MassProperties, AnalyticalBrep),
            [(1, typeof(Curve))] = (V.Standard | V.AreaCentroid, AnalyticalCurve),
            [(1, typeof(Surface))] = (V.Standard | V.AreaCentroid, AnalyticalSurface),
            [(1, typeof(Mesh))] = (V.Standard | V.MassProperties, AnalyticalMesh),
            [(1, typeof(PointCloud))] = (V.Standard, AnalyticalPointCloud),
            [(2, typeof(Curve))] = (V.Standard, ExtremalCurve),
            [(2, typeof(Surface))] = (V.Standard, ExtremalSurface),
            [(2, typeof(GeometryBase))] = (V.BoundingBox, ExtremalBoundingBox),
            [(3, typeof(NurbsCurve))] = (V.Standard, GrevilleNurbsCurve),
            [(3, typeof(NurbsSurface))] = (V.Standard, GrevilleNurbsSurface),
            [(3, typeof(Curve))] = (V.Standard, GrevilleCurve),
            [(3, typeof(Surface))] = (V.Standard, GrevilleSurface),
            [(4, typeof(NurbsCurve))] = (V.Standard | V.Degeneracy, InflectionNurbs),
            [(4, typeof(Curve))] = (V.Standard | V.Degeneracy, InflectionCurve),
            [(5, typeof(Curve))] = (V.Tolerance, QuadrantCurve),
            [(6, typeof(Brep))] = (V.Standard | V.Topology, EdgeMidpointsBrep),
            [(6, typeof(Mesh))] = (V.Standard | V.MeshSpecific, EdgeMidpointsMesh),
            [(6, typeof(Curve))] = (V.Standard, EdgeMidpointsCurve),
            [(7, typeof(Brep))] = (V.Standard | V.Topology, FaceCentroidsBrep),
            [(7, typeof(Mesh))] = (V.Standard | V.MeshSpecific, FaceCentroidsMesh),
            [(10, typeof(Curve))] = (V.Standard | V.Degeneracy, DivideByCountCurve),
            [(10, typeof(Surface))] = (V.Standard, DivideByCountSurface),
            [(11, typeof(Curve))] = (V.Standard | V.Degeneracy, DivideByLengthCurve),
            [(12, typeof(Curve))] = (V.Standard, ExtremeParametersCurve),
            [(13, typeof(Curve))] = (V.Standard, DiscontinuityCurve),
        }.ToFrozenDictionary();

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
            : NormalizeAndExtract(geometry, kind, param, includeEnds, context);
    }

    [Pure]
    private static Result<IReadOnlyList<Point3d>> NormalizeAndExtract(GeometryBase geometry, byte kind, object? param, bool includeEnds, IGeometryContext context) {
        (GeometryBase normalized, bool shouldDispose) = geometry switch {
            Extrusion ext when kind is 1 or 6 => (ext.ToBrep(splitKinkyFaces: true), true),
            SubD sd when kind is 1 or 6 => (sd.ToBrep(), true),
            _ => (geometry, false),
        };

        try {
            return FindExtractor(kind, normalized.GetType()) switch {
                (V mode, ExtractionFunc extractor) => ResultFactory.Create(value: normalized)
                    .Validate(args: mode == V.None ? null : [context, mode])
                    .Bind(g => ResultFactory.Create(value: (IReadOnlyList<Point3d>)extractor(g, param, includeEnds, context).AsReadOnly())),
                _ => ResultFactory.Create<IReadOnlyList<Point3d>>(
                    error: E.Geometry.InvalidExtraction.WithContext($"Kind: {kind}, Type: {normalized.GetType().Name}")),
            };
        } finally {
            if (shouldDispose) {
                (normalized as IDisposable)?.Dispose();
            }
        }
    }

    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (V, ExtractionFunc)? FindExtractor(byte kind, Type geometryType) =>
        _dispatch.TryGetValue((kind, geometryType), out (V, ExtractionFunc) exact) ? exact :
            _dispatch.Where(kv => kv.Key.Kind == kind && kv.Key.GeometryType.IsAssignableFrom(geometryType))
                .OrderByDescending(kv => kv.Key.GeometryType, Comparer<Type>.Create(static (a, b) => a.IsAssignableFrom(b) ? -1 : b.IsAssignableFrom(a) ? 1 : 0))
                .Select(kv => ((V, ExtractionFunc)?)kv.Value)
                .FirstOrDefault();

    // Analytical extraction (kind 1) - mass properties and characteristic points
    [Pure] private static Point3d[] AnalyticalBrep(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Brep b ? VolumeMassProperties.Compute(b) switch {
            { Centroid: { IsValid: true } ct } => [ct, .. b.Vertices.Select(v => v.Location)],
            _ => [.. b.Vertices.Select(v => v.Location)],
        } : [];

    [Pure] private static Point3d[] AnalyticalCurve(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Curve crv ? (AreaMassProperties.Compute(crv), crv) switch {
            ( { Centroid: { IsValid: true } ct }, Curve cv) =>
                [ct, cv.PointAtStart, cv.PointAt(cv.Domain.ParameterAt(0.5)), cv.PointAtEnd],
            (_, Curve cv) => [cv.PointAtStart, cv.PointAt(cv.Domain.ParameterAt(0.5)), cv.PointAtEnd],
        } : [];

    [Pure] private static Point3d[] AnalyticalSurface(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Surface s ? (AreaMassProperties.Compute(s), s, s.Domain(0), s.Domain(1)) switch {
            ( { Centroid: { IsValid: true } ct }, Surface sf, Interval u, Interval v) =>
                [ct, sf.PointAt(u.Min, v.Min), sf.PointAt(u.Max, v.Min), sf.PointAt(u.Max, v.Max), sf.PointAt(u.Min, v.Max)],
            (_, Surface sf, Interval u, Interval v) =>
                [sf.PointAt(u.Min, v.Min), sf.PointAt(u.Max, v.Min), sf.PointAt(u.Max, v.Max), sf.PointAt(u.Min, v.Max)],
        } : [];

    [Pure] private static Point3d[] AnalyticalMesh(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Mesh m ? (VolumeMassProperties.Compute(m), m) switch {
            ( { Centroid: { IsValid: true } ct }, Mesh mesh) => [ct, .. mesh.Vertices.ToPoint3dArray()],
            (_, Mesh mesh) => mesh.Vertices.ToPoint3dArray(),
        } : [];

    [Pure] private static Point3d[] AnalyticalPointCloud(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is PointCloud pc && pc.GetPoints() is Point3d[] pts && pts.Length > 0
            ? [pts.Aggregate(Point3d.Origin, static (s, pt) => s + pt) / pts.Length, .. pts]
            : [];

    // Extremal extraction (kind 2) - endpoints and domain corners
    [Pure] private static Point3d[] ExtremalCurve(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Curve crv ? [crv.PointAtStart, crv.PointAtEnd] : [];

    [Pure] private static Point3d[] ExtremalSurface(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Surface s ? (s.Domain(0), s.Domain(1), s) switch {
            (Interval u, Interval v, Surface sf) =>
                [sf.PointAt(u.Min, v.Min), sf.PointAt(u.Max, v.Min), sf.PointAt(u.Max, v.Max), sf.PointAt(u.Min, v.Max)],
        } : [];

    [Pure] private static Point3d[] ExtremalBoundingBox(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g.GetBoundingBox(accurate: true).GetCorners();

    // Greville extraction (kind 3) - NURBS control point parameters
    [Pure] private static Point3d[] GrevilleNurbsCurve(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is NurbsCurve nc ? [.. nc.GrevillePoints()] : [];

    [Pure] private static Point3d[] GrevilleNurbsSurface(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is NurbsSurface ns && ns.Points is NurbsSurfacePointList pts
            ? [.. from u in Enumerable.Range(0, pts.CountU)
                  from v in Enumerable.Range(0, pts.CountV)
                  let gp = pts.GetGrevillePoint(u, v)
                  select ns.PointAt(gp.X, gp.Y),
            ]
            : [];

    [Pure] private static Point3d[] GrevilleCurve(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Curve crv ? WithDisposal<NurbsCurve, Point3d[]>(crv.ToNurbsCurve(), static nc => nc?.GrevillePoints()?.ToArray() ?? []) : [];

    [Pure] private static Point3d[] GrevilleSurface(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Surface s ? WithDisposal<NurbsSurface, Point3d[]>(s.ToNurbsSurface(), static ns => ns?.Points is NurbsSurfacePointList pts
            ? [.. from u in Enumerable.Range(0, pts.CountU)
                  from v in Enumerable.Range(0, pts.CountV)
                  let gp = pts.GetGrevillePoint(u, v)
                  select ns.PointAt(gp.X, gp.Y),
            ]
            : []) : [];

    // Inflection extraction (kind 4) - curvature sign changes
    [Pure] private static Point3d[] InflectionNurbs(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is NurbsCurve nc ? nc.InflectionPoints() ?? [] : [];

    [Pure] private static Point3d[] InflectionCurve(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Curve crv ? WithDisposal<NurbsCurve, Point3d[]>(crv.ToNurbsCurve(), static nc => nc?.InflectionPoints() ?? []) : [];

    // Quadrant extraction (kind 5) - special points on circular/elliptical curves
    [Pure] private static Point3d[] QuadrantCurve(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Curve crv ? (crv, c.AbsoluteTolerance) switch {
            (Curve cv, double tol) when cv.TryGetCircle(out Circle circ, tol) =>
                [circ.PointAt(0), circ.PointAt(Math.PI / 2), circ.PointAt(Math.PI), circ.PointAt(3 * Math.PI / 2),],
            (Curve cv, double) when cv.TryGetEllipse(out Ellipse e, c.AbsoluteTolerance) =>
                [e.Center + (e.Plane.XAxis * e.Radius1), e.Center + (e.Plane.YAxis * e.Radius2),
                 e.Center - (e.Plane.XAxis * e.Radius1), e.Center - (e.Plane.YAxis * e.Radius2),],
            (Curve cv, double) when cv.TryGetPolyline(out Polyline pl) => [.. pl],
            (Curve cv, double) when cv.IsLinear(c.AbsoluteTolerance) => [cv.PointAtStart, cv.PointAtEnd,],
            _ => [],
        } : [];

    // Edge midpoint extraction (kind 6) - topology-based midpoints
    [Pure] private static Point3d[] EdgeMidpointsBrep(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Brep b ? [.. b.Edges.Select(e => e.PointAtNormalizedLength(0.5))] : [];

    [Pure] private static Point3d[] EdgeMidpointsMesh(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Mesh m ? [.. Enumerable.Range(0, m.TopologyEdges.Count)
            .Select(i => m.TopologyEdges.EdgeLine(i))
            .Where(static ln => ln.IsValid)
            .Select(static ln => ln.PointAt(0.5)),
        ] : [];

    [Pure] private static Point3d[] EdgeMidpointsCurve(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Curve crv ? crv.DuplicateSegments() is Curve[] { Length: > 0 } segs
            ? [.. segs.Select(static seg => seg.PointAtNormalizedLength(0.5))]
            : crv.TryGetPolyline(out Polyline pl)
                ? [.. pl.GetSegments().Where(static ln => ln.IsValid).Select(static ln => ln.PointAt(0.5))]
                : []
        : [];

    // Face centroid extraction (kind 7) - topology-based face centers
    [Pure] private static Point3d[] FaceCentroidsBrep(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Brep b ? [.. b.Faces.Select(f => WithDisposal<Brep, Point3d>(
            f.DuplicateFace(duplicateMeshes: false),
            static dup => AreaMassProperties.Compute(dup)?.Centroid ?? Point3d.Unset))
            .Where(static pt => pt != Point3d.Unset),
        ] : [];

    [Pure] private static Point3d[] FaceCentroidsMesh(GeometryBase g, object? p, bool ie, IGeometryContext c) =>
        g is Mesh m ? [.. Enumerable.Range(0, m.Faces.Count)
            .Select(i => m.Faces.GetFaceCenter(i))
            .Where(static pt => pt.IsValid),
        ] : [];

    // Division extraction (kind 10) - divide by count
    [Pure] private static Point3d[] DivideByCountCurve(GeometryBase g, object? param, bool includeEnds, IGeometryContext c) =>
        g is Curve crv && param is int count ? crv.DivideByCount(count, includeEnds) switch {
            double[] ts => [.. ts.Select(crv.PointAt)],
            _ => [],
        } : [];

    [Pure] private static Point3d[] DivideByCountSurface(GeometryBase g, object? param, bool includeEnds, IGeometryContext c) =>
        g is Surface s && param is int d ? (s.Domain(0), s.Domain(1), s) switch {
            (Interval u, Interval v, Surface sf) =>
                [.. from ui in Enumerable.Range(0, d)
                    from vi in Enumerable.Range(0, d)
                    let up = d == 1 ? 0.5 : includeEnds ? ui / (double)(d - 1) : (ui + 0.5) / d
                    let vp = d == 1 ? 0.5 : includeEnds ? vi / (double)(d - 1) : (vi + 0.5) / d
                    select sf.PointAt(u.ParameterAt(up), v.ParameterAt(vp)),
                ],
        } : [];

    // Division extraction (kind 11) - divide by length
    [Pure] private static Point3d[] DivideByLengthCurve(GeometryBase g, object? param, bool includeEnds, IGeometryContext c) =>
        g is Curve crv && param is double length ? crv.DivideByLength(length, includeEnds) switch {
            double[] ts => [.. ts.Select(crv.PointAt)],
            _ => [],
        } : [];

    // Extreme parameters extraction (kind 12) - direction-based extrema
    [Pure] private static Point3d[] ExtremeParametersCurve(GeometryBase g, object? param, bool ie, IGeometryContext c) =>
        g is Curve crv && param is Vector3d dir ? crv.ExtremeParameters(dir) switch {
            double[] ts => [.. ts.Select(crv.PointAt)],
            _ => [],
        } : [];

    // Discontinuity extraction (kind 13) - continuity-based discontinuities
    [Pure] private static Point3d[] DiscontinuityCurve(GeometryBase g, object? param, bool ie, IGeometryContext c) {
        if (g is not Curve crv || param is not Continuity cont) {
            return [];
        }
        List<Point3d> pts = [];
        double t0 = crv.Domain.Min;
        while (crv.GetNextDiscontinuity(cont, t0, crv.Domain.Max, out double t)) {
            pts.Add(crv.PointAt(t));
            t0 = t;
        }
        return [.. pts];
    }

    // Utility: Execute function with automatic disposal of temporary geometry
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TResult WithDisposal<TGeometry, TResult>(TGeometry? geometry, Func<TGeometry?, TResult> func) where TGeometry : class, IDisposable {
#pragma warning disable IDISP007 // Don't dispose injected - geometry is locally created, not injected
        try {
            return func(geometry);
        } finally {
            geometry?.Dispose();
        }
#pragma warning restore IDISP007
    }
}
