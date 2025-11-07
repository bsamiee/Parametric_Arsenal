namespace Arsenal.Rhino.Intersection;

/// <summary>Configuration constants for intersection operations.</summary>
internal static class IntersectionConfig {
    /// <summary>Default tolerance multiplier for intersection operations when tolerance is not explicitly provided.</summary>
    internal const double DefaultToleranceMultiplier = 1.0;

    /// <summary>Maximum intersection results before truncation in batch operations.</summary>
    internal const int MaxIntersectionResults = 10000;
}
