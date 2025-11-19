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

/// <summary>Orchestration layer for extraction operations via UnifiedOperation.</summary>
[Pure]
internal static class ExtractionCore {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExecutePoints<T>(T geometry, Extraction.PointOperation operation, IGeometryContext context) where T : GeometryBase =>
        !ExtractionConfig.PointOperations.TryGetValue(operation.GetType(), out ExtractionConfig.ExtractionOperationMetadata? opMeta)
            ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Unknown point operation: {operation.GetType().Name}"))
            : NormalizeGeometry(geometry: geometry, _: operation) switch {
                (GeometryBase normalized, bool shouldDispose) => UnifiedOperation.Apply(
                    input: normalized,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<Point3d>>>)(item => DispatchPointOperation(geometry: item, operation: operation, context: context)),
                    config: new OperationConfig<GeometryBase, Point3d> {
                        Context = context,
                        ValidationMode = ExtractionConfig.GetValidationMode(_: operation.GetType(), geometryType: normalized.GetType(), baseMode: opMeta.ValidationMode),
                        OperationName = opMeta.OperationName,
                    }).Tap(
                        onSuccess: _ => { if (shouldDispose) { (normalized as IDisposable)?.Dispose(); } },
                        onFailure: _ => { if (shouldDispose) { (normalized as IDisposable)?.Dispose(); } }),
            };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExecuteCurves<T>(T geometry, Extraction.CurveOperation operation, IGeometryContext context) where T : GeometryBase =>
        !ExtractionConfig.CurveOperations.TryGetValue(operation.GetType(), out ExtractionConfig.ExtractionOperationMetadata? opMeta)
            ? ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Unknown curve operation: {operation.GetType().Name}"))
            : UnifiedOperation.Apply(
                input: geometry,
                operation: (Func<T, Result<IReadOnlyList<Curve>>>)(item => DispatchCurveOperation(geometry: item, operation: operation, _: context)),
                config: new OperationConfig<T, Curve> {
                    Context = context,
                    ValidationMode = ExtractionConfig.GetValidationMode(_: operation.GetType(), geometryType: geometry.GetType(), baseMode: opMeta.ValidationMode),
                    OperationName = opMeta.OperationName,
                });

    private static (GeometryBase Geometry, bool ShouldDispose) NormalizeGeometry(GeometryBase geometry, Extraction.PointOperation _) =>
        (geometry, _) switch {
            (Extrusion ext, Extraction.Analytical or Extraction.EdgeMidpoints or Extraction.FaceCentroids or Extraction.ByCount or Extraction.ByLength or Extraction.ByDirection)
                => (ext.ToBrep(splitKinkyFaces: true) ?? geometry, true),
            (SubD subd, Extraction.Analytical or Extraction.EdgeMidpoints or Extraction.FaceCentroids or Extraction.ByCount or Extraction.ByLength or Extraction.ByDirection)
                => (subd.ToBrep() ?? geometry, true),
            (GeometryBase geom, _) => (geom, false),
        };

    private static Result<IReadOnlyList<Point3d>> DispatchPointOperation(GeometryBase geometry, Extraction.PointOperation operation, IGeometryContext context) =>
        operation switch {
            Extraction.Analytical => ExtractAnalytical(geometry: geometry),
            Extraction.Extremal => ExtractExtremal(geometry: geometry),
            Extraction.Greville => ExtractGreville(geometry: geometry),
            Extraction.Inflection => ExtractInflection(geometry: geometry),
            Extraction.Quadrant => ExtractQuadrant(geometry: geometry, _: context.AbsoluteTolerance),
            Extraction.EdgeMidpoints => ExtractEdgeMidpoints(geometry: geometry),
            Extraction.FaceCentroids => ExtractFaceCentroids(geometry: geometry),
            Extraction.OsculatingFrames osc => ExtractOsculatingFrames(geometry: geometry, count: osc.Count),
            Extraction.ByCount byCount => ExtractByCount(geometry: geometry, count: byCount.Count, includeEnds: byCount.IncludeEnds),
            Extraction.ByLength byLength => ExtractByLength(geometry: geometry, length: byLength.Length, includeEnds: byLength.IncludeEnds),
            Extraction.ByDirection byDir => ExtractByDirection(geometry: geometry, direction: byDir.Direction),
            Extraction.Discontinuity disc => ExtractDiscontinuity(geometry: geometry, continuity: disc.Type),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Unhandled point operation: {operation.GetType().Name}")),
        };

    private static Result<IReadOnlyList<Curve>> DispatchCurveOperation(GeometryBase geometry, Extraction.CurveOperation operation, IGeometryContext _) =>
        operation switch {
            Extraction.Boundary => ExtractBoundary(geometry: geometry),
            Extraction.Isocurves iso => ExtractIsocurves(geometry: geometry, direction: iso.Direction, count: iso.Count),
            Extraction.IsocurvesAt isoAt => ExtractIsocurvesAt(geometry: geometry, direction: isoAt.Direction, parameters: isoAt.Parameters),
            Extraction.FeatureEdges feat => ExtractFeatureEdges(geometry: geometry, angleThreshold: feat.AngleThreshold),
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Unhandled curve operation: {operation.GetType().Name}")),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractAnalytical(GeometryBase geometry) =>
        geometry switch {
            Brep brep => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using VolumeMassProperties? mp = VolumeMassProperties.Compute(brep);
                return mp is { Centroid: { IsValid: true } c }
                    ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c, .. brep.Vertices.Select(static v => v.Location),])
                    : ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. brep.Vertices.Select(static v => v.Location),]);
            }))(),
            Curve curve => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using AreaMassProperties? mp = AreaMassProperties.Compute(curve);
                return mp is { Centroid: { IsValid: true } c }
                    ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c, curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,])
                    : ResultFactory.Create(value: (IReadOnlyList<Point3d>)[curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,]);
            }))(),
            Surface surface => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using AreaMassProperties? mp = AreaMassProperties.Compute(surface);
                (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1));
                return mp is { Centroid: { IsValid: true } c }
                    ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c, surface.PointAt(u.Min, v.Min), surface.PointAt(u.Max, v.Min), surface.PointAt(u.Max, v.Max), surface.PointAt(u.Min, v.Max),])
                    : ResultFactory.Create(value: (IReadOnlyList<Point3d>)[surface.PointAt(u.Min, v.Min), surface.PointAt(u.Max, v.Min), surface.PointAt(u.Max, v.Max), surface.PointAt(u.Min, v.Max),]);
            }))(),
            Mesh mesh => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using VolumeMassProperties? mp = VolumeMassProperties.Compute(mesh);
                return mp is { Centroid: { IsValid: true } c }
                    ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c, .. mesh.Vertices.ToPoint3dArray(),])
                    : ResultFactory.Create(value: (IReadOnlyList<Point3d>)mesh.Vertices.ToPoint3dArray());
            }))(),
            PointCloud cloud when cloud.GetPoints() is Point3d[] pts && pts.Length > 0 =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[pts.Aggregate(Point3d.Origin, static (s, p) => s + p) / pts.Length, .. pts,]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Analytical not supported for {geometry.GetType().Name}")),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractExtremal(GeometryBase geometry) =>
        geometry switch {
            Curve c => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c.PointAtStart, c.PointAtEnd,]),
            Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[s.PointAt(u.Min, v.Min), s.PointAt(u.Max, v.Min), s.PointAt(u.Max, v.Max), s.PointAt(u.Min, v.Max),]),
            GeometryBase g => ResultFactory.Create(value: (IReadOnlyList<Point3d>)g.GetBoundingBox(accurate: true).GetCorners()),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractGreville(GeometryBase geometry) {
        return geometry switch {
            NurbsCurve nc when nc.GrevillePoints() is Point3dList gp => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. gp,]),
            NurbsSurface ns when ns.Points is NurbsSurfacePointList cp =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from int u in Enumerable.Range(0, cp.CountU) from int v in Enumerable.Range(0, cp.CountV) let g = cp.GetGrevillePoint(u, v) select ns.PointAt(g.X, g.Y),]),
            Curve c when c.ToNurbsCurve() is NurbsCurve n => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using (n) {
                    return n.GrevillePoints() is Point3dList gp
                        ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. gp,])
                        : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Greville failed"));
                }
            }))(),
            Surface s when s.ToNurbsSurface() is NurbsSurface n && n.Points is not null => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using (n) {
                    return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. from int u in Enumerable.Range(0, n.Points.CountU) from int v in Enumerable.Range(0, n.Points.CountV) let g = n.Points.GetGrevillePoint(u, v) select n.PointAt(g.X, g.Y),]);
                }
            }))(),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Greville requires NURBS")),
        };
    }

    private static Result<IReadOnlyList<Point3d>> ExtractInflection(GeometryBase geometry) {
        return geometry switch {
            NurbsCurve nc when nc.InflectionPoints() is Point3d[] inf => ResultFactory.Create(value: (IReadOnlyList<Point3d>)inf),
            Curve c when c.ToNurbsCurve() is NurbsCurve n => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using (n) {
                    Point3d[]? inf = n.InflectionPoints();
                    return inf is not null
                        ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)inf)
                        : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Inflection computation failed"));
                }
            }))(),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Inflection requires curve")),
        };
    }

    private static Result<IReadOnlyList<Point3d>> ExtractQuadrant(GeometryBase geometry, double _) =>
        geometry switch {
            Curve c when c.TryGetCircle(out Circle circ, _) =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[circ.PointAt(0), circ.PointAt(RhinoMath.HalfPI), circ.PointAt(Math.PI), circ.PointAt(3 * RhinoMath.HalfPI),]),
            Curve c when c.TryGetEllipse(out Ellipse ell, _) =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[ell.Center + (ell.Plane.XAxis * ell.Radius1), ell.Center + (ell.Plane.YAxis * ell.Radius2), ell.Center - (ell.Plane.XAxis * ell.Radius1), ell.Center - (ell.Plane.YAxis * ell.Radius2),]),
            Curve c when c.TryGetPolyline(out Polyline pl) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pl]),
            Curve c when c.IsLinear(_) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[c.PointAtStart, c.PointAtEnd,]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant unsupported")),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractEdgeMidpoints(GeometryBase geometry) {
        return geometry switch {
            Brep b => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Edges.Select(static e => e.PointAtNormalizedLength(0.5)),]),
            Mesh m => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. Enumerable.Range(0, m.TopologyEdges.Count).Select(i => m.TopologyEdges.EdgeLine(i)).Where(static l => l.IsValid).Select(static l => l.PointAt(0.5)),]),
            Curve c when c.DuplicateSegments() is Curve[] { Length: > 0 } segs => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                List<Point3d> midpoints = new(capacity: segs.Length);
                for (int i = 0; i < segs.Length; i++) {
                    using Curve s = segs[i];
                    midpoints.Add(s.PointAtNormalizedLength(0.5));
                }
                return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. midpoints]);
            }))(),
            Curve c when c.TryGetPolyline(out Polyline pl) => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pl.GetSegments().Where(static l => l.IsValid).Select(static l => l.PointAt(0.5)),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("EdgeMidpoints requires Brep/Mesh/Curve")),
        };
    }

    private static Result<IReadOnlyList<Point3d>> ExtractFaceCentroids(GeometryBase geometry) {
        return geometry switch {
            Brep b => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. b.Faces.Select(f => {
                Brep? dup = f.DuplicateFace(duplicateMeshes: false);
                if (dup is null) {
                    return Point3d.Unset;
                }
                using (dup) {
                    using AreaMassProperties? mp = AreaMassProperties.Compute(dup);
                    return mp?.Centroid is Point3d { IsValid: true } c ? c : Point3d.Unset;
                }
            }).Where(static p => p != Point3d.Unset),
            ]),
            Mesh m => ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. Enumerable.Range(0, m.Faces.Count).Select(i => m.Faces.GetFaceCenter(i)).Where(static p => p.IsValid),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("FaceCentroids requires Brep/Mesh")),
        };
    }

    private static Result<IReadOnlyList<Point3d>> ExtractOsculatingFrames(GeometryBase geometry, int count) =>
        geometry switch {
            Curve c when count >= 2 && c.GetPerpendicularFrames(parameters: [.. Enumerable.Range(0, count).Select(i => c.Domain.ParameterAt(i / (double)(count - 1))),]) is Plane[] frames && frames.Length > 0 =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. frames.Select(static f => f.Origin),]),
            Curve => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Frame count must be >= 2")),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("OsculatingFrames requires Curve")),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractByCount(GeometryBase geometry, int count, bool includeEnds) =>
        geometry switch {
            Curve c when count > 0 && c.DivideByCount(count, includeEnds) is double[] pars =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(t => c.PointAt(t)),]),
            Surface s when count > 0 && (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) =>
                ResultFactory.Create(
                    value: (IReadOnlyList<Point3d>)[
                        .. from int ui in Enumerable.Range(0, count)
                           from int vi in Enumerable.Range(0, count)
                           let up = count == 1 ? 0.5 : includeEnds ? ui / (double)(count - 1) : (ui + 0.5) / count
                           let vp = count == 1 ? 0.5 : includeEnds ? vi / (double)(count - 1) : (vi + 0.5) / count
                           select s.PointAt(u.ParameterAt(up), v.ParameterAt(vp)),
                    ]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("ByCount requires Curve/Surface and count > 0")),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractByLength(GeometryBase geometry, double length, bool includeEnds) =>
        geometry switch {
            Curve c when length > 0 && c.DivideByLength(length, includeEnds) is double[] pars =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(t => c.PointAt(t)),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("ByLength requires Curve and length > 0")),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractByDirection(GeometryBase geometry, Vector3d direction) =>
        geometry switch {
            Curve c when c.ExtremeParameters(direction) is double[] pars =>
                ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. pars.Select(t => c.PointAt(t)),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("ByDirection requires Curve")),
        };

    private static Result<IReadOnlyList<Point3d>> ExtractDiscontinuity(GeometryBase geometry, Continuity continuity) {
        return geometry switch {
            Curve c => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                List<Point3d> discs = [];
                double param = c.Domain.Min;
                while (c.GetNextDiscontinuity(continuity, param, c.Domain.Max, out double next)) {
                    discs.Add(c.PointAt(next));
                    param = next;
                }
                return ResultFactory.Create(value: (IReadOnlyList<Point3d>)[.. discs]);
            }))(),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Discontinuity requires Curve")),
        };
    }

    private static Result<IReadOnlyList<Curve>> ExtractBoundary(GeometryBase geometry) =>
        geometry switch {
            Surface s when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) && new Curve?[] { s.IsoCurve(0, u.Min), s.IsoCurve(1, v.Min), s.IsoCurve(0, u.Max), s.IsoCurve(1, v.Max), } is Curve?[] curves =>
                ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. curves.OfType<Curve>(),]),
            Brep b => ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. b.DuplicateEdgeCurves(nakedOnly: false),]),
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("Boundary requires Surface/Brep")),
        };

    private static Result<IReadOnlyList<Curve>> ExtractIsocurves(GeometryBase geometry, Extraction.IsocurveDirection direction, int count) =>
        (count >= ExtractionConfig.MinIsocurveCount && count <= ExtractionConfig.MaxIsocurveCount) switch {
            false => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidCount.WithContext($"Count must be {ExtractionConfig.MinIsocurveCount}-{ExtractionConfig.MaxIsocurveCount}")),
            true => (geometry, direction) switch {
                (Surface s, Extraction.UDirection) when s.Domain(1) is Interval v =>
                    ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. Enumerable.Range(0, count).Select(i => s.IsoCurve(0, v.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                (Surface s, Extraction.VDirection) when s.Domain(0) is Interval u =>
                    ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. Enumerable.Range(0, count).Select(i => s.IsoCurve(1, u.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                (Surface s, Extraction.BothDirections) when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) =>
                    ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. Enumerable.Range(0, count).Select(i => s.IsoCurve(0, v.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(), .. Enumerable.Range(0, count).Select(i => s.IsoCurve(1, u.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("Isocurves requires Surface")),
            },
        };

    private static Result<IReadOnlyList<Curve>> ExtractIsocurvesAt(GeometryBase geometry, Extraction.IsocurveDirection direction, double[] parameters) =>
        (geometry, direction, parameters.Length > 0) switch {
            (Surface s, Extraction.UDirection, true) when s.Domain(1) is Interval v =>
                ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. parameters.Select(t => s.IsoCurve(0, v.ParameterAt(t))).OfType<Curve>(),]),
            (Surface s, Extraction.VDirection, true) when s.Domain(0) is Interval u =>
                ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. parameters.Select(t => s.IsoCurve(1, u.ParameterAt(t))).OfType<Curve>(),]),
            (Surface s, Extraction.BothDirections, true) when (s.Domain(0), s.Domain(1)) is (Interval u, Interval v) =>
                ResultFactory.Create(value: (IReadOnlyList<Curve>)[.. parameters.Select(t => s.IsoCurve(0, v.ParameterAt(t))).OfType<Curve>(), .. parameters.Select(t => s.IsoCurve(1, u.ParameterAt(t))).OfType<Curve>(),]),
            (_, _, false) => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty")),
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("IsocurvesAt requires Surface")),
        };

    private static Result<IReadOnlyList<Curve>> ExtractFeatureEdges(GeometryBase geometry, double angleThreshold) =>
        geometry is not Brep b
            ? ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("FeatureEdges requires Brep"))
            : ResultFactory.Create(
                value: (IReadOnlyList<Curve>)[..
                    b.Edges
                        .Select(e => new { Edge = e, Adj = e.AdjacentFaces(), Mid = e.PointAt(e.Domain.ParameterAt(0.5)), })
                        .Where(x => x.Adj is int[] adj && adj.Length == 2)
                        .Select(x => new {
                            x.Edge,
                            x.Adj,
                            x.Mid,
                            Face0 = b.Faces[x.Adj[0]],
                            Face1 = b.Faces[x.Adj[1]],
                        })
                        .Select(x => new {
                            x.Edge,
                            x.Face0,
                            x.Face1,
                            x.Mid,
                            HasClosest0 = x.Face0.ClosestPoint(testPoint: x.Mid, u: out double u0, v: out double v0),
                            U0 = u0,
                            V0 = v0,
                            HasClosest1 = x.Face1.ClosestPoint(testPoint: x.Mid, u: out double u1, v: out double v1),
                            U1 = u1,
                            V1 = v1,
                        })
                        .Where(x =>
                            x.HasClosest0 && x.HasClosest1 &&
                            Math.Abs(
                                Vector3d.VectorAngle(
                                    x.Face0.NormalAt(u: x.U0, v: x.V0),
                                    x.Face1.NormalAt(u: x.U1, v: x.V1)
                                )
                            ) >= angleThreshold)
                        .Select(x => x.Edge.DuplicateCurve())
                        .OfType<Curve>(),
                ]);
}
