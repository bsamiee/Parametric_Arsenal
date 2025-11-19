using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Errors;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Extraction;

/// <summary>Orchestration layer for extraction operations via UnifiedOperation.</summary>
[Pure]
internal static class ExtractionCore {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Point3d>> ExecutePoints<T>(T geometry, Extraction.PointMode mode, IGeometryContext context) where T : GeometryBase {
        ExtractionConfig.ExtractionOperationMetadata meta = ExtractionConfig.GetPointMetadata(mode.GetType(), geometry.GetType());
        (GeometryBase normalized, bool shouldDispose) = NormalizeGeometry(geometry: geometry, mode: mode);
        return normalized is null
            ? ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext("Normalization failed"))
            : ((Func<Result<IReadOnlyList<Point3d>>>)(() => {
                Result<IReadOnlyList<Point3d>> result = UnifiedOperation.Apply(
                    input: normalized,
                    operation: (Func<GeometryBase, Result<IReadOnlyList<Point3d>>>)(item => DispatchPointExtraction(geometry: item, mode: mode, context: context)),
                    config: new OperationConfig<GeometryBase, Point3d> {
                        Context = context,
                        ValidationMode = meta.ValidationMode,
                        OperationName = meta.OperationName,
                    });
                return (shouldDispose, normalized) switch {
                    (true, IDisposable disposable) => result.Tap(onSuccess: _ => disposable.Dispose(), onFailure: _ => disposable.Dispose()),
                    _ => result,
                };
            }))();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<IReadOnlyList<Point3d>>> ExecutePointsMultiple<T>(
        IReadOnlyList<T> geometries,
        Extraction.PointMode mode,
        IGeometryContext context,
        bool accumulateErrors,
        bool enableParallel) where T : GeometryBase {
        Result<IReadOnlyList<Point3d>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => ExecutePoints(geometry: item, mode: mode, context: context)),]
            : [.. geometries.Select(item => ExecutePoints(geometry: item, mode: mode, context: context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Point3d>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Point3d>>)[.. results.Select(static r => r.Value),]),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<Curve>> ExecuteCurves<T>(T geometry, Extraction.CurveMode mode, IGeometryContext context) where T : GeometryBase {
        ExtractionConfig.ExtractionOperationMetadata meta = ExtractionConfig.GetCurveMetadata(mode.GetType(), geometry.GetType());
        return UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<T, Result<IReadOnlyList<Curve>>>)(item => DispatchCurveExtraction(geometry: item, mode: mode, context: context)),
            config: new OperationConfig<T, Curve> {
                Context = context,
                ValidationMode = meta.ValidationMode,
                OperationName = meta.OperationName,
            });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<IReadOnlyList<IReadOnlyList<Curve>>> ExecuteCurvesMultiple<T>(
        IReadOnlyList<T> geometries,
        Extraction.CurveMode mode,
        IGeometryContext context,
        bool accumulateErrors,
        bool enableParallel) where T : GeometryBase {
        Result<IReadOnlyList<Curve>>[] results = enableParallel
            ? [.. geometries.AsParallel().Select(item => ExecuteCurves(geometry: item, mode: mode, context: context)),]
            : [.. geometries.Select(item => ExecuteCurves(geometry: item, mode: mode, context: context)),];
        return (accumulateErrors, results.All(static r => r.IsSuccess)) switch {
            (true, true) => ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
            (true, false) => ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. results.Where(static r => !r.IsSuccess).SelectMany(static r => r.Errors),]),
            (false, _) => results.FirstOrDefault(static r => !r.IsSuccess) is { IsSuccess: false } failure
                ? ResultFactory.Create<IReadOnlyList<IReadOnlyList<Curve>>>(errors: [.. failure.Errors,])
                : ResultFactory.Create(value: (IReadOnlyList<IReadOnlyList<Curve>>)[.. results.Select(static r => r.Value),]),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Extraction.FeatureExtractionResult> ExecuteFeatureExtraction(Brep brep, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: brep,
            operation: (Func<Brep, Result<IReadOnlyList<Extraction.FeatureExtractionResult>>>)(item =>
                ExtractionCompute.ExtractDesignFeatures(brep: item, context: context)
                    .Map(r => (IReadOnlyList<Extraction.FeatureExtractionResult>)[r,])),
            config: new OperationConfig<Brep, Extraction.FeatureExtractionResult> {
                Context = context,
                ValidationMode = ExtractionConfig.FeatureExtractionMetadata.ValidationMode,
                OperationName = ExtractionConfig.FeatureExtractionMetadata.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Extraction.PrimitiveDecompositionResult> ExecutePrimitiveDecomposition(GeometryBase geometry, IGeometryContext context) =>
        UnifiedOperation.Apply(
            input: geometry,
            operation: (Func<GeometryBase, Result<IReadOnlyList<Extraction.PrimitiveDecompositionResult>>>)(item =>
                ExtractionCompute.DecomposeToPrimitives(geometry: item, context: context)
                    .Map(r => (IReadOnlyList<Extraction.PrimitiveDecompositionResult>)[r,])),
            config: new OperationConfig<GeometryBase, Extraction.PrimitiveDecompositionResult> {
                Context = context,
                ValidationMode = ExtractionConfig.PrimitiveDecompositionMetadata.ValidationMode,
                OperationName = ExtractionConfig.PrimitiveDecompositionMetadata.OperationName,
            }).Map(static r => r[0]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Result<Extraction.PatternExtractionResult> ExecutePatternExtraction(GeometryBase[] geometries, IGeometryContext context) =>
        geometries.Length < ExtractionConfig.PatternMinInstances
            ? ResultFactory.Create<Extraction.PatternExtractionResult>(error: E.Geometry.NoPatternDetected.WithContext($"Need at least {ExtractionConfig.PatternMinInstances} instances"))
            : ExtractionCompute.ExtractPatterns(geometries: geometries, context: context);

    private static (GeometryBase? Normalized, bool ShouldDispose) NormalizeGeometry(GeometryBase geometry, Extraction.PointMode mode) =>
        (geometry, mode) switch {
            (Extrusion ext, Extraction.Analytical or Extraction.EdgeMidpoints or Extraction.FaceCentroids or Extraction.DivideByCount or Extraction.DivideByLength or Extraction.DirectionalExtreme) => (ext.ToBrep(splitKinkyFaces: true), true),
            (SubD subd, Extraction.Analytical or Extraction.EdgeMidpoints or Extraction.FaceCentroids or Extraction.DivideByCount or Extraction.DivideByLength or Extraction.DirectionalExtreme) => (subd.ToBrep(), true),
            _ => (geometry, false),
        };

    private static Result<IReadOnlyList<Point3d>> DispatchPointExtraction(GeometryBase geometry, Extraction.PointMode mode, IGeometryContext context) =>
        mode switch {
            Extraction.Analytical => ExtractionCompute.ExtractAnalytical(geometry: geometry),
            Extraction.Extremal => ExtractionCompute.ExtractExtremal(geometry: geometry),
            Extraction.Greville => ExtractionCompute.ExtractGreville(geometry: geometry),
            Extraction.Inflection => ExtractionCompute.ExtractInflection(geometry: geometry),
            Extraction.Quadrant => ExtractionCompute.ExtractQuadrant(geometry: geometry, tolerance: context.AbsoluteTolerance),
            Extraction.EdgeMidpoints => ExtractionCompute.ExtractEdgeMidpoints(geometry: geometry),
            Extraction.FaceCentroids => ExtractionCompute.ExtractFaceCentroids(geometry: geometry),
            Extraction.OsculatingFrames frames => ExtractionCompute.ExtractOsculatingFrames(geometry: geometry, count: frames.Count ?? ExtractionConfig.DefaultOsculatingFrameCount),
            Extraction.DivideByCount divide => ExtractionCompute.ExtractDivideByCount(geometry: geometry, count: divide.Count, includeEnds: divide.IncludeEnds),
            Extraction.DivideByLength divide => ExtractionCompute.ExtractDivideByLength(geometry: geometry, length: divide.Length, includeEnds: divide.IncludeEnds),
            Extraction.DirectionalExtreme extreme => ExtractionCompute.ExtractDirectionalExtreme(geometry: geometry, direction: extreme.Direction),
            Extraction.DiscontinuityPoints discontinuity => ExtractionCompute.ExtractDiscontinuityPoints(geometry: geometry, continuity: discontinuity.Continuity),
            _ => ResultFactory.Create<IReadOnlyList<Point3d>>(error: E.Geometry.InvalidExtraction.WithContext($"Unknown point mode: {mode.GetType().Name}")),
        };

    private static Result<IReadOnlyList<Curve>> DispatchCurveExtraction(GeometryBase geometry, Extraction.CurveMode mode, IGeometryContext context) =>
        mode switch {
            Extraction.Boundary => ExtractionCompute.ExtractBoundary(geometry: geometry),
            Extraction.IsocurveUniform uniform => geometry switch {
                Surface surface => ExtractionCompute.ExtractIsocurveUniform(surface: surface, direction: uniform.Direction),
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("IsocurveUniform requires Surface")),
            },
            Extraction.IsocurveCount isocurve => geometry switch {
                Surface surface => ExtractionCompute.ExtractIsocurveCount(surface: surface, count: isocurve.Count, direction: isocurve.Direction),
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("IsocurveCount requires Surface")),
            },
            Extraction.IsocurveParameters isocurve => geometry switch {
                Surface surface => ExtractionCompute.ExtractIsocurveParameters(surface: surface, parameters: isocurve.Parameters, direction: isocurve.Direction),
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("IsocurveParameters requires Surface")),
            },
            Extraction.FeatureEdges edges => geometry switch {
                Brep brep => ExtractionCompute.ExtractFeatureEdges(brep: brep, angleThreshold: edges.AngleThreshold ?? ExtractionConfig.DefaultFeatureEdgeAngleThreshold),
                _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext("FeatureEdges requires Brep")),
            },
            _ => ResultFactory.Create<IReadOnlyList<Curve>>(error: E.Geometry.InvalidExtraction.WithContext($"Unknown curve mode: {mode.GetType().Name}")),
        };
}
