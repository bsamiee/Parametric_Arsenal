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
            : brep.Edges.Count is 0
                ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Geometry.FeatureExtractionFailed)
                : ResultFactory.Create(value: (
                    Features: brep.Edges
                        .Where(e => e.EdgeCurve is not null)
                        .Select(e => {
                            bool isG2Continuous = !e.GetNextDiscontinuity(Continuity.G2_locus_continuous, e.Domain.Min, e.Domain.Max, out double _);
                            bool hasConstantCurvature = !e.EdgeCurve.TryGetPolyline(out Polyline _) && Math.Abs(e.EdgeCurve.CurvatureAt(e.Domain.Min).Length - e.EdgeCurve.CurvatureAt(e.Domain.Max).Length) < ExtractionConfig.FilletG2Threshold * e.EdgeCurve.CurvatureAt(e.Domain.Mid).Length;
                            double tangentAngle = Math.Abs(Vector3d.VectorAngle(e.TangentAt(e.Domain.Min), e.TangentAt(e.Domain.Max)));
                            return (
                                Type: isG2Continuous && hasConstantCurvature
                                    ? (byte)0
                                    : isG2Continuous && !hasConstantCurvature
                                        ? (byte)4
                                        : Math.Abs(tangentAngle - (Math.PI / 4)) < ExtractionConfig.ChamferAngleTolerance
                                            ? (byte)1
                                            : (byte)3,
                                Param: e.EdgeCurve.GetLength()
                            );
                        })
                        .Concat(
                            brep.Loops
                                .Select(l => (Loop: l, Curve: l.To3dCurve()))
                                .Where(t => t.Loop.LoopType == BrepLoopType.Inner && t.Curve?.IsClosed is true && t.Curve.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHoleSides)
                                .Select(t => {
                                    Curve curve = t.Curve;
                                    AreaMassProperties? amp = AreaMassProperties.Compute(curve);
                                    double area = amp?.Area ?? 0.0;
                                    amp?.Dispose();
                                    curve?.Dispose();
                                    return (Type: (byte)2, Param: area);
                                })
                        )
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
                            .Select(f => {
                                Surface surf = f.DuplicateSurface();
                                (bool success, byte type, Plane frame, double[] pars) = ClassifySurface(surf);
                                surf?.Dispose();
                                return (Success: success, Primitive: (Type: type, Frame: frame, Params: pars));
                            })
                            .Where(t => t.Success)
                            .Select(t => t.Primitive).ToArray(),
                    Residuals: b.Faces.Select(_ => 0.0).ToArray()
                )),
            _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.DecompositionFailed),
        };

    [Pure]
    private static (bool Success, byte Type, Plane Frame, double[] Params) ClassifySurface(Surface s) {
#pragma warning disable IDE0004
        return s.TryGetPlane(out Plane pl, tolerance: ExtractionConfig.PrimitiveFitTolerance)
            ? (true, (byte)0, pl, [pl.OriginX, pl.OriginY, pl.OriginZ,])
            : s.TryGetCylinder(out Cylinder cyl, tolerance: ExtractionConfig.PrimitiveFitTolerance)
                ? (true, (byte)1, new Plane(cyl.Center, cyl.Axis), [cyl.Radius, cyl.TotalHeight,])
                : s.TryGetSphere(out Sphere sph, tolerance: ExtractionConfig.PrimitiveFitTolerance)
                    ? (true, (byte)2, new Plane(sph.Center, Vector3d.ZAxis), [sph.Radius,])
                    : s.TryGetCone(out Cone cone, tolerance: ExtractionConfig.PrimitiveFitTolerance)
                        ? (true, (byte)4, new Plane(cone.BasePoint, cone.Axis), [cone.Radius, cone.Height, 0.0,])
                        : s.TryGetTorus(out Torus torus, tolerance: ExtractionConfig.PrimitiveFitTolerance)
                            ? (true, (byte)5, torus.Plane, [torus.MajorRadius, torus.MinorRadius,])
                            : (false, (byte)3, new Plane(Point3d.Origin, Vector3d.ZAxis), Array.Empty<double>());
#pragma warning restore IDE0004
    }

    [Pure]
    internal static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        geometries.Length < ExtractionConfig.PatternMinInstances
            ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected)
            : geometries.Select(g => g.GetBoundingBox(accurate: false).Center).ToArray() is Point3d[] pts && pts.Length >= 2
                ? pts.Zip(pts.Skip(1), (a, b) => b - a).ToArray() is Vector3d[] deltas && deltas.All(d => (d - deltas[0]).Length < context.AbsoluteTolerance)
                    ? ResultFactory.Create(value: (Type: (byte)0, SymmetryTransform: Transform.Translation(deltas[0]), Confidence: 1.0))
                    : pts.Skip(1).Select(p => Vector3d.VectorAngle(pts[0] - pts[1], p - pts[1])).ToArray() is double[] angles && angles.Length > 0 && ComputeAngularVariance(angles) < ExtractionConfig.SymmetryAngleTolerance
                        ? ResultFactory.Create(value: (Type: (byte)1, SymmetryTransform: Transform.Rotation(angles[0], Vector3d.ZAxis, pts[0]), Confidence: 0.8))
                        : ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected)
                : ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected);

    [Pure]
    private static double ComputeAngularVariance(double[] angles) =>
        angles.Length switch {
            0 => double.MaxValue,
            1 => 0.0,
            _ => angles.Average(a => Math.Pow(a - angles.Average(), 2)),
        };
}
