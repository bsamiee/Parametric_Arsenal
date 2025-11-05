using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Analysis;

/// <summary>Analysis error definitions providing structured diagnostics for Rhino evaluations.</summary>
public static class AnalysisErrors {
    /// <summary>Parameter validation errors for analysis operations.</summary>
    public static class Parameters {
        /// <summary>Curve parameter must be supplied or fall within domain for derivative evaluations.</summary>
        public static readonly SystemError MissingCurveParameter =
            new(ErrorDomain.Geometry, 2300, "Curve parameter is required for analysis");

        /// <summary>Derivative order must be non-negative and supported by the geometry type.</summary>
        public static readonly SystemError InvalidDerivativeOrder =
            new(ErrorDomain.Geometry, 2301, "Invalid derivative order for analysis");

        /// <summary>Mesh element index must be valid when analyzing vertex-based data.</summary>
        public static readonly SystemError InvalidMeshElement =
            new(ErrorDomain.Geometry, 2302, "Invalid mesh element index for analysis");

        /// <summary>Supplied parameters must lie within the geometry domain.</summary>
        public static readonly SystemError ParameterOutOfDomain =
            new(ErrorDomain.Geometry, 2303, "Analysis parameters out of domain");
    }

    /// <summary>Operation-level errors for unsupported geometry or metrics.</summary>
    public static class Operation {
        /// <summary>Geometry type is not supported by the analysis strategies.</summary>
        public static readonly SystemError UnsupportedGeometry =
            new(ErrorDomain.Geometry, 2310, "Unsupported geometry type for analysis");
    }

    /// <summary>Evaluation computation errors for derivative and surface operations.</summary>
    public static class Evaluation {
        /// <summary>Derivative computation failed at specified parameter.</summary>
        public static readonly SystemError DerivativeComputationFailed =
            new(ErrorDomain.Geometry, 2311, "Derivative evaluation failed");

        /// <summary>Surface.Evaluate() method failed to compute derivatives.</summary>
        public static readonly SystemError SurfaceEvaluationFailed =
            new(ErrorDomain.Geometry, 2312, "Surface.Evaluate() failed");
    }

    /// <summary>Discontinuity detection and continuity testing errors.</summary>
    public static class Discontinuity {
        /// <summary>No discontinuities detected in specified domain range.</summary>
        public static readonly SystemError NoneFound =
            new(ErrorDomain.Geometry, 2320, "No discontinuities detected in domain");

        /// <summary>Continuity test failed at parameter value.</summary>
        public static readonly SystemError TestFailed =
            new(ErrorDomain.Geometry, 2321, "Continuity test failed");
    }

    /// <summary>Topology navigation and connectivity errors.</summary>
    public static class Topology {
        /// <summary>Geometry lacks topology information for analysis.</summary>
        public static readonly SystemError NoTopologyData =
            new(ErrorDomain.Geometry, 2330, "Geometry lacks topology information");

        /// <summary>Topology edge enumeration or access failed.</summary>
        public static readonly SystemError EdgeAccessFailed =
            new(ErrorDomain.Geometry, 2331, "Topology edge enumeration failed");

        /// <summary>Topology vertex access or unified vertex computation failed.</summary>
        public static readonly SystemError VertexAccessFailed =
            new(ErrorDomain.Geometry, 2332, "Topology vertex access failed");
    }

    /// <summary>Proximity and closest point computation errors.</summary>
    public static class Proximity {
        /// <summary>Closest point computation failed or returned invalid result.</summary>
        public static readonly SystemError ClosestPointFailed =
            new(ErrorDomain.Geometry, 2340, "Closest point computation failed");

        /// <summary>Component identification failed for Brep closest point.</summary>
        public static readonly SystemError ComponentIdentificationFailed =
            new(ErrorDomain.Geometry, 2341, "Component identification failed");
    }

    /// <summary>Singularity and seam detection errors.</summary>
    public static class Singularity {
        /// <summary>Singularity detection failed at parameter location.</summary>
        public static readonly SystemError DetectionFailed =
            new(ErrorDomain.Geometry, 2350, "Singularity detection failed");

        /// <summary>Seam identification or location computation failed.</summary>
        public static readonly SystemError SeamIdentificationFailed =
            new(ErrorDomain.Geometry, 2351, "Seam identification failed");
    }
}
