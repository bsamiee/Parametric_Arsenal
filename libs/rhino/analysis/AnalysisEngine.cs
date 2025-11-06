using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Results;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic analysis engine with unified operation dispatch and type-based method selection.</summary>
public static class AnalysisEngine {
    /// <summary>Default comprehensive analysis methods using readonly struct collection for zero allocation.</summary>
    private static readonly FrozenSet<AnalysisMethod> _comprehensiveMethods = new HashSet<AnalysisMethod> {
        AnalysisMethod.Derivatives, AnalysisMethod.Frame, AnalysisMethod.Curvature,
        AnalysisMethod.Discontinuity, AnalysisMethod.Topology, AnalysisMethod.Proximity,
        AnalysisMethod.Metrics, AnalysisMethod.Domains,
    }.ToFrozenSet();

    /// <summary>Analyzes geometry producing evaluation data with Result monad eliminating null reference issues.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<AnalysisData> Analyze<T>(
        T input,
        IGeometryContext context,
        IEnumerable<AnalysisMethod>? methods = null,
        (double? Curve, (double, double)? Surface, int? Mesh)? parameters = null,
        int derivativeOrder = 2) where T : notnull =>
        AnalysisStrategies.Analyze(
            input,
            methods?.ToFrozenSet() ?? _comprehensiveMethods,
            context,
            parameters,
            derivativeOrder);
}
