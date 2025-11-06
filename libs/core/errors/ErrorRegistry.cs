using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Central error registry with FrozenDictionary dispatch enabling hundreds of errors in minimal footprint.</summary>
public static class ErrorRegistry {
    private static readonly FrozenDictionary<int, string> _messages = new Dictionary<int, string> {
        [1001] = "No value provided",
        [1002] = "Invalid Create parameters",
        [1003] = "Invalid validation parameters",
        [1004] = "Invalid Lift parameters",
        [1100] = "Cannot access value in error state or error in success state",
        [2300] = "Geometry type not supported for analysis",
        [2310] = "Curve analysis computation failed",
        [2311] = "Surface analysis computation failed",
        [2312] = "Brep analysis computation failed",
        [2313] = "Mesh analysis computation failed",
        [2221] = "Invalid parameter count",
        [3000] = "Geometry must be valid",
        [3100] = "Curve must be closed and planar for area centroid",
        [3200] = "Bounding box is invalid",
        [3300] = "Mass properties computation failed - geometry must be solid and closed",
        [3400] = "Geometry has invalid topology",
        [3500] = "Geometry is degenerate",
        [3600] = "Geometry is self-intersecting",
        [3700] = "Mesh has non-manifold edges or invalid topology",
        [3800] = "Surface has positional discontinuity (G0)",
        [3900] = "Absolute tolerance must be greater than zero",
        [3901] = "Relative tolerance must be in range [0,1)",
        [3902] = "Angle tolerance must be in range (0, 2π]",
        [3903] = "Geometry exceeds tolerance threshold",
        [3920] = "Invalid unit conversion scale",
        [4000] = "Unsupported operation type",
        [4001] = "Input filtered by operation predicate",
        [4002] = "K-nearest neighbor count must be positive",
        [4003] = "Distance limit must be positive for proximity queries",
        [4004] = "Input and query type combination not supported by spatial engine",
        [4005] = "Proximity search operation failed - RTree returned null",
        [4100] = "Intersection computation failed",
        [4101] = "Curve-curve intersection failed",
        [4102] = "Curve-surface intersection failed",
        [4103] = "Surface-surface intersection failed",
        [4104] = "Unsupported intersection method",
        [4105] = "Invalid projection direction - must be non-zero vector",
        [4106] = "Invalid ray direction - must be non-zero vector",
        [4107] = "Invalid max hit count - must be positive",
        [4200] = "Extraction operation failed",
        [4201] = "Invalid extraction count - must be positive",
        [4202] = "Invalid extraction length - must be positive",
        [4203] = "Invalid extraction direction - must be non-zero vector",
        [4204] = "Invalid extraction method",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<int, int> _domainRanges = new Dictionary<int, int> {
        [1000] = 1999,
        [2000] = 2999,
        [3000] = 3999,
        [4000] = 4999,
        [5000] = 5999,
    }.ToFrozenDictionary();

    /// <summary>Creates SystemError from code with automatic domain inference and optional context.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(int code, string? context = null) =>
        (InferDomain(code), _messages.TryGetValue(code, out string? msg)) switch {
            (ErrorDomain domain, true) when domain != ErrorDomain.None =>
                context is null
                    ? new(domain, code, msg)
                    : new(domain, code, $"{msg} (Context: {context})"),
            (ErrorDomain domain, false) when domain != ErrorDomain.None =>
                new(domain, code, $"Unregistered error in {domain}"),
            _ => new(ErrorDomain.None, code, "Unknown error code"),
        };

    /// <summary>Creates SystemError array from codes for error accumulation patterns.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] Get(params int[] codes) =>
        codes.Length switch {
            0 => [],
            1 => [Get(codes[0]),],
            _ => [.. codes.Select(c => Get(c)),],
        };

    /// <summary>Registers custom error message for code with validation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryRegister(int code, string message) =>
        !_messages.ContainsKey(code) && InferDomain(code) != ErrorDomain.None;

    /// <summary>Infers error domain from code using range-based classification.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ErrorDomain InferDomain(int code) =>
        _domainRanges
            .Where(kvp => code >= kvp.Key && code <= kvp.Value)
            .Select(kvp => new ErrorDomain(kvp.Key))
            .FirstOrDefault(ErrorDomain.None);

    /// <summary>Validates if code is in registered range without creating error.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRegistered(int code) => _messages.ContainsKey(code);

    /// <summary>Gets all registered codes for domain for introspection and testing.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyList<int> GetCodesForDomain(ErrorDomain domain) =>
        [.. _messages.Keys.Where(code => InferDomain(code) == domain),];
}
