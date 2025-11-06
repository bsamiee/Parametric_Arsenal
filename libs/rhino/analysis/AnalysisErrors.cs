using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Analysis;

/// <summary>Analysis error definitions organized by failure category.</summary>
public static class AnalysisErrors {
    /// <summary>Operation-level errors for unsupported geometry types or dispatch failures.</summary>
    public static class Operation {
        public static readonly SystemError UnsupportedGeometry =
            new(ErrorDomain.Geometry, 2300, "Geometry type not supported for analysis");
    }

    /// <summary>Evaluation computation errors for SDK method failures during analysis.</summary>
    public static class Evaluation {
        public static readonly SystemError CurveAnalysisFailed =
            new(ErrorDomain.Geometry, 2310, "Curve analysis computation failed");

        public static readonly SystemError SurfaceAnalysisFailed =
            new(ErrorDomain.Geometry, 2311, "Surface analysis computation failed");

        public static readonly SystemError BrepAnalysisFailed =
            new(ErrorDomain.Geometry, 2312, "Brep analysis computation failed");

        public static readonly SystemError MeshAnalysisFailed =
            new(ErrorDomain.Geometry, 2313, "Mesh analysis computation failed");
    }
}
