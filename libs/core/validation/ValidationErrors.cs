using Arsenal.Core.Errors;

namespace Arsenal.Core.Validation;

/// <summary>Hierarchical validation error factory with consistent error codes - aliases for backward compatibility.</summary>
[Obsolete("Use ErrorFactory.Validation instead", error: false)]
public static class ValidationErrors {
    /// <summary>Geometry validation errors (3000-3999).</summary>
    public static class Geometry {
        public static readonly SystemError Invalid = ErrorFactory.Validation.GeometryInvalid();

        /// <summary>Curve validation errors (3100-3199).</summary>
        public static class Curve {
            public static readonly SystemError NotClosedOrPlanar = ErrorFactory.Validation.CurveNotClosedOrPlanar();
        }

        /// <summary>Bounding box validation errors (3200-3299).</summary>
        public static class BoundingBox {
            public static readonly SystemError Invalid = ErrorFactory.Validation.BoundingBoxInvalid();
        }

        /// <summary>Mass properties validation errors (3300-3399).</summary>
        public static class Properties {
            public static readonly SystemError ComputationFailed = ErrorFactory.Validation.MassPropertiesComputationFailed();
        }

        /// <summary>Topology validation errors (3400-3499).</summary>
        public static class Topology {
            public static readonly SystemError InvalidTopology = ErrorFactory.Validation.InvalidTopology();
        }

        /// <summary>Degeneracy validation errors (3500-3599).</summary>
        public static class Degeneracy {
            public static readonly SystemError DegenerateGeometry = ErrorFactory.Validation.DegenerateGeometry();
        }

        /// <summary>Self-intersection validation errors (3600-3699).</summary>
        public static class SelfIntersection {
            public static readonly SystemError SelfIntersecting = ErrorFactory.Validation.SelfIntersecting();
        }

        /// <summary>Mesh-specific validation errors (3700-3799).</summary>
        public static class MeshTopology {
            public static readonly SystemError NonManifoldEdges = ErrorFactory.Validation.NonManifoldEdges();
        }

        /// <summary>Surface continuity validation errors (3800-3899).</summary>
        public static class Continuity {
            public static readonly SystemError PositionalDiscontinuity = ErrorFactory.Validation.PositionalDiscontinuity();
        }
    }

    /// <summary>Context validation errors (3900-3999).</summary>
    public static class Context {
        /// <summary>Tolerance validation errors (3900-3919).</summary>
        public static class Tolerance {
            public static readonly SystemError InvalidAbsolute = ErrorFactory.Validation.InvalidAbsoluteTolerance();
            public static readonly SystemError InvalidRelative = ErrorFactory.Validation.InvalidRelativeTolerance();
            public static readonly SystemError InvalidAngle = ErrorFactory.Validation.InvalidAngleTolerance();
            public static readonly SystemError ToleranceExceeded = ErrorFactory.Validation.ToleranceExceeded();
        }
        public static readonly SystemError InvalidUnitConversion = ErrorFactory.Validation.InvalidUnitConversion();
    }

    /// <summary>Operation validation errors (4000-4099).</summary>
    public static class Operations {
        public static readonly SystemError UnsupportedOperationType = ErrorFactory.Validation.UnsupportedOperationType();
        public static readonly SystemError InputFiltered = ErrorFactory.Validation.InputFiltered();
    }
}
