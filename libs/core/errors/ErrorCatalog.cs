using System.Collections.Frozen;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Arsenal.Core.Errors;

/// <summary>Centralized frozen error catalog with O(1) lookup for all system errors via domain-code tuple dispatch.</summary>
public static class ErrorCatalog {
    private static readonly FrozenDictionary<(ErrorDomain Domain, int Code), SystemError> _errors =
        new Dictionary<(ErrorDomain, int), SystemError> {
            [(ErrorDomain.Results, 1001)] = new(ErrorDomain.Results, 1001, "No value provided"),
            [(ErrorDomain.Results, 1002)] = new(ErrorDomain.Results, 1002, "Invalid Create parameters"),
            [(ErrorDomain.Results, 1003)] = new(ErrorDomain.Results, 1003, "Invalid validation parameters"),
            [(ErrorDomain.Results, 1004)] = new(ErrorDomain.Results, 1004, "Invalid Lift parameters"),
            [(ErrorDomain.Results, 1100)] = new(ErrorDomain.Results, 1100, "Cannot access value in error state or error in success state"),
            [(ErrorDomain.Geometry, 2000)] = new(ErrorDomain.Geometry, 2000, "Invalid extraction method specified"),
            [(ErrorDomain.Geometry, 2001)] = new(ErrorDomain.Geometry, 2001, "Insufficient parameters for extraction operation"),
            [(ErrorDomain.Geometry, 2002)] = new(ErrorDomain.Geometry, 2002, "Count parameter must be positive"),
            [(ErrorDomain.Geometry, 2003)] = new(ErrorDomain.Geometry, 2003, "Length parameter must be greater than zero tolerance"),
            [(ErrorDomain.Geometry, 2004)] = new(ErrorDomain.Geometry, 2004, "Direction parameter required for positional extrema"),
            [(ErrorDomain.Geometry, 2200)] = new(ErrorDomain.Geometry, 2200, "Intersection method not supported for geometry types"),
            [(ErrorDomain.Geometry, 2201)] = new(ErrorDomain.Geometry, 2201, "Intersection computation failed"),
            [(ErrorDomain.Geometry, 2202)] = new(ErrorDomain.Geometry, 2202, "Projection direction vector is invalid or zero-length"),
            [(ErrorDomain.Geometry, 2204)] = new(ErrorDomain.Geometry, 2204, "Ray direction vector is invalid or zero-length"),
            [(ErrorDomain.Geometry, 2205)] = new(ErrorDomain.Geometry, 2205, "Maximum hit count must be positive"),
            [(ErrorDomain.Geometry, 2300)] = new(ErrorDomain.Geometry, 2300, "Geometry type not supported for analysis"),
            [(ErrorDomain.Geometry, 2310)] = new(ErrorDomain.Geometry, 2310, "Curve analysis computation failed"),
            [(ErrorDomain.Geometry, 2311)] = new(ErrorDomain.Geometry, 2311, "Surface analysis computation failed"),
            [(ErrorDomain.Geometry, 2312)] = new(ErrorDomain.Geometry, 2312, "Brep analysis computation failed"),
            [(ErrorDomain.Geometry, 2313)] = new(ErrorDomain.Geometry, 2313, "Mesh analysis computation failed"),
            [(ErrorDomain.Geometry, 4001)] = new(ErrorDomain.Geometry, 4001, "K-nearest neighbor count must be positive"),
            [(ErrorDomain.Geometry, 4002)] = new(ErrorDomain.Geometry, 4002, "Distance limit must be positive"),
            [(ErrorDomain.Validation, 3000)] = new(ErrorDomain.Validation, 3000, "Geometry must be valid"),
            [(ErrorDomain.Validation, 3100)] = new(ErrorDomain.Validation, 3100, "Curve must be closed and planar for area centroid"),
            [(ErrorDomain.Validation, 3200)] = new(ErrorDomain.Validation, 3200, "Bounding box is invalid"),
            [(ErrorDomain.Validation, 3300)] = new(ErrorDomain.Validation, 3300, "Mass properties computation failed"),
            [(ErrorDomain.Validation, 3400)] = new(ErrorDomain.Validation, 3400, "Geometry has invalid topology"),
            [(ErrorDomain.Validation, 3500)] = new(ErrorDomain.Validation, 3500, "Geometry is degenerate"),
            [(ErrorDomain.Validation, 3600)] = new(ErrorDomain.Validation, 3600, "Geometry is self-intersecting"),
            [(ErrorDomain.Validation, 3700)] = new(ErrorDomain.Validation, 3700, "Mesh has non-manifold edges"),
            [(ErrorDomain.Validation, 3800)] = new(ErrorDomain.Validation, 3800, "Surface has positional discontinuity (G0)"),
            [(ErrorDomain.Validation, 3900)] = new(ErrorDomain.Validation, 3900, "Absolute tolerance must be greater than zero"),
            [(ErrorDomain.Validation, 3901)] = new(ErrorDomain.Validation, 3901, "Relative tolerance must be in range [0,1)"),
            [(ErrorDomain.Validation, 3902)] = new(ErrorDomain.Validation, 3902, "Angle tolerance must be in range (0, 2π]"),
            [(ErrorDomain.Validation, 3903)] = new(ErrorDomain.Validation, 3903, "Geometry exceeds tolerance threshold"),
            [(ErrorDomain.Validation, 3920)] = new(ErrorDomain.Validation, 3920, "Invalid unit conversion scale"),
            [(ErrorDomain.Validation, 3930)] = new(ErrorDomain.Validation, 3930, "Unsupported operation type"),
            [(ErrorDomain.Validation, 3931)] = new(ErrorDomain.Validation, 3931, "Input filtered"),
            [(ErrorDomain.Validation, 4003)] = new(ErrorDomain.Validation, 4003, "Input and query type combination not supported"),
            [(ErrorDomain.Geometry, 4004)] = new(ErrorDomain.Geometry, 4004, "Proximity search operation failed"),
        }.ToFrozenDictionary();

    /// <summary>Retrieves error from catalog using domain-code tuple lookup with aggressive inlining.</summary>
    [Pure, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SystemError Get(ErrorDomain domain, int code) =>
        _errors.TryGetValue((domain, code), out SystemError error) switch {
            true => error,
            false => new SystemError(ErrorDomain.None, 0, string.Create(CultureInfo.InvariantCulture, $"Unknown error: {domain}.{code.ToString(CultureInfo.InvariantCulture)}")),
        };
}
