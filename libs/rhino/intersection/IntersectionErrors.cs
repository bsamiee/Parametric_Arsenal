using Arsenal.Core.Errors;

namespace Arsenal.Rhino.Intersection;

/// <summary>Intersection operation error definitions.</summary>
public static class IntersectionErrors {
    public static class Operation {
        public static readonly SystemError UnsupportedMethod =
            new(ErrorDomain.Geometry, 2200, "Intersection method not supported for geometry types");

        public static readonly SystemError ComputationFailed =
            new(ErrorDomain.Geometry, 2201, "Intersection computation failed");
    }

    public static class Parameters {
        public static readonly SystemError InvalidProjectionDirection =
            new(ErrorDomain.Geometry, 2202, "Projection direction vector is invalid or zero-length");

        public static readonly SystemError EmptyGeometryCollection =
            new(ErrorDomain.Geometry, 2203, "Empty geometry collection provided for batch operation");

        public static readonly SystemError InvalidRayDirection =
            new(ErrorDomain.Geometry, 2204, "Ray direction vector is invalid or zero-length");

        public static readonly SystemError InvalidMaxHitCount =
            new(ErrorDomain.Geometry, 2205, "Maximum hit count must be positive");
    }
}
