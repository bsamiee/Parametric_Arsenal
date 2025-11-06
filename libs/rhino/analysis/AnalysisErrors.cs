using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Analysis;

/// <summary>Analysis error definitions organized by failure category - aliases for backward compatibility.</summary>
[Obsolete("Use ErrorFactory.Analysis instead", error: false)]
public static class AnalysisErrors {
    /// <summary>Operation-level errors for unsupported geometry types or dispatch failures.</summary>
    public static class Operation {
        public static readonly SystemError UnsupportedGeometry = ErrorFactory.Analysis.UnsupportedGeometry();
    }

    /// <summary>Evaluation computation errors for SDK method failures during analysis.</summary>
    public static class Evaluation {
        public static readonly SystemError CurveAnalysisFailed = ErrorFactory.Analysis.CurveAnalysisFailed();
        public static readonly SystemError SurfaceAnalysisFailed = ErrorFactory.Analysis.SurfaceAnalysisFailed();
        public static readonly SystemError BrepAnalysisFailed = ErrorFactory.Analysis.BrepAnalysisFailed();
        public static readonly SystemError MeshAnalysisFailed = ErrorFactory.Analysis.MeshAnalysisFailed();
    }
}
