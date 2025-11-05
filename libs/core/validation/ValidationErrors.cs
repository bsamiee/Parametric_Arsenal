using Arsenal.Core.Errors;

namespace Arsenal.Core.Validation;

/// <summary>Hierarchical validation error factory with consistent error codes.</summary>
public static class ValidationErrors {
    /// <summary>Geometry validation errors (3000-3999).</summary>
    public static class Geometry {
        public static readonly SystemError Invalid = new(ErrorDomain.Validation, 3000, "Geometry must be valid");

        /// <summary>Curve validation errors (3100-3199).</summary>
        public static class Curve {
            public static readonly SystemError NotClosedOrPlanar = new(ErrorDomain.Validation, 3100, "Curve must be closed and planar for area centroid");
        }

        /// <summary>Bounding box validation errors (3200-3299).</summary>
        public static class BoundingBox {
            public static readonly SystemError Invalid = new(ErrorDomain.Validation, 3200, "Bounding box is invalid");
        }

        /// <summary>Mass properties validation errors (3300-3399).</summary>
        public static class Properties {
            public static readonly SystemError ComputationFailed = new(ErrorDomain.Validation, 3300, "Mass properties computation failed");
        }

        /// <summary>Topology validation errors (3400-3499).</summary>
        public static class Topology {
            public static readonly SystemError InvalidTopology = new(ErrorDomain.Validation, 3400, "Geometry has invalid topology");
        }

        /// <summary>Degeneracy validation errors (3500-3599).</summary>
        public static class Degeneracy {
            public static readonly SystemError DegenerateGeometry = new(ErrorDomain.Validation, 3500, "Geometry is degenerate");
        }

        /// <summary>Self-intersection validation errors (3600-3699).</summary>
        public static class SelfIntersection {
            public static readonly SystemError SelfIntersecting = new(ErrorDomain.Validation, 3600, "Geometry is self-intersecting");
        }

        /// <summary>Mesh-specific validation errors (3700-3799).</summary>
        public static class MeshTopology {
            public static readonly SystemError NonManifoldEdges = new(ErrorDomain.Validation, 3700, "Mesh has non-manifold edges");
        }

        /// <summary>Surface continuity validation errors (3800-3899).</summary>
        public static class Continuity {
            public static readonly SystemError PositionalDiscontinuity = new(ErrorDomain.Validation, 3800, "Surface has positional discontinuity (G0)");
        }
    }

    /// <summary>Context validation errors (3900-3999).</summary>
    public static class Context {
        /// <summary>Tolerance validation errors (3900-3919).</summary>
        public static class Tolerance {
            public static readonly SystemError InvalidAbsolute = new(ErrorDomain.Validation, 3900, "Absolute tolerance must be greater than zero");
            public static readonly SystemError InvalidRelative = new(ErrorDomain.Validation, 3901, "Relative tolerance must be in range [0,1)");
            public static readonly SystemError InvalidAngle = new(ErrorDomain.Validation, 3902, "Angle tolerance must be in range (0, 2π]");
            public static readonly SystemError ToleranceExceeded = new(ErrorDomain.Validation, 3903, "Geometry exceeds tolerance threshold");
        }
        public static readonly SystemError InvalidUnitConversion = new(ErrorDomain.Validation, 3920, "Invalid unit conversion scale");
    }

    /// <summary>Operation validation errors (4000-4099).</summary>
    public static class Operations {
        public static readonly SystemError UnsupportedOperationType = new(ErrorDomain.Validation, 4000, "Unsupported operation type");
        public static readonly SystemError InputFiltered = new(ErrorDomain.Validation, 4001, "Input filtered");
    }
}
