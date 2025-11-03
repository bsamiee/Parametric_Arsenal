using Arsenal.Core.Errors;

namespace Arsenal.Core.Validation;

/// <summary>Hierarchical validation error factory with consistent error codes.</summary>
public static class ValidationErrors {
    /// <summary>Geometry validation errors (3000-3999).</summary>
    public static class Geometry {
        public static readonly SystemError Null = new(ErrorDomain.Validation, 3000, "Geometry cannot be null");
        public static readonly SystemError Invalid = new(ErrorDomain.Validation, 3001, "Geometry must be valid");
        public static readonly SystemError Unsupported = new(ErrorDomain.Validation, 3002, "Unsupported geometry type");

        /// <summary>Curve validation errors (3100-3199).</summary>
        public static class Curve {
            public static readonly SystemError NotClosedOrPlanar = new(ErrorDomain.Validation, 3101, "Curve must be closed and planar for area centroid");
        }

        /// <summary>Bounding box validation errors (3500-3599).</summary>
        public static class BoundingBox {
            public static readonly SystemError Invalid = new(ErrorDomain.Validation, 3500, "Bounding box is invalid");
        }

        /// <summary>Mass properties validation errors (3600-3699).</summary>
        public static class Properties {
            public static readonly SystemError ComputationFailed = new(ErrorDomain.Validation, 3600, "Mass properties computation failed");
        }

        /// <summary>Topology validation errors (3700-3799).</summary>
        public static class Topology {
            public static readonly SystemError InvalidTopology = new(ErrorDomain.Validation, 3703, "Geometry has invalid topology");
        }

        /// <summary>Degeneracy validation errors (3800-3899).</summary>
        public static class Degeneracy {
            public static readonly SystemError DegenerateGeometry = new(ErrorDomain.Validation, 3800, "Geometry is degenerate");
        }

        /// <summary>Self-intersection validation errors (3900-3999).</summary>
        public static class SelfIntersection {
            public static readonly SystemError SelfIntersecting = new(ErrorDomain.Validation, 3900, "Geometry is self-intersecting");
        }

        /// <summary>Mesh-specific validation errors (4000-4099).</summary>
        public static class MeshTopology {
            public static readonly SystemError NonManifoldEdges = new(ErrorDomain.Validation, 4000, "Mesh has non-manifold edges");
        }

        /// <summary>Surface continuity validation errors (4100-4199).</summary>
        public static class Continuity {
            public static readonly SystemError PositionalDiscontinuity = new(ErrorDomain.Validation, 4100, "Surface has positional discontinuity (G0)");
        }
    }

    /// <summary>Context validation errors (4200-4299).</summary>
    public static class Context {
        /// <summary>Tolerance validation errors (4200-4219).</summary>
        public static class Tolerance {
            public static readonly SystemError InvalidAbsolute = new(ErrorDomain.Validation, 4200, "Absolute tolerance must be greater than zero");
            public static readonly SystemError InvalidRelative = new(ErrorDomain.Validation, 4201, "Relative tolerance must be in range [0,1)");
            public static readonly SystemError InvalidAngle = new(ErrorDomain.Validation, 4202, "Angle tolerance must be in range (0, 2π]");
            public static readonly SystemError ToleranceExceeded = new(ErrorDomain.Validation, 4203, "Geometry exceeds tolerance threshold");
        }
        public static readonly SystemError InvalidUnitConversion = new(ErrorDomain.Validation, 4220, "Invalid unit conversion scale");
    }
}
