using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing error definitions with hierarchical codes (2220-2229).</summary>
public static class SpatialErrors {
    /// <summary>Parameter validation errors for spatial operations (2220-2229).</summary>
    public static class Parameters {
        /// <summary>Count parameter (k) is negative or zero for proximity queries.</summary>
        public static readonly SystemError InvalidCount =
            new(ErrorDomain.Geometry, 2221, "Invalid count parameter for proximity query");

        /// <summary>Distance limit parameter is negative for range queries.</summary>
        public static readonly SystemError InvalidDistance =
            new(ErrorDomain.Geometry, 2222, "Invalid distance parameter for spatial query");

        /// <summary>Needle points collection is null or empty for proximity queries.</summary>
        public static readonly SystemError InvalidNeedles =
            new(ErrorDomain.Geometry, 2224, "Invalid needle points for proximity query");
    }
}
