using Arsenal.Core.Errors;

namespace Arsenal.Core.Diagnostics;

/// <summary>Diagnostic system errors (5000-5099).</summary>
public static class DiagnosticErrors {
    /// <summary>Diagnostic capture and retrieval errors (5000-5019).</summary>
    public static class Capture {
        public static readonly SystemError MetadataNotFound = new(ErrorDomain.Diagnostics, 5000, "Diagnostic metadata not found for Result instance");
        public static readonly SystemError ActivitySourceUnavailable = new(ErrorDomain.Diagnostics, 5001, "ActivitySource tracing unavailable in release builds");
    }

    /// <summary>Performance threshold violation errors (5020-5039).</summary>
    public static class Performance {
        public static readonly SystemError AllocationExceeded = new(ErrorDomain.Diagnostics, 5020, "Operation allocations exceeded threshold");
        public static readonly SystemError TimeoutExceeded = new(ErrorDomain.Diagnostics, 5021, "Operation elapsed time exceeded threshold");
    }
}
