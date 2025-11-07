using Arsenal.Core.Errors;

namespace Arsenal.Core.Validation;

/// <summary>Hierarchical validation error factory - aliases to E.Validation for backward compatibility.</summary>
[Obsolete("Use E.Validation instead", error: false)]
public static class ValidationErrors {
    /// <summary>Geometry validation errors (3000-3999).</summary>
    public static class Geometry {
        public static readonly SystemError Invalid = E.Validation.GeometryInvalid;

        /// <summary>Curve validation errors (3100-3199).</summary>
        public static class Curve {
            public static readonly SystemError NotClosedOrPlanar = E.Validation.CurveNotClosedOrPlanar;
        }

        /// <summary>Bounding box validation errors (3200-3299).</summary>
        public static class BoundingBox {
            public static readonly SystemError Invalid = E.Validation.BoundingBoxInvalid;
        }

        /// <summary>Mass properties validation errors (3300-3399).</summary>
        public static class Properties {
            public static readonly SystemError ComputationFailed = E.Validation.MassPropertiesComputationFailed;
        }

        /// <summary>Topology validation errors (3400-3499).</summary>
        public static class Topology {
            public static readonly SystemError InvalidTopology = E.Validation.InvalidTopology;
        }

        /// <summary>Degeneracy validation errors (3500-3599).</summary>
        public static class Degeneracy {
            public static readonly SystemError DegenerateGeometry = E.Validation.DegenerateGeometry;
        }

        /// <summary>Self-intersection validation errors (3600-3699).</summary>
        public static class SelfIntersection {
            public static readonly SystemError SelfIntersecting = E.Validation.SelfIntersecting;
        }

        /// <summary>Mesh-specific validation errors (3700-3799).</summary>
        public static class MeshTopology {
            public static readonly SystemError NonManifoldEdges = E.Validation.NonManifoldEdges;
        }

        /// <summary>Surface continuity validation errors (3800-3899).</summary>
        public static class Continuity {
            public static readonly SystemError PositionalDiscontinuity = E.Validation.PositionalDiscontinuity;
        }
    }

    /// <summary>Context validation errors (3900-3999).</summary>
    public static class Context {
        /// <summary>Tolerance validation errors (3900-3919).</summary>
        public static class Tolerance {
            public static readonly SystemError InvalidAbsolute = E.Validation.ToleranceAbsoluteInvalid;
            public static readonly SystemError InvalidRelative = E.Validation.ToleranceRelativeInvalid;
            public static readonly SystemError InvalidAngle = E.Validation.ToleranceAngleInvalid;
            public static readonly SystemError ToleranceExceeded = E.Validation.ToleranceExceeded;
        }
        public static readonly SystemError InvalidUnitConversion = E.Validation.InvalidUnitConversion;
    }

    /// <summary>Operation validation errors (4000-4099).</summary>
    public static class Operations {
        public static readonly SystemError UnsupportedOperationType = E.Validation.UnsupportedOperationType;
        public static readonly SystemError InputFiltered = E.Validation.InputFiltered;
    }
}
