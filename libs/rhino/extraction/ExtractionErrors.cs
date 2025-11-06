using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Extraction;

/// <summary>Error codes for point extraction operations with Rhino geometry integration - aliases for backward compatibility.</summary>
[Obsolete("Use ErrorFactory.Extraction instead", error: false)]
public static class ExtractionErrors {
    /// <summary>Operation-level extraction errors for geometry processing failures.</summary>
    public static class Operation {
        /// <summary>Extraction method not supported for target geometry type.</summary>
        public static readonly SystemError InvalidMethod = ErrorFactory.Extraction.InvalidMethod();

        /// <summary>Required parameters missing for selected extraction method.</summary>
        public static readonly SystemError InsufficientParameters = ErrorFactory.Extraction.InsufficientParameters();

        /// <summary>Count parameter must be positive for uniform extraction.</summary>
        public static readonly SystemError InvalidCount = ErrorFactory.Extraction.InvalidCount();

        /// <summary>Length parameter must exceed tolerance for uniform extraction.</summary>
        public static readonly SystemError InvalidLength = ErrorFactory.Extraction.InvalidLength();

        /// <summary>Direction parameter required for positional extrema extraction.</summary>
        public static readonly SystemError InvalidDirection = ErrorFactory.Extraction.InvalidDirection();
    }
}
