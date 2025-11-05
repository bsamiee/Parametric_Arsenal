using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Extraction;

/// <summary>Error codes for point extraction operations with Rhino geometry integration.</summary>
public static class ExtractionErrors {
    /// <summary>Operation-level extraction errors for geometry processing failures.</summary>
    public static class Operation {
        /// <summary>Extraction method not supported for target geometry type.</summary>
        public static readonly SystemError InvalidMethod =
            new(ErrorDomain.Geometry, 2100, "Invalid extraction method specified");

        /// <summary>Required parameters missing for selected extraction method.</summary>
        public static readonly SystemError InsufficientParameters =
            new(ErrorDomain.Geometry, 2101, "Insufficient parameters for extraction operation");

        /// <summary>Count parameter must be positive for uniform extraction.</summary>
        public static readonly SystemError InvalidCount =
            new(ErrorDomain.Geometry, 2102, "Count parameter must be positive");

        /// <summary>Length parameter must exceed tolerance for uniform extraction.</summary>
        public static readonly SystemError InvalidLength =
            new(ErrorDomain.Geometry, 2103, "Length parameter must be greater than zero tolerance");
    }
}
