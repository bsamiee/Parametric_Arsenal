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
}
