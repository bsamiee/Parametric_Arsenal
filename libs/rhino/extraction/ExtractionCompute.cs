using System.Diagnostics.Contracts;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Feature extraction algorithms: design features, primitive decomposition, pattern recognition.</summary>
internal static class ExtractionCompute {
    [Pure]
    internal static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeatures(Brep brep) =>
        !brep.IsValid
            ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Validation.GeometryInvalid)
            : brep.Faces.Count is 0
                ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no faces"))
                : brep.Edges.Count is 0
                    ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Geometry.FeatureExtractionFailed.WithContext("Brep has no edges"))
                    : ResultFactory.Create(value: (
                    Features: brep.Edges
                        .Where(e => e.EdgeCurve is not null)
                        .Select(e => {
                            (double tMin, double tMid, double tMax) = (e.Domain.Min, e.Domain.ParameterAt(0.5), e.Domain.Max);
                            (double k0, double k1, double k2) = (e.EdgeCurve.CurvatureAt(tMin).Length, e.EdgeCurve.CurvatureAt(tMid).Length, e.EdgeCurve.CurvatureAt(tMax).Length);
                            (double min, double max, double avg) = (Math.Min(k0, Math.Min(k1, k2)), Math.Max(k0, Math.Max(k1, k2)), (k0 + k1 + k2) / 3.0);
                            (bool isG2, bool isConstCurv) = (!e.GetNextDiscontinuity(Continuity.G2_locus_continuous, tMin, tMax, out double _), !e.EdgeCurve.TryGetPolyline(out Polyline _) && (max - min) < Math.Max(ExtractionConfig.FilletG2Threshold * avg, ExtractionConfig.FilletG2AbsoluteTolerance));
                            double dihedral = e.AdjacentFaces() is int[] adj && adj.Length == 2 && e.PointAt(tMid) is Point3d pt && e.Brep.Faces[adj[0]].ClosestPoint(pt, out double u0, out double v0) && e.Brep.Faces[adj[1]].ClosestPoint(pt, out double u1, out double v1)
                                ? Math.Abs(Vector3d.VectorAngle(e.Brep.Faces[adj[0]].NormalAt(u0, v0), e.Brep.Faces[adj[1]].NormalAt(u1, v1)))
                                : 0.0;
                            return ((byte)(isG2 && isConstCurv ? 0 : isG2 ? 4 : Math.Abs(dihedral - (Math.PI / 4)) < ExtractionConfig.ChamferAngleTolerance ? 1 : 3), e.EdgeCurve.GetLength());
                        })
                        .Concat(brep.Loops.Where(l => l.LoopType == BrepLoopType.Inner && l.To3dCurve() is Curve c && c.IsClosed && c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHoleSides)
                            .Select(l => ((Func<(byte, double)>)(() => { using Curve? c = l.To3dCurve(); using AreaMassProperties? amp = c is not null ? AreaMassProperties.Compute(c) : null; return (2, amp?.Area ?? 0.0); }))()))
                        .ToArray(),
                    Confidence: brep.Edges.Count > 0 ? 1.0 - (brep.Edges.Count(e => e.EdgeCurve is null) / (double)brep.Edges.Count) : 0.0
                ));

    private static readonly double[] _zeroResidual = [0.0,];

    [Pure]
    internal static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry) =>
        geometry switch {
            Surface s => ClassifySurface(s) switch {
                (true, 0, Plane pl, double[] p) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: ([(Type: 0, Frame: pl, Params: p),], _zeroResidual)),
                (true, 1, Plane fr, double[] p) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: ([(Type: 1, Frame: fr, Params: p),], _zeroResidual)),
                (true, 2, Plane fr, double[] p) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: ([(Type: 2, Frame: fr, Params: p),], _zeroResidual)),
                (true, 4, Plane fr, double[] p) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: ([(Type: 4, Frame: fr, Params: p),], _zeroResidual)),
                (true, 5, Plane fr, double[] p) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: ([(Type: 5, Frame: fr, Params: p),], _zeroResidual)),
                _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.NoPrimitivesDetected),
            },
            Brep b => b.Faces.Count is 0
                ? ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.DecompositionFailed)
                : ResultFactory.Create(value: (
                    Primitives: b.Faces
                            .Select(f => f.DuplicateSurface() switch {
                                null => (Success: false, Primitive: default),
                                Surface surf => ((Func<(bool Success, (byte Type, Plane Frame, double[] Params) Primitive)>)(() => {
                                    (bool success, byte type, Plane frame, double[] pars) = ClassifySurface(surf);
                                    surf.Dispose();
                                    return (success, (type, frame, pars));
                                }))(),
                            })
                            .Where(t => t.Success)
                            .Select(t => t.Primitive).ToArray(),
                    Residuals: b.Faces.Select(_ => 0.0).ToArray()
                )),
            _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.DecompositionFailed),
        };

    [Pure]
    private static (bool Success, byte Type, Plane Frame, double[] Params) ClassifySurface(Surface s) =>
        s.TryGetPlane(out Plane pl, tolerance: ExtractionConfig.PrimitiveFitTolerance)
            ? (true, 0, pl, [pl.OriginX, pl.OriginY, pl.OriginZ,])
            : s.TryGetCylinder(out Cylinder cyl, tolerance: ExtractionConfig.PrimitiveFitTolerance) && cyl.Radius > ExtractionConfig.PrimitiveFitTolerance
                ? (true, 1, new Plane(cyl.Center, cyl.Axis), [cyl.Radius, cyl.TotalHeight,])
                : s.TryGetSphere(out Sphere sph, tolerance: ExtractionConfig.PrimitiveFitTolerance) && sph.Radius > ExtractionConfig.PrimitiveFitTolerance
                    ? (true, 2, new Plane(sph.Center, Vector3d.ZAxis), [sph.Radius,])
                    : s.TryGetCone(out Cone cone, tolerance: ExtractionConfig.PrimitiveFitTolerance) && cone.Radius > ExtractionConfig.PrimitiveFitTolerance && cone.Height > ExtractionConfig.PrimitiveFitTolerance && Math.Abs(cone.Height) > 1e-8
                        ? (true, 4, new Plane(cone.BasePoint, cone.Axis), [cone.Radius, cone.Height, Math.Atan(cone.Radius / cone.Height),])
                        : s.TryGetTorus(out Torus torus, tolerance: ExtractionConfig.PrimitiveFitTolerance) && torus.MajorRadius > ExtractionConfig.PrimitiveFitTolerance && torus.MinorRadius > ExtractionConfig.PrimitiveFitTolerance
                            ? (true, 5, torus.Plane, [torus.MajorRadius, torus.MinorRadius,])
                            : s switch {
                                Extrusion ext when ext.IsValid && ext.PathLineCurve() is LineCurve lc => (true, 6, new Plane(ext.PathStart, lc.Line.Direction), [lc.Line.Length,]),
                                _ => (false, 3, new Plane(Point3d.Origin, Vector3d.ZAxis), []),
                            };

    [Pure]
    internal static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        geometries.Length < ExtractionConfig.PatternMinInstances
            ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected)
            : geometries.Select(g => g.GetBoundingBox(accurate: false).Center).ToArray() is Point3d[] pts && pts.Length >= 2
                ? DetectPatternType(pts: pts, context: context)
                : ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected);

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> DetectPatternType(Point3d[] pts, IGeometryContext context) {
        Vector3d[] deltas = [.. pts.Zip(pts.Skip(1), (a, b) => b - a),];

        // Linear translation (type 0)
        return deltas.Length is 0
            ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected)
            : deltas.All(d => (d - deltas[0]).Length < context.AbsoluteTolerance)
            ? ResultFactory.Create(value: (Type: (byte)0, SymmetryTransform: Transform.Translation(deltas[0]), Confidence: 1.0))
            : pts.Skip(1).Select(p => Vector3d.VectorAngle(pts[0] - pts[1], p - pts[1])).ToArray() is double[] angles && angles.Length > 0 && Math.Sqrt(ComputeAngularVariance(angles)) < ExtractionConfig.SymmetryAngleTolerance
                ? ResultFactory.Create(value: (Type: (byte)1, SymmetryTransform: Transform.Rotation(angles[0], Vector3d.ZAxis, pts[0]), Confidence: 0.8))
                : TryDetectGridPattern(pts: pts, context: context) is Result<(byte, Transform, double)> gridResult && gridResult.IsSuccess
                    ? gridResult
                    : TryDetectScalingPattern(pts: pts, context: context) is Result<(byte, Transform, double)> scaleResult && scaleResult.IsSuccess
                        ? scaleResult
                        : ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected);
    }

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> TryDetectGridPattern(Point3d[] pts, IGeometryContext context) {
        Point3d origin = pts[0];
        Point3d[] relative = [.. pts.Select(p => new Point3d(p.X - origin.X, p.Y - origin.Y, p.Z - origin.Z)),];

        // Try to find two basis vectors
        Vector3d[] candidates = [.. relative.Skip(1).Select(p => new Vector3d(p.X, p.Y, p.Z)).Where(v => v.Length > context.AbsoluteTolerance),];
        return candidates.Length >= 2 && FindGridBasis(candidates: candidates, context: context) is (Vector3d u, Vector3d v, bool success) && success
            ? relative.Skip(1).All(p => IsGridPoint(point: new Vector3d(p.X, p.Y, p.Z), u: u, v: v, context: context))
                ? ResultFactory.Create(value: (Type: (byte)2, SymmetryTransform: Transform.Identity, Confidence: 0.9))
                : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected)
            : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected);
    }

    private static (Vector3d U, Vector3d V, bool Success) FindGridBasis(Vector3d[] candidates, IGeometryContext context) {
        Vector3d u = candidates[0] / candidates[0].Length;
        Vector3d v = candidates.Skip(1).FirstOrDefault(c => c.Length > context.AbsoluteTolerance && Math.Abs(Vector3d.Multiply(u, c / c.Length)) < ExtractionConfig.GridOrthogonalityThreshold);
        return v.Length > context.AbsoluteTolerance ? (u * candidates[0].Length, v, true) : (Vector3d.Zero, Vector3d.Zero, false);
    }

    private static bool IsGridPoint(Vector3d point, Vector3d u, Vector3d v, IGeometryContext context) {
        double uLen = u.Length;
        double vLen = v.Length;
        return uLen > context.AbsoluteTolerance && vLen > context.AbsoluteTolerance && Vector3d.Multiply(point, u) / (uLen * uLen) is double a && Vector3d.Multiply(point, v) / (vLen * vLen) is double b && Math.Abs(a - Math.Round(a)) < ExtractionConfig.GridPointDeviationThreshold && Math.Abs(b - Math.Round(b)) < ExtractionConfig.GridPointDeviationThreshold;
    }

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> TryDetectScalingPattern(Point3d[] pts, IGeometryContext context) {
        Point3d origin = pts[0];
        double[] distances = [.. pts.Skip(1).Select(p => origin.DistanceTo(p)).Where(d => d > context.AbsoluteTolerance),];
        double[] ratios = [.. distances.Zip(distances.Skip(1), (a, b) => (a, b)).Where(pair => pair.a > context.AbsoluteTolerance).Select(pair => pair.b / pair.a),];
        return distances.Length >= 2 && ratios.Length > 0 && Math.Sqrt(ComputeAngularVariance(ratios)) < ExtractionConfig.ScalingVarianceThreshold
            ? ResultFactory.Create(value: (Type: (byte)3, SymmetryTransform: Transform.Scale(anchor: origin, scaleFactor: ratios[0]), Confidence: 0.7))
            : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected);
    }

    [Pure]
    private static double ComputeAngularVariance(double[] angles) =>
        angles.Length switch {
            0 => double.MaxValue,
            1 => 0.0,
            int n => angles.Average() is double mean ? angles.Sum(a => (a - mean) * (a - mean)) / n : 0.0,
        };
}
