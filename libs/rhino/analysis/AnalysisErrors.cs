using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Analysis;

/// <summary>Analysis error definitions - aliases to E.Geometry for backward compatibility.</summary>
[Obsolete("Use E.Geometry instead", error: false)]
public static class AnalysisErrors {
    /// <summary>Operation-level errors for unsupported geometry types or dispatch failures.</summary>
    public static class Operation {
        public static readonly SystemError UnsupportedGeometry = E.Geometry.UnsupportedAnalysis;
    }

    /// <summary>Evaluation computation errors for SDK method failures during analysis.</summary>
    public static class Evaluation {
        public static readonly SystemError CurveAnalysisFailed = E.Geometry.CurveAnalysisFailed;
        public static readonly SystemError SurfaceAnalysisFailed = E.Geometry.SurfaceAnalysisFailed;
        public static readonly SystemError BrepAnalysisFailed = E.Geometry.BrepAnalysisFailed;
        public static readonly SystemError MeshAnalysisFailed = E.Geometry.MeshAnalysisFailed;
    }
}
