using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing error definitions with hierarchical codes for RhinoCommon RTree operations.</summary>
public static class SpatialErrors {
    /// <summary>Parameter validation errors for spatial operations.</summary>
    public static class Parameters {
        /// <summary>K-nearest neighbor count parameter must be positive for proximity queries.</summary>
        public static readonly SystemError InvalidCount =
            new(ErrorDomain.Geometry, 2221, "Invalid count parameter for proximity query");

        /// <summary>Distance limit parameter must be positive for spatial range queries.</summary>
        public static readonly SystemError InvalidDistance =
            new(ErrorDomain.Geometry, 2222, "Invalid distance parameter for spatial query");

        /// <summary>Needle points collection required for proximity search operations.</summary>
        public static readonly SystemError InvalidNeedles =
            new(ErrorDomain.Geometry, 2224, "Invalid needle points for proximity query");
    }
}
