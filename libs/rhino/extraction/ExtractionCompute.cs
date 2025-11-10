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
                    : ExtractFeaturesInternal(brep: brep);

    [Pure]
    private static Result<((byte Type, double Param)[] Features, double Confidence)> ExtractFeaturesInternal(Brep brep) {
        BrepEdge[] validEdges = [.. brep.Edges.Where(e => e.EdgeCurve is not null),];
        (byte Type, double Param)[] edgeFeatures = new (byte, double)[validEdges.Length];

        for (int i = 0; i < validEdges.Length; i++) {
            edgeFeatures[i] = ClassifyEdge(edge: validEdges[i], brep: brep);
        }

        (byte Type, double Param)[] holeFeatures = [.. brep.Loops
            .Where(l => l.LoopType == BrepLoopType.Inner)
            .Select(l => ClassifyHole(loop: l))
            .Where(h => h.IsHole)
            .Select(h => (Type: ExtractionConfig.FeatureTypeHole, Param: h.Area)),
        ];

        (byte Type, double Param)[] allFeatures = [.. edgeFeatures.Concat(holeFeatures),];
        double confidence = brep.Edges.Count > 0
            ? 1.0 - (brep.Edges.Count(e => e.EdgeCurve is null) / (double)brep.Edges.Count)
            : 0.0;

        return ResultFactory.Create(value: (Features: allFeatures, Confidence: confidence));
    }

    [Pure]
    private static (byte Type, double Param) ClassifyEdge(BrepEdge edge, Brep brep) {
        double[] curvatures = [.. Enumerable.Range(0, ExtractionConfig.Params["FilletCurvatureSamples"])
            .Select(i => edge.EdgeCurve.CurvatureAt(edge.Domain.ParameterAt(i / (ExtractionConfig.Params["FilletCurvatureSamples"] - 1.0))))
            .Where(v => v.IsValid)
            .Select(v => v.Length),
        ];

        if (curvatures.Length < 2) {
            return (ExtractionConfig.FeatureTypeGenericEdge, edge.EdgeCurve.GetLength());
        }

        (double[], double) data = (curvatures, curvatures.Average());

        return ExtractionConfig.EdgeClassifiers.TryGetValue("Fillet", out Func<BrepEdge, Brep, (double[], double)?, (byte, double)>? filletClassifier)
            && filletClassifier(edge, brep, data) is (byte t, double p) && t == ExtractionConfig.FeatureTypeFillet
                ? (t, p)
                : ExtractionConfig.EdgeClassifiers.TryGetValue("Chamfer", out Func<BrepEdge, Brep, (double[], double)?, (byte, double)>? chamferClassifier)
                    ? chamferClassifier(edge, brep, data)
                    : (ExtractionConfig.FeatureTypeGenericEdge, edge.EdgeCurve.GetLength());
    }

    [Pure]
    private static (bool IsHole, double Area) ClassifyHole(BrepLoop loop) {
        using Curve? c = loop.To3dCurve();

        if (c?.IsClosed is not true) {
            return (false, 0.0);
        }

        double area = 0.0;
        bool isHole = false;

        if (c.TryGetCircle(out Circle circ, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])) {
            area = Math.PI * circ.Radius * circ.Radius;
            isHole = true;
        } else if (c.TryGetEllipse(out Ellipse ell, tolerance: ExtractionConfig.Thresholds["PrimitiveFit"])) {
            area = Math.PI * ell.Radius1 * ell.Radius2;
            isHole = true;
        } else if (c.TryGetPolyline(out Polyline pl) && pl.Count >= ExtractionConfig.Params["MinHolePolySides"]) {
            using AreaMassProperties? amp = AreaMassProperties.Compute(c);
            area = amp?.Area ?? 0.0;
            isHole = true;
        }

        return (isHole, area);
    }

    private static readonly double[] _zeroResidual = [0.0,];

    [Pure]
    internal static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeToPrimitives(GeometryBase geometry) =>
        geometry switch {
            Surface s => ClassifySurface(surface: s) switch {
                (true, byte type, Plane frame, double[] pars) => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                    value: ([(Type: type, Frame: frame, Params: pars),], _zeroResidual)),
                _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                    error: E.Geometry.NoPrimitivesDetected),
            },
            Brep b when b.Faces.Count is 0 => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                error: E.Geometry.DecompositionFailed.WithContext("Brep has no faces")),
            Brep b => DecomposeBrepFaces(brep: b),
            _ => ResultFactory.Create<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)>(
                error: E.Geometry.DecompositionFailed.WithContext($"Unsupported geometry type: {geometry.GetType().Name}")),
        };

    [Pure]
    private static Result<((byte Type, Plane Frame, double[] Params)[] Primitives, double[] Residuals)> DecomposeBrepFaces(Brep brep) {
        (bool Success, byte Type, Plane Frame, double[] Params, double Residual)[] classified =
            new (bool, byte, Plane, double[], double)[brep.Faces.Count];

        for (int i = 0; i < brep.Faces.Count; i++) {
            classified[i] = brep.Faces[i].DuplicateSurface() switch {
                null => (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, [], 0.0),
                Surface surf => ((Func<(bool, byte, Plane, double[], double)>)(() => {
                    (bool success, byte type, Plane frame, double[] pars) = ClassifySurface(surface: surf);
                    double residual = success ? ComputeSurfaceResidual(surface: surf, type: type, frame: frame, pars: pars) : 0.0;
                    surf.Dispose();
                    return (success, type, frame, pars, residual);
                }))(),
            };
        }

        (byte Type, Plane Frame, double[] Params)[] primitives = [.. classified
            .Where(c => c.Success)
            .Select(c => (c.Type, c.Frame, c.Params)),
        ];

        double[] residuals = [.. classified
            .Where(c => c.Success)
            .Select(c => c.Residual),
        ];

        return primitives.Length > 0
            ? ResultFactory.Create(value: (Primitives: primitives, Residuals: residuals))
            : ResultFactory.Create<((byte, Plane, double[])[], double[])>(
                error: E.Geometry.NoPrimitivesDetected.WithContext("No faces classified as primitives"));
    }

    [Pure]
    private static (bool Success, byte Type, Plane Frame, double[] Params) ClassifySurface(Surface surface) =>
        new[] {
            ExtractionConfig.PrimitiveTypePlane,
            ExtractionConfig.PrimitiveTypeCylinder,
            ExtractionConfig.PrimitiveTypeSphere,
            ExtractionConfig.PrimitiveTypeCone,
            ExtractionConfig.PrimitiveTypeTorus,
            ExtractionConfig.PrimitiveTypeExtrusion,
        }.Select(type => ExtractionConfig.PrimitiveClassifiers.TryGetValue(type, out Func<Surface, double, (bool, byte, Plane, double[])>? classifier)
            ? classifier(surface, ExtractionConfig.Thresholds["PrimitiveFit"])
            : (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, []))
        .FirstOrDefault(result => result.Item1, (false, ExtractionConfig.PrimitiveTypeUnknown, Plane.WorldXY, []));

    [Pure]
    private static double ComputeSurfaceResidual(Surface surface, byte type, Plane frame, double[] pars) =>
        (surface.Domain(0), surface.Domain(1), (int)Math.Ceiling(Math.Sqrt(ExtractionConfig.Params["PrimitiveResidualSamples"]))) is (Interval u, Interval v, int samplesPerDir)
            && ExtractionConfig.PrimitiveProjectors.TryGetValue(type, out Func<Point3d, Plane, double[], Point3d>? projector)
            ? Math.Sqrt(Enumerable.Range(0, samplesPerDir).SelectMany(i => Enumerable.Range(0, samplesPerDir).Select(j =>
                surface.PointAt(u: u.ParameterAt(i / (double)(samplesPerDir - 1)), v: v.ParameterAt(j / (double)(samplesPerDir - 1))) is Point3d sp
                    ? sp.DistanceToSquared(projector(sp, frame, pars))
                    : 0.0)).Sum() / (samplesPerDir * samplesPerDir))
            : 0.0;

    [Pure]
    internal static Result<(byte Type, Transform SymmetryTransform, double Confidence)> ExtractPatterns(GeometryBase[] geometries, IGeometryContext context) =>
        geometries.Length < ExtractionConfig.Params["PatternMinInstances"]
            ? ResultFactory.Create<(byte Type, Transform SymmetryTransform, double Confidence)>(
                error: E.Geometry.NoPatternDetected.WithContext($"Need at least {ExtractionConfig.Params["PatternMinInstances"].ToString(System.Globalization.CultureInfo.InvariantCulture)} instances"))
            : DetectPatternType(centers: [.. geometries.Select(g => g.GetBoundingBox(accurate: false).Center),], context: context);

    private static Result<(byte Type, Transform SymmetryTransform, double Confidence)> DetectPatternType(Point3d[] centers, IGeometryContext context) =>
        new[] {
            ExtractionConfig.PatternTypeLinear,
            ExtractionConfig.PatternTypeRadial,
            ExtractionConfig.PatternTypeGrid,
            ExtractionConfig.PatternTypeScaling,
        }.Select(type => ExtractionConfig.PatternDetectors.TryGetValue(type, out Func<Point3d[], IGeometryContext, Result<(byte, Transform, double)>>? detector)
            ? detector(centers, context)
            : ResultFactory.Create<(byte, Transform, double)>(error: E.Geometry.NoPatternDetected))
        .FirstOrDefault(r => r.IsSuccess, ResultFactory.Create<(byte, Transform, double)>(
            error: E.Geometry.NoPatternDetected.WithContext("No linear, radial, grid, or scaling pattern detected")));
}
