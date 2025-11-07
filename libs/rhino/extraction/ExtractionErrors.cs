using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Extraction;

/// <summary>Error codes for point extraction operations - aliases to E.Geometry for backward compatibility.</summary>
[Obsolete("Use E.Geometry instead", error: false)]
public static class ExtractionErrors {
    /// <summary>Operation-level extraction errors for geometry processing failures.</summary>
    public static class Operation {
        public static readonly SystemError InvalidMethod = E.Geometry.InvalidExtraction;
        public static readonly SystemError InsufficientParameters = E.Geometry.InsufficientParameters;
        public static readonly SystemError InvalidCount = E.Geometry.InvalidCount;
        public static readonly SystemError InvalidLength = E.Geometry.InvalidLength;
        public static readonly SystemError InvalidDirection = E.Geometry.InvalidDirection;
    }
}
