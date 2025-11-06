using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Polymorphic error factory with frozen dispatch for zero-allocation error construction across all domains.</summary>
public static class ErrorFactory {
    private static readonly FrozenDictionary<int, (ErrorDomain Domain, string Message)> _registry =
        new Dictionary<int, (ErrorDomain, string)> {
            [1001] = (ErrorDomain.Results, "No value provided"),
            [1002] = (ErrorDomain.Results, "Invalid Create parameters"),
            [1003] = (ErrorDomain.Results, "Invalid validation parameters"),
            [1004] = (ErrorDomain.Results, "Invalid Lift parameters"),
            [1100] = (ErrorDomain.Results, "Cannot access value in error state or error in success state"),
            [2000] = (ErrorDomain.Geometry, "Invalid extraction method specified"),
            [2001] = (ErrorDomain.Geometry, "Insufficient parameters for extraction operation"),
            [2002] = (ErrorDomain.Geometry, "Count parameter must be positive"),
            [2003] = (ErrorDomain.Geometry, "Length parameter must be greater than zero tolerance"),
            [2004] = (ErrorDomain.Geometry, "Direction parameter required for positional extrema"),
            [2200] = (ErrorDomain.Geometry, "Intersection method not supported for geometry types"),
            [2201] = (ErrorDomain.Geometry, "Intersection computation failed"),
            [2202] = (ErrorDomain.Geometry, "Projection direction vector is invalid or zero-length"),
            [2204] = (ErrorDomain.Geometry, "Ray direction vector is invalid or zero-length"),
            [2205] = (ErrorDomain.Geometry, "Maximum hit count must be positive"),
            [2300] = (ErrorDomain.Geometry, "Geometry type not supported for analysis"),
            [2310] = (ErrorDomain.Geometry, "Curve analysis computation failed"),
            [2311] = (ErrorDomain.Geometry, "Surface analysis computation failed"),
            [2312] = (ErrorDomain.Geometry, "Brep analysis computation failed"),
            [2313] = (ErrorDomain.Geometry, "Mesh analysis computation failed"),
            [3000] = (ErrorDomain.Validation, "Geometry must be valid"),
            [3100] = (ErrorDomain.Validation, "Curve must be closed and planar for area centroid"),
            [3200] = (ErrorDomain.Validation, "Bounding box is invalid"),
            [3300] = (ErrorDomain.Validation, "Mass properties computation failed"),
            [3400] = (ErrorDomain.Validation, "Geometry has invalid topology"),
            [3500] = (ErrorDomain.Validation, "Geometry is degenerate"),
            [3600] = (ErrorDomain.Validation, "Geometry is self-intersecting"),
            [3700] = (ErrorDomain.Validation, "Mesh has non-manifold edges"),
            [3800] = (ErrorDomain.Validation, "Surface has positional discontinuity (G0)"),
            [3900] = (ErrorDomain.Validation, "Absolute tolerance must be greater than zero"),
            [3901] = (ErrorDomain.Validation, "Relative tolerance must be in range [0,1)"),
            [3902] = (ErrorDomain.Validation, "Angle tolerance must be in range (0, 2π]"),
            [3903] = (ErrorDomain.Validation, "Geometry exceeds tolerance threshold"),
            [3920] = (ErrorDomain.Validation, "Invalid unit conversion scale"),
            [4000] = (ErrorDomain.Validation, "Unsupported operation type"),
            [4001] = (ErrorDomain.Validation, "Input filtered"),
            [4002] = (ErrorDomain.Geometry, "K-nearest neighbor count must be positive"),
            [4003] = (ErrorDomain.Geometry, "Distance limit must be positive"),
            [4004] = (ErrorDomain.Geometry, "Input and query type combination not supported"),
            [4005] = (ErrorDomain.Geometry, "Proximity search operation failed"),
            [5000] = (ErrorDomain.Diagnostics, "Diagnostic capture failed"),
        }.ToFrozenDictionary();

    /// <summary>Creates error using polymorphic code-based dispatch with frozen lookup.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Create(int code, string? context = null) =>
        _registry.TryGetValue(code, out (ErrorDomain Domain, string Message) entry) switch {
            true when context is null => new(entry.Domain, code, entry.Message),
            true => new(entry.Domain, code, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{entry.Message} (Context: {context})")),
            false => new(ErrorDomain.None, code, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Unknown error code: {code}")),
        };

    /// <summary>Creates error with explicit domain override for custom error handling.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Create(ErrorDomain domain, int code, string message, string? context = null) =>
        context switch {
            null => new(domain, code, message),
            _ => new(domain, code, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{message} (Context: {context})")),
        };
}
