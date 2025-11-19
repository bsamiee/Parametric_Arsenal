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
            : ((Func<Result<Extraction.FeatureExtractionResult>>)(() => {
                BrepEdge[] validEdges = [.. brep.Edges.Where(static e => e.EdgeCurve is not null),];
                Extraction.FeatureType[] edgeFeatures = new Extraction.FeatureType[validEdges.Length];
                for (int i = 0; i < validEdges.Length; i++) { edgeFeatures[i] = ClassifyEdge(edge: validEdges[i], brep: brep); }
                Extraction.FeatureType[] holeFeatures = [.. brep.Loops.Where(static l => l.LoopType == BrepLoopType.Inner).Select(l => ClassifyHole(loop: l, tolerance: context.AbsoluteTolerance)).Where(static h => h is not null).Cast<Extraction.FeatureType>(),];
                double confidence = brep.Edges.Count > 0 ? 1.0 - (brep.Edges.Count(static e => e.EdgeCurve is null) / (double)brep.Edges.Count) : 0.0;
                return ResultFactory.Create(value: new Extraction.FeatureExtractionResult(Features: [.. edgeFeatures, .. holeFeatures,], Confidence: confidence));
            }))();

    private static Extraction.FeatureType ClassifyEdge(BrepEdge edge, Brep brep) =>
        Enumerable.Range(0, ExtractionConfig.FilletCurvatureSampleCount).Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.FilletCurvatureSampleCount - 1.0)))).Where(static v => v.IsValid).Select(static v => v.Length).ToArray() is double[] curvatures && curvatures.Length >= 2
            ? ClassifyEdgeFromCurvature(edge: edge, brep: brep, curvatures: curvatures)
            : new Extraction.GenericEdgeFeature(Length: edge.EdgeCurve.GetLength());

    private static Extraction.FeatureType ClassifyEdgeFromCurvature(BrepEdge edge, Brep brep, double[] curvatures) {
        double mean = curvatures.Average();
        double coeffVar = Math.Sqrt(curvatures.Sum(k => (k - mean) * (k - mean)) / curvatures.Length) / (mean > RhinoMath.ZeroTolerance ? mean : 1.0);
        bool isG2 = !edge.GetNextDiscontinuity(Continuity.G2_locus_continuous, edge.Domain.Min, edge.Domain.Max, out double _);
        return isG2 && coeffVar < ExtractionConfig.FilletCurvatureVariationThreshold && mean > RhinoMath.ZeroTolerance
            ? new Extraction.FilletFeature(Radius: 1.0 / mean)
            : ClassifyEdgeByDihedral(edge: edge, brep: brep, mean: mean);
    }

    private static Extraction.FeatureType ClassifyEdgeByDihedral(BrepEdge edge, Brep brep, double mean) {
        int[] adj = edge.AdjacentFaces();
        return adj.Length == 2 && edge.PointAt(edge.Domain.ParameterAt(0.5)) is Point3d mid && brep.Faces[adj[0]].ClosestPoint(mid, out double u0, out double v0) && brep.Faces[adj[1]].ClosestPoint(mid, out double u1, out double v1)
            ? ClassifyEdgeByAngle(edge: edge, normal0: brep.Faces[adj[0]].NormalAt(u0, v0), normal1: brep.Faces[adj[1]].NormalAt(u1, v1), mean: mean)
            : new Extraction.GenericEdgeFeature(Length: edge.EdgeCurve.GetLength());
    }

    private static Extraction.FeatureType ClassifyEdgeByAngle(BrepEdge edge, Vector3d normal0, Vector3d normal1, double mean) {
        double angle = Math.Abs(Vector3d.VectorAngle(normal0, normal1));
        return (angle > ExtractionConfig.SmoothEdgeAngleThreshold, angle < ExtractionConfig.SharpEdgeAngleThreshold, mean > RhinoMath.ZeroTolerance) switch {
            (false, false, _) => new Extraction.ChamferFeature(Angle: angle),
            (true, _, true) => new Extraction.VariableRadiusFilletFeature(Radius: 1.0 / mean),
            _ => new Extraction.GenericEdgeFeature(Length: edge.EdgeCurve.GetLength()),
        };
    }

    private static Extraction.FeatureType? ClassifyHole(BrepLoop loop, double tolerance) {
        using Curve? c = loop.To3dCurve();
        return c switch {
            null => null,
            _ when !c.IsClosed => null,
            _ when c.TryGetCircle(out Circle circ, tolerance) => new Extraction.HoleFeature(Area: Math.PI * circ.Radius * circ.Radius),
            _ when c.TryGetEllipse(out Ellipse ell, tolerance) => new Extraction.HoleFeature(Area: Math.PI * ell.Radius1 * ell.Radius2),
            _ when c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHolePolySides => ((Func<Extraction.FeatureType?>)(() => { using AreaMassProperties? amp = AreaMassProperties.Compute(c); return amp is { Area: double a } ? new Extraction.HoleFeature(Area: a) : null; }))(),
            _ => null,
        };
    }

    internal static Result<Extraction.PrimitiveDecompositionResult> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        geometry switch {
            Surface surface => ClassifySurface(surface: surface, tolerance: context.AbsoluteTolerance) switch {
                (Extraction.PrimitiveType prim, double residual) when prim is not Extraction.UnknownPrimitive => ResultFactory.Create(value: new Extraction.PrimitiveDecompositionResult(Decomposition: [(prim, residual),])),
                _ => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.NoPrimitivesDetected),
            },
            Brep brep when brep.Faces.Count > 0 => DecomposeBrepFaces(brep: brep, tolerance: context.AbsoluteTolerance),
            _ => ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.DecompositionFailed.WithContext($"Unsupported: {geometry.GetType().Name}")),
        };

    private static Result<Extraction.PrimitiveDecompositionResult> DecomposeBrepFaces(Brep brep, double tolerance) {
        List<(Extraction.PrimitiveType, double)> results = [];
        for (int i = 0; i < brep.Faces.Count; i++) {
            using Surface? dup = brep.Faces[i].DuplicateSurface();
            if (dup is null) { continue; }
            (Extraction.PrimitiveType prim, double residual) = ClassifySurface(surface: dup, tolerance: tolerance);
            if (prim is not Extraction.UnknownPrimitive) { results.Add((prim, residual)); }
        }
        return results.Count > 0 ? ResultFactory.Create(value: new Extraction.PrimitiveDecompositionResult(Decomposition: [.. results,])) : ResultFactory.Create<Extraction.PrimitiveDecompositionResult>(error: E.Geometry.NoPrimitivesDetected);
    }

    private static (Extraction.PrimitiveType, double) ClassifySurface(Surface surface, double tolerance) =>
        surface.TryGetPlane(out Plane pl, tolerance) ? (new Extraction.PlanePrimitive(Frame: pl), 0.0)
            : surface.TryGetCylinder(out Cylinder cyl, tolerance) && cyl.Radius > RhinoMath.ZeroTolerance && cyl.TotalHeight > RhinoMath.ZeroTolerance ? (new Extraction.CylinderPrimitive(Frame: new Plane(cyl.CircleAt(0).Center, cyl.Axis), Radius: cyl.Radius, Height: cyl.TotalHeight), ComputeResidual(surface, new Extraction.CylinderPrimitive(new Plane(cyl.CircleAt(0).Center, cyl.Axis), cyl.Radius, cyl.TotalHeight)))
            : surface.TryGetSphere(out Sphere sph, tolerance) && sph.Radius > RhinoMath.ZeroTolerance ? (new Extraction.SpherePrimitive(Frame: new Plane(sph.Center, Vector3d.ZAxis), Radius: sph.Radius), ComputeResidual(surface, new Extraction.SpherePrimitive(new Plane(sph.Center, Vector3d.ZAxis), sph.Radius)))
            : surface.TryGetCone(out Cone cone, tolerance) && cone.Radius > RhinoMath.ZeroTolerance && cone.Height > RhinoMath.ZeroTolerance ? (new Extraction.ConePrimitive(Frame: new Plane(cone.BasePoint, cone.Axis), Radius: cone.Radius, Height: cone.Height, Angle: Math.Atan(cone.Radius / cone.Height)), ComputeResidual(surface, new Extraction.ConePrimitive(new Plane(cone.BasePoint, cone.Axis), cone.Radius, cone.Height, Math.Atan(cone.Radius / cone.Height))))
            : surface.TryGetTorus(out Torus torus, tolerance) && torus.MajorRadius > RhinoMath.ZeroTolerance && torus.MinorRadius > RhinoMath.ZeroTolerance ? (new Extraction.TorusPrimitive(Frame: torus.Plane, MajorRadius: torus.MajorRadius, MinorRadius: torus.MinorRadius), ComputeResidual(surface, new Extraction.TorusPrimitive(torus.Plane, torus.MajorRadius, torus.MinorRadius)))
            : surface is Extrusion ext && ext.IsValid && ext.PathLineCurve() is LineCurve lc ? (new Extraction.ExtrusionPrimitive(Frame: new Plane(ext.PathStart, lc.Line.Direction), Length: lc.Line.Length), 0.0)
            : (new Extraction.UnknownPrimitive(), 0.0);

    private static double ComputeResidual(Surface surface, Extraction.PrimitiveType primitive) {
        (Interval u, Interval v) = (surface.Domain(0), surface.Domain(1));
        int n = (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.PrimitiveResidualSampleCount));
        double sum = 0.0;
        for (int i = 0; i < n; i++) {
            for (int j = 0; j < n; j++) {
                Point3d sp = surface.PointAt(u.ParameterAt(i / (n - 1.0)), v.ParameterAt(j / (n - 1.0)));
                Point3d pp = primitive switch {
                    Extraction.PlanePrimitive pl => pl.Frame.ClosestPoint(sp),
                    Extraction.CylinderPrimitive cyl => ProjectToCylinder(sp, cyl.Frame, cyl.Radius),
                    Extraction.SpherePrimitive sph => ProjectToSphere(sp, sph.Frame.Origin, sph.Radius),
                    Extraction.ConePrimitive cone => ProjectToCone(sp, cone.Frame, cone.Radius, cone.Height),
                    Extraction.TorusPrimitive tor => ProjectToTorus(sp, tor.Frame, tor.MajorRadius, tor.MinorRadius),
                    _ => sp,
                };
                sum += sp.DistanceToSquared(pp);
            }
        }
        return Math.Sqrt(sum / (n * n));
    }

    private static Point3d ProjectToCylinder(Point3d pt, Plane frame, double r) {
        Vector3d v = pt - frame.Origin;
        Point3d ax = frame.Origin + (frame.ZAxis * (v * frame.ZAxis));
        Vector3d rd = pt - ax;
        return rd.Length > RhinoMath.ZeroTolerance ? ax + ((rd / rd.Length) * r) : ax + (frame.XAxis * r);
    }

    private static Point3d ProjectToSphere(Point3d pt, Point3d center, double r) {
        Vector3d d = pt - center;
        return d.Length > RhinoMath.ZeroTolerance ? center + ((d / d.Length) * r) : center + new Vector3d(r, 0, 0);
    }

    private static Point3d ProjectToCone(Point3d pt, Plane frame, double baseR, double h) {
        Vector3d v = pt - frame.Origin;
        double axProj = v * frame.ZAxis;
        double coneR = baseR * (1.0 - (axProj / h));
        Point3d ax = frame.Origin + (frame.ZAxis * axProj);
        Vector3d rd = pt - ax;
        return rd.Length > RhinoMath.ZeroTolerance ? ax + ((rd / rd.Length) * coneR) : ax + (frame.XAxis * coneR);
    }

    private static Point3d ProjectToTorus(Point3d pt, Plane frame, double major, double minor) {
        Vector3d v = pt - frame.Origin;
        Vector3d inPlane = v - (frame.ZAxis * (v * frame.ZAxis));
        Point3d majorPt = inPlane.Length > RhinoMath.ZeroTolerance ? frame.Origin + ((inPlane / inPlane.Length) * major) : frame.Origin + (frame.XAxis * major);
        Vector3d toMinor = pt - majorPt;
        return toMinor.Length > RhinoMath.ZeroTolerance ? majorPt + ((toMinor / toMinor.Length) * minor) : majorPt + (frame.ZAxis * minor);
    }

    internal static Result<Extraction.PatternExtractionResult> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        !geometries.All(static g => g is not null) ? ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Validation.GeometryInvalid.WithContext("Array contains null"))
            : geometries.Select(static g => g.GetBoundingBox(accurate: false).Center).ToArray() is Point3d[] centers && centers.Length >= ExtractionConfig.PatternMinInstances
                ? DetectPattern(centers: centers, tolerance: context.AbsoluteTolerance)
                : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected);

    private static Result<Extraction.PatternExtractionResult> DetectPattern(Point3d[] centers, double tolerance) {
        Vector3d[] deltas = new Vector3d[centers.Length - 1];
        for (int i = 0; i < deltas.Length; i++) { deltas[i] = centers[i + 1] - centers[i]; }
        return deltas.Length > 0 && deltas[0].Length > tolerance && deltas.All(d => (d - deltas[0]).Length < tolerance)
            ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.LinearPattern(), SymmetryTransform: Transform.Translation(deltas[0]), Confidence: 1.0))
            : TryRadial(centers, tolerance) is Result<Extraction.PatternExtractionResult> { IsSuccess: true } radial ? radial
            : TryGrid(centers, tolerance) is Result<Extraction.PatternExtractionResult> { IsSuccess: true } grid ? grid
            : TryScaling(centers, tolerance) is Result<Extraction.PatternExtractionResult> { IsSuccess: true } scale ? scale
            : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected);
    }

    private static Result<Extraction.PatternExtractionResult> TryRadial(Point3d[] centers, double tolerance) {
        Point3d centroid = new(centers.Average(static p => p.X), centers.Average(static p => p.Y), centers.Average(static p => p.Z));
        double meanDist = centers.Average(c => centroid.DistanceTo(c));
        return meanDist > tolerance && centers.All(c => RhinoMath.EpsilonEquals(centroid.DistanceTo(c), meanDist, meanDist * ExtractionConfig.RadialDistanceVariationThreshold))
            && centers.Select(c => c - centroid).ToArray() is Vector3d[] radii && Plane.FitPlaneToPoints(centers, out Plane fit) == PlaneFitResult.Success
            && Enumerable.Range(0, radii.Length - 1).Select(i => Vector3d.VectorAngle(radii[i], radii[i + 1])).ToArray() is double[] angles && angles.Average() is double mean && angles.All(a => RhinoMath.EpsilonEquals(a, mean, ExtractionConfig.RadialAngleVariationThreshold))
            ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.RadialPattern(), SymmetryTransform: Transform.Rotation(mean, fit.Normal, centroid), Confidence: 0.9))
            : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected);
    }

    private static Result<Extraction.PatternExtractionResult> TryGrid(Point3d[] centers, double tolerance) {
        Vector3d[] rel = Enumerable.Range(1, centers.Length - 1).Select(i => centers[i] - centers[0]).Where(v => v.Length > tolerance).ToArray();
        return rel.Length >= 2 && FindGridBasis(rel, tolerance) is (Vector3d u, Vector3d v, true) && rel.All(vec => IsGridPoint(vec, u, v, tolerance))
            ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.GridPattern(), SymmetryTransform: Transform.PlaneToPlane(Plane.WorldXY, new Plane(centers[0], u, v)), Confidence: 0.9))
            : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected);
    }

    private static (Vector3d, Vector3d, bool) FindGridBasis(Vector3d[] candidates, double tolerance) {
        Vector3d u = candidates[0];
        double minSq = u.SquareLength;
        for (int i = 1; i < candidates.Length; i++) { if (candidates[i].SquareLength < minSq) { u = candidates[i]; minSq = candidates[i].SquareLength; } }
        double uLen = u.Length;
        if (uLen <= tolerance) { return (Vector3d.Zero, Vector3d.Zero, false); }
        Vector3d uDir = u / uLen;
        for (int i = 0; i < candidates.Length; i++) {
            double cLen = candidates[i].Length;
            if (cLen > tolerance && Math.Abs(uDir * (candidates[i] / cLen)) < ExtractionConfig.GridOrthogonalityThreshold) { return (u, candidates[i], true); }
        }
        return (Vector3d.Zero, Vector3d.Zero, false);
    }

    private static bool IsGridPoint(Vector3d vec, Vector3d u, Vector3d v, double tolerance) {
        double uLen = u.Length, vLen = v.Length;
        return uLen > tolerance && vLen > tolerance && (vec * u) / (uLen * uLen) is double a && (vec * v) / (vLen * vLen) is double b && Math.Abs(a - Math.Round(a)) < ExtractionConfig.GridPointDeviationThreshold && Math.Abs(b - Math.Round(b)) < ExtractionConfig.GridPointDeviationThreshold;
    }

    private static Result<Extraction.PatternExtractionResult> TryScaling(Point3d[] centers, double tolerance) {
        Point3d centroid = new(centers.Average(static p => p.X), centers.Average(static p => p.Y), centers.Average(static p => p.Z));
        double[] dists = centers.Select(c => centroid.DistanceTo(c)).ToArray();
        double[] ratios = Enumerable.Range(0, dists.Length - 1).Select(i => dists[i] > tolerance ? dists[i + 1] / dists[i] : 0.0).Where(r => r > tolerance).ToArray();
        return ratios.Length >= 2 && ratios.Average() is double mean && ratios.Sum(r => (r - mean) * (r - mean)) / ratios.Length < ExtractionConfig.ScalingVarianceThreshold
            ? ResultFactory.Create(value: new Extraction.PatternExtractionResult(Pattern: new Extraction.ScalingPattern(), SymmetryTransform: Transform.Scale(centroid, mean), Confidence: 0.7))
            : ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected);
    }
}
