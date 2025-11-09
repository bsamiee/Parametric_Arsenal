using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Feature extraction algorithms: design features, primitive decomposition, pattern recognition.</summary>
internal static class ExtractionCompute {
    internal static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeatures(Brep brep) =>
        brep.Edges.Count is 0
            ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Geometry.FeatureExtractionFailed)
            : ResultFactory.Create<((byte Type, double Param)[] Features, double Confidence)>(value: (
                Features: new (byte Type, double Param)[]  {}.Concat(
                    brep.Edges
                        .Where(e => e.EdgeCurve is not null)
                        .Select(e => (
                            Type: !e.GetNextDiscontinuity(Continuity.G2_locus_continuous, e.Domain.Min, e.Domain.Max, out double _)
                                ? (byte)0
                                : Math.Abs(Vector3d.VectorAngle(e.TangentAt(e.Domain.Mid), e.EdgeCurve.TangentAt(e.EdgeCurve.Domain.Mid)) - Math.PI / 4) < ExtractionConfig.ChamferAngleTolerance
                                    ? (byte)1
                                    : (byte)3,
                            Param: e.EdgeCurve.GetLength()
                        ))
                        .Concat(
                            brep.Loops
                                .Where(l => l.LoopType == BrepLoopType.Inner && l.To3dCurve() is Curve c && c.IsClosed && c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.MinHoleSides)
                                .Select(l => (Type: (byte)2, Param: AreaMassProperties.Compute(l.To3dCurve())?.Area ?? 0.0))
                        )
                ).ToArray(),
                Confidence: brep.Edges.Count > 0 ? 1.0 - (brep.Edges.Count(e => e.EdgeCurve is null) / (double)brep.Edges.Count) : 0.0
            ));

    internal static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry) =>
        geometry switch {
            Surface s => (s.TryGetPlane(out Plane pl, tolerance: ExtractionConfig.PrimitiveFitTolerance), s.TryGetCylinder(out Cylinder cyl, tolerance: ExtractionConfig.PrimitiveFitTolerance), s.TryGetSphere(out Sphere sph, tolerance: ExtractionConfig.PrimitiveFitTolerance)) switch {
                (true, _, _) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (Primitives: new (byte Type, Plane Frame, double[] Params)[] { (Type: (byte)0, Frame: pl, Params: new double[] { pl.OriginX, pl.OriginY, pl.OriginZ }) }, Residuals: new double[] { 0.0 })),
                (_, true, _) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (Primitives: new (byte Type, Plane Frame, double[] Params)[] { (Type: (byte)1, Frame: new Plane(cyl.Center, cyl.Axis), Params: new double[] { cyl.Radius, cyl.TotalHeight }) }, Residuals: new double[] { 0.0 })),
                (_, _, true) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (Primitives: new (byte Type, Plane Frame, double[] Params)[] { (Type: (byte)2, Frame: new Plane(sph.Center, Vector3d.ZAxis), Params: new double[] { sph.Radius }) }, Residuals: new double[] { 0.0 })),
                _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.NoPrimitivesDetected),
            },
            Brep b => b.Faces.Count is 0
                ? ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.DecompositionFailed)
                : ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (
                    Primitives: b.Faces
                            .Select(f => f.DuplicateSurface())
                            .Where(s => s is not null)
                            .Select(s => (
                                Success: s.TryGetPlane(out Plane p, tolerance: ExtractionConfig.PrimitiveFitTolerance) || s.TryGetCylinder(out Cylinder c, tolerance: ExtractionConfig.PrimitiveFitTolerance) || s.TryGetSphere(out Sphere sp, tolerance: ExtractionConfig.PrimitiveFitTolerance),
                                Primitive: s.TryGetPlane(out Plane pl, tolerance: ExtractionConfig.PrimitiveFitTolerance) ? (Type: (byte)0, Frame: pl, Params: new double[] { pl.OriginX, pl.OriginY, pl.OriginZ }) : s.TryGetCylinder(out Cylinder cy, tolerance: ExtractionConfig.PrimitiveFitTolerance) ? (Type: (byte)1, Frame: new Plane(cy.Center, cy.Axis), Params: new double[] { cy.Radius, cy.TotalHeight }) : s.TryGetSphere(out Sphere sph, tolerance: ExtractionConfig.PrimitiveFitTolerance) ? (Type: (byte)2, Frame: new Plane(sph.Center, Vector3d.ZAxis), Params: new double[] { sph.Radius }) : (Type: (byte)3, Frame: Plane.WorldXY, Params: Array.Empty<double>())
                            ))
                            .Where(t => t.Success)
                            .Select(t => t.Primitive).ToArray(),
                    Residuals: b.Faces.Select(_ => 0.0).ToArray()
                )),
            _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.DecompositionFailed),
        };

    internal static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries) =>
        geometries.Length < ExtractionConfig.PatternMinInstances
            ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected)
            : geometries.Select(g => g.GetBoundingBox(accurate: false).Center).ToArray() is Point3d[] pts && pts.Length >= 2
                ? pts.Zip(pts.Skip(1), (a, b) => b - a).ToArray() is Vector3d[] deltas && deltas.All(d => (d - deltas[0]).Length < 0.001)
                    ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(value: (Type: (byte)0, SymmetryTransform: Transform.Translation(deltas[0]), Confidence: 1.0))
                    : pts.Skip(1).Select((p, i) => Vector3d.VectorAngle(pts[0] - pts[1], p - pts[1])).ToArray() is double[] angles && angles.Length > 0 && angles.All(a => Math.Abs(a - angles[0]) < ExtractionConfig.SymmetryAngleTolerance)
                        ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(value: (Type: (byte)1, SymmetryTransform: Transform.Rotation(angles[0], Vector3d.ZAxis, pts[0]), Confidence: 0.8))
                        : ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected)
                : ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected);
}
