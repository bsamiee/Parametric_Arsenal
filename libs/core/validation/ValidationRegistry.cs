using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Validation;

/// <summary>Central validation mode registry with FrozenDictionary dispatch for rule-based validation.</summary>
public static class ValidationRegistry {
    /// <summary>Validation rule configuration with properties, methods, and error code.</summary>
    public readonly record struct RuleConfig(string[] Properties, string[] Methods, int ErrorCode);

    private static readonly FrozenDictionary<int, RuleConfig> _rules = new Dictionary<int, RuleConfig> {
        [1] = new(["IsValid",], [], 3000),
        [2] = new(["IsClosed",], ["IsPlanar",], 3100),
        [4] = new([], ["GetBoundingBox",], 3200),
        [8] = new(["IsSolid", "IsClosed",], [], 3300),
        [16] = new(["IsManifold", "IsClosed", "IsSolid", "IsSurface",], ["IsManifold", "IsPointInside",], 3400),
        [32] = new(["IsPeriodic", "IsPolyline",], ["IsShort", "IsSingular", "IsDegenerate", "IsRectangular",], 3500),
        [64] = new([], ["IsPlanar", "IsLinear", "IsArc", "IsCircle", "IsEllipse",], 3903),
        [128] = new([], ["SelfIntersections",], 3600),
        [256] = new(["IsManifold", "IsClosed", "HasNgons", "HasVertexColors", "HasVertexNormals", "IsTriangleMesh", "IsQuadMesh",], ["IsValidWithLog",], 3700),
        [512] = new(["IsPeriodic",], ["IsContinuous",], 3800),
    }.ToFrozenDictionary();

    /// <summary>Gets rule configuration for validation mode with flag decomposition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuleConfig[] GetRules(ValidationMode mode) =>
        mode.Value switch {
            0 => [],
            1023 => [.. _rules.Values,],
            _ => [.. _rules.Where(kvp => (mode.Value & kvp.Key) == kvp.Key).Select(kvp => kvp.Value),],
        };
}
