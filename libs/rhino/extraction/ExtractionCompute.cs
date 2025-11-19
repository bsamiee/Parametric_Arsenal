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

/// <summary>Dense algorithmic implementations for extraction operations.</summary>
[Pure]
internal static class ExtractionCompute {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractAnalytical(GeometryBase geometry) =>
        geometry switch {
            Brep brep => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(brep);
                return vmp is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, .. brep.Vertices.Select(static v => v.Location),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. brep.Vertices.Select(static v => v.Location),]);
            }))(),
            Curve curve => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(curve);
                return amp is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(value: [curve.PointAtStart, curve.PointAtNormalizedLength(0.5), curve.PointAtEnd,]);
            }))(),
            Surface surface => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using AreaMassProperties? amp = AreaMassProperties.Compute(surface);
                (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1));
                return amp is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, surface.PointAt(u.Min, v.Min), surface.PointAt(u.Max, v.Min), surface.PointAt(u.Max, v.Max), surface.PointAt(u.Min, v.Max),])
                    : ResultFactory.Create<IReadOnlyList<Point3d>>(value: [surface.PointAt(u.Min, v.Min), surface.PointAt(u.Max, v.Min), surface.PointAt(u.Max, v.Max), surface.PointAt(u.Min, v.Max),]);
            }))(),
            Mesh mesh => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                using VolumeMassProperties? vmp = VolumeMassProperties.Compute(mesh);
                return vmp is { Centroid: { IsValid: true } centroid }
                    ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [centroid, .. mesh.Vertices.ToPoint3dArray(),])
                    : ResultFactory.Create(value: (IReadOnlyList<Point3d>)mesh.Vertices.ToPoint3dArray());
            }))(),
            PointCloud cloud when cloud.GetPoints() is Point3d[] pts && pts.Length > 0 =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [pts.Aggregate(Point3d.Origin, static (sum, p) => sum + p) / pts.Length, .. pts,]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Analytical unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractExtremal(GeometryBase geometry) =>
        geometry switch {
            Curve curve => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [curve.PointAtStart, curve.PointAtEnd,]),
            Surface surface when (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [surface.PointAt(u.Min, v.Min), surface.PointAt(u.Max, v.Min), surface.PointAt(u.Max, v.Max), surface.PointAt(u.Min, v.Max),]),
            GeometryBase g => ResultFactory.Create(value: (IReadOnlyList<Point3d>)g.GetBoundingBox(accurate: true).GetCorners()),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractGreville(GeometryBase geometry) =>
        geometry switch {
            NurbsCurve nurbs when nurbs.GrevillePoints() is Point3dList pts => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. pts,]),
            NurbsSurface nurbs when nurbs.Points is NurbsSurfacePointList cps =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. from int u in Enumerable.Range(0, cps.CountU) from int v in Enumerable.Range(0, cps.CountV) let g = cps.GetGrevillePoint(u, v) select nurbs.PointAt(g.X, g.Y),]),
            Curve curve when curve.ToNurbsCurve() is NurbsCurve nurbs => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                try { return nurbs.GrevillePoints() is Point3dList pts ? ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. pts,]) : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Greville extraction failed")); }
                finally { nurbs.Dispose(); }
            }))(),
            Surface surface when surface.ToNurbsSurface() is NurbsSurface nurbs && nurbs.Points is not null => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                try { return ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. from int u in Enumerable.Range(0, nurbs.Points.CountU) from int v in Enumerable.Range(0, nurbs.Points.CountV) let g = nurbs.Points.GetGrevillePoint(u, v) select nurbs.PointAt(g.X, g.Y),]); }
                finally { nurbs.Dispose(); }
            }))(),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Greville unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractInflection(GeometryBase geometry) =>
        geometry switch {
            NurbsCurve nurbs when nurbs.InflectionPoints() is Point3d[] pts => ResultFactory.Create(value: (IReadOnlyList<Point3d>)pts),
            Curve curve when curve.ToNurbsCurve() is NurbsCurve nurbs => ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                try { return nurbs.InflectionPoints() is Point3d[] pts ? ResultFactory.Create(value: (IReadOnlyList<Point3d>)pts) : ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Inflection computation failed")); }
                finally { nurbs.Dispose(); }
            }))(),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Inflection unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractQuadrant(GeometryBase geometry, double tolerance) =>
        geometry switch {
            Curve curve when curve.TryGetCircle(out Circle circle, tolerance) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [circle.PointAt(0), circle.PointAt(RhinoMath.HalfPI), circle.PointAt(Math.PI), circle.PointAt(3 * RhinoMath.HalfPI),]),
            Curve curve when curve.TryGetEllipse(out Ellipse ellipse, tolerance) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [ellipse.Center + (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center + (ellipse.Plane.YAxis * ellipse.Radius2), ellipse.Center - (ellipse.Plane.XAxis * ellipse.Radius1), ellipse.Center - (ellipse.Plane.YAxis * ellipse.Radius2),]),
            Curve curve when curve.TryGetPolyline(out Polyline polyline) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. polyline,]),
            Curve curve when curve.IsLinear(tolerance) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [curve.PointAtStart, curve.PointAtEnd,]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Quadrant extraction unsupported")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractEdgeMidpoints(GeometryBase geometry) =>
        geometry switch {
            Brep brep => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. brep.Edges.Select(static e => e.PointAtNormalizedLength(0.5)),]),
            Mesh mesh => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. Enumerable.Range(0, mesh.TopologyEdges.Count).Select(i => mesh.TopologyEdges.EdgeLine(i)).Where(static l => l.IsValid).Select(static l => l.PointAt(0.5)),]),
            Curve curve when curve.DuplicateSegments() is Curve[] { Length: > 0 } segs => ((Func<Result<IReadOnlyList<Point3d>>>)(() =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. segs.Select(static s => { try { return s.PointAtNormalizedLength(0.5); } finally { s.Dispose(); } }),])))(),
            Curve curve when curve.TryGetPolyline(out Polyline pl) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. pl.GetSegments().Where(static l => l.IsValid).Select(static l => l.PointAt(0.5)),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"EdgeMidpoints unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractFaceCentroids(GeometryBase geometry) =>
        geometry switch {
            Brep brep => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. brep.Faces.Select(f => f.DuplicateFace(duplicateMeshes: false) switch {
                Brep dup => ((Func<Point3d>)(() => { try { using AreaMassProperties? amp = AreaMassProperties.Compute(dup); return amp?.Centroid is { IsValid: true } c ? c : Point3d.Unset; } finally { dup.Dispose(); } }))(),
                _ => Point3d.Unset,
            }).Where(static p => p != Point3d.Unset),]),
            Mesh mesh => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. Enumerable.Range(0, mesh.Faces.Count).Select(i => mesh.Faces.GetFaceCenter(i)).Where(static p => p.IsValid),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"FaceCentroids unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractOsculatingFrames(GeometryBase geometry, int count) =>
        geometry switch {
            Curve curve when count >= 2 && curve.GetPerpendicularFrames([.. Enumerable.Range(0, count).Select(i => curve.Domain.ParameterAt(i / (double)(count - 1))),]) is Plane[] frames && frames.Length > 0 =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. frames.Select(static f => f.Origin),]),
            Curve => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("GetPerpendicularFrames failed")),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"OsculatingFrames unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractDivideByCount(GeometryBase geometry, int count, bool includeEnds) =>
        geometry switch {
            Curve curve when curve.DivideByCount(count, includeEnds) is double[] ts =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. ts.Select(t => curve.PointAt(t)),]),
            Surface surface when count > 0 && (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. from int ui in Enumerable.Range(0, count) from int vi in Enumerable.Range(0, count) let up = count == 1 ? 0.5 : includeEnds ? ui / (double)(count - 1) : (ui + 0.5) / count let vp = count == 1 ? 0.5 : includeEnds ? vi / (double)(count - 1) : (vi + 0.5) / count select surface.PointAt(u.ParameterAt(up), v.ParameterAt(vp)),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"DivideByCount unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractDivideByLength(GeometryBase geometry, double length, bool includeEnds) =>
        geometry switch {
            Curve curve when length > 0 && curve.DivideByLength(length, includeEnds) is double[] ts =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. ts.Select(t => curve.PointAt(t)),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"DivideByLength unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractDirectionalExtreme(GeometryBase geometry, Vector3d direction) =>
        geometry switch {
            Curve curve when curve.ExtremeParameters(direction) is double[] ts =>
                ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. ts.Select(t => curve.PointAt(t)),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"DirectionalExtreme unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExtractDiscontinuityPoints(GeometryBase geometry, Continuity continuity) =>
        geometry switch {
            Curve curve => ResultFactory.Create<IReadOnlyList<Point3d>>(value: [.. ((Func<List<Point3d>>)(() => {
                List<Point3d> pts = [];
                double t = curve.Domain.Min;
                while (curve.GetNextDiscontinuity(continuity, t, curve.Domain.Max, out double next)) { pts.Add(curve.PointAt(next)); t = next; }
                return pts;
            }))(),]),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"DiscontinuityPoints unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExtractBoundary(GeometryBase geometry) =>
        geometry switch {
            Surface surface when (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) =>
                ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. new[] { surface.IsoCurve(0, u.Min), surface.IsoCurve(1, v.Min), surface.IsoCurve(0, u.Max), surface.IsoCurve(1, v.Max) }.OfType<Curve>(),]),
            Brep brep => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. brep.DuplicateEdgeCurves(nakedOnly: false),]),
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Boundary unsupported for {geometry.GetType().Name}")),
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExtractIsocurveUniform(Surface surface, Extraction.SurfaceDirection direction) =>
        (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) ? direction switch {
            Extraction.UDirection => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(0, v.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),]),
            Extraction.VDirection => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(1, u.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),]),
            Extraction.BothDirections => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(0, v.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(), .. Enumerable.Range(0, ExtractionConfig.BoundaryIsocurveCount).Select(i => surface.IsoCurve(1, u.ParameterAt(i / (double)(ExtractionConfig.BoundaryIsocurveCount - 1)))).OfType<Curve>(),]),
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidDirection),
        } : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExtractIsocurveCount(Surface surface, int count, Extraction.SurfaceDirection direction) =>
        count < ExtractionConfig.MinIsocurveCount || count > ExtractionConfig.MaxIsocurveCount
            ? ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidCount.WithContext($"Count must be between {ExtractionConfig.MinIsocurveCount} and {ExtractionConfig.MaxIsocurveCount}"))
            : (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) ? direction switch {
                Extraction.UDirection => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(0, v.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                Extraction.VDirection => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(1, u.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                Extraction.BothDirections => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. Enumerable.Range(0, count).Select(i => surface.IsoCurve(0, v.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(), .. Enumerable.Range(0, count).Select(i => surface.IsoCurve(1, u.ParameterAt(i / (double)(count - 1)))).OfType<Curve>(),]),
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidDirection),
            } : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExtractIsocurveParameters(Surface surface, double[] parameters, Extraction.SurfaceDirection direction) =>
        parameters.Length == 0
            ? ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidParameters.WithContext("Parameters array is empty"))
            : (surface.Domain(0), surface.Domain(1)) is (Interval u, Interval v) ? direction switch {
                Extraction.UDirection => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. parameters.Select(t => surface.IsoCurve(0, v.ParameterAt(t))).OfType<Curve>(),]),
                Extraction.VDirection => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. parameters.Select(t => surface.IsoCurve(1, u.ParameterAt(t))).OfType<Curve>(),]),
                Extraction.BothDirections => ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. parameters.Select(t => surface.IsoCurve(0, v.ParameterAt(t))).OfType<Curve>(), .. parameters.Select(t => surface.IsoCurve(1, u.ParameterAt(t))).OfType<Curve>(),]),
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidDirection),
            } : ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("Surface domain unavailable"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExtractFeatureEdges(Brep brep, double angleThreshold) =>
        ResultFactory.Create<IReadOnlyList<Curve>>(value: [.. brep.Edges.Where(e => e.AdjacentFaces() is int[] adj && adj.Length == 2 && e.PointAt(e.Domain.ParameterAt(0.5)) is Point3d mid && brep.Faces[adj[0]].ClosestPoint(mid, out double u0, out double v0) && brep.Faces[adj[1]].ClosestPoint(mid, out double u1, out double v1) && Math.Abs(Vector3d.VectorAngle(brep.Faces[adj[0]].NormalAt(u0, v0), brep.Faces[adj[1]].NormalAt(u1, v1))) >= angleThreshold).Select(static e => e.DuplicateCurve()).OfType<Curve>(),]);

    internal static Result<Extraction.FeatureExtractionResult> ExtractDesignFeatures(Brep brep, IGeometryContext context) =>
        brep.Faces.Count == 0 ? ResultFactory.Create<Extraction.FeatureExtractionResult>(error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no faces"))
            : brep.Edges.Count == 0 ? ResultFactory.Create<Extraction.FeatureExtractionResult>(error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no edges"))
            : ResultFactory.Create(value: new Extraction.FeatureExtractionResult(
                Features: [
                    .. brep.Edges.Where(static e => e.EdgeCurve is not null).Select(edge =>
                        Enumerable.Range(0, ExtractionConfig.FilletCurvatureSampleCount).Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.FilletCurvatureSampleCount - 1.0)))).Where(static v => v.IsValid).Select(static v => v.Length).ToArray() is double[] curvatures && curvatures.Length >= 2
                            ? ((Func<Extraction.FeatureType>)(() => {
                                double mean = curvatures.Average();
                                double coeffVar = Math.Sqrt(curvatures.Sum(k => (k - mean) * (k - mean)) / curvatures.Length) / (mean > RhinoMath.ZeroTolerance ? mean : 1.0);
                                return !edge.GetNextDiscontinuity(Continuity.G2_locus_continuous, edge.Domain.Min, edge.Domain.Max, out double _) && coeffVar < ExtractionConfig.FilletCurvatureVariationThreshold && mean > RhinoMath.ZeroTolerance
                                    ? new Extraction.FilletFeature(Radius: 1.0 / mean)
                                    : edge.AdjacentFaces() is int[] adj && adj.Length == 2 && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d mid && brep.Faces[adj[0]].ClosestPoint(mid, out double u0, out double v0) && brep.Faces[adj[1]].ClosestPoint(mid, out double u1, out double v1)
                                        ? ((Func<Extraction.FeatureType>)(() => { double angle = Math.Abs(Vector3d.VectorAngle(brep.Faces[adj[0]].NormalAt(u0, v0), brep.Faces[adj[1]].NormalAt(u1, v1))); return (angle > ExtractionConfig.SmoothEdgeAngleThreshold, angle < ExtractionConfig.SharpEdgeAngleThreshold, mean > RhinoMath.ZeroTolerance) switch { (false, false, _) => new Extraction.ChamferFeature(Angle: angle), (true, _, true) => new Extraction.VariableRadiusFilletFeature(Radius: 1.0 / mean), _ => new Extraction.GenericEdgeFeature(Length: edge.EdgeCurve.GetLength()), }; }))()
                                        : new Extraction.GenericEdgeFeature(Length: edge.EdgeCurve.GetLength());
                            }))()
                            : new Extraction.GenericEdgeFeature(Length: edge.EdgeCurve.GetLength())),
                    .. brep.Loops.Where(static l => l.LoopType == BrepLoopType.Inner).Select(loop => { using Curve? c = loop.To3dCurve(); return c switch { null => (Extraction.FeatureType?)null, _ when !c.IsClosed => null, _ when c.TryGetCircle(out Circle circ, context.AbsoluteTolerance) => new Extraction.HoleFeature(Area: Math.PI * circ.Radius * circ.Radius), _ when c.TryGetEllipse(out Ellipse ell, context.AbsoluteTolerance) => new Extraction.HoleFeature(Area: Math.PI * ell.Radius1 * ell.Radius2), _ when c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHolePolySides => ((Func<Extraction.FeatureType?>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(c); return amp is { Area: double a } ? new Extraction.HoleFeature(Area: a) : null; }))(), _ => null, }; }).Where(static h => h is not null).Cast<Extraction.FeatureType>(),
                ],
                Confidence: brep.Edges.Count > 0 ? 1.0 - (brep.Edges.Count(static e => e.EdgeCurve is null) / (double)brep.Edges.Count) : 0.0));

    internal static Result<Extraction.PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        geometry switch {
            Surface surface => ((Func<(Extraction.PrimitiveType, double)>)(() =>
                surface.TryGetPlane(out Plane pl, context.AbsoluteTolerance) ? (new Extraction.PlanePrimitive(Frame: pl), 0.0)
                    : surface.TryGetCylinder(out Cylinder cyl, context.AbsoluteTolerance) && cyl.Radius > RhinoMath.ZeroTolerance && cyl.TotalHeight > RhinoMath.ZeroTolerance ? (new Extraction.CylinderPrimitive(Frame: new Plane(cyl.CircleAt(0).Center, cyl.Axis), Radius: cyl.Radius, Height: cyl.TotalHeight), ((Func<double>)(() => { (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1)); int n = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount)); return Math.Sqrt(Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, n).Select(j => surface.PointAt(u.ParameterAt(i / (n - 1.0)), v.ParameterAt(j / (n - 1.0))))).Sum(sp => { Vector3d vec = sp - new Plane(cyl.CircleAt(0).Center, cyl.Axis).Origin; Point3d ax = new Plane(cyl.CircleAt(0).Center, cyl.Axis).Origin + (new Plane(cyl.CircleAt(0).Center, cyl.Axis).ZAxis * (vec * new Plane(cyl.CircleAt(0).Center, cyl.Axis).ZAxis)); Vector3d rd = sp - ax; return sp.DistanceToSquared(rd.Length > RhinoMath.ZeroTolerance ? ax + ((rd / rd.Length) * cyl.Radius) : ax + (new Plane(cyl.CircleAt(0).Center, cyl.Axis).XAxis * cyl.Radius)); }) / (n * n)); }))())
                    : surface.TryGetSphere(out Sphere sph, context.AbsoluteTolerance) && sph.Radius > RhinoMath.ZeroTolerance ? (new Extraction.SpherePrimitive(Frame: new Plane(sph.Center, Vector3d.ZAxis), Radius: sph.Radius), ((Func<double>)(() => { (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1)); int n = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount)); return Math.Sqrt(Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, n).Select(j => surface.PointAt(u.ParameterAt(i / (n - 1.0)), v.ParameterAt(j / (n - 1.0))))).Sum(sp => { Vector3d d = sp - sph.Center; return sp.DistanceToSquared(d.Length > RhinoMath.ZeroTolerance ? sph.Center + ((d / d.Length) * sph.Radius) : sph.Center + new Vector3d(sph.Radius, 0, 0)); }) / (n * n)); }))())
                    : surface.TryGetCone(out Cone cone, context.AbsoluteTolerance) && cone.Radius > RhinoMath.ZeroTolerance && cone.Height > RhinoMath.ZeroTolerance ? (new Extraction.ConePrimitive(Frame: new Plane(cone.BasePoint, cone.Axis), Radius: cone.Radius, Height: cone.Height, Angle: Math.Atan(cone.Radius / cone.Height)), ((Func<double>)(() => { (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1)); int n = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount)); Plane frame = new(cone.BasePoint, cone.Axis); return Math.Sqrt(Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, n).Select(j => surface.PointAt(u.ParameterAt(i / (n - 1.0)), v.ParameterAt(j / (n - 1.0))))).Sum(sp => { Vector3d vec = sp - frame.Origin; double axProj = vec * frame.ZAxis; double coneR = cone.Radius * (1.0 - (axProj / cone.Height)); Point3d ax = frame.Origin + (frame.ZAxis * axProj); Vector3d rd = sp - ax; return sp.DistanceToSquared(rd.Length > RhinoMath.ZeroTolerance ? ax + ((rd / rd.Length) * coneR) : ax + (frame.XAxis * coneR)); }) / (n * n)); }))())
                    : surface.TryGetTorus(out Torus torus, context.AbsoluteTolerance) && torus.MajorRadius > RhinoMath.ZeroTolerance && torus.MinorRadius > RhinoMath.ZeroTolerance ? (new Extraction.TorusPrimitive(Frame: torus.Plane, MajorRadius: torus.MajorRadius, MinorRadius: torus.MinorRadius), ((Func<double>)(() => { (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1)); int n = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount)); return Math.Sqrt(Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, n).Select(j => surface.PointAt(u.ParameterAt(i / (n - 1.0)), v.ParameterAt(j / (n - 1.0))))).Sum(sp => { Vector3d vec = sp - torus.Plane.Origin; Vector3d inPlane = vec - (torus.Plane.ZAxis * (vec * torus.Plane.ZAxis)); Point3d majorPt = inPlane.Length > RhinoMath.ZeroTolerance ? torus.Plane.Origin + ((inPlane / inPlane.Length) * torus.MajorRadius) : torus.Plane.Origin + (torus.Plane.XAxis * torus.MajorRadius); Vector3d toMinor = sp - majorPt; return sp.DistanceToSquared(toMinor.Length > RhinoMath.ZeroTolerance ? majorPt + ((toMinor / toMinor.Length) * torus.MinorRadius) : majorPt + (torus.Plane.ZAxis * torus.MinorRadius)); }) / (n * n)); }))())
                    : surface is Extrusion ext && ext.IsValid && ext.PathLineCurve() is LineCurve lc ? (new Extraction.ExtrusionPrimitive(Frame: new Plane(ext.PathStart, lc.Line.Direction), Length: lc.Line.Length), 0.0)
                    : (new Extraction.UnknownPrimitive(), 0.0)))() switch {
                (Extraction.PrimitiveType prim, double residual) when prim is not Extraction.UnknownPrimitive => ResultFactory.Create(value: new Extraction.PrimitiveDecompositionResult(Decomposition: [(prim, residual),])),
                _ => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.NoPrimitivesDetected),
            },
            Brep brep when brep.Faces.Count > 0 => brep.Faces.Select(face => { using Surface? dup = face.DuplicateSurface(); return dup is null ? (new Extraction.UnknownPrimitive(), 0.0) : dup.TryGetPlane(out Plane pl, context.AbsoluteTolerance) ? (new Extraction.PlanePrimitive(Frame: pl), 0.0) : dup.TryGetCylinder(out Cylinder cyl, context.AbsoluteTolerance) && cyl.Radius > RhinoMath.ZeroTolerance && cyl.TotalHeight > RhinoMath.ZeroTolerance ? (new Extraction.CylinderPrimitive(Frame: new Plane(cyl.CircleAt(0).Center, cyl.Axis), Radius: cyl.Radius, Height: cyl.TotalHeight), ((Func<double>)(() => { (Interval u, Interval v) = (dup.Domain(0), dup.Domain(1)); int n = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount)); return Math.Sqrt(Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, n).Select(j => dup.PointAt(u.ParameterAt(i / (n - 1.0)), v.ParameterAt(j / (n - 1.0))))).Sum(sp => { Vector3d vec = sp - new Plane(cyl.CircleAt(0).Center, cyl.Axis).Origin; Point3d ax = new Plane(cyl.CircleAt(0).Center, cyl.Axis).Origin + (new Plane(cyl.CircleAt(0).Center, cyl.Axis).ZAxis * (vec * new Plane(cyl.CircleAt(0).Center, cyl.Axis).ZAxis)); Vector3d rd = sp - ax; return sp.DistanceToSquared(rd.Length > RhinoMath.ZeroTolerance ? ax + ((rd / rd.Length) * cyl.Radius) : ax + (new Plane(cyl.CircleAt(0).Center, cyl.Axis).XAxis * cyl.Radius)); }) / (n * n)); }))()) : dup.TryGetSphere(out Sphere sph, context.AbsoluteTolerance) && sph.Radius > RhinoMath.ZeroTolerance ? (new Extraction.SpherePrimitive(Frame: new Plane(sph.Center, Vector3d.ZAxis), Radius: sph.Radius), ((Func<double>)(() => { (Interval u, Interval v) = (dup.Domain(0), dup.Domain(1)); int n = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount)); return Math.Sqrt(Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, n).Select(j => dup.PointAt(u.ParameterAt(i / (n - 1.0)), v.ParameterAt(j / (n - 1.0))))).Sum(sp => { Vector3d d = sp - sph.Center; return sp.DistanceToSquared(d.Length > RhinoMath.ZeroTolerance ? sph.Center + ((d / d.Length) * sph.Radius) : sph.Center + new Vector3d(sph.Radius, 0, 0)); }) / (n * n)); }))()) : dup.TryGetCone(out Cone cone, context.AbsoluteTolerance) && cone.Radius > RhinoMath.ZeroTolerance && cone.Height > RhinoMath.ZeroTolerance ? (new Extraction.ConePrimitive(Frame: new Plane(cone.BasePoint, cone.Axis), Radius: cone.Radius, Height: cone.Height, Angle: Math.Atan(cone.Radius / cone.Height)), 0.0) : dup.TryGetTorus(out Torus torus, context.AbsoluteTolerance) && torus.MajorRadius > RhinoMath.ZeroTolerance && torus.MinorRadius > RhinoMath.ZeroTolerance ? (new Extraction.TorusPrimitive(Frame: torus.Plane, MajorRadius: torus.MajorRadius, MinorRadius: torus.MinorRadius), 0.0) : (new Extraction.UnknownPrimitive(), 0.0); }).Where(static r => r.Item1 is not Extraction.UnknownPrimitive).ToArray() is (Extraction.PrimitiveType, double)[] results && results.Length > 0 ? ResultFactory.Create(value: new Extraction.PrimitiveDecompositionResult(Decomposition: results)) : ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.NoPrimitivesDetected),
            _ => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.DecompositionFailed.WithContext($"Unsupported: {geometry.GetType().Name}")),
        };

    internal static Result<Extraction.PatternExtractionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        !geometries.All(static g => g is not null) ? ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Validation.GeometryInvalid.WithContext("Array contains null"))
            : geometries.Select(static g => g.GetBoundingBox(accurate: false).Center).ToArray() is Point3d[] centers && centers.Length >= ExtractionConfig.PatternMinInstances
                ? ((Func<Result<Extraction.PatternExtractionResult>>)(() => {
                    Vector3d[] deltas = [.. Enumerable.Range(0, centers.Length - 1).Select(i => centers[i + 1] - centers[i]),];
                    return deltas.Length > 0 && deltas[0].Length > context.AbsoluteTolerance && deltas.All(d => (d - deltas[0]).Length < context.AbsoluteTolerance)
                        ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.LinearPattern(), SymmetryTransform: Transform.Translation(deltas[0]), Confidence: 1.0))
                        : ((Func<Result<Extraction.PatternExtractionResult>>)(() => { Point3d centroid = new(centers.Average(static p => p.X), centers.Average(static p => p.Y), centers.Average(static p => p.Z)); double meanDist = centers.Average(c => centroid.DistanceTo(c)); return meanDist > context.AbsoluteTolerance && centers.All(c => RhinoMath.EpsilonEquals(centroid.DistanceTo(c), meanDist, meanDist * ExtractionConfig.RadialDistanceVariationThreshold)) && centers.Select(c => c - centroid).ToArray() is Vector3d[] radii && Plane.FitPlaneToPoints(centers, out Plane fit) == PlaneFitResult.Success && Enumerable.Range(0, radii.Length - 1).Select(i => Vector3d.VectorAngle(radii[i], radii[i + 1])).ToArray() is double[] angles && angles.Average() is double mean && angles.All(a => RhinoMath.EpsilonEquals(a, mean, ExtractionConfig.RadialAngleVariationThreshold)) ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.RadialPattern(), SymmetryTransform: Transform.Rotation(mean, fit.Normal, centroid), Confidence: 0.9)) : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected); }))() is { IsSuccess: true } radial ? radial
                        : ((Func<Result<Extraction.PatternExtractionResult>>)(() => { Vector3d[] rel = Enumerable.Range(1, centers.Length - 1).Select(i => centers[i] - centers[0]).Where(v => v.Length > context.AbsoluteTolerance).ToArray(); return rel.Length >= 2 && ((Func<(Vector3d, Vector3d, bool)>)(() => { Vector3d u = rel.MinBy(static v => v.SquareLength); double uLen = u.Length; return uLen <= context.AbsoluteTolerance ? (Vector3d.Zero, Vector3d.Zero, false) : rel.Where(c => c.Length > context.AbsoluteTolerance && Math.Abs((u / uLen) * (c / c.Length)) < ExtractionConfig.GridOrthogonalityThreshold).Select(c => (u, c, true)).FirstOrDefault((Vector3d.Zero, Vector3d.Zero, false)); }))() is (Vector3d u, Vector3d v, true) && rel.All(vec => u.Length > context.AbsoluteTolerance && v.Length > context.AbsoluteTolerance && (vec * u) / (u.Length * u.Length) is double a && (vec * v) / (v.Length * v.Length) is double b && Math.Abs(a - Math.Round(a)) < ExtractionConfig.GridPointDeviationThreshold && Math.Abs(b - Math.Round(b)) < ExtractionConfig.GridPointDeviationThreshold) ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.GridPattern(), SymmetryTransform: Transform.PlaneToPlane(Plane.WorldXY, new Plane(centers[0], u, v)), Confidence: 0.9)) : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected); }))() is { IsSuccess: true } grid ? grid
                        : ((Func<Result<Extraction.PatternExtractionResult>>)(() => { Point3d centroid = new(centers.Average(static p => p.X), centers.Average(static p => p.Y), centers.Average(static p => p.Z)); double[] dists = centers.Select(c => centroid.DistanceTo(c)).ToArray(); double[] ratios = Enumerable.Range(0, dists.Length - 1).Select(i => dists[i] > context.AbsoluteTolerance ? dists[i + 1] / dists[i] : 0.0).Where(r => r > context.AbsoluteTolerance).ToArray(); return ratios.Length >= 2 && ratios.Average() is double mean && ratios.Sum(r => (r - mean) * (r - mean)) / ratios.Length < ExtractionConfig.ScalingVarianceThreshold ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.ScalingPattern(), SymmetryTransform: Transform.Scale(centroid, mean), Confidence: 0.7)) : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected); }))() is { IsSuccess: true } scale ? scale
                        : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected);
                }))()
                : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected);
}
