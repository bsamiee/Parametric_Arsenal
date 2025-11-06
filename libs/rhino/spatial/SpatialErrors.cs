using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial analysis error definitions with hierarchical codes for RhinoCommon RTree operations.</summary>
public static class SpatialErrors {
    /// <summary>Parameter validation errors for spatial operations.</summary>
    public static class Parameters {
        /// <summary>K-nearest neighbor count parameter must be positive for proximity queries.</summary>
        public static readonly SystemError InvalidCount =
            new(ErrorDomain.Geometry, 2100, "K-nearest neighbor count must be positive");

        /// <summary>Distance limit parameter must be positive for spatial range queries.</summary>
        public static readonly SystemError InvalidDistance =
            new(ErrorDomain.Geometry, 2101, "Distance limit must be positive for spatial query");

        /// <summary>Unsupported source and query type combination for spatial analysis.</summary>
        public static readonly SystemError UnsupportedOperation =
            new(ErrorDomain.Geometry, 2102, "Unsupported spatial operation for given source and query types");
    }
}
