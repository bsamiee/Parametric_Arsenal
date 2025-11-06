using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing error definitions with hierarchical codes for RhinoCommon RTree operations - aliases for backward compatibility.</summary>
[Obsolete("Use ErrorFactory.Spatial instead", error: false)]
public static class SpatialErrors {
    /// <summary>Query parameter and type combination validation errors.</summary>
    public static class Query {
        /// <summary>K-nearest neighbor count parameter must be positive for proximity queries.</summary>
        public static readonly SystemError InvalidK = ErrorFactory.Spatial.InvalidK();

        /// <summary>Distance limit parameter must be positive for proximity queries.</summary>
        public static readonly SystemError InvalidDistance = ErrorFactory.Spatial.InvalidDistance();

        /// <summary>Input/query type combination not supported by spatial indexing engine.</summary>
        public static readonly SystemError UnsupportedTypeCombo = ErrorFactory.Spatial.UnsupportedTypeCombo();

        /// <summary>RTree proximity search returned null indicating algorithm failure.</summary>
        public static readonly SystemError ProximityFailed = ErrorFactory.Spatial.ProximityFailed();
    }
}
