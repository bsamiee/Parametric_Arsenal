using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Unified analysis engine providing dense evaluation packets for Rhino geometry.</summary>
public static class AnalysisEngine {
    /// <summary>Aggregates analysis packets for arbitrary Rhino geometry inputs.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<AnalysisPacket>> Analyze<T>(
        T input,
        IGeometryContext context,
        AnalysisParameters parameters = default) where T : notnull =>
        UnifiedOperation.Apply(
            input,
            (T item) => AnalysisStrategies.Analyze(item, context, parameters),
            new OperationConfig<T, AnalysisPacket> {
                Context = context,
                ValidationMode = ValidationMode.None,
                AccumulateErrors = true,
                SkipInvalid = true,
            });
}

/// <summary>Optional analysis parameters controlling evaluation sampling.</summary>
public readonly record struct AnalysisParameters(
    double? CurveParameter = null,
    (double? U, double? V)? SurfaceParameters = null,
    int DerivativeOrder = 2,
    int? MeshElementIndex = null,
    bool IncludeGlobalMetrics = true,
    bool IncludeDomains = true,
    bool IncludeOrientation = true);

/// <summary>Dense analysis payload exposing evaluated geometry state.</summary>
public readonly record struct AnalysisPacket(
    Point3d Point,
    IReadOnlyList<Vector3d> Derivatives,
    Plane Frame,
    AnalysisCurvature? Curvature,
    AnalysisMetrics Metrics,
    IReadOnlyList<Interval> Domains,
    AnalysisOrientation Orientation,
    AnalysisParameters EvaluatedParameters);

/// <summary>Curvature data across geometry categories.</summary>
public readonly record struct AnalysisCurvature(
    double? Gaussian,
    double? Mean,
    double? Minimum,
    double? Maximum,
    Vector3d? MinimumDirection,
    Vector3d? MaximumDirection,
    Vector3d? CurveVector);

/// <summary>Metric aggregates derived from Rhino mass property solvers.</summary>
public readonly record struct AnalysisMetrics(
    double? Length,
    double? Area,
    double? Volume);

/// <summary>Orientation vectors providing tangent/normal frames.</summary>
public readonly record struct AnalysisOrientation(
    IReadOnlyList<Vector3d> TangentBasis,
    Vector3d? Normal,
    Plane? Frame);
