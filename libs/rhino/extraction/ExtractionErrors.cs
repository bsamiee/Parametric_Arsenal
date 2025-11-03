using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Extraction;

/// <summary>Point extraction operation error codes with systematic categorization and error domain mapping.</summary>
public static class ExtractionErrors {
    /// <summary>Operation-level extraction errors with standardized messaging and diagnostic codes (2100-2199).</summary>
    public static class Operation {
        /// <summary>Extraction method not supported for target geometry type.</summary>
        public static readonly SystemError InvalidMethod =
            new(ErrorDomain.Geometry, 2100, "Invalid extraction method specified");

        /// <summary>Required parameters missing or conflicting for selected extraction method.</summary>
        public static readonly SystemError InsufficientParameters =
            new(ErrorDomain.Geometry, 2101, "Insufficient parameters for extraction operation");

        /// <summary>Count parameter violates positive integer constraint for uniform extraction.</summary>
        public static readonly SystemError InvalidCount =
            new(ErrorDomain.Geometry, 2102, "Count parameter must be positive");

        /// <summary>Length parameter below tolerance threshold for uniform extraction.</summary>
        public static readonly SystemError InvalidLength =
            new(ErrorDomain.Geometry, 2103, "Length parameter must be greater than zero tolerance");
    }
}
