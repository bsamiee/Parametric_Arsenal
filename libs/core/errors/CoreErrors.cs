namespace Arsenal.Core.Errors;

/// <summary>Centralized core library errors with automatic registration and hierarchical organization.</summary>
public static class CoreErrors {

    /// <summary>Result system errors (1000-1999).</summary>
    public static class Results {
        public static readonly SystemError NoValueProvided = ErrorRegistry.Register(domain: ErrorDomain.Results, code: 1001, message: "No value provided");
        public static readonly SystemError InvalidCreateParameters = ErrorRegistry.Register(domain: ErrorDomain.Results, code: 1002, message: "Invalid Create parameters");
        public static readonly SystemError InvalidValidateParameters = ErrorRegistry.Register(domain: ErrorDomain.Results, code: 1003, message: "Invalid validation parameters");
        public static readonly SystemError InvalidLiftParameters = ErrorRegistry.Register(domain: ErrorDomain.Results, code: 1004, message: "Invalid Lift parameters");
        public static readonly SystemError InvalidAccess = ErrorRegistry.Register(domain: ErrorDomain.Results, code: 1100, message: "Cannot access value in error state or error in success state");
    }

    /// <summary>Geometry validation errors (3000-3999).</summary>
    public static class Geometry {
        public static readonly SystemError Invalid = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3000, message: "Geometry must be valid");

        public static class Curve {
            public static readonly SystemError NotClosedOrPlanar = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3100, message: "Curve must be closed and planar for area centroid");
        }

        public static class BoundingBox {
            public static readonly SystemError Invalid = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3200, message: "Bounding box is invalid");
        }

        public static class Properties {
            public static readonly SystemError ComputationFailed = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3300, message: "Mass properties computation failed");
        }

        public static class Topology {
            public static readonly SystemError InvalidTopology = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3400, message: "Geometry has invalid topology");
        }

        public static class Degeneracy {
            public static readonly SystemError DegenerateGeometry = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3500, message: "Geometry is degenerate");
        }

        public static class SelfIntersection {
            public static readonly SystemError SelfIntersecting = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3600, message: "Geometry is self-intersecting");
        }

        public static class MeshTopology {
            public static readonly SystemError NonManifoldEdges = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3700, message: "Mesh has non-manifold edges");
        }

        public static class Continuity {
            public static readonly SystemError PositionalDiscontinuity = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3800, message: "Surface has positional discontinuity (G0)");
        }
    }

    /// <summary>Context validation errors (3900-3999).</summary>
    public static class Context {
        public static readonly SystemError InvalidUnitConversion = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3920, message: "Invalid unit conversion scale");

        public static class Tolerance {
            public static readonly SystemError InvalidAbsolute = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3900, message: "Absolute tolerance must be greater than zero");
            public static readonly SystemError InvalidRelative = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3901, message: "Relative tolerance must be in range [0,1)");
            public static readonly SystemError InvalidAngle = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3902, message: "Angle tolerance must be in range (0, 2π]");
            public static readonly SystemError ToleranceExceeded = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 3903, message: "Geometry exceeds tolerance threshold");
        }
    }

    /// <summary>Operation validation errors (4000-4099).</summary>
    public static class Operations {
        public static readonly SystemError UnsupportedOperationType = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 4000, message: "Unsupported operation type");
        public static readonly SystemError InputFiltered = ErrorRegistry.Register(domain: ErrorDomain.Validation, code: 4001, message: "Input filtered");
    }
}
