using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;

namespace Arsenal.Rhino.Topology;

/// <summary>Algebraic base type for topology healing strategies with nested concrete implementations and diagnosis result.</summary>
[Pure]
public abstract record TopologyHealingStrategy {
    /// <summary>Conservative repair with minimal tolerance (0.1× context tolerance).</summary>
    [Pure]
    public sealed record ConservativeRepair : TopologyHealingStrategy;

    /// <summary>Moderate join of naked edges with standard tolerance (1.0× context tolerance).</summary>
    [Pure]
    public sealed record ModerateJoin : TopologyHealingStrategy;

    /// <summary>Aggressive join with high tolerance (10.0× context tolerance).</summary>
    [Pure]
    public sealed record AggressiveJoin : TopologyHealingStrategy;

    /// <summary>Combined conservative repair followed by moderate join.</summary>
    [Pure]
    public sealed record CombinedRepairAndJoin : TopologyHealingStrategy;

    /// <summary>Targeted join based on near-miss edge pair analysis.</summary>
    [Pure]
    public sealed record TargetedJoin : TopologyHealingStrategy;

    /// <summary>Component join for disconnected brep components.</summary>
    [Pure]
    public sealed record ComponentJoin : TopologyHealingStrategy;

    /// <summary>Topology diagnosis result with edge gaps, near-misses, and suggested repair strategies.</summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    [Pure]
    public sealed record Diagnosis(
        double[] EdgeGaps,
        (int EdgeA, int EdgeB, double Distance)[] NearMisses,
        TopologyHealingStrategy[] SuggestedStrategies) {
        [Pure]
        private string DebuggerDisplay => string.Create(
            CultureInfo.InvariantCulture,
            $"Diagnosis: Gaps={this.EdgeGaps.Length} | NearMisses={this.NearMisses.Length} | Strategies={this.SuggestedStrategies.Length}");
    }
}
