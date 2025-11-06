using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Arsenal.Core.Context;
using Arsenal.Core.Operations;
using Arsenal.Core.Results;
using Arsenal.Core.Validation;
using Rhino.Geometry;

namespace Arsenal.Rhino.Analysis;

/// <summary>Polymorphic analysis engine with unified operation dispatch and FrozenSet method selection.</summary>
public static class AnalysisEngine {
    /// <summary>Default comprehensive analysis method set including all available evaluations.</summary>
    private static readonly FrozenSet<string> _comprehensiveMethods = new HashSet<string>(StringComparer.Ordinal) {
        "derivatives", "frame", "curvature", "discontinuity", "topology", "proximity", "metrics", "domains",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Analyzes geometry producing evaluation data with Result monad eliminating null reference issues.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<IReadOnlyList<AnalysisData>> Analyze<T>(
        T input,
        IGeometryContext context,
        IEnumerable<string>? methods = null,
        (double? Curve, (double, double)? Surface, int? Mesh)? parameters = null,
        int derivativeOrder = 2) where T : notnull =>
        UnifiedOperation.Apply(
            input,
            (Func<object, Result<IReadOnlyList<AnalysisData>>>)(item =>
                AnalysisStrategies.Analyze(
                    item,
                    methods?.ToFrozenSet(StringComparer.Ordinal) ?? _comprehensiveMethods,
                    context,
                    parameters,
                    derivativeOrder)
                .Map(result => (IReadOnlyList<AnalysisData>)[result])),
            new OperationConfig<object, AnalysisData> {
                Context = context,
                ValidationMode = ValidationMode.None,
                AccumulateErrors = false,
                EnableCache = true,
            });
}
