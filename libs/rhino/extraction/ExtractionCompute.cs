using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Feature extraction algorithms: design features, primitive decomposition, pattern recognition.</summary>
internal static class ExtractionCompute {
    internal static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeatures(Brep brep, IGeometryContext context) =>
        brep.Edges.Count is 0
            ? ResultFactory.Create<((byte Type, double Param)[], double Confidence)>(error: E.Geometry.FeatureExtractionFailed)
            : ResultFactory.Create<((byte Type, double Param)[] Features, double Confidence)>(value: (
                Features: [
                    .. brep.Edges
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
                        ),
                ],
                Confidence: brep.Edges.Count > 0 ? 1.0 - (brep.Edges.Count(e => e.EdgeCurve is null) / (double)brep.Edges.Count) : 0.0
            ));

    internal static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry, IGeometryContext context) =>
        geometry switch {
            Surface s => (s.TryGetPlane(out Plane pl, tolerance: ExtractionConfig.PrimitiveFitTolerance), s.TryGetCylinder(out Cylinder cyl, tolerance: ExtractionConfig.PrimitiveFitTolerance), s.TryGetSphere(out Sphere sph, tolerance: ExtractionConfig.PrimitiveFitTolerance)) switch {
                (true, _, _) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (Primitives: [(Type: (byte)0, Frame: pl, Params: [pl.OriginX, pl.OriginY, pl.OriginZ])], Residuals: [0.0])),
                (_, true, _) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (Primitives: [(Type: (byte)1, Frame: new Plane(cyl.Center, cyl.Axis), Params: [cyl.Radius, cyl.TotalHeight])], Residuals: [0.0])),
                (_, _, true) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (Primitives: [(Type: (byte)2, Frame: new Plane(sph.Center, Vector3d.ZAxis), Params: [sph.Radius])], Residuals: [0.0])),
                _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.NoPrimitivesDetected),
            },
            Brep b => b.Faces.Count is 0
                ? ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.DecompositionFailed)
                : ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(value: (
                    Primitives: [
                        .. b.Faces
                            .Select(f => f.DuplicateSurface())
                            .Where(s => s is not null)
                            .Select(s => (
                                Success: s.TryGetPlane(out Plane p, tolerance: ExtractionConfig.PrimitiveFitTolerance) || s.TryGetCylinder(out Cylinder c, tolerance: ExtractionConfig.PrimitiveFitTolerance) || s.TryGetSphere(out Sphere sp, tolerance: ExtractionConfig.PrimitiveFitTolerance),
                                Primitive: s.TryGetPlane(out Plane pl, tolerance: ExtractionConfig.PrimitiveFitTolerance) ? (Type: (byte)0, Frame: pl, Params: new double[] { pl.OriginX, pl.OriginY, pl.OriginZ }) : s.TryGetCylinder(out Cylinder cy, tolerance: ExtractionConfig.PrimitiveFitTolerance) ? (Type: (byte)1, Frame: new Plane(cy.Center, cy.Axis), Params: new double[] { cy.Radius, cy.TotalHeight }) : s.TryGetSphere(out Sphere sph, tolerance: ExtractionConfig.PrimitiveFitTolerance) ? (Type: (byte)2, Frame: new Plane(sph.Center, Vector3d.ZAxis), Params: new double[] { sph.Radius }) : (Type: (byte)3, Frame: Plane.WorldXY, Params: new double[] { })
                            ))
                            .Where(t => t.Success)
                            .Select(t => t.Primitive),
                    ],
                    Residuals: [.. b.Faces.Select(_ => 0.0)]
                )),
            _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(error: E.Geometry.DecompositionFailed),
        };

    internal static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        geometries.Length < ExtractionConfig.PatternMinInstances
            ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected)
            : (
                Centroids: [.. geometries.Select(g => g.GetBoundingBox(accurate: false).Center)],
                Avg: geometries.Select(g => g.GetBoundingBox(accurate: false).Center).Aggregate(Point3d.Origin, static (acc, p) => acc + p) / geometries.Length
            ) switch {
                { Centroids: Point3d[] pts } when pts.Length >= 2 => (
                    TranslationVector: pts[1] - pts[0],
                    IsLinear: pts.Zip(pts.Skip(1), static (a, b) => b - a).Distinct().Count() == 1
                ) switch {
                    { IsLinear: true, TranslationVector: Vector3d v } => ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(value: (Type: (byte)0, SymmetryTransform: Transform.Translation(v), Confidence: 1.0)),
                    _ => (
                        RotationAxis: new Line(pts[0], pts[1]),
                        Angles: [.. pts.Skip(1).Select((p, i) => Vector3d.VectorAngle(pts[0] - pts[1], p - pts[1]))]
                    ) switch {
                        { Angles: double[] angles } when angles.Length > 0 && angles.All(a => Math.Abs(a - angles[0]) < ExtractionConfig.SymmetryAngleTolerance) =>
                            ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(value: (Type: (byte)1, SymmetryTransform: Transform.Rotation(angle: angles[0], axis: Vector3d.ZAxis, point: pts[0]), Confidence: 0.8)),
                        _ => ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected),
                    },
                },
                _ => ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(error: E.Geometry.NoPatternDetected),
            };
}
