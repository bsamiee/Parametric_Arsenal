using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing error definitions with hierarchical codes for RhinoCommon RTree operations.</summary>
public static class SpatialErrors {
    /// <summary>Query parameter and type combination validation errors.</summary>
    public static class Query {
        /// <summary>K-nearest neighbor count parameter must be positive for proximity queries.</summary>
        public static readonly SystemError InvalidK =
            new(ErrorDomain.Geometry, 4001, "K-nearest neighbor count must be positive");

        /// <summary>Distance limit parameter must be positive for proximity queries.</summary>
        public static readonly SystemError InvalidDistance =
            new(ErrorDomain.Geometry, 4002, "Distance limit must be positive");

        /// <summary>Input/query type combination not supported by spatial indexing engine.</summary>
        public static readonly SystemError UnsupportedTypeCombo =
            new(ErrorDomain.Validation, 4003, "Input and query type combination not supported");

        /// <summary>RTree proximity search returned null indicating algorithm failure.</summary>
        public static readonly SystemError ProximityFailed =
            new(ErrorDomain.Geometry, 4004, "Proximity search operation failed");
    }
}
