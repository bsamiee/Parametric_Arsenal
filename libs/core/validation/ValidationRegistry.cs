using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Validation;

/// <summary>Central validation mode registry with FrozenDictionary dispatch for rule-based validation.</summary>
public static class ValidationRegistry {
    /// <summary>Validation rule configuration tuple with properties, methods, and error code.</summary>
    public readonly struct RuleConfig(string[] properties, string[] methods, int errorCode) {
        public readonly string[] Properties = properties;
        public readonly string[] Methods = methods;
        public readonly int ErrorCode = errorCode;
    }

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

    /// <summary>Gets error code for specific validation mode flag.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetErrorCode(ValidationMode mode) =>
        _rules.TryGetValue(mode.Value, out RuleConfig config) ? config.ErrorCode : 3000;

    /// <summary>Checks if validation mode has registered rules.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasRules(ValidationMode mode) =>
        mode.Value switch {
            0 => false,
            _ => _rules.Keys.Any(key => (mode.Value & key) == key),
        };

    /// <summary>Gets all properties for validation mode with flag decomposition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string[] GetProperties(ValidationMode mode) =>
        [.. GetRules(mode).SelectMany(r => r.Properties).Distinct(),];

    /// <summary>Gets all methods for validation mode with flag decomposition.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string[] GetMethods(ValidationMode mode) =>
        [.. GetRules(mode).SelectMany(r => r.Methods).Distinct(),];
}
