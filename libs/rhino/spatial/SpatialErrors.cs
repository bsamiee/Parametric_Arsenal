using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Spatial;

/// <summary>Spatial indexing error definitions - aliases to E.Spatial for backward compatibility during transition.</summary>
public static class SpatialErrors {
    /// <summary>Query parameter and type combination validation errors.</summary>
    public static class Query {
        public static readonly SystemError InvalidK = E.Spatial.InvalidK;
        public static readonly SystemError InvalidDistance = E.Spatial.InvalidDistance;
        public static readonly SystemError UnsupportedTypeCombo = E.Validation.UnsupportedTypeCombo;
        public static readonly SystemError ProximityFailed = E.Spatial.ProximityFailed;
    }
}
