using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Central error registry with FrozenDictionary dispatch and polymorphic creation.</summary>
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

    /// <summary>Creates SystemError with automatic domain inference and polymorphic context.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(int code, string? context = null, ErrorDomain? domain = null, string? message = null) =>
        (domain ?? InferDomain(code), message ?? (_messages.TryGetValue(code, out string? msg) ? msg : null), context) switch {
            ({ } d, { } m, null) when d != ErrorDomain.None => new(d, code, m),
            ({ } d, { } m, { } c) when d != ErrorDomain.None => new(d, code, $"{m} (Context: {c})"),
            ({ } d, null, _) when d != ErrorDomain.None => new(d, code, $"Unregistered error in {d}"),
            _ => new(ErrorDomain.None, code, "Unknown error code"),
        };

    /// <summary>Creates SystemError array from codes with optional context tuples.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] Get(params int[] codes) => [.. codes.Select(Get),];

    /// <summary>Creates SystemError array from code/context tuples for batch operations.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] Get(params (int Code, string? Context)[] errors) =>
        [.. errors.Select(e => Get(e.Code, e.Context)),];

    /// <summary>Creates conditional error for validation chains.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] When(bool condition, int code, string? context = null) =>
        condition ? [Get(code, context),] : [];

    /// <summary>Creates conditional error with predicate dispatch.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError[] When<T>(T value, Func<T, bool> predicate, int code, string? context = null) =>
        predicate(value) ? [Get(code, context),] : [];

    /// <summary>Wraps exception as SystemError for error chaining.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError FromException(Exception exception, int fallbackCode = 1100) =>
        Get(fallbackCode, $"{exception.GetType().Name}: {exception.Message}");

    /// <summary>Infers error domain from code using dense pattern matching.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ErrorDomain InferDomain(int code) =>
        code switch {
            >= 1000 and < 2000 => ErrorDomain.Results,
            >= 2000 and < 3000 => ErrorDomain.Geometry,
            >= 3000 and < 4000 => ErrorDomain.Validation,
            >= 4000 and < 5000 => ErrorDomain.Operations,
            >= 5000 and < 6000 => ErrorDomain.Diagnostics,
            _ => ErrorDomain.None,
        };

    /// <summary>Validates if code is registered without creating error.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRegistered(int code) => _messages.ContainsKey(code);

    /// <summary>Gets all registered codes for domain for introspection.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyList<int> GetCodesForDomain(ErrorDomain domain) =>
        [.. _messages.Keys.Where(code => InferDomain(code) == domain),];
}
