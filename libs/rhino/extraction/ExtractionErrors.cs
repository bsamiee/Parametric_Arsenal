using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Extraction;

/// <summary>Error codes for point extraction operations with Rhino geometry integration.</summary>
public static class ExtractionErrors {
    /// <summary>Operation-level extraction errors for geometry processing failures.</summary>
    public static class Operation {
        /// <summary>Extraction method not supported for target geometry type.</summary>
        public static readonly SystemError InvalidMethod =
            new(ErrorDomain.Geometry, 2000, "Invalid extraction method specified");

        /// <summary>Required parameters missing for selected extraction method.</summary>
        public static readonly SystemError InsufficientParameters =
            new(ErrorDomain.Geometry, 2001, "Insufficient parameters for extraction operation");

        /// <summary>Count parameter must be positive for uniform extraction.</summary>
        public static readonly SystemError InvalidCount =
            new(ErrorDomain.Geometry, 2002, "Count parameter must be positive");

        /// <summary>Length parameter must exceed tolerance for uniform extraction.</summary>
        public static readonly SystemError InvalidLength =
            new(ErrorDomain.Geometry, 2003, "Length parameter must be greater than zero tolerance");

        /// <summary>Direction parameter required for positional extrema extraction.</summary>
        public static readonly SystemError InvalidDirection =
            new(ErrorDomain.Geometry, 2004, "Direction parameter required for positional extrema");
    }
}
